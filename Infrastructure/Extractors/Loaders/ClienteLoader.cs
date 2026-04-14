using Dapper;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Data.SqlClient;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.Logging;
using System.Data;

namespace ETLProject.Infrastructure.Loaders;

public class ClienteLoader : IDimensionLoader<StgCustomer>
{
    private readonly string _conn;
    private readonly ILogger<ClienteLoader> _logger;

    public ClienteLoader(IConfiguration config, ILogger<ClienteLoader> logger)
    {
        _conn   = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task LoadAsync(IEnumerable<StgCustomer> data)
    {
        var list = data.ToList();
        _logger.LogInformation("DimCliente: cargando {n} registros...", list.Count);

        if (list.Count == 0) return;

        // Crear DataTable para bulk insert
        var dt = new DataTable();
        dt.Columns.Add("ClienteID", typeof(int));
        dt.Columns.Add("Nombre", typeof(string));
        dt.Columns.Add("Email", typeof(string));
        dt.Columns.Add("Pais", typeof(string));
        dt.Columns.Add("Ciudad", typeof(string));
        dt.Columns.Add("Segmento", typeof(string));

        foreach (var c in list)
        {
            dt.Rows.Add(
                c.CustomerID,
                $"{c.FirstName} {c.LastName}".Trim(),
                c.Email,
                c.Country,
                c.City,
                "General"
            );
        }

        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Crear tabla temporal
        using var cmd = new SqlCommand("CREATE TABLE #TempClientes (ClienteID INT, Nombre NVARCHAR(255), Email NVARCHAR(255), Pais NVARCHAR(100), Ciudad NVARCHAR(100), Segmento NVARCHAR(50))", conn);
        await cmd.ExecuteNonQueryAsync();

        // Bulk insert a tabla temporal
        using var bulk = new SqlBulkCopy(conn);
        bulk.DestinationTableName = "#TempClientes";
        await bulk.WriteToServerAsync(dt);

        // Merge desde tabla temporal
        const string sql = """
            MERGE dim.DimCliente AS target
            USING #TempClientes AS source
                ON target.ClienteID = source.ClienteID
            WHEN MATCHED THEN
                UPDATE SET
                    Nombre   = source.Nombre,
                    Email    = source.Email,
                    Pais     = source.Pais,
                    Ciudad   = source.Ciudad,
                    Segmento = source.Segmento
            WHEN NOT MATCHED THEN
                INSERT (ClienteID, Nombre, Email, Pais, Ciudad, Segmento)
                VALUES (source.ClienteID, source.Nombre, source.Email, source.Pais, source.Ciudad, source.Segmento);
            """;

        await conn.ExecuteAsync(sql);

        _logger.LogInformation("DimCliente: {n} registros procesados", list.Count);
    }
}