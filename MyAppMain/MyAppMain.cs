using MyWebApi;

namespace MyAppMain;

public sealed class MyAppMain
{
    private readonly Func<string, Task> _onStart;
    private readonly Func<string, Task> _onEnd;
    private readonly MyWebApiHost _host = new();

    public MyAppMain(Func<string, Task> onStart, Func<string, Task> onEnd)
    {
        _onStart = onStart ?? (_ => Task.CompletedTask);
        _onEnd = onEnd ?? (_ => Task.CompletedTask);
    }

    public bool IsRunning => _host.IsRunning;

    public void Start(int port)
    {
        _host.StartRequested += _onStart;
        _host.EndRequested += _onEnd;
        _host.Start(port);
    }

    public void Stop()
    {
        _host.Stop();
        _host.StartRequested -= _onStart;
        _host.EndRequested -= _onEnd;
    }
}
