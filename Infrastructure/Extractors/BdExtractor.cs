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

    public async Task<IEnumerable<StgCustomer>> ExtractCustomersAsync()
    {
        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        return await conn.QueryAsync<StgCustomer>(
            "SELECT CustomerID, FirstName, LastName, Email, Phone, City, Country FROM stg.Stg_Customers");
    }

    public async Task<IEnumerable<StgOrder>> ExtractOrdersAsync()
    {
        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        return await conn.QueryAsync<StgOrder>(
            "SELECT OrderID, CustomerID, OrderDate, StatusOrder FROM stg.Stg_Orders");
    }

    public async Task<IEnumerable<StgOrderDetail>> ExtractAsync()
    {
        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        return await conn.QueryAsync<StgOrderDetail>(
            "SELECT OrderDetailID, OrderID, ProductID, Quantity, UnitPrice FROM stg.Stg_OrderDetails");
    }

    public async Task<IEnumerable<StgProduct>> ExtractProductsAsync()
    {
        using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();
        return await conn.QueryAsync<StgProduct>(
            "SELECT ProductID, ProductName, Category, Price FROM stg.Stg_Products");
    }


 }