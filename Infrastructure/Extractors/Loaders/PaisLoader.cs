using Dapper;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ETLProject.Infrastructure.Loaders;

public class PaisLoader : IPaisLoader
{
    private readonly string _conn;
    private readonly ILogger<PaisLoader> _logger;

    public PaisLoader(IConfiguration config, ILogger<PaisLoader> logger)
    {
        _conn   = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task LoadAsync(IEnumerable<StgCustomer> data)
    {
        // Países únicos extraídos de clientes
        var paises = data
            .Where(c => !string.IsNullOrWhiteSpace(c.Country))
            .Select(c => c.Country.Trim())
            .Distinct()
            .ToList();

        _logger.LogInformation("DimPais: {n} países únicos a cargar...", paises.Count);

        if (paises.Count == 0) return;

        // Crear DataTable para bulk insert
        var dt = new DataTable();
        dt.Columns.Add("NombrePais", typeof(string));
        dt.Columns.Add("Region", typeof(string));

        foreach (var pais in paises)
        {
            dt.Rows.Add(
                pais,
                ResolveRegion(pais)
            );
        }

        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Crear tabla temporal
        using var cmd = new SqlCommand("CREATE TABLE #TempPaises (NombrePais NVARCHAR(100), Region NVARCHAR(50))", conn);
        await cmd.ExecuteNonQueryAsync();

        // Bulk insert a tabla temporal
        using var bulk = new SqlBulkCopy(conn);
        bulk.DestinationTableName = "#TempPaises";
        await bulk.WriteToServerAsync(dt);

        // Merge desde tabla temporal
        const string sql = """
            MERGE dim.DimPais AS target
            USING #TempPaises AS source
                ON target.NombrePais = source.NombrePais
            WHEN NOT MATCHED THEN
                INSERT (NombrePais, Region)
                VALUES (source.NombrePais, source.Region);
            """;

        await conn.ExecuteAsync(sql);

        _logger.LogInformation("DimPais: {n} países procesados", paises.Count);
    }

    private static string ResolveRegion(string pais) => pais.ToLower() switch
    {
        "united states" or "canada" or "mexico"           => "América del Norte",
        "dominican republic" or "colombia" or "brazil"
            or "argentina" or "peru" or "chile"           => "América Latina",
        "spain" or "france" or "germany" or "italy"
            or "united kingdom"                           => "Europa",
        "china" or "japan" or "india" or "south korea"    => "Asia",
        "faroe islands" or "holy see (vatican city state)"=> "Europa",
        _                                                  => "Otros"
    };
}