using Dapper;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ETLProject.Infrastructure.Loaders;

public class FechaLoader : IDimensionLoader<StgOrder>
{
    private readonly string _conn;
    private readonly ILogger<FechaLoader> _logger;

    public FechaLoader(IConfiguration config, ILogger<FechaLoader> logger)
    {
        _conn   = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task LoadAsync(IEnumerable<StgOrder> data)
    {
        // Extraer fechas únicas parseando el campo varchar OrderDate
        var fechas = data
            .Where(o => DateTime.TryParse(o.OrderDate, out _))
            .Select(o => DateTime.Parse(o.OrderDate).Date)
            .Distinct()
            .ToList();

        _logger.LogInformation("DimFecha: {n} fechas únicas a cargar...", fechas.Count);

        if (fechas.Count == 0) return;

        // Crear DataTable para bulk insert
        var dt = new DataTable();
        dt.Columns.Add("FechaKey", typeof(int));
        dt.Columns.Add("FechaCompleta", typeof(DateTime));
        dt.Columns.Add("Anio", typeof(int));
        dt.Columns.Add("Mes", typeof(int));
        dt.Columns.Add("NombreMes", typeof(string));
        dt.Columns.Add("Trimestre", typeof(int));
        dt.Columns.Add("Dia", typeof(int));
        dt.Columns.Add("DiaSemana", typeof(string));

        foreach (var fecha in fechas)
        {
            dt.Rows.Add(
                int.Parse(fecha.ToString("yyyyMMdd")),
                fecha,
                fecha.Year,
                fecha.Month,
                fecha.ToString("MMMM", new System.Globalization.CultureInfo("es-ES")),
                (fecha.Month - 1) / 3 + 1,
                fecha.Day,
                fecha.ToString("dddd", new System.Globalization.CultureInfo("es-ES"))
            );
        }

        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Crear tabla temporal
        using var cmd = new SqlCommand("CREATE TABLE #TempFechas (FechaKey INT, FechaCompleta DATE, Anio INT, Mes INT, NombreMes NVARCHAR(20), Trimestre INT, Dia INT, DiaSemana NVARCHAR(20))", conn);
        await cmd.ExecuteNonQueryAsync();

        // Bulk insert a tabla temporal
        using var bulk = new SqlBulkCopy(conn);
        bulk.DestinationTableName = "#TempFechas";
        await bulk.WriteToServerAsync(dt);

        // Merge desde tabla temporal
        const string sql = """
            MERGE dim.DimFecha AS target
            USING #TempFechas AS source
                ON target.FechaKey = source.FechaKey
            WHEN NOT MATCHED THEN
                INSERT (FechaKey, FechaCompleta, Anio, Mes, NombreMes,
                        Trimestre, Dia, DiaSemana)
                VALUES (source.FechaKey, source.FechaCompleta, source.Anio, source.Mes, source.NombreMes,
                        source.Trimestre, source.Dia, source.DiaSemana);
            """;

        await conn.ExecuteAsync(sql);

        _logger.LogInformation("DimFecha: {n} fechas procesadas", fechas.Count);
    }
}