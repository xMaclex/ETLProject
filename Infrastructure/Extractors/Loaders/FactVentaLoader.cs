using Dapper;
//using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Data.SqlClient;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.Logging;

namespace ETLProject.Infrastructure.Loaders;

public class FactVentaLoader
{
    private readonly string _conn;
    private readonly ILogger<FactVentaLoader> _logger;

    public FactVentaLoader(IConfiguration config, ILogger<FactVentaLoader> logger)
    {
        _conn   = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task LoadAsync(
        IEnumerable<StgOrder> orders,
        IEnumerable<StgOrderDetail> orderDetails,
        IEnumerable<StgCustomer> customers,
        IEnumerable<StgProduct> products)
    {
        _logger.LogInformation("FactVenta: comenzando carga de hechos...");

        // Crear lookup tables para las claves de dimensión
        var customerLookup = customers.ToDictionary(c => c.CustomerID, c => c);
        var productLookup = products.ToDictionary(p => p.ProductID, p => p);
        var orderLookup = orders.ToDictionary(o => o.OrderID, o => o);

        // Unir order details con orders para obtener fechas
        var factData = orderDetails
            .Where(od => orderLookup.ContainsKey(od.OrderID) &&
                        customerLookup.ContainsKey(orderLookup[od.OrderID].CustomerID) &&
                        productLookup.ContainsKey(od.ProductID))
            .Select(od =>
            {
                var order = orderLookup[od.OrderID];
                var customer = customerLookup[order.CustomerID];
                var productId = od.ProductID;

                // Parse quantities and prices
                int.TryParse(od.Quantity, out var quantity);
                decimal.TryParse(od.UnitPrice, out var unitPrice);

                return new
                {
                    FechaKey = DateTime.TryParse(order.OrderDate, out var date)
                        ? int.Parse(date.ToString("yyyyMMdd"))
                        : 0,
                    ClienteID = order.CustomerID,
                    ProductoID = productId,
                    PaisNombre = customer.Country,
                    Cantidad = quantity,
                    PrecioUnitario = unitPrice,
                    IngresoTotal = quantity * unitPrice,
                    NumeroOrden = order.OrderID
                };
            })
            .ToList();

        _logger.LogInformation("FactVenta: {n} registros de hechos a cargar...", factData.Count);

        if (factData.Count == 0)
        {
            _logger.LogInformation("FactVenta: no hay hechos para cargar");
            return;
        }

        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        // Obtener lookups de claves de dimensión
        var clienteKeys = await conn.QueryAsync<(int ClienteKey, int ClienteID)>("SELECT ClienteKey, ClienteID FROM dim.DimCliente");
        var clienteLookupKeys = clienteKeys.ToDictionary(c => c.ClienteID, c => c.ClienteKey);

        var productoKeys = await conn.QueryAsync<(int ProductoKey, int ProductoID)>("SELECT ProductoKey, ProductoID FROM dim.DimProducto");
        var productoLookupKeys = productoKeys.ToDictionary(p => p.ProductoID, p => p.ProductoKey);

        var paisKeys = await conn.QueryAsync<(int PaisKey, string NombrePais)>("SELECT PaisKey, NombrePais FROM dim.DimPais");
        var paisLookupKeys = paisKeys.ToDictionary(p => p.NombrePais, p => p.PaisKey);

        // Mapear factData con las claves correctas
        var mappedFactData = factData.Select(f => new
        {
            FechaKey = f.FechaKey,
            ClienteKey = clienteLookupKeys.TryGetValue(f.ClienteID, out var ck) ? ck : 0,
            ProductoKey = productoLookupKeys.TryGetValue(f.ProductoID, out var pk) ? pk : 0,
            PaisKey = paisLookupKeys.TryGetValue(f.PaisNombre, out var psk) ? psk : 0,
            Cantidad = f.Cantidad,
            PrecioUnitario = f.PrecioUnitario,
            IngresoTotal = f.IngresoTotal,
            NumeroOrden = f.NumeroOrden
        }).ToList();

        // Crear DataTable para bulk insert
        var dt = new System.Data.DataTable();
        dt.Columns.Add("FechaKey", typeof(int));
        dt.Columns.Add("ClienteKey", typeof(int));
        dt.Columns.Add("ProductoKey", typeof(int));
        dt.Columns.Add("PaisKey", typeof(int));
        dt.Columns.Add("Cantidad", typeof(int));
        dt.Columns.Add("PrecioUnitario", typeof(decimal));
        dt.Columns.Add("IngresoTotal", typeof(decimal));
        dt.Columns.Add("NumeroOrden", typeof(int));

        foreach (var fact in mappedFactData)
        {
            dt.Rows.Add(
                fact.FechaKey,
                fact.ClienteKey,
                fact.ProductoKey,
                fact.PaisKey,
                fact.Cantidad,
                fact.PrecioUnitario,
                fact.IngresoTotal,
                fact.NumeroOrden
            );
        }

        const string sql = """
            INSERT INTO fact.FactVentas (
                FechaKey, ClienteKey, ProductoKey, PaisKey,
                Cantidad, PrecioUnitario, IngresoTotal, NumeroOrden
            )
            SELECT
                FechaKey, ClienteKey, ProductoKey, PaisKey,
                Cantidad, PrecioUnitario, IngresoTotal, NumeroOrden
            FROM #TempFactVentas;
            """;

        using var cmd = new SqlCommand(
            "CREATE TABLE #TempFactVentas (" +
            "FechaKey INT, ClienteKey INT, ProductoKey INT, PaisKey INT, " +
            "Cantidad INT, PrecioUnitario DECIMAL(18,2), IngresoTotal DECIMAL(18,2), NumeroOrden INT)",
            conn);
        await cmd.ExecuteNonQueryAsync();

        using var bulk = new SqlBulkCopy(conn);
        bulk.DestinationTableName = "#TempFactVentas";
        await bulk.WriteToServerAsync(dt);

        var affected = await conn.ExecuteAsync(sql);

        _logger.LogInformation("FactVenta: {n} registros insertados", affected);
    }
}