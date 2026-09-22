using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// One snapshot of a running check: which stage it is in, and how much of the source it has seen.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is a plain, immutable value. It carries the identity of the request it belongs to, so a
/// snapshot that arrives after the run it describes has ended — or after a later run has started —
/// can be recognised and ignored rather than applied to the wrong run.
/// </para>
/// <para>
/// There is deliberately no percentage, no estimate and no completion total. The number of objects in
/// a source tree is not known until it has been walked, and the existing-object check may ask about
/// ancestors that the inventory never listed, so a "percent complete" here would be a fabricated fact.
/// The counters are counts of what has been <b>observed so far</b> and nothing more.
/// </para>
/// </remarks>
public sealed record PreflightRunProgress
{
    /// <summary>Creates a progress snapshot.</summary>
    /// <param name="requestId">Identity of the request this snapshot belongs to.</param>
    /// <param name="phase">The stage the run is currently in.</param>
    /// <param name="sourceScan">
    /// The latest source-scan snapshot, or <see langword="null"/> while no source observation has been
    /// reported yet. After the scan has finished it is the scan's final snapshot and is carried
    /// unchanged through the later stages, so the source counters stay visible without being
    /// re-derived.
    /// </param>
    public PreflightRunProgress(
        Guid requestId,
        PreflightRunPhase phase,
        SourceScanProgress? sourceScan)
    {
        RequestId = requestId;
        Phase = phase;
        SourceScan = sourceScan;
    }

    /// <summary>Identity of the request this snapshot belongs to.</summary>
    public Guid RequestId { get; }

    /// <summary>The stage the run is currently in.</summary>
    public PreflightRunPhase Phase { get; }

    /// <summary>
    /// What the source observation had reported when this snapshot was taken, or
    /// <see langword="null"/> when it has reported nothing yet.
    /// </summary>
    public SourceScanProgress? SourceScan { get; }
}
