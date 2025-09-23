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
/// The transport remains generic while the behaviour is delegated to an
/// injected <see cref="ITestImuScenario"/> so tests can script flows.
/// </summary>
internal sealed class TestImuServer : IDisposable
{
    private const byte MsgImuState = 0x01;
    private const byte MsgImuData = 0x02;
    private const byte MsgSetImuState = 0x81;

    private readonly Func<ITestImuScenario> _scenarioFactory;
    private readonly TimeSpan _tickInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<ConnectionState> _connections = new();

    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task? _tickLoop;

    public TestImuServer()
        : this(() => new DefaultImuScenario(), TimeSpan.FromMilliseconds(10)) { }

    public TestImuServer(
        Func<ITestImuScenario> scenarioFactory,
        TimeSpan? tickInterval = null
    )
    {
        _scenarioFactory = scenarioFactory;
        _tickInterval = tickInterval ?? TimeSpan.FromMilliseconds(10);
    }

    public int Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var ct = _cts.Token;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(ct), ct);
        _tickLoop = Task.Run(() => TickLoopAsync(ct), ct);
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        if (_listener == null)
            throw new InvalidOperationException("Server not started");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                var stream = client.GetStream();
                var scenario = _scenarioFactory();
                var connection = new ConnectionState(
                    client,
                    stream,
                    scenario,
                    RemoveConnection
                );

                lock (_connections)
                {
                    _connections.Add(connection);
                }

                await scenario.OnClientConnectedAsync(connection, ct);

                _ = Task.Run(() => ClientRecvLoopAsync(connection, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ClientRecvLoopAsync(
        ConnectionState connection,
        CancellationToken ct
    )
    {
        var stream = connection.Stream;
        var scenario = connection.Scenario;
        var header = new byte[5];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await ReadExactAsync(stream, header, 0, header.Length, ct);
                var id = header[0];
                var len = BitConverter.ToUInt32(header, 1);
                var payload = Array.Empty<byte>();
                if (len > 0)
                {
                    payload = new byte[len];
                    await ReadExactAsync(stream, payload, 0, (int)len, ct);
                }

                if (id == MsgSetImuState && payload.Length >= 1)
                {
                    var requestedOn = payload[0] == 1;
                    await scenario.OnStateChangeRequestedAsync(
                        connection,
                        requestedOn,
                        ct
                    );
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        finally
        {
            connection.Dispose();
        }
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConnectionState[] snapshot;
                lock (_connections)
                {
                    snapshot = _connections.ToArray();
                }

                foreach (var connection in snapshot)
                {
                    try
                    {
                        await connection.Scenario.OnTickAsync(connection, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Ignore scenario errors during tick processing to keep server alive.
                    }
                }

                await Task.Delay(_tickInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RemoveConnection(ConnectionState connection)
    {
        lock (_connections)
        {
            _connections.Remove(connection);
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

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch { }

        try
        {
            _acceptLoop?.Wait(100);
        }
        catch { }

        try
        {
            _tickLoop?.Wait(100);
        }
        catch { }

        lock (_connections)
        {
            foreach (var connection in _connections.ToArray())
            {
                connection.Dispose();
            }

            _connections.Clear();
        }

        try
        {
            _listener?.Stop();
        }
        catch { }
    }

    private sealed class ConnectionState : ITestImuConnection, IDisposable
    {
        private readonly TcpClient _client;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Action<ConnectionState> _onDisposed;
        private int _disposed;

        public ConnectionState(
            TcpClient client,
            NetworkStream stream,
            ITestImuScenario scenario,
            Action<ConnectionState> onDisposed
        )
        {
            _client = client;
            Stream = stream;
            Scenario = scenario;
            _onDisposed = onDisposed;
        }

        public NetworkStream Stream { get; }

        public ITestImuScenario Scenario { get; }

        public async ValueTask SendStateAsync(bool isOn, CancellationToken ct)
        {
            var header = new byte[5];
            header[0] = MsgImuState;
            BitConverter.TryWriteBytes(new Span<byte>(header, 1, 4), (uint)1);
            var body = new[] { isOn ? (byte)1 : (byte)0 };
            await WriteAsync(header, ct);
            await WriteAsync(body, ct);
        }

        public async ValueTask SendSampleAsync(
            ImuSampleFrame frame,
            CancellationToken ct
        )
        {
            var buffer = new byte[5 + 32];
            buffer[0] = MsgImuData;
            BitConverter.TryWriteBytes(new Span<byte>(buffer, 1, 4), (uint)32);
            BitConverter.TryWriteBytes(
                new Span<byte>(buffer, 5, 8),
                frame.TimestampMicroseconds
            );
            BitConverter.TryWriteBytes(new Span<byte>(buffer, 13, 4), frame.AccelX);
            BitConverter.TryWriteBytes(new Span<byte>(buffer, 17, 4), frame.AccelY);
            BitConverter.TryWriteBytes(new Span<byte>(buffer, 21, 4), frame.AccelZ);
            BitConverter.TryWriteBytes(new Span<byte>(buffer, 25, 4), frame.GyroX);
            BitConverter.TryWriteBytes(new Span<byte>(buffer, 29, 4), frame.GyroY);
            BitConverter.TryWriteBytes(new Span<byte>(buffer, 33, 4), frame.GyroZ);
            await WriteAsync(buffer, ct);
        }

        private async ValueTask WriteAsync(byte[] buffer, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                await Stream.WriteAsync(buffer, 0, buffer.Length, ct);
                await Stream.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try
            {
                _client.Close();
            }
            catch { }

            try
            {
                Stream.Close();
            }
            catch { }

            _writeLock.Dispose();
            _onDisposed(this);
        }
    }

    private sealed class DefaultImuScenario : ITestImuScenario
    {
        private bool _imuOn;

        public async Task OnClientConnectedAsync(
            ITestImuConnection connection,
            CancellationToken ct
        )
        {
            await connection.SendStateAsync(false, ct);
        }

        public async Task OnStateChangeRequestedAsync(
            ITestImuConnection connection,
            bool requestedOn,
            CancellationToken ct
        )
        {
            if (_imuOn == requestedOn)
                return;

            _imuOn = requestedOn;
            await connection.SendStateAsync(_imuOn, ct);
        }

        public async Task OnTickAsync(
            ITestImuConnection connection,
            CancellationToken ct
        )
        {
            if (!_imuOn)
                return;

            var now =
                (ulong)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds
                * 1_000_000UL;
            var frame = new ImuSampleFrame(now, 0.1f, 0.2f, 0.3f, 1.0f, 2.0f, 3.0f);
            await connection.SendSampleAsync(frame, ct);
        }
    }
}
