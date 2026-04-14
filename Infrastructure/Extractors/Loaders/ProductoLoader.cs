using Dapper;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ETLProject.Infrastructure.Loaders;

public class ProductoLoader : IDimensionLoader<StgProduct>
{
    private readonly string _conn;
    private readonly ILogger<ProductoLoader> _logger;

    public ProductoLoader(IConfiguration config, ILogger<ProductoLoader> logger)
    {
        _conn   = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task LoadAsync(IEnumerable<StgProduct> data)
    {
        var list = data.ToList();
        _logger.LogInformation("DimProducto: cargando {n} registros...", list.Count);

        if (list.Count == 0) return;

        // Crear DataTable para bulk insert
        var dt = new DataTable();
        dt.Columns.Add("ProductoID", typeof(int));
        dt.Columns.Add("Nombre", typeof(string));
        dt.Columns.Add("Categoria", typeof(string));
        dt.Columns.Add("PrecioLista", typeof(decimal));

        foreach (var p in list)
        {
            // Price viene como string desde staging — se parsea aquí
            decimal.TryParse(p.Price,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var precio);

            dt.Rows.Add(
                p.ProductID,
                p.ProductName,
                p.Category,
                precio
            );
        }

        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Crear tabla temporal
        using var cmd = new SqlCommand("CREATE TABLE #TempProductos (ProductoID INT, Nombre NVARCHAR(255), Categoria NVARCHAR(100), PrecioLista DECIMAL(18,2))", conn);
        await cmd.ExecuteNonQueryAsync();

        // Bulk insert a tabla temporal
        using var bulk = new SqlBulkCopy(conn);
        bulk.DestinationTableName = "#TempProductos";
        await bulk.WriteToServerAsync(dt);

        // Merge desde tabla temporal
        const string sql = """
            MERGE dim.DimProducto AS target
            USING #TempProductos AS source
                ON target.ProductoID = source.ProductoID
            WHEN MATCHED THEN
                UPDATE SET
                    Nombre      = source.Nombre,
                    Categoria   = source.Categoria,
                    PrecioLista = source.PrecioLista,
                    Activo      = 1
            WHEN NOT MATCHED THEN
                INSERT (ProductoID, Nombre, Categoria, PrecioLista, Activo)
                VALUES (source.ProductoID, source.Nombre, source.Categoria, source.PrecioLista, 1);
            """;

        await conn.ExecuteAsync(sql);

        _logger.LogInformation("DimProducto: {n} registros procesados", list.Count);
    }
}