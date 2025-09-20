using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MyAppMain.Tests;

/// <summary>
/// Lightweight TCP server used by integration tests to simulate an IMU device.
/// Exposes start/stop helpers and publishes state/data frames in the same
/// format that the main application expects.
/// </summary>
internal sealed class TestImuServer : IDisposable
{
    private const byte MsgImuState = 0x01;
    private const byte MsgImuData = 0x02;
    private const byte MsgSetImuState = 0x81;

    private TcpListener? _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly List<NetworkStream> _streams = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _broadcaster;
    private volatile bool _imuOn;

    public int Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
        _broadcaster = Task.Run(() => BroadcastLoopAsync(_cts.Token));
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        if (_listener == null)
            throw new InvalidOperationException("Server not started");

        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(ct);
            lock (_clients)
            {
                _clients.Add(client);
                _streams.Add(client.GetStream());
            }

            await SendImuStateAsync(
                client.GetStream(),
                _imuOn ? (byte)1 : (byte)0,
                ct
            );
            _ = Task.Run(() => ClientRecvLoopAsync(client, ct));
        }
    }

    private async Task ClientRecvLoopAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var s = client.GetStream();
            var header = new byte[5];
            while (!ct.IsCancellationRequested)
            {
                await ReadExactAsync(s, header, 0, header.Length, ct);
                var id = header[0];
                var len = BitConverter.ToUInt32(header, 1);
                var payload = new byte[len];
                if (len > 0)
                    await ReadExactAsync(s, payload, 0, (int)len, ct);

                if (id == MsgSetImuState)
                {
                    var newOn = payload[0] == 1;
                    if (newOn != _imuOn)
                    {
                        _imuOn = newOn;
                        await BroadcastStateAsync(ct);
                    }
                }
            }
        }
        catch
        {
            // Ignore socket errors/closures during shutdown.
        }
    }

    private async Task BroadcastStateAsync(CancellationToken ct)
    {
        List<NetworkStream> streams;
        lock (_clients)
        {
            streams = _streams.ToList();
        }

        foreach (var stream in streams)
        {
            try
            {
                await SendImuStateAsync(stream, _imuOn ? (byte)1 : (byte)0, ct);
            }
            catch
            {
                // Ignore broadcast errors; individual client loops handle cleanup.
            }
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken ct)
    {
        var buf = new byte[5 + 32];
        while (!ct.IsCancellationRequested)
        {
            if (_imuOn)
            {
                buf[0] = MsgImuData;
                BitConverter.TryWriteBytes(new Span<byte>(buf, 1, 4), (uint)32);
                var ts =
                    (ulong)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds
                    * 1_000_000UL;
                BitConverter.TryWriteBytes(new Span<byte>(buf, 5, 8), ts);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 13, 4), 0.1f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 17, 4), 0.2f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 21, 4), 0.3f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 25, 4), 1.0f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 29, 4), 2.0f);
                BitConverter.TryWriteBytes(new Span<byte>(buf, 33, 4), 3.0f);

                List<NetworkStream> streams;
                lock (_clients)
                {
                    streams = _streams.ToList();
                }

                foreach (var stream in streams)
                {
                    try
                    {
                        await stream.WriteAsync(buf, 0, buf.Length, ct);
                        await stream.FlushAsync(ct);
                    }
                    catch
                    {
                        // Ignore transmit errors; client loop will close sockets.
                    }
                }
            }

            await Task.Delay(10, ct);
        }
    }

    private static async Task SendImuStateAsync(
        NetworkStream stream,
        byte state,
        CancellationToken ct
    )
    {
        var header = new byte[5];
        header[0] = MsgImuState;
        BitConverter.TryWriteBytes(new Span<byte>(header, 1, 4), (uint)1);
        await stream.WriteAsync(header, 0, header.Length, ct);
        await stream.WriteAsync(new[] { state }, 0, 1, ct);
        await stream.FlushAsync(ct);
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

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Ignore cancellation errors during teardown.
        }

        try
        {
            _broadcaster?.Wait(100);
        }
        catch
        {
            // Ignore aggregator shutdown errors.
        }

        lock (_clients)
        {
            foreach (var stream in _streams)
            {
                try
                {
                    stream.Close();
                }
                catch { }
            }

            foreach (var client in _clients)
            {
                try
                {
                    client.Close();
                }
                catch { }
            }

            _streams.Clear();
            _clients.Clear();
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Ignore listener shutdown failures.
        }
    }
}
