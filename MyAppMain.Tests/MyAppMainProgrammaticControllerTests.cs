using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyAppMain;

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
        var app = new global::MyAppMain.MyAppMain();
        var controller = new ProgrammaticImuController("ctrl1");
        app.RegisterController(controller);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var start = await controller.StartImuAsync("{}");
            Assert.AreEqual(ImuControlStatus.Success, start.Status);

            var stop = await controller.StopImuAsync();
            Assert.AreEqual(ImuControlStatus.Success, stop.Status);
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
        var app = new global::MyAppMain.MyAppMain();
        var owner = new ProgrammaticImuController("owner");
        var other = new ProgrammaticImuController("other");
        app.RegisterController(owner);
        app.RegisterController(other);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var startOwner = await owner.StartImuAsync("{}");
            Assert.AreEqual(ImuControlStatus.Success, startOwner.Status);

            var stopOther = await other.StopImuAsync();
            Assert.AreEqual(ImuControlStatus.OwnershipError, stopOther.Status);

            var startOther = await other.StartImuAsync("{}");
            Assert.AreEqual(ImuControlStatus.OwnershipError, startOther.Status);

            var stopOwner = await owner.StopImuAsync();
            Assert.AreEqual(ImuControlStatus.Success, stopOwner.Status);

            var startOtherAfterRelease = await other.StartImuAsync("{}");
            Assert.AreEqual(ImuControlStatus.Success, startOtherAfterRelease.Status);
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
        var app = new global::MyAppMain.MyAppMain();
        var owner = new ProgrammaticImuController("owner");
        var other = new ProgrammaticImuController("other");
        app.RegisterController(owner);
        app.RegisterController(other);

        try
        {
            Assert.IsTrue(await app.StartAsync());

            var startOwner = await owner.StartImuAsync("{}");
            Assert.AreEqual(ImuControlStatus.Success, startOwner.Status);

            Assert.IsTrue(app.UnregisterController(owner));

            var stopOther = await other.StopImuAsync();
            Assert.AreEqual(ImuControlStatus.Success, stopOther.Status);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
