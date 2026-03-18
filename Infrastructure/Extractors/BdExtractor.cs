
public class BdExtractor : IExtractor
{
    private readonly string _connectionString;
    public  BdExtractor(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    } 

    public async Task<IEnumerable<dynamic>> ExtractAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var result = await conn.QueryAsync("SELECT * FROM Clientes");

        return result;
    }

}