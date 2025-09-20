using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MyAppMain;

/// <summary>
/// Encapsulates TCP connectivity and IMU data streaming for MyAppMain.
/// </summary>
internal sealed class ImuClient : IDisposable
{
    private const byte MsgImuState = 0x01;
    private const byte MsgImuData = 0x02;
    private const byte MsgSetImuState = 0x81;

    private readonly object _sync = new();
    private readonly MyAppNotificationHub.MyAppNotificationHub? _notificationHub;
    private TcpClient? _tcpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiverTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImuClient"/> class.
    /// </summary>
    /// <param name="notificationHub">
    /// Optional hub that receives IMU connection and data notifications.
    /// </param>
    public ImuClient(MyAppNotificationHub.MyAppNotificationHub? notificationHub)
    {
        _notificationHub = notificationHub;
    }

    /// <summary>
    /// Gets a value indicating whether a TCP connection to the IMU server is active.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _tcpClient?.Connected ?? false;
            }
        }
    }

    /// <summary>
    /// Connects to the specified IMU server and begins streaming data.
    /// </summary>
    /// <param name="address">Remote server address.</param>
    /// <param name="port">Remote server port.</param>
    public void Connect(string address, int port)
    {
        lock (_sync)
        {
            DisconnectInternal();

            try
            {
                var client = new TcpClient();
                client.Connect(address, port);
                _tcpClient = client;
                _cts = new CancellationTokenSource();
                _receiverTask = Task.Run(
                    () => ReceiveLoopAsync(client, _cts.Token),
                    CancellationToken.None
                );

                var endpoint = client.Client.RemoteEndPoint?.ToString();
                Console.WriteLine($"Connected to TCP server at {address}:{port}");
                _notificationHub?.NotifyImuConnected(
                    new MyAppNotificationHub.MyAppNotificationHub.ImuConnectionChangedDto(
                        true,
                        endpoint
                    )
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TCP connection failed: {ex.Message}");
                DisconnectInternal();
            }
        }
    }

    /// <summary>
    /// Terminates the IMU connection if active.
    /// </summary>
    public void Disconnect()
    {
        lock (_sync)
        {
            DisconnectInternal();
        }
    }

    /// <summary>
    /// Disposes the client and releases the underlying TCP connection.
    /// </summary>
    public void Dispose()
    {
        Disconnect();
    }

    private async Task ReceiveLoopAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            var header = new byte[5];
            while (!ct.IsCancellationRequested)
            {
                await ReadExactAsync(stream, header, 0, header.Length, ct);
                var id = header[0];
                var len = BitConverter.ToUInt32(header, 1);
                if (len > 1_000_000)
                    throw new InvalidOperationException("Invalid payload size");
                var payload = new byte[len];
                if (len > 0)
                    await ReadExactAsync(stream, payload, 0, (int)len, ct);

                await ProcessMessageAsync(stream, id, payload, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IMU loop error: {ex.Message}");
        }
    }

    private async Task ProcessMessageAsync(
        NetworkStream stream,
        byte messageId,
        byte[] payload,
        CancellationToken ct
    )
    {
        switch (messageId)
        {
            case MsgImuState:
                await HandleStateMessageAsync(stream, payload, ct);
                break;
            case MsgImuData when payload.Length == 32:
                HandleDataMessage(payload);
                break;
            default:
                break;
        }
    }

    private async Task HandleStateMessageAsync(
        NetworkStream stream,
        byte[] payload,
        CancellationToken ct
    )
    {
        var state = payload.Length > 0 ? payload[0] : (byte)0;
        var isOn = state == 1;
        _notificationHub?.NotifyImuStateUpdated(
            new MyAppNotificationHub.MyAppNotificationHub.ImuStateChangedDto(isOn)
        );
        if (!isOn)
        {
            await SendImuOnOffRequestAsync(stream, true, ct);
        }
    }

    private void HandleDataMessage(byte[] payload)
    {
        var ts = BitConverter.ToUInt64(payload, 0);
        var gx = BitConverter.ToSingle(payload, 8);
        var gy = BitConverter.ToSingle(payload, 12);
        var gz = BitConverter.ToSingle(payload, 16);
        var ax = BitConverter.ToSingle(payload, 20);
        var ay = BitConverter.ToSingle(payload, 24);
        var az = BitConverter.ToSingle(payload, 28);
        var dto = new MyAppNotificationHub.MyAppNotificationHub.ImuSampleDto(
            ts,
            new MyAppNotificationHub.MyAppNotificationHub.ImuVector3(gx, gy, gz),
            new MyAppNotificationHub.MyAppNotificationHub.ImuVector3(ax, ay, az)
        );
        _notificationHub?.NotifyImuSample(dto);
    }

    private void DisconnectInternal()
    {
        var client = _tcpClient;
        var cts = _cts;
        var task = _receiverTask;

        _tcpClient = null;
        _cts = null;
        _receiverTask = null;

        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch { }
            finally
            {
                cts.Dispose();
            }
        }

        if (task != null)
        {
            try
            {
                task.Wait(100);
            }
            catch { }
        }

        if (client != null)
        {
            try
            {
                client.Close();
            }
            catch { }
            finally
            {
                client.Dispose();
            }

            _notificationHub?.NotifyImuDisconnected(
                new MyAppNotificationHub.MyAppNotificationHub.ImuConnectionChangedDto(
                    false,
                    null
                )
            );
            Console.WriteLine("Disconnected from TCP server");
        }
    }

    private static async Task ReadExactAsync(
        NetworkStream stream,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken ct
    )
    {
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(
                buffer.AsMemory(offset + read, count - read),
                ct
            );
            if (n == 0)
                throw new IOException("Stream closed");
            read += n;
        }
    }

    private static async Task SendImuOnOffRequestAsync(
        NetworkStream stream,
        bool on,
        CancellationToken ct
    )
    {
        var header = new byte[5];
        header[0] = MsgSetImuState;
        BitConverter.TryWriteBytes(new Span<byte>(header, 1, 4), (uint)1);
        var payload = new byte[] { (byte)(on ? 1 : 0) };
        await stream.WriteAsync(header, 0, header.Length, ct);
        await stream.WriteAsync(payload, 0, payload.Length, ct);
        await stream.FlushAsync(ct);
    }
}
