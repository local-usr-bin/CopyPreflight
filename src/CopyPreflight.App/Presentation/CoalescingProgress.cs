namespace CopyPreflight.App;

/// <summary>
/// A progress channel that keeps the newest snapshot and hands it over once, instead of queueing every
/// snapshot it is given.
/// </summary>
/// <remarks>
/// <para>
/// The scanner reports at a rate the interface cannot be assumed to keep up with: a directory holding
/// hundreds of thousands of entries is reported on as it is listed, and a tree of many small
/// directories produces a report per directory. <see cref="Progress{T}"/> puts one callback on the
/// captured synchronization context for each report, so a fast producer and a busy interface turn into
/// an unbounded backlog of stale snapshots waiting to be displayed and then discarded. This type is the
/// fix for that: what reaches the queue is bounded, and what it shows is the latest state rather than
/// the state as it was several hundred reports ago.
/// </para>
/// <para>
/// <b>At most one delivery is ever in flight.</b> A report either schedules a delivery — the first one
/// to find nothing in flight — or merely replaces the value that the pending delivery will pick up when
/// it runs. Therefore the number of callbacks queued on the target context never exceeds one, however
/// many reports arrive while the interface is busy, and the value finally delivered is the newest one.
/// A snapshot that is superseded before it is displayed is never displayed; that is the point.
/// </para>
/// <para>
/// <b>Ordering is preserved.</b> Deliveries are never re-ordered, and a superseded snapshot is replaced
/// by a newer one rather than the other way round, so a stage or a count can only appear to move
/// forwards. Nothing here interprets the value: dropping an intermediate stage from the display is the
/// caller's business to accept, and the caller's own guards on run identity, stage and run state still
/// apply to whatever arrives.
/// </para>
/// <para>
/// <b>Nothing is scheduled that outlives a run.</b> There is no timer and no background thread; the
/// only resource is whatever the synchronization context already owns. A delivery that is still queued
/// when a run ends reaches the handler late, which is why the handler is built to ignore a snapshot from
/// a run that is over.
/// </para>
/// <para>
/// The synchronization context is captured when the type is constructed, exactly as
/// <see cref="Progress{T}"/> does, so an implementation must be created on the thread whose queue its
/// handler is allowed to touch. When there is no context, the handler runs on the reporting thread —
/// again as <see cref="Progress{T}"/> does — and nothing needs to be merged, because there is only one
/// thread.
/// </para>
/// </remarks>
/// <typeparam name="T">The kind of snapshot being delivered.</typeparam>
internal sealed class CoalescingProgress<T> : IProgress<T>
{
    private readonly SynchronizationContext? _context;
    private readonly Action<T> _handler;
    private readonly SendOrPostCallback _deliver;
    private readonly object _gate = new();

    private T? _latest;
    private bool _inFlight;

    /// <summary>Creates a channel that delivers on the current synchronization context.</summary>
    /// <param name="handler">What to do with the newest snapshot, on the context that was captured.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    internal CoalescingProgress(Action<T> handler)
        : this(handler, SynchronizationContext.Current)
    {
    }

    /// <summary>Creates a channel that delivers on a supplied synchronization context.</summary>
    /// <param name="handler">What to do with the newest snapshot.</param>
    /// <param name="context">
    /// Where to deliver, or <see langword="null"/> to deliver on the reporting thread.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    internal CoalescingProgress(Action<T> handler, SynchronizationContext? context)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
        _context = context;
        _deliver = _ => Deliver();
    }

    /// <inheritdoc />
    public void Report(T value)
    {
        bool schedule;

        lock (_gate)
        {
            _latest = value;

            // The first report to find nothing in flight owns the delivery; every other report only
            // replaces the value that delivery will read. This is what bounds the queue to one entry.
            schedule = !_inFlight;
            _inFlight = true;
        }

        if (!schedule)
        {
            return;
        }

        if (_context is null)
        {
            Deliver();

            return;
        }

        _context.Post(_deliver, null);
    }

    /// <summary>Hands the newest snapshot to the handler, on the delivery context.</summary>
    private void Deliver()
    {
        T value;

        lock (_gate)
        {
            value = _latest!;

            // Cleared before the handler runs, so that a report arriving while the handler is still
            // working schedules its own delivery rather than being silently folded into this one.
            _inFlight = false;
        }

        _handler(value);
    }
}
