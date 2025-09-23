using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;
using MyNotificationHub;
using static MyAppMain.Tests.TestHelpers;
using NotificationHub = MyNotificationHub.MyNotificationHub;

namespace MyAppMain.Tests;

[TestClass]
public class MyAppMainDirectApiControllerTests
{
    /// <summary>
    /// Ensures the direct API controller can start and stop the IMU.
    /// </summary>
    [TestMethod]
    public async Task Direct_Controller_Starts_And_Stops_IMU()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var controller = new DirectApiController("ctrl1");
        app.RegisterController(controller);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var startResultTask = WaitForResultAsync(hub, controller.Id, "start");
            Assert.IsTrue(await controller.StartImuAsync("{}"));
            var startResult = await WaitAsync(
                startResultTask,
                TimeSpan.FromSeconds(3)
            );
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(startResult));

            var stopResultTask = WaitForResultAsync(hub, controller.Id, "end");
            Assert.IsTrue(await controller.StopImuAsync());
            var stopResult = await WaitAsync(stopResultTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(stopResult));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// Validates that only the owning controller can stop or restart the IMU.
    /// </summary>
    [TestMethod]
    public async Task Direct_Controller_Respects_Ownership()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var owner = new DirectApiController("owner");
        var other = new DirectApiController("other");
        app.RegisterController(owner);
        app.RegisterController(other);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var ownerStartTask = WaitForResultAsync(hub, owner.Id, "start");
            Assert.IsTrue(await owner.StartImuAsync("{}"));
            var ownerStart = await WaitAsync(ownerStartTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(ownerStart));

            var otherStopTask = WaitForResultAsync(hub, other.Id, "end");
            Assert.IsTrue(await other.StopImuAsync());
            var otherStop = await WaitAsync(otherStopTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.OwnershipError, GetStatus(otherStop));

            var otherStartTask = WaitForResultAsync(hub, other.Id, "start");
            Assert.IsTrue(await other.StartImuAsync("{}"));
            var otherStart = await WaitAsync(otherStartTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.OwnershipError, GetStatus(otherStart));

            var ownerStopTask = WaitForResultAsync(hub, owner.Id, "end");
            Assert.IsTrue(await owner.StopImuAsync());
            var ownerStop = await WaitAsync(ownerStopTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(ownerStop));

            var otherStartAfterReleaseTask = WaitForResultAsync(
                hub,
                other.Id,
                "start"
            );
            Assert.IsTrue(await other.StartImuAsync("{}"));
            var otherStartAfterRelease = await WaitAsync(
                otherStartAfterReleaseTask,
                TimeSpan.FromSeconds(3)
            );
            Assert.AreEqual(
                ImuControlStatus.Success,
                GetStatus(otherStartAfterRelease)
            );
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// Confirms ownership is cleared when the owning controller is unregistered.
    /// </summary>
    [TestMethod]
    public async Task Ownership_Clears_When_Direct_Controller_Unregistered()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var owner = new DirectApiController("owner");
        var other = new DirectApiController("other");
        app.RegisterController(owner);
        app.RegisterController(other);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var ownerStartTask = WaitForResultAsync(hub, owner.Id, "start");
            Assert.IsTrue(await owner.StartImuAsync("{}"));
            var ownerStart = await WaitAsync(ownerStartTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(ownerStart));

            Assert.IsTrue(app.UnregisterController(owner));

            var otherStopTask = WaitForResultAsync(hub, other.Id, "end");
            Assert.IsTrue(await other.StopImuAsync());
            var otherStop = await WaitAsync(otherStopTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(otherStop));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// Returns AlreadyRunning when the owning controller issues Start again.
    /// </summary>
    [TestMethod]
    public async Task Direct_Controller_Start_Twice_Returns_AlreadyRunning()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var owner = new DirectApiController("owner");
        app.RegisterController(owner);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var firstTask = WaitForResultAsync(hub, owner.Id, "start");
            Assert.IsTrue(await owner.StartImuAsync("{}"));
            var first = await WaitAsync(firstTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(first));

            var secondTask = WaitForResultAsync(hub, owner.Id, "start");
            Assert.IsTrue(await owner.StartImuAsync("{}"));
            var second = await WaitAsync(secondTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.AlreadyRunning, GetStatus(second));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// Ensures synchronous control helpers enqueue commands successfully.
    /// </summary>
    [TestMethod]
    public async Task Direct_Controller_Sync_Methods_Work()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var controller = new DirectApiController("ctrl-sync");
        app.RegisterController(controller);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var startTask = WaitForResultAsync(hub, controller.Id, "start");
            Assert.IsTrue(controller.StartImu("{}"));
            var start = await WaitAsync(startTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(start));

            var stopTask = WaitForResultAsync(hub, controller.Id, "end");
            Assert.IsTrue(controller.StopImu());
            var stop = await WaitAsync(stopTask, TimeSpan.FromSeconds(3));
            Assert.AreEqual(ImuControlStatus.Success, GetStatus(stop));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static Task<ModelResult> WaitForResultAsync(
        NotificationHub hub,
        string controllerId,
        string type
    )
    {
        var tcs = NewTcs<ModelResult>();

        void Handler(ModelResult result)
        {
            if (result.ControllerId == controllerId && result.Type == type)
            {
                hub.ResultPublished -= Handler;
                tcs.TrySetResult(result);
            }
        }

        hub.ResultPublished += Handler;
        return tcs.Task;
    }

    private static ImuControlStatus GetStatus(ModelResult result)
    {
        Assert.IsNotNull(result.Payload, "Expected IMU payload to be present.");
        var statusProperty = result.Payload?.GetType().GetProperty("Status");
        Assert.IsNotNull(
            statusProperty,
            "Expected payload to expose Status property."
        );
        return (ImuControlStatus)(statusProperty?.GetValue(result.Payload) ?? 0);
    }
}
