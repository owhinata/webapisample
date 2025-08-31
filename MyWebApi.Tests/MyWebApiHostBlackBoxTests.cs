using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyWebApi;

namespace MyWebApi.Tests;

[TestClass]
public class MyWebApiHostBlackBoxTests
{
    [TestMethod]
    public async Task StartHost_RespondsToStartAndEnd()
    {
        var port = GetFreeTcpPort();
        var host = new MyWebApiHost();

        try
        {
            host.Start(port);

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

            var r1 = await client.PostAsync("/start", new StringContent("", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, r1.StatusCode);
            var body1 = await r1.Content.ReadAsStringAsync();
            Assert.IsTrue(body1.Contains("started", StringComparison.OrdinalIgnoreCase), body1);

            var r2 = await client.PostAsync("/end", new StringContent("", Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.OK, r2.StatusCode);
            var body2 = await r2.Content.ReadAsStringAsync();
            Assert.IsTrue(body2.Contains("ended", StringComparison.OrdinalIgnoreCase), body2);
        }
        finally
        {
            host.Stop();
        }
    }

    [TestMethod]
    public void StartTwice_Throws()
    {
        var port = GetFreeTcpPort();
        var host = new MyWebApiHost();
        try
        {
            host.Start(port);
            Assert.ThrowsException<InvalidOperationException>(() => host.Start(port));
        }
        finally
        {
            host.Stop();
        }
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

