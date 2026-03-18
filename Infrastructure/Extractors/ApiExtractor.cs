public class ApiExtractor : IExtractor
{
    private readonly HttpClient _http;

    public ApiExtractor(HttpClient http)
    {
        _http = http;
    }

    public async Task<IEnumerable<dynamic>> ExtractAsync()
    {
        var response = await _http.GetStringAsync("https://api.com/comments");
        return new List<string> { response };
    }
}