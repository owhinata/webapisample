using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;
using MyNotificationHub;
using static MyAppMain.Tests.TestHelpers;
using NotificationHub = MyNotificationHub.MyNotificationHub;

namespace MyAppMain.Tests;

[TestClass]
public class MyAppMainProgrammaticControllerTests
{
    /// <summary>
    /// Ensures the programmatic controller can start and stop the IMU.
    /// </summary>
    [TestMethod]
    public async Task Programmatic_Controller_Starts_And_Stops_IMU()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var controller = new ProgrammaticImuController("ctrl1");
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
    public async Task Only_Owner_Can_Control_IMU()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var owner = new ProgrammaticImuController("owner");
        var other = new ProgrammaticImuController("other");
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
    public async Task Ownership_Clears_When_Owner_Unregistered()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var owner = new ProgrammaticImuController("owner");
        var other = new ProgrammaticImuController("other");
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
    public async Task Owner_Start_Twice_Returns_AlreadyRunning()
    {
        var hub = new NotificationHub();
        var app = new global::MyAppMain.MyAppMain(hub);
        var owner = new ProgrammaticImuController("owner");
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
