namespace ETLProject.Application.Interfaces;

public interface IExtractor<T>
{
    Task<IEnumerable<T>> ExtractAsync();
}