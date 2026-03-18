public interface IExtractor
{
    Task<IEnumerable<dynamic>> ExtractAsync();
}