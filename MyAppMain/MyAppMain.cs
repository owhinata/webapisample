using MyWebApi;
using IfUtilityLib;

namespace MyAppMain;

public sealed class MyAppMain : IDisposable
{
    private readonly MyWebApiHost _host = new();
    private readonly IfUtility _utility;
    private bool _disposed;

    public MyAppMain(IfUtility? utility = null)
    {
        _utility = utility ?? new IfUtility();
        // Subscribe at construction time to avoid missing early POSTs right after Start.
        _host.StartRequested += OnStartMessageReceived;
        _host.EndRequested += OnEndMessageReceived;
    }

    public bool IsRunning => _host.IsRunning;

    public void Start(int port) => _host.Start(port);

    public void Stop() => _host.Stop();

    // Event handlers wired to MyWebApiHost events
    public void OnStartMessageReceived(string json) => _utility.HandleStart(json);

    public void OnEndMessageReceived(string json) => _utility.HandleEnd(json);

    public void Dispose()
    {
        if (_disposed) return;
        _host.StartRequested -= OnStartMessageReceived;
        _host.EndRequested -= OnEndMessageReceived;
        _disposed = true;
    }
}
