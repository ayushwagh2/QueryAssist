using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using QueryAssist.Models;

namespace QueryAssist.Services;

public class RelationshipEmbeddingQueue
{
    private readonly ConcurrentQueue<RelationshipPayload> _workItems = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void QueueBackgroundWorkItem(RelationshipPayload workItem)
    {
        if (workItem == null)
        {
            throw new System.ArgumentNullException(nameof(workItem));
        }

        _workItems.Enqueue(workItem);
        _signal.Release();
    }

    public async Task<RelationshipPayload> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        _workItems.TryDequeue(out var workItem);
        return workItem;
    }
}
