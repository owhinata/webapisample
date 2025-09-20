using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;
using MyAppNotificationHub;
using MyWebApi;

namespace MyAppMain.Tests;

[TestClass]
public class MyAppMainBlackBoxTests
{
    /// <summary>
    /// Verifies single start request invokes the hub and returns success.
    /// </summary>
    [TestMethod]
    public async Task Start_Post_Invokes_OnStart_Delegate()
    {
        var tcs = NewTcs<MyAppNotificationHub.ModelResult>();
        var hub = new MyAppNotificationHub.MyAppNotificationHub();
        hub.StartCompleted += result => tcs.TrySetResult(result);
        var app = new global::MyAppMain.MyAppMain(hub);
        var port = GetFreeTcpPort();
        app.RegisterController(new WebApiControllerAdapter(new MyWebApiHost(port)));

        try
        {
            // Arrange HTTP client against the hosted Web API.
            app.Start();
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
            };
            var body = "{\"message\":\"hello\"}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            // Act
            var res = await client.PostAsync("/v1/start", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);

            // Assert hub callback fires with success metadata.
            var received = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(3));
            Assert.AreEqual("start", received.Type);
            Assert.IsTrue(received.Success);
        }
        finally
        {
            app.Stop();
        }
    }

    /// <summary>
    /// Ensures parallel start calls yield one 200 and one 429 via rate limiting.
    /// </summary>
    [TestMethod]
    public async Task Concurrent_Start_Requests_Yield_One_200_And_One_429()
    {
        var hub = new MyAppNotificationHub.MyAppNotificationHub();
        var firstRequestEntered = NewTcs<bool>();
        var releaseFirstRequest = NewTcs<bool>();
        var gateFlag = 0;
        var host = new MyWebApiHost(GetFreeTcpPort());
        host.StartRequested += _ =>
        {
            if (Interlocked.CompareExchange(ref gateFlag, 1, 0) == 0)
            {
                firstRequestEntered.TrySetResult(true);
                releaseFirstRequest.Task.GetAwaiter().GetResult();
            }
        };
        var app = new global::MyAppMain.MyAppMain(hub);
        app.RegisterController(new WebApiControllerAdapter(host));

        try
        {
            // Spin up Web API and two clients to issue concurrent requests.
            app.Start();
            using var client1 = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{host.Port}"),
            };
            using var client2 = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{host.Port}"),
            };
            var body1 = "{\"message\":\"r1\"}";
            var body2 = "{\"message\":\"r2\"}";
            var t1 = client1.PostAsync(
                "/v1/start",
                new StringContent(body1, Encoding.UTF8, "application/json")
            );
            await WaitAsync(firstRequestEntered.Task, TimeSpan.FromSeconds(1));

            // Second request should be throttled while first is active.
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

    /// <summary>
    /// Verifies end request triggers hub notification and returns success.
    /// </summary>
    [TestMethod]
    public async Task End_Post_Invokes_OnEnd_Delegate()
    {
        var tcs = NewTcs<MyAppNotificationHub.ModelResult>();
        var hub = new MyAppNotificationHub.MyAppNotificationHub();
        hub.EndCompleted += result => tcs.TrySetResult(result);
        var app = new global::MyAppMain.MyAppMain(hub);
        var port = GetFreeTcpPort();
        app.RegisterController(new WebApiControllerAdapter(new MyWebApiHost(port)));

        try
        {
            app.Start();
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
            };
            var body = "{\"message\":\"bye\"}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            // Act
            var res = await client.PostAsync("/v1/end", content);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);

            // Assert end notification reaches the hub.
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

    private static TaskCompletionSource<T> NewTcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Validates full IMU startup flow reaches connected, ON, and sample events.
    /// </summary>
    [TestMethod]
    public async Task Start_Message_With_Server_Info_Connects_To_TCP_Server()
    {
        // Start test IMU server
        using var testServer = new TestImuServer();
        var serverPort = testServer.Start();

        // Create hub and event waiters
        var hub2 = new MyAppNotificationHub.MyAppNotificationHub();
        var startDone = NewTcs<bool>();
        var connected = NewTcs<bool>();
        var imuOnObserved = false;
        var imuOn = NewTcs<bool>();
        var imuSample = NewTcs<bool>();
        var sampleBeforeOn = NewTcs<bool>();
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
                // Guard: samples must not precede an ON state.
                sampleBeforeOn.TrySetResult(true);
                return;
            }

            imuSample.TrySetResult(true);
        };
        var app = new global::MyAppMain.MyAppMain(hub2);
        var apiPort = GetFreeTcpPort();
        app.RegisterController(
            new WebApiControllerAdapter(new MyWebApiHost(apiPort))
        );

        try
        {
            // Arrange API client and point to the synthetic IMU server。
            app.Start();
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
