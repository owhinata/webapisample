using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;
using MyWebApi;

namespace MyAppMain.Tests;

[TestClass]
public class MyAppMainBlackBoxTests
{
    [TestMethod]
    public async Task Start_Post_Invokes_OnStart_Delegate()
    {
        var tcs = new TaskCompletionSource<StartCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = new global::MyAppMain.MyAppMain(cmd => { tcs.TrySetResult(cmd); return Task.CompletedTask; }, _ => Task.CompletedTask);
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var content = new StringContent("{\"message\":\"hello\"}", Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/start", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            Assert.AreEqual("hello", received.Message);
        }
        finally
        {
            app.Stop();
        }
    }

    [TestMethod]
    public async Task End_Post_Invokes_OnEnd_Delegate()
    {
        var tcs = new TaskCompletionSource<EndCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = new global::MyAppMain.MyAppMain(_ => Task.CompletedTask, cmd => { tcs.TrySetResult(cmd); return Task.CompletedTask; });
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var content = new StringContent("{\"message\":\"bye\"}", Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/end", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            Assert.AreEqual("bye", received.Message);
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
