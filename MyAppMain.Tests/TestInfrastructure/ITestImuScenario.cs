using System.Threading;
using System.Threading.Tasks;

namespace MyAppMain.Tests;

/// <summary>
/// Defines a pluggable script that drives the synthetic IMU server during tests.
/// </summary>
internal interface ITestImuScenario
{
    /// <summary>
    /// Called once a TCP client connection has been established.
    /// </summary>
    Task OnClientConnectedAsync(ITestImuConnection connection, CancellationToken ct);

    /// <summary>
    /// Triggered for each set-state request coming from the application under test.
    /// </summary>
    Task OnStateChangeRequestedAsync(
        ITestImuConnection connection,
        bool requestedOn,
        CancellationToken ct
    );

    /// <summary>
    /// Invoked on every server tick so scenarios can emit samples or perform work.
    /// </summary>
    Task OnTickAsync(ITestImuConnection connection, CancellationToken ct);
}
