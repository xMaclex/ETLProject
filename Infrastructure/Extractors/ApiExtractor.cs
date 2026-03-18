using System.Text.Json;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ETLProject.Infrastructure.Extractors;
public class ApiExtractor : IExtractor<StgOrder>
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly ILogger<ApiExtractor> _logger;

    public ApiExtractor(HttpClient http, IConfiguration config, ILogger<ApiExtractor> logger)
    {
        _http = http;
        _baseUrl = config["ApiSenttings:BaseUrl"]!;
        _logger = logger;
    }

    public async Task<IEnumerable<StgOrder>> ExtractAsync()
    {

        _logger.LogInformation("API: consultando {url}/orders", _baseUrl);
        var response = await _http.GetStringAsync($"{_baseUrl}/orders");

        var orders = JsonSerializer.Deserialize<List<StgOrder>>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _logger.LogInformation("API: {n} orders obtenidos", orders?.Count ?? 0);
        return orders ?? [];
    }
}