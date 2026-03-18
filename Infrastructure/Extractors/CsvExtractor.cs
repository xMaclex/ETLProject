using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ETLProject.Infrastructure.Extractors;

// Maps explícitos — solo columnas que existen en stg.*
public sealed class StgCustomerMap : ClassMap<StgCustomer>
{
    public StgCustomerMap()
    {
        Map(m => m.CustomerID).Name("CustomerID");
        Map(m => m.FirstName).Name("FirstName");
        Map(m => m.LastName).Name("LastName");
        Map(m => m.Email).Name("Email");
        Map(m => m.Phone).Name("Phone");
        Map(m => m.City).Name("City");
        Map(m => m.Country).Name("Country");
    }
}

public sealed class StgProductMap : ClassMap<StgProduct>
{
    public StgProductMap()
    {
        Map(m => m.ProductID).Name("ProductID");
        Map(m => m.ProductName).Name("ProductName");
        Map(m => m.Category).Name("Category");
        Map(m => m.Price).Name("Price");
        // Stock ignorado — no existe en stg.Stg_Products
    }
}

public sealed class StgOrderMap : ClassMap<StgOrder>
{
    public StgOrderMap()
    {
        Map(m => m.OrderID).Name("OrderID");
        Map(m => m.CustomerID).Name("CustomerID");
        Map(m => m.OrderDate).Name("OrderDate");
        Map(m => m.StatusOrder).Name("StatusOrder");
    }
}

public sealed class StgOrderDetailMap : ClassMap<StgOrderDetail>
{
    public StgOrderDetailMap()
    {
        // OrderDetailID no viene en el CSV — se deja en 0, la BD lo autogenera
        Map(m => m.OrderID).Name("OrderID");
        Map(m => m.ProductID).Name("ProductID");
        Map(m => m.Quantity).Name("Quantity");
        // UnitPrice no viene en el CSV — TotalPrice se ignora
        Map(m => m.UnitPrice).Constant("0");
    }
}

public class CsvExtractor : IExtractor<StgProduct>
{
    private readonly string _folderPath;
    private readonly ILogger<CsvExtractor> _logger;

    public CsvExtractor(IConfiguration config, ILogger<CsvExtractor> logger)
    {
        _folderPath = config["Extraction:CsvFolder"]!;
        _logger     = logger;
    }

    private CsvConfiguration CsvConfig => new(CultureInfo.InvariantCulture)
    {
        HeaderValidated   = null,
        MissingFieldFound = null
    };

    private List<T> ReadFile<T, TMap>(string filePath) where TMap : ClassMap
    {
        using var reader = new StreamReader(filePath);
        using var csv    = new CsvReader(reader, CsvConfig);
        csv.Context.RegisterClassMap<TMap>();
        return csv.GetRecords<T>().ToList();
    }

    private string FilePath(string name) => Path.Combine(_folderPath, name);

    public async Task<IEnumerable<StgProduct>> ExtractAsync()
    {
        var file = FilePath("products.csv");
        if (!File.Exists(file)) { _logger.LogWarning("CSV: no existe {f}", file); return []; }
        var data = ReadFile<StgProduct, StgProductMap>(file);
        _logger.LogInformation("CSV productos: {n} registros", data.Count);
        return await Task.FromResult(data);
    }

    public async Task<IEnumerable<StgCustomer>> ExtractCustomersAsync()
    {
        var file = FilePath("customers.csv");
        if (!File.Exists(file)) { _logger.LogWarning("CSV: no existe {f}", file); return []; }
        var data = ReadFile<StgCustomer, StgCustomerMap>(file);
        _logger.LogInformation("CSV clientes: {n} registros", data.Count);
        return await Task.FromResult(data);
    }

    public async Task<IEnumerable<StgOrder>> ExtractOrdersAsync()
    {
        var file = FilePath("orders.csv");
        if (!File.Exists(file)) { _logger.LogWarning("CSV: no existe {f}", file); return []; }
        var data = ReadFile<StgOrder, StgOrderMap>(file);
        _logger.LogInformation("CSV órdenes: {n} registros", data.Count);
        return await Task.FromResult(data);
    }

    public async Task<IEnumerable<StgOrderDetail>> ExtractOrderDetailsAsync()
    {
        var file = FilePath("order_details.csv");
        if (!File.Exists(file)) { _logger.LogWarning("CSV: no existe {f}", file); return []; }
        var data = ReadFile<StgOrderDetail, StgOrderDetailMap>(file);
        _logger.LogInformation("CSV detalles: {n} registros", data.Count);
        return await Task.FromResult(data);
    }
}