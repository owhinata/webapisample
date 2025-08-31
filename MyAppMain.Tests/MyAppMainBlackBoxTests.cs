using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;
using IfUtilityLib;

namespace MyAppMain.Tests;

[TestClass]
public class MyAppMainBlackBoxTests
{
    [TestMethod]
    public async Task Start_Post_Invokes_OnStart_Delegate()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var util = new TestIfUtility(tcs, null);
        var app = new global::MyAppMain.MyAppMain(util);
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var content = new StringContent("{\"message\":\"hello\"}", Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/start", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            StringAssert.Contains(received, "\"hello\"");
        }
        finally
        {
            app.Stop();
        }
    }

    [TestMethod]
    public async Task End_Post_Invokes_OnEnd_Delegate()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var util = new TestIfUtility(null, tcs);
        var app = new global::MyAppMain.MyAppMain(util);
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var content = new StringContent("{\"message\":\"bye\"}", Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/end", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            StringAssert.Contains(received, "\"bye\"");
        }
        finally
        {
            app.Stop();
        }
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (completed == task) return await task;
        throw new TimeoutException("Timed out waiting for delegate invocation");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

file static class TcsExtensions { public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout) { using var cts = new CancellationTokenSource(timeout); var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token)); if (completed == task) return await task; throw new TimeoutException(); } }

class TestIfUtility : IfUtility
{
    private readonly TaskCompletionSource<string>? _startTcs;
    private readonly TaskCompletionSource<string>? _endTcs;
    public TestIfUtility(TaskCompletionSource<string>? startTcs, TaskCompletionSource<string>? endTcs)
    { _startTcs = startTcs; _endTcs = endTcs; }
    public override void HandleStart(string json) { _startTcs?.TrySetResult(json); }
    public override void HandleEnd(string json) { _endTcs?.TrySetResult(json); }
}
