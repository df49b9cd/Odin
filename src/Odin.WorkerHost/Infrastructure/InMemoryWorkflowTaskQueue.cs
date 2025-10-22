using System.Threading.Channels;
using Odin.Sdk;

namespace Odin.WorkerHost.Infrastructure;

public interface IWorkflowTaskQueue
{
    ValueTask EnqueueAsync(WorkflowTask task, CancellationToken cancellationToken);
    ValueTask<WorkflowTask> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryWorkflowTaskQueue : IWorkflowTaskQueue
{
    private readonly Channel<WorkflowTask> _channel = Channel.CreateUnbounded<WorkflowTask>();

    public ValueTask EnqueueAsync(WorkflowTask task, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(task, cancellationToken);

    public async ValueTask<WorkflowTask> DequeueAsync(CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Reader.TryRead(out var task))
            {
                return task;
            }
        }

        throw new TaskCanceledException();
    }
}
