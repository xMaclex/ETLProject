using System.Collections.Concurrent;

namespace ETLProject.Infrastructure;

public class EtlLogBuffer
{
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly List<Action<string>>    _subscribers = new();
    private readonly object _lock = new();

    public void Add(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.Enqueue(entry);

        lock (_lock)
            foreach (var sub in _subscribers)
                sub(entry);
    }

    public IEnumerable<string> GetAll() => _logs.ToArray();

    public void Subscribe(Action<string> handler)
    {
        lock (_lock) _subscribers.Add(handler);
    }

    public void Unsubscribe(Action<string> handler)
    {
        lock (_lock) _subscribers.Remove(handler);
    }
}