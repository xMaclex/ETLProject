using Dapper;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;


namespace ETLProject.Infrastructure.Extractors;
 public class BdExtractor : IExtractor<StgOrderDetail>
 {
    private readonly string _conn;
    private readonly ILogger<BdExtractor> _logger;  

    public BdExtractor(IConfiguration config, ILogger<BdExtractor> logger)
    {
        _conn = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    public async Task<IEnumerable<StgOrderDetail>> ExtractAsync()
    {
        using var connection = new SqlConnection(_conn);
        await connection.OpenAsync();
        _logger.LogInformation("Conexión a la base de datos establecida.");

        var result = await connection.QueryAsync<StgOrderDetail>
        ("SELECT OrderDetailID, OrderID, ProductID, Quantity, UnitPrice FROM StgOrderDetails");

        return result;
    }
    public async Task<IEnumerable<StgCustomer>> ExtractCustomersAsync()
    {
        using var connection = new SqlConnection(_conn);
        await connection.OpenAsync();
        return await connection.QueryAsync<StgCustomer>
        ("SELECT CustomerID, FirstName, LastName, Email, Phone, City, Country FROM StgCustomers");
    }

    public async Task<IEnumerable<StgOrder>> ExtractOrdersAsync()
    {
        using var connection = new SqlConnection(_conn);
        await connection.OpenAsync();
        return await connection.QueryAsync<StgOrder>
        ("SELECT OrderID, CustomerID, OrderDate, StatusOrder FROM StgOrders");
    }

    public async Task<IEnumerable<StgProduct>> ExtractProductsAsync()
    {
        using var connection = new SqlConnection(_conn);
        await connection.OpenAsync();
        return await connection.QueryAsync<StgProduct>
        ("SELECT ProductID, ProductName, Category, Price FROM StgProducts");
    }


 }