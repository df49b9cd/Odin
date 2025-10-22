using System.Threading.Channels;

namespace Odin.ExecutionEngine.Matching;

public sealed class MatchingSubscription : IAsyncDisposable
{
    private readonly TaskQueueDispatcher _dispatcher;

    internal MatchingSubscription(TaskQueueDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ChannelReader<MatchingTask> Reader => _dispatcher.Reader;

    public ValueTask DisposeAsync() => _dispatcher.DisposeAsync();
}
