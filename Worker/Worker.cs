using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using ETLProject.Infrastructure.Extractors;

namespace ETLProject;

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

        // Crear un scope para resolver los servicios Scoped
        await using var scope = _scopeFactory.CreateAsyncScope();

        var bdExtractor  = scope.ServiceProvider.GetRequiredService<BdExtractor>();
        var csvExtractor = scope.ServiceProvider.GetRequiredService<IExtractor<StgProduct>>();
        var apiExtractor = scope.ServiceProvider.GetRequiredService<IExtractor<StgOrder>>();

        try
        {
            // BD — 4 tablas staging
            var customers    = await bdExtractor.ExtractCustomersAsync();
            var orders       = await bdExtractor.ExtractOrdersAsync();
            var orderDetails = await bdExtractor.ExtractAsync();
            var productsDb   = await bdExtractor.ExtractProductsAsync();

            _logger.LogInformation("BD | Customers: {c} | Orders: {o} | Details: {d} | Products: {p}",
                customers.Count(), orders.Count(), orderDetails.Count(), productsDb.Count());

            // CSV
            var productsCsv = await csvExtractor.ExtractAsync();
            _logger.LogInformation("CSV | Productos: {n}", productsCsv.Count());

            // API
            var ordersApi = await apiExtractor.ExtractAsync();
            _logger.LogInformation("API | Órdenes: {n}", ordersApi.Count());

            _logger.LogInformation("=== Extracción completada: {time} ===", DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en la extracción ETL");
        }
    }
}