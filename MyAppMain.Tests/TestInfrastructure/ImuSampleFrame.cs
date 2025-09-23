namespace MyAppMain.Tests;

/// <summary>
/// Represents a single IMU sample containing timestamped acceleration and gyro data.
/// </summary>
internal readonly struct ImuSampleFrame
{
    public ImuSampleFrame(
        ulong timestampMicroseconds,
        float accelX,
        float accelY,
        float accelZ,
        float gyroX,
        float gyroY,
        float gyroZ
    )
    {
        TimestampMicroseconds = timestampMicroseconds;
        AccelX = accelX;
        AccelY = accelY;
        AccelZ = accelZ;
        GyroX = gyroX;
        GyroY = gyroY;
        GyroZ = gyroZ;
    }

    public ulong TimestampMicroseconds { get; }

    public float AccelX { get; }

    public float AccelY { get; }

    public float AccelZ { get; }

    public float GyroX { get; }

    public float GyroY { get; }

    public float GyroZ { get; }
}
