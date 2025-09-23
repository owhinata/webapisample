using System;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MyAppMain;

/// <summary>
/// Manages command processing and notification dispatch for MyAppMain.
/// </summary>
internal sealed class CommandPipeline : IAsyncDisposable
{
    private readonly CommandHandler _handler;
    private readonly MyAppNotificationHub.MyAppNotificationHub? _notificationHub;
    private readonly Channel<MyAppNotificationHub.ModelCommand> _commandChannel =
        Channel.CreateUnbounded<MyAppNotificationHub.ModelCommand>();
    private readonly Channel<MyAppNotificationHub.ModelResult> _resultChannel =
        Channel.CreateUnbounded<MyAppNotificationHub.ModelResult>();
    private CancellationTokenSource? _cts;
    private Task? _processorTask;
    private Task? _dispatcherTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandPipeline"/> class.
    /// </summary>
    /// <param name="handler">Handler used to execute incoming commands.</param>
    /// <param name="notificationHub">Hub notified when results are produced.</param>
    public CommandPipeline(
        CommandHandler handler,
        MyAppNotificationHub.MyAppNotificationHub? notificationHub
    )
    {
        _handler = handler;
        _notificationHub = notificationHub;
    }

    /// <summary>
    /// Starts background processing for commands and notifications.
    /// </summary>
    /// <param name="token">Cancellation token linked to the application lifecycle.</param>
    public void Start(CancellationToken token)
    {
        if (_cts is not null)
            throw new InvalidOperationException("Command pipeline already started.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var linkedToken = _cts.Token;
        _processorTask = Task.Run(
            () => ProcessCommandsAsync(linkedToken),
            linkedToken
        );
        _dispatcherTask = Task.Run(
            () => DispatchNotificationsAsync(linkedToken),
            linkedToken
        );
    }

    /// <summary>
    /// Stops background processing and waits for inflight work to complete.
    /// </summary>
    /// <param name="cancellationToken">Optional token controlling the wait.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var cts = _cts;
        if (cts is null)
            return;

        _cts = null;
        cts.Cancel();

        try
        {
            await Task.WhenAll(
                    _processorTask ?? Task.CompletedTask,
                    _dispatcherTask ?? Task.CompletedTask
                )
                .WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (AggregateException ex)
            when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // Ignore cancellation-driven exceptions.
        }
        finally
        {
            cts.Dispose();
            _processorTask = null;
            _dispatcherTask = null;
        }
    }

    /// <summary>
    /// Attempts to enqueue a command for processing.
    /// </summary>
    /// <param name="command">Command to enqueue.</param>
    /// <returns><c>true</c> if the command was accepted; otherwise <c>false</c>.</returns>
    public bool TryWriteCommand(MyAppNotificationHub.ModelCommand command) =>
        _commandChannel.Writer.TryWrite(command);

    /// <summary>
    /// Disposes the pipeline and stops the background workers.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task ProcessCommandsAsync(CancellationToken ct)
    {
        var reader = _commandChannel.Reader;
        while (!ct.IsCancellationRequested)
        {
            MyAppNotificationHub.ModelCommand? cmd = null;
            try
            {
                if (!await reader.WaitToReadAsync(ct))
                    break;
                if (!reader.TryRead(out cmd))
                    continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (cmd is null)
                continue;

            var result = await _handler.HandleAsync(cmd);
            _resultChannel.Writer.TryWrite(result);
        }
    }

    private async Task DispatchNotificationsAsync(CancellationToken ct)
    {
        var reader = _resultChannel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var result))
                {
                    if (result is null)
                        continue;

                    _notificationHub?.NotifyResult(result);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
    }
}
