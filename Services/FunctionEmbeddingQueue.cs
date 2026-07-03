using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using QueryAssist.Models;

namespace QueryAssist.Services;

public class FunctionEmbeddingQueue
{
    private readonly ConcurrentQueue<FunctionPayload> _workItems = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void QueueBackgroundWorkItem(FunctionPayload workItem)
    {
        if (workItem == null)
        {
            throw new ArgumentNullException(nameof(workItem));
        }

        _workItems.Enqueue(workItem);
        _signal.Release();
    }

    public async Task<FunctionPayload> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        _workItems.TryDequeue(out var workItem);

        return workItem!;
    }
}
