using MyWebApi;

namespace MyAppMain;

public sealed class MyAppMain : IDisposable
{
    private readonly Func<string, Task> _onStart;
    private readonly Func<string, Task> _onEnd;
    private readonly MyWebApiHost _host = new();
    private bool _disposed;

    public MyAppMain(Func<string, Task> onStart, Func<string, Task> onEnd)
    {
        _onStart = onStart ?? (_ => Task.CompletedTask);
        _onEnd = onEnd ?? (_ => Task.CompletedTask);
        // Subscribe at construction time to avoid missing early POSTs right after Start.
        _host.StartRequested += _onStart;
        _host.EndRequested += _onEnd;
    }

    public bool IsRunning => _host.IsRunning;

    public void Start(int port) => _host.Start(port);

    public void Stop() => _host.Stop();

    public void Dispose()
    {
        if (_disposed) return;
        _host.StartRequested -= _onStart;
        _host.EndRequested -= _onEnd;
        _disposed = true;
    }
}
