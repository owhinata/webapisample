namespace MyAppMain;

public interface IAppController
{
    string Id { get; }
    event Action<MyAppNotificationHub.ModelCommand>? CommandRequested;
    Task<bool> StartAsync(CancellationToken ct = default);
    Task<bool> StopAsync(CancellationToken ct = default);
}
