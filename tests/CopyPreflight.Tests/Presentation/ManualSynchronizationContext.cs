using CopyPreflight.App;

namespace CopyPreflight.Tests;

/// <summary>
/// A synchronization context whose queue only advances when a test says so.
/// </summary>
/// <remarks>
/// <para>
/// This is how "how much is on its way to the interface" becomes an assertion instead of a guess: a
/// sink that posts to this context can be fed as hard as a test likes, and the number of callbacks
/// waiting to run can be read at any point, without any of them actually running. Nothing here waits,
/// sleeps or depends on a thread being scheduled.
/// </para>
/// <para>
/// It stands in for the dispatcher context a window installs, which the real sink captures when a run
/// starts. Like that context it is single-queue and first-in-first-out, which is the property the
/// presentation layer relies on when it decides where a snapshot belongs.
/// </para>
/// </remarks>
internal sealed class ManualSynchronizationContext : SynchronizationContext
{
    private readonly Queue<Action> _queue = new();
    private readonly object _gate = new();

    /// <summary>How many callbacks are waiting to run.</summary>
    internal int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count;
            }
        }
    }

    /// <inheritdoc />
    public override void Post(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            _queue.Enqueue(() => callback(state));
        }
    }

    /// <summary>Runs everything queued, including whatever running it queues, and says how many ran.</summary>
    internal int Drain()
    {
        var ran = 0;

        while (true)
        {
            Action work;

            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    return ran;
                }

                work = _queue.Dequeue();
            }

            work();
            ran++;
        }
    }
}
