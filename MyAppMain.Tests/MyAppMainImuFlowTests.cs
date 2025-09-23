using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyNotificationHub;
using MyWebApi;
using static MyAppMain.Tests.TestHelpers;
using NotificationHub = MyNotificationHub.MyNotificationHub;

namespace MyAppMain.Tests;

internal sealed class ScriptedImuScenario : ITestImuScenario
{
    private bool _imuOn;

    public async Task OnClientConnectedAsync(
        ITestImuConnection connection,
        CancellationToken ct
    )
    {
        await connection.SendStateAsync(_imuOn, ct);
    }

    public async Task OnStateChangeRequestedAsync(
        ITestImuConnection connection,
        bool requestedOn,
        CancellationToken ct
    )
    {
        _imuOn = requestedOn;

        await connection.SendStateAsync(_imuOn, ct);
    }

    public async Task OnTickAsync(ITestImuConnection connection, CancellationToken ct)
    {
        if (!_imuOn)
            return;

        var timestamp =
            (ulong)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds
            * 1_000_000UL;
        var frame = new ImuSampleFrame(timestamp, 0.1f, 0.2f, 0.3f, 1.0f, 2.0f, 3.0f);
        await connection.SendSampleAsync(frame, ct);
    }
}

[TestClass]
public class MyAppMainImuFlowTests
{
    /// <summary>
    /// Validates full IMU startup flow reaches connected, ON, and sample events.
    /// </summary>
    [TestMethod]
    public async Task Start_Message_With_Server_Info_Connects_To_TCP_Server()
    {
        // Start test IMU server
        using var testServer = new TestImuServer(() => new ScriptedImuScenario());
        var serverPort = testServer.Start();

        // Create hub and event waiters
        var hub = new NotificationHub();
        var startDone = NewTcs<bool>();
        var connected = NewTcs<bool>();
        var imuOnObserved = false;
        var imuOn = NewTcs<bool>();
        var imuSample = NewTcs<bool>();
        var sampleBeforeOn = NewTcs<bool>();
        var stateNotifications = new List<bool>();
        hub.ResultPublished += res =>
        {
            if (res.Type == "start" && res.Success)
                startDone.TrySetResult(true);
        };
        hub.ImuConnected += _ => connected.TrySetResult(true);
        hub.ImuStateUpdated += dto =>
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
        hub.ImuSampleReceived += _ =>
        {
            if (!imuOnObserved)
            {
                // Guard: samples must not precede an ON state.
                sampleBeforeOn.TrySetResult(true);
                return;
            }

            imuSample.TrySetResult(true);
        };
        var app = new global::MyAppMain.MyAppMain(hub);
        var apiPort = GetFreeTcpPort();
        app.RegisterController(
            new WebApiControllerAdapter(new MyWebApiHost(apiPort))
        );

        try
        {
            // Arrange API client and point to the synthetic IMU server.
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
