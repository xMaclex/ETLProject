namespace ETLProject.Application.Interfaces;

public interface IDimensionLoader<T>
{
    Task LoadAsync(IEnumerable<T> data);
}

public interface IPaisLoader
{
    Task LoadAsync(IEnumerable<ETLProject.Domain.StgCustomer> data);
}