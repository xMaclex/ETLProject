using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using ETLProject.Infrastructure.Extractors;

namespace ETLProject;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker>          _logger;
    private readonly BdExtractor              _bdExtractor;
    private readonly IExtractor<StgProduct>   _csvExtractor;
    private readonly IExtractor<StgOrder>     _apiExtractor;

    public Worker(
        ILogger<Worker>        logger,
        BdExtractor            bdExtractor,
        IExtractor<StgProduct> csvExtractor,
        IExtractor<StgOrder>   apiExtractor)
    {
        _logger       = logger;
        _bdExtractor  = bdExtractor;
        _csvExtractor = csvExtractor;
        _apiExtractor = apiExtractor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== ETL POS iniciando: {time} ===", DateTimeOffset.Now);

        try
        {
            // 1. Staging desde SQL Server (Azure Docker)
            var customers     = await _bdExtractor.ExtractCustomersAsync();
            var orders        = await _bdExtractor.ExtractOrdersAsync();
            var orderDetails  = await _bdExtractor.ExtractAsync();         
            var productsFromDb = await _bdExtractor.ExtractProductsAsync();

            _logger.LogInformation("BD | Customers: {c} | Orders: {o} | Details: {d} | Products: {p}",
                customers.Count(), orders.Count(), orderDetails.Count(), productsFromDb.Count());

            // 2. Productos desde CSV
            var productsFromCsv = await _csvExtractor.ExtractAsync();
            _logger.LogInformation("CSV | Productos: {n}", productsFromCsv.Count());

            // 3. Órdenes desde API
            var ordersFromApi = await _apiExtractor.ExtractAsync();
            _logger.LogInformation("API | Órdenes: {n}", ordersFromApi.Count());

            _logger.LogInformation("=== Extracción completada: {time} ===", DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en la extracción ETL");
        }
    }
}