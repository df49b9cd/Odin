using System.Text.Json;
using System.Threading.Channels;
using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;

namespace Odin.ExecutionEngine.Matching;

internal sealed class TaskQueueDispatcher : IAsyncDisposable
{
    private static readonly TimeSpan EmptyPollDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(1);

    private readonly string _queueName;
    private readonly string _workerIdentity;
    private readonly ITaskQueueRepository _repository;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger _logger;
    private readonly TaskQueue<TaskDispatchItem> _queue;
    private readonly TaskQueueChannelAdapter<TaskDispatchItem> _adapter;
    private readonly Channel<MatchingTask> _output;
    private readonly CancellationTokenSource _cts;
    private readonly Task _pollingLoop;
    private readonly Task _deliveryLoop;

    public TaskQueueDispatcher(
        string queueName,
        string workerIdentity,
        ITaskQueueRepository repository,
        JsonSerializerOptions serializerOptions,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerIdentity);

        _queueName = queueName;
        _workerIdentity = workerIdentity;
        _repository = repository;
        _serializerOptions = serializerOptions;
        _logger = logger;

        var options = new TaskQueueOptions
        {
            Capacity = 1024,
            LeaseDuration = TimeSpan.FromMinutes(1),
            HeartbeatInterval = TimeSpan.FromSeconds(30),
            LeaseSweepInterval = TimeSpan.FromSeconds(30),
            RequeueDelay = TimeSpan.FromSeconds(5),
            MaxDeliveryAttempts = 5
        };

        _queue = new TaskQueue<TaskDispatchItem>(
            options,
            TimeProvider.System,
            static (_, _) => ValueTask.CompletedTask);

        var channel = Channel.CreateUnbounded<TaskQueueLease<TaskDispatchItem>>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        _adapter = TaskQueueChannelAdapter<TaskDispatchItem>.Create(
            _queue,
            channel,
            concurrency: 1,
            ownsQueue: true);

        _output = Channel.CreateUnbounded<MatchingTask>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        _cts = new CancellationTokenSource();
        _pollingLoop = Task.Run(() => PollLoopAsync(_cts.Token));
        _deliveryLoop = Task.Run(() => DeliveryLoopAsync(_cts.Token));
    }

    public ChannelReader<MatchingTask> Reader => _output.Reader;

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Result<TaskLease?> pollResult;
            try
            {
                pollResult = await _repository.PollAsync(_queueName, _workerIdentity, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling loop crashed for queue {QueueName}", _queueName);
                await Task.Delay(FailureDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (pollResult.IsFailure)
            {
                _logger.LogWarning(
                    "Polling queue {QueueName} failed: {Error}",
                    _queueName,
                    pollResult.Error?.Message);
                await Task.Delay(FailureDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var lease = pollResult.Value;
            if (lease is null)
            {
                await Task.Delay(EmptyPollDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var dispatchItemResult = TaskDispatchItem.Create(lease, _repository, _serializerOptions, _logger);
            if (dispatchItemResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to build dispatch item for lease {LeaseId}: {Error}",
                    lease.LeaseId,
                    dispatchItemResult.Error?.Message);
                await _repository.FailAsync(lease.LeaseId, "Dispatch item creation failed", requeue: false, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await _queue.EnqueueAsync(dispatchItemResult.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue dispatch item for queue {QueueName}", _queueName);
                await _repository.FailAsync(lease.LeaseId, ex.Message, requeue: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DeliveryLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _adapter.Reader;
        while (!cancellationToken.IsCancellationRequested)
        {
            TaskQueueLease<TaskDispatchItem> lease;
            try
            {
                lease = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            var dispatchItem = lease.Value;
            var matchingTask = new MatchingTask(
                dispatchItem,
                async ct =>
                {
                    var result = await dispatchItem.CompleteAsync(ct).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        await lease.CompleteAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var error = result.Error ?? Error.From("Task completion failed", OdinErrorCodes.TaskQueueError);
                        await lease.FailAsync(error, requeue: true, ct).ConfigureAwait(false);
                    }

                    return result;
                },
                async (reason, requeue, ct) =>
                {
                    var result = await dispatchItem.FailAsync(reason, requeue, ct).ConfigureAwait(false);
                    var error = Error.From(reason, OdinErrorCodes.TaskQueueError);
                    await lease.FailAsync(error, requeue, ct).ConfigureAwait(false);
                    return result;
                },
                async ct =>
                {
                    var result = await dispatchItem.HeartbeatAsync(ct).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        await lease.HeartbeatAsync(ct).ConfigureAwait(false);
                    }
                    return result;
                });

            if (!await _output.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            await _output.Writer.WriteAsync(matchingTask, cancellationToken).ConfigureAwait(false);
        }

        _output.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await Task.WhenAll(_pollingLoop, _deliveryLoop).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dispatcher dispose encountered an error for queue {QueueName}", _queueName);
        }

        await _adapter.DisposeAsync().ConfigureAwait(false);
        await _queue.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
