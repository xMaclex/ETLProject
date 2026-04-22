using Dapper;
using ETLProject.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;

namespace ETLProject.Infrastructure.Loaders;

/// <summary>
/// Carga los hechos de venta en fact.FactVentas.
/// IMPORTANTE: Siempre ejecutar DESPUÉS de que todas las dimensiones estén cargadas.
/// </summary>
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
        IEnumerable<StgOrder>       orders,
        IEnumerable<StgOrderDetail> orderDetails,
        IEnumerable<StgCustomer>    customers,
        IEnumerable<StgProduct>     products)
    {
        _logger.LogInformation("FactVenta: comenzando carga de hechos...");

        // ── 1. MATERIALIZAR LISTAS ────────────────────────────────────────────
        // Convertimos a listas concretas para evitar múltiples enumeraciones.
        var orderList       = orders.ToList();
        var orderDetailList = orderDetails.ToList();
        var customerList    = customers.ToList();
        var productList     = products.ToList();

        // ── 2. DICCIONARIOS DE LOOKUP (staging) ──────────────────────────────
        // Usamos diccionarios para hacer búsquedas O(1) en vez de O(n) en bucles.
        // Esto es clave para el rendimiento cuando hay miles de registros.
        var orderLookup    = orderList.ToDictionary(o => o.OrderID, o => o);
        var customerLookup = customerList.ToDictionary(c => c.CustomerID, c => c);
        var productLookup  = productList.ToDictionary(p => p.ProductID, p => p);

        // ── 3. CONSTRUIR LOS REGISTROS DE HECHOS (EN MEMORIA) ────────────────
        // Cruzamos OrderDetails con Orders, Customers y Products para reunir
        // toda la información que necesita una fila de FactVentas.
        var factRows = new List<FactRowStaging>();

        foreach (var od in orderDetailList)
        {
            // Verificar que existan los padres; si falta alguno, saltamos el detalle
            if (!orderLookup.TryGetValue(od.OrderID, out var order))
            {
                _logger.LogWarning("FactVenta: OrderID {id} no encontrado — se omite detalle", od.OrderID);
                continue;
            }
            if (!customerLookup.ContainsKey(order.CustomerID))
            {
                _logger.LogWarning("FactVenta: CustomerID {id} no encontrado — se omite detalle", order.CustomerID);
                continue;
            }
            if (!productLookup.ContainsKey(od.ProductID))
            {
                _logger.LogWarning("FactVenta: ProductID {id} no encontrado — se omite detalle", od.ProductID);
                continue;
            }

            var customer = customerLookup[order.CustomerID];

            // Parseo de campos numéricos que vienen como string desde staging
            int.TryParse(od.Quantity,  out var cantidad);
            decimal.TryParse(od.UnitPrice,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var precioUnitario);

            // Calcular FechaKey en formato YYYYMMDD (entero usado en DimFecha)
            int fechaKey = 0;
            if (DateTime.TryParse(order.OrderDate, out var fecha))
                fechaKey = int.Parse(fecha.ToString("yyyyMMdd"));

            factRows.Add(new FactRowStaging
            {
                FechaKey       = fechaKey,
                ClienteID      = order.CustomerID,
                ProductoID     = od.ProductID,
                PaisNombre     = customer.Country.Trim(),
                Cantidad       = cantidad,
                PrecioUnitario = precioUnitario,
                IngresoTotal   = cantidad * precioUnitario,
                NumeroOrden    = order.OrderID
            });
        }

        _logger.LogInformation("FactVenta: {n} filas de hechos construidas en memoria", factRows.Count);

        if (factRows.Count == 0)
        {
            _logger.LogInformation("FactVenta: no hay hechos para cargar — proceso terminado");
            return;
        }

        // ── 4. ABRIR CONEXIÓN Y RESOLVER CLAVES DE DIMENSIÓN ─────────────────
        // Aquí consultamos las dimensiones ya cargadas en la BD para obtener
        // sus llaves surrogate (ClienteKey, ProductoKey, PaisKey).
        // Estas llaves son las que realmente se guardan en FactVentas.
        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        _logger.LogInformation("FactVenta: leyendo claves surrogate de dimensiones...");

        var clienteKeys  = (await conn.QueryAsync<(int Key, int ID)>(
            "SELECT ClienteKey, ClienteID FROM dim.DimCliente"))
            .ToDictionary(x => x.ID, x => x.Key);

        var productoKeys = (await conn.QueryAsync<(int Key, int ID)>(
            "SELECT ProductoKey, ProductoID FROM dim.DimProducto"))
            .ToDictionary(x => x.ID, x => x.Key);

        var paisKeys     = (await conn.QueryAsync<(int Key, string Nombre)>(
            "SELECT PaisKey, NombrePais FROM dim.DimPais"))
            .ToDictionary(x => x.Nombre, x => x.Key, StringComparer.OrdinalIgnoreCase);

        // ── 5. MAPEAR CON CLAVES SURROGATE ───────────────────────────────────
        // Sustituimos los IDs naturales por las claves surrogate de dimensión.
        // Si no encontramos la clave, usamos 0 (clave de "desconocido").
        int sinCliente = 0, sinProducto = 0, sinPais = 0;

        var dt = new DataTable();
        dt.Columns.Add("FechaKey",       typeof(int));
        dt.Columns.Add("ClienteKey",     typeof(int));
        dt.Columns.Add("ProductoKey",    typeof(int));
        dt.Columns.Add("PaisKey",        typeof(int));
        dt.Columns.Add("Cantidad",       typeof(int));
        dt.Columns.Add("PrecioUnitario", typeof(decimal));
        dt.Columns.Add("IngresoTotal",   typeof(decimal));
        dt.Columns.Add("NumeroOrden",    typeof(int));

        foreach (var row in factRows)
        {
            if (!clienteKeys.TryGetValue(row.ClienteID, out var ck))
            { sinCliente++; ck = 0; }

            if (!productoKeys.TryGetValue(row.ProductoID, out var pk))
            { sinProducto++; pk = 0; }

            if (!paisKeys.TryGetValue(row.PaisNombre, out var psk))
            { sinPais++; psk = 0; }

            dt.Rows.Add(row.FechaKey, ck, pk, psk,
                        row.Cantidad, row.PrecioUnitario,
                        row.IngresoTotal, row.NumeroOrden);
        }

        if (sinCliente  > 0) _logger.LogWarning("FactVenta: {n} filas sin ClienteKey  → se insertarán con 0", sinCliente);
        if (sinProducto > 0) _logger.LogWarning("FactVenta: {n} filas sin ProductoKey → se insertarán con 0", sinProducto);
        if (sinPais     > 0) _logger.LogWarning("FactVenta: {n} filas sin PaisKey     → se insertarán con 0", sinPais);

        // ── 6. LIMPIAR LA TABLA DE HECHOS ────────────────────────────────────
        // TRUNCATE borra todos los datos y reinicia el identity en ~1 ms.
        // Es más rápido que DELETE porque no registra fila por fila en el log.
        // Usamos esto porque hacemos carga COMPLETA (no incremental).
        _logger.LogInformation("FactVenta: limpiando tabla fact.FactVentas (TRUNCATE)...");
        await conn.ExecuteAsync("TRUNCATE TABLE fact.FactVentas");

        // ── 7. TABLA TEMPORAL + BULK INSERT ──────────────────────────────────
        // Creamos una tabla temporal en memoria del servidor,
        // volcamos todos los datos de golpe (bulk), y luego hacemos
        // un INSERT masivo desde la temporal a la tabla real.
        await conn.ExecuteAsync("""
            CREATE TABLE #TempFactVentas (
                FechaKey       INT,
                ClienteKey     INT,
                ProductoKey    INT,
                PaisKey        INT,
                Cantidad       INT,
                PrecioUnitario DECIMAL(18,2),
                IngresoTotal   DECIMAL(18,2),
                NumeroOrden    INT
            )
            """);

        using (var bulk = new SqlBulkCopy(conn))
        {
            bulk.DestinationTableName = "#TempFactVentas";
            bulk.BulkCopyTimeout      = 120; // segundos
            await bulk.WriteToServerAsync(dt);
        }

        _logger.LogInformation("FactVenta: {n} filas en tabla temporal — insertando en FactVentas...", dt.Rows.Count);

        // ── 8. INSERT FINAL ───────────────────────────────────────────────────
        // Solo insertamos filas donde las claves de dimensión sean válidas (> 0).
        // Las filas con clave 0 se omiten para no contaminar el DW.
        const string insertSql = """
            INSERT INTO fact.FactVentas (
                FechaKey, ClienteKey, ProductoKey, PaisKey,
                Cantidad, PrecioUnitario, IngresoTotal, NumeroOrden
            )
            SELECT
                FechaKey, ClienteKey, ProductoKey, PaisKey,
                Cantidad, PrecioUnitario, IngresoTotal, NumeroOrden
            FROM #TempFactVentas
            WHERE ClienteKey  > 0
              AND ProductoKey > 0
              AND PaisKey     > 0
              AND FechaKey    > 0;
            """;

        var inserted = await conn.ExecuteAsync(insertSql);

        _logger.LogInformation("FactVenta: {n} registros insertados exitosamente en fact.FactVentas", inserted);

        int omitidas = dt.Rows.Count - inserted;
        if (omitidas > 0)
            _logger.LogWarning("FactVenta: {n} filas omitidas por claves de dimensión inválidas (= 0)", omitidas);
    }

    // DTO interno — solo para organizar la lógica de mapeo en memoria
    private sealed class FactRowStaging
    {
        public int     FechaKey       { get; init; }
        public int     ClienteID      { get; init; }
        public int     ProductoID     { get; init; }
        public string  PaisNombre     { get; init; } = string.Empty;
        public int     Cantidad       { get; init; }
        public decimal PrecioUnitario { get; init; }
        public decimal IngresoTotal   { get; init; }
        public int     NumeroOrden    { get; init; }
    }
}