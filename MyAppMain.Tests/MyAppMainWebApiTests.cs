using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;
using MyNotificationHub;
using MyWebApi;
using static MyAppMain.Tests.TestHelpers;
using NotificationHub = MyNotificationHub.MyNotificationHub;

namespace MyAppMain.Tests;

[TestClass]
public class MyAppMainWebApiTests
{
    /// <summary>
    /// Verifies single start request invokes the hub and returns success.
    /// </summary>
    [TestMethod]
    public async Task Start_Post_Invokes_OnStart_Delegate()
    {
        var tcs = NewTcs<MyNotificationHub.ModelResult>();
        var hub = new NotificationHub();
        hub.ResultPublished += result =>
        {
            if (result.Type == "start")
                tcs.TrySetResult(result);
        };
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
        var hub = new NotificationHub();
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
        var tcs = NewTcs<MyNotificationHub.ModelResult>();
        var hub = new NotificationHub();
        hub.ResultPublished += result =>
        {
            if (result.Type == "end")
                tcs.TrySetResult(result);
        };
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
}
