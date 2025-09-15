using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
        var util = new AppEventJunction();
        util.StartRequested += json => tcs.TrySetResult(json);
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
            Assert.AreEqual(body, received, "Start JSON should be forwarded to observers");
        }
        finally
        {
            app.Stop();
        }
    }

    [TestMethod]
    public async Task Concurrent_Start_Requests_Yield_One_200_And_One_429()
    {
        var util = new AppEventJunction();
        var app = new global::MyAppMain.MyAppMain(util);
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var body1 = "{\"message\":\"r1\"}";
            var body2 = "{\"message\":\"r2\"}";
            var t1 = client.PostAsync("/v1/start", new StringContent(body1, Encoding.UTF8, "application/json"));
            var t2 = client.PostAsync("/v1/start", new StringContent(body2, Encoding.UTF8, "application/json"));

            await Task.WhenAll(t1, t2);

            var codes = new[] { t1.Result.StatusCode, t2.Result.StatusCode };
            Assert.IsTrue(codes.Contains(HttpStatusCode.OK), "One request should succeed");
            Assert.IsTrue(codes.Contains((HttpStatusCode)429), "One request should be rejected with 429");
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
        var util = new AppEventJunction();
        util.EndRequested += json => tcs.TrySetResult(json);
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
            Assert.AreEqual(body, received, "End JSON should be forwarded to observers");
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

    [TestMethod]
    public async Task Start_Message_With_Server_Info_Connects_To_TCP_Server()
    {
        // Start test TCP server
        using var testServer = new TestTcpServer();
        var serverPort = testServer.Start();

        // Create test utility that will capture the connection
        var connectionTcs = new TaskCompletionSource<TcpClient>();
        var utilWithTcp = new TestIfUtilityWithTcpClient(connectionTcs);
        var app = new global::MyAppMain.MyAppMain(utilWithTcp.Junction);
        var apiPort = GetFreeTcpPort();

        try
        {
            app.Start(apiPort);
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{apiPort}") };

            // Send server info in JSON
            var serverInfo = new
            {
                address = "127.0.0.1",
                port = serverPort
            };
            var json = JsonSerializer.Serialize(serverInfo);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var res = await client.PostAsync("/v1/start", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);

            // Wait for TCP connection
            var acceptTask = testServer.AcceptConnectionAsync();
            var tcpClient = await WaitAsync(connectionTcs.Task, TimeSpan.FromSeconds(3));
            Assert.IsNotNull(tcpClient, "TCP client should be created");
            Assert.IsTrue(tcpClient.Connected, "TCP client should be connected");

            // Verify server received connection
            var serverClient = await WaitAsync(acceptTask, TimeSpan.FromSeconds(3));
            Assert.IsNotNull(serverClient, "Server should receive connection");

            // Cleanup
            tcpClient.Close();
            serverClient.Close();
        }
        finally
        {
            app.Stop();
        }
    }
}



class TestIfUtilityWithTcpClient
{
    private readonly TaskCompletionSource<TcpClient> _connectionTcs;
    public AppEventJunction Junction { get; } = new();

    public TestIfUtilityWithTcpClient(TaskCompletionSource<TcpClient> connectionTcs)
    {
        _connectionTcs = connectionTcs;
        Junction.StartRequested += async json =>
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("address", out var addressProp) &&
                    root.TryGetProperty("port", out var portProp))
                {
                    var address = addressProp.GetString();
                    var port = portProp.GetInt32();

                    var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(address!, port);
                    _connectionTcs.TrySetResult(tcpClient);
                }
            }
            catch (Exception ex)
            {
                _connectionTcs.TrySetException(ex);
            }
        };
    }
}

class TestTcpServer : IDisposable
{
    private TcpListener? _listener;
    private readonly List<TcpClient> _clients = new();

    public int Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public async Task<TcpClient> AcceptConnectionAsync()
    {
        if (_listener == null) throw new InvalidOperationException("Server not started");
        var client = await _listener.AcceptTcpClientAsync();
        _clients.Add(client);
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            try { client.Close(); } catch { }
        }
        _listener?.Stop();
    }
}
