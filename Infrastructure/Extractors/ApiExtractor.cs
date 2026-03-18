using System.Text.Json;
using ETLProject.Application.Interfaces;
using ETLProject.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ETLProject.Infrastructure.Extractors;

public class ApiExtractor : IExtractor<StgOrder>
{
    private readonly IHttpClientFactory    _httpFactory;
    private readonly string                _baseUrl;
    private readonly ILogger<ApiExtractor> _logger;

    public ApiExtractor(
        IHttpClientFactory    httpFactory,
        IConfiguration        config,
        ILogger<ApiExtractor> logger)
    {
        _httpFactory = httpFactory;
        _baseUrl     = config["ApiSettings:BaseUrl"] ?? string.Empty;
        _logger      = logger;
    }

    public async Task<IEnumerable<StgOrder>> ExtractAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            _logger.LogWarning("API: BaseUrl no configurada, extracción omitida.");
            return [];
        }

        try
        {
            // IHttpClientFactory crea y gestiona el HttpClient correctamente
            var client   = _httpFactory.CreateClient("ApiClient");
            var response = await client.GetStringAsync($"{_baseUrl}/orders");

            var orders = JsonSerializer.Deserialize<List<StgOrder>>(response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation("API: {n} órdenes recibidas", orders?.Count ?? 0);
            return orders ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API: error al consumir el endpoint {url}", _baseUrl);
            return [];
        }
    }
}