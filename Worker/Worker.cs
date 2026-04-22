using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using ETLProject.Infrastructure.Extractors;
using ETLProject.Infrastructure.Loaders;

namespace ETLProject;

/// <summary>
/// Worker: orquesta el pipeline ETL completo.
/// Orden obligatorio: extraer → cargar dimensiones → cargar hechos.
/// Las claves surrogate de las dimensiones deben existir ANTES de insertar hechos.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker>      _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory)
    {
        _logger       = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== ETL POS iniciando: {time} ===", DateTimeOffset.Now);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // ── Extractores ──────────────────────────────────────────────────────
        var bdExtractor  = sp.GetRequiredService<BdExtractor>();
        var csvExtractor = sp.GetRequiredService<CsvExtractor>();
        var apiExtractor = sp.GetRequiredService<IExtractor<StgOrder>>();

        // ── Loaders de dimensiones (se deben cargar ANTES de los hechos) ─────
        var clienteLoader  = sp.GetRequiredService<IDimensionLoader<StgCustomer>>();
        var productoLoader = sp.GetRequiredService<IDimensionLoader<StgProduct>>();
        var fechaLoader    = sp.GetRequiredService<IDimensionLoader<StgOrder>>();
        var paisLoader     = sp.GetRequiredService<IPaisLoader>();

        // ── Loader de hechos (siempre al final) ──────────────────────────────
        var factVentaLoader = sp.GetRequiredService<FactVentaLoader>();

        try
        {
            // ════════════════════════════════════════════════════════════════
            // FASE 1 — EXTRACCIÓN
            // Traemos datos de las 3 fuentes: BD, CSV y API.
            // ════════════════════════════════════════════════════════════════
            _logger.LogInformation("Extrayendo desde SQL Server...");
            var customersDb    = (await bdExtractor.ExtractCustomersAsync()).ToList();
            var ordersDb       = (await bdExtractor.ExtractOrdersAsync()).ToList();
            var orderDetailsDb = (await bdExtractor.ExtractAsync()).ToList();
            var productsDb     = (await bdExtractor.ExtractProductsAsync()).ToList();
            _logger.LogInformation("BD | Customers: {c} | Orders: {o} | Details: {d} | Products: {p}",
                customersDb.Count, ordersDb.Count, orderDetailsDb.Count, productsDb.Count);

            _logger.LogInformation("Extrayendo desde CSV...");
            var customersCsv    = (await csvExtractor.ExtractCustomersAsync()).ToList();
            var ordersCsv       = (await csvExtractor.ExtractOrdersAsync()).ToList();
            var orderDetailsCsv = (await csvExtractor.ExtractOrderDetailsAsync()).ToList();
            var productsCsv     = (await csvExtractor.ExtractAsync()).ToList();
            _logger.LogInformation("CSV | Customers: {c} | Orders: {o} | Details: {d} | Products: {p}",
                customersCsv.Count, ordersCsv.Count, orderDetailsCsv.Count, productsCsv.Count);

            var ordersApi = (await apiExtractor.ExtractAsync()).ToList();
            _logger.LogInformation("API | Órdenes: {n}", ordersApi.Count);

            // ── Combinar y deduplicar por clave natural ───────────────────
            var allCustomers = customersDb
                .Concat(customersCsv)
                .DistinctBy(c => c.CustomerID)
                .ToList();

            var allProducts = productsDb
                .Concat(productsCsv)
                .DistinctBy(p => p.ProductID)
                .ToList();

            var allOrders = ordersDb
                .Concat(ordersCsv)
                .Concat(ordersApi)
                .DistinctBy(o => o.OrderID)
                .ToList();

            // Para detalles usamos una clave compuesta para evitar duplicados
            var allOrderDetails = orderDetailsDb
                .Concat(orderDetailsCsv)
                .DistinctBy(od => new { od.OrderID, od.ProductID, od.Quantity, od.UnitPrice })
                .ToList();

            // ════════════════════════════════════════════════════════════════
            // FASE 2 — CARGA DE DIMENSIONES
            // Orden importante: Pais antes de Cliente (Cliente referencia Pais).
            // Todas las dimensiones DEBEN estar listas antes de FactVentas.
            // ════════════════════════════════════════════════════════════════
            _logger.LogInformation("Cargando dimensiones...");

            await paisLoader.LoadAsync(allCustomers);    // 1°: DimPais
            await clienteLoader.LoadAsync(allCustomers); // 2°: DimCliente
            await productoLoader.LoadAsync(allProducts); // 3°: DimProducto
            await fechaLoader.LoadAsync(allOrders);      // 4°: DimFecha

            _logger.LogInformation("Dimensiones cargadas. Iniciando carga de hechos...");

            // ════════════════════════════════════════════════════════════════
            // FASE 3 — CARGA DE HECHOS
            // Solo después de que todas las dimensiones existan en la BD,
            // porque necesitamos sus claves surrogate (ClienteKey, etc.).
            // ════════════════════════════════════════════════════════════════
            await factVentaLoader.LoadAsync(
                allOrders,
                allOrderDetails,
                allCustomers,
                allProducts);

            _logger.LogInformation(
                "=== Extracción, carga de dimensiones y hechos completadas: {time} ===",
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en el pipeline ETL");
        }
    }
}