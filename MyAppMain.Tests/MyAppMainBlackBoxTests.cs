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
            var body = "{\"message\":\"hello\"}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/start", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            Assert.AreEqual(body, received, "IfUtility.HandleStart argument mismatch");
            Assert.AreEqual(1, util.StartCallCount, "HandleStart should be called once");
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
            var body = "{\"message\":\"bye\"}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/end", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            Assert.AreEqual(body, received, "IfUtility.HandleEnd argument mismatch");
            Assert.AreEqual(1, util.EndCallCount, "HandleEnd should be called once");
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
    public int StartCallCount { get; private set; }
    public int EndCallCount { get; private set; }
    public string? LastStartArg { get; private set; }
    public string? LastEndArg { get; private set; }
    public TestIfUtility(TaskCompletionSource<string>? startTcs, TaskCompletionSource<string>? endTcs)
    { _startTcs = startTcs; _endTcs = endTcs; }
    public override void HandleStart(string json)
    {
        StartCallCount++;
        LastStartArg = json;
        _startTcs?.TrySetResult(json);
    }
    public override void HandleEnd(string json)
    {
        EndCallCount++;
        LastEndArg = json;
        _endTcs?.TrySetResult(json);
    }
}
