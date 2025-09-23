using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MyAppMain.Tests;

/// <summary>
/// Shared async utilities and networking helpers for integration tests.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Waits for the task to complete or throws if the timeout elapses.
    /// </summary>
    public static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(
            task,
            Task.Delay(Timeout.InfiniteTimeSpan, cts.Token)
        );
        if (completed == task)
            return await task;
        throw new TimeoutException("Timed out waiting for delegate invocation");
    }

    /// <summary>
    /// Creates a TCS that runs continuations asynchronously to avoid deadlocks.
    /// </summary>
    public static TaskCompletionSource<T> NewTcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Binds a transient TCP listener to obtain an available ephemeral port.
    /// </summary>
    public static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
