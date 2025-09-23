using System.Threading;
using System.Threading.Tasks;

namespace MyAppMain.Tests;

/// <summary>
/// Abstraction that allows IMU test scenarios to interact with the server transport.
/// </summary>
internal interface ITestImuConnection
{
    /// <summary>
    /// Sends a state notification frame to the connected client.
    /// </summary>
    ValueTask SendStateAsync(bool isOn, CancellationToken ct);

    /// <summary>
    /// Sends an IMU sample frame to the connected client.
    /// </summary>
    ValueTask SendSampleAsync(ImuSampleFrame frame, CancellationToken ct);
}
