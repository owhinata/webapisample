using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;
using MyAppNotificationHub;

namespace MyAppMain.Tests;

[TestClass]
public class MyAppMainBlackBoxTests
{
    [TestMethod]
    public async Task Start_Post_Invokes_OnStart_Delegate()
    {
        var tcs = new TaskCompletionSource<MyAppNotificationHub.ModelResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var hub = new MyAppNotificationHub.MyAppNotificationHub();
        hub.StartCompleted += result => tcs.TrySetResult(result);
        var app = new global::MyAppMain.MyAppMain(hub);
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
            };
            var body = "{\"message\":\"hello\"}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/start", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            Assert.AreEqual("start", received.Type);
            Assert.IsTrue(received.Success);
        }
        finally
        {
            app.Stop();
        }
    }

    [TestMethod]
    public async Task Concurrent_Start_Requests_Yield_One_200_And_One_429()
    {
        var hub = new MyAppNotificationHub.MyAppNotificationHub();
        var firstRequestEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseFirstRequest = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var gateFlag = 0;
        var app = new global::MyAppMain.MyAppMain(hub);
        var hostField = typeof(global::MyAppMain.MyAppMain).GetField(
            "_host",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        var host = hostField?.GetValue(app) as MyWebApi.MyWebApiHost;
        Assert.IsNotNull(host, "Failed to access MyWebApiHost instance");
        host!.StartRequested += _ =>
        {
            if (Interlocked.CompareExchange(ref gateFlag, 1, 0) == 0)
            {
                firstRequestEntered.TrySetResult(true);
                releaseFirstRequest.Task.GetAwaiter().GetResult();
            }
        };
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client1 = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
            };
            using var client2 = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
            };
            var body1 = "{\"message\":\"r1\"}";
            var body2 = "{\"message\":\"r2\"}";
            var t1 = client1.PostAsync(
                "/v1/start",
                new StringContent(body1, Encoding.UTF8, "application/json")
            );
            await WaitAsync(firstRequestEntered.Task, TimeSpan.FromSeconds(1));

            var t2 = client2.PostAsync(
                "/v1/start",
                new StringContent(body2, Encoding.UTF8, "application/json")
            );

            var completed = await Task.WhenAny(
                t2,
                Task.Delay(TimeSpan.FromMilliseconds(200))
            );
            if (completed != t2)
            {
                releaseFirstRequest.TrySetResult(true);
                Assert.Fail(
                    "Second request did not complete while the first was in-flight"
                );
            }

            releaseFirstRequest.TrySetResult(true);

            var responses = await Task.WhenAll(t1, t2);
            var codes = responses.Select(r => r.StatusCode).ToArray();
            Assert.IsTrue(
                codes.Contains(HttpStatusCode.OK),
                "One request should succeed"
            );
            Assert.IsTrue(
                codes.Contains((HttpStatusCode)429),
                "One request should be rejected with 429"
            );
        }
        finally
        {
            releaseFirstRequest.TrySetResult(true);
            app.Stop();
        }
    }

    [TestMethod]
    public async Task End_Post_Invokes_OnEnd_Delegate()
    {
        var tcs = new TaskCompletionSource<MyAppNotificationHub.ModelResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var hub = new MyAppNotificationHub.MyAppNotificationHub();
        hub.EndCompleted += result => tcs.TrySetResult(result);
        var app = new global::MyAppMain.MyAppMain(hub);
        var port = GetFreeTcpPort();

        try
        {
            app.Start(port);
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
            };
            var body = "{\"message\":\"bye\"}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var res = await client.PostAsync("/v1/end", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            Assert.AreEqual("end", received.Type);
            Assert.IsTrue(received.Success);
        }
        finally
        {
            app.Stop();
        }
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout)
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
        // Start test IMU server
        using var testServer = new TestImuServer();
        var serverPort = testServer.Start();

        // Create hub and event waiters
        var hub2 = new MyAppNotificationHub.MyAppNotificationHub();
        var startDone = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var connected = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var imuOnObserved = false;
        var imuOn = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var imuSample = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var sampleBeforeOn = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var stateNotifications = new List<bool>();
        hub2.StartCompleted += res =>
        {
            if (res.Success)
                startDone.TrySetResult(true);
        };
        hub2.ImuConnected += _ => connected.TrySetResult(true);
        hub2.ImuStateUpdated += dto =>
        {
            lock (stateNotifications)
            {
                stateNotifications.Add(dto.IsOn);
            }

            if (dto.IsOn)
            {
                imuOnObserved = true;
                imuOn.TrySetResult(true);
            }
        };
        hub2.ImuSampleReceived += _ =>
        {
            if (!imuOnObserved)
            {
                sampleBeforeOn.TrySetResult(true);
                return;
            }

            imuSample.TrySetResult(true);
        };
        var app = new global::MyAppMain.MyAppMain(hub2);
        var apiPort = GetFreeTcpPort();

        try
        {
            app.Start(apiPort);
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{apiPort}"),
            };

            // Send server info in JSON
            var serverInfo = new { address = "127.0.0.1", port = serverPort };
            var json = JsonSerializer.Serialize(serverInfo);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var res = await client.PostAsync("/v1/start", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);

            // Wait for IMU flow: start -> connected -> ON -> sample
            await WaitAsync(startDone.Task, TimeSpan.FromSeconds(3));
            await WaitAsync(connected.Task, TimeSpan.FromSeconds(3));
            var onOrFailure = await Task.WhenAny(
                imuOn.Task,
                sampleBeforeOn.Task,
                Task.Delay(TimeSpan.FromSeconds(3))
            );
            if (onOrFailure == sampleBeforeOn.Task)
            {
                Assert.Fail("Received IMU sample before ON state notification");
            }
            else if (onOrFailure != imuOn.Task)
            {
                Assert.Fail("Timed out waiting for IMU ON notification");
            }

            var sampleResult = await Task.WhenAny(
                imuSample.Task,
                sampleBeforeOn.Task,
                Task.Delay(TimeSpan.FromSeconds(3))
            );
            if (sampleResult == sampleBeforeOn.Task)
            {
                Assert.Fail("Received IMU sample before ON state notification");
            }
            else if (sampleResult != imuSample.Task)
            {
                Assert.Fail("Timed out waiting for IMU sample after ON notification");
            }

            lock (stateNotifications)
            {
                Assert.IsTrue(
                    stateNotifications.Count > 0,
                    "Expected at least one IMU state notification"
                );
            }
        }
        finally
        {
            app.Stop();
        }
    }
}

// Subscriber helper no longer needed under result-driven notifications

class TestImuServer : IDisposable
{
    private TcpListener? _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly List<NetworkStream> _streams = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _broadcaster;
    private volatile bool _imuOn;

    public int Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        _broadcaster = Task.Run(() => BroadcastLoopAsync(_cts.Token));
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        if (_listener == null)
            throw new InvalidOperationException("Server not started");
        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(ct);
            lock (_clients)
            {
                _clients.Add(client);
                _streams.Add(client.GetStream());
            }
            await SendImuStateAsync(
                client.GetStream(),
                _imuOn ? (byte)1 : (byte)0,
                ct
            );
            _ = Task.Run(() => ClientRecvLoopAsync(client, ct));
        }
    }

    private async Task ClientRecvLoopAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var s = client.GetStream();
            var header = new byte[5];
            while (!ct.IsCancellationRequested)
            {
                await ReadExactAsync(s, header, 0, 5, ct);
                var id = header[0];
                var len = BitConverter.ToUInt32(header, 1);
                var payload = new byte[len];
                if (len > 0)
                    await ReadExactAsync(s, payload, 0, (int)len, ct);
                if (id == 0x81)
                {
                    var newOn = payload[0] == 1;
                    if (newOn != _imuOn)
                    {
                        _imuOn = newOn;
                        await BroadcastStateAsync(ct);
                    }
                }
            }
        }
        catch { }
    }

    private async Task BroadcastStateAsync(CancellationToken ct)
    {
        List<NetworkStream> streams;
        lock (_clients)
        {
            streams = _streams.ToList();
        }
        foreach (var s in streams)
        {
            try
            {
                await SendImuStateAsync(s, _imuOn ? (byte)1 : (byte)0, ct);
            }
            catch { }
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken ct)
    {
        var buf = new byte[5 + 32];
        while (!ct.IsCancellationRequested)
        {
            if (_imuOn)
            {
                buf[0] = 0x02; // IMU_DATA
                BitConverter.TryWriteBytes(new Span<byte>(buf, 1, 4), (uint)32);
                var ts =
                    (ulong)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds
                    * 1_000_000UL;
                BitConverter.TryWriteBytes(new Span<byte>(buf, 5, 8), ts);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 13, 4), 0.1f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 17, 4), 0.2f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 21, 4), 0.3f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 25, 4), 1.0f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 29, 4), 2.0f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 33, 4), 3.0f);
                List<NetworkStream> streams;
                lock (_clients)
                {
                    streams = _streams.ToList();
                }
                foreach (var s in streams)
                {
                    try
                    {
                        await s.WriteAsync(buf, 0, buf.Length, ct);
                        await s.FlushAsync(ct);
                    }
                    catch { }
                }
            }
            await Task.Delay(10, ct); // ~100Hz
        }
    }

    private static async Task SendImuStateAsync(
        NetworkStream s,
        byte state,
        CancellationToken ct
    )
    {
        byte[] header = new byte[5];
        header[0] = 0x01;
        BitConverter.TryWriteBytes(new Span<byte>(header, 1, 4), (uint)1);
        await s.WriteAsync(header, 0, 5, ct);
        await s.WriteAsync(new[] { state }, 0, 1, ct);
        await s.FlushAsync(ct);
    }

    private static async Task ReadExactAsync(
        NetworkStream s,
        byte[] buf,
        int ofs,
        int count,
        CancellationToken ct
    )
    {
        var read = 0;
        while (read < count)
        {
            var n = await s.ReadAsync(buf.AsMemory(ofs + read, count - read), ct);
            if (n == 0)
                throw new IOException("closed");
            read += n;
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch { }
        try
        {
            _broadcaster?.Wait(100);
        }
        catch { }
        lock (_clients)
        {
            foreach (var s in _streams)
            {
                try
                {
                    s.Close();
                }
                catch { }
            }
            foreach (var c in _clients)
            {
                try
                {
                    c.Close();
                }
                catch { }
            }
            _streams.Clear();
            _clients.Clear();
        }
        try
        {
            _listener?.Stop();
        }
        catch { }
    }
}
