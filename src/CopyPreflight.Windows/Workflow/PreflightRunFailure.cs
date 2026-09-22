namespace CopyPreflight.Windows;

/// <summary>
/// Why a run stopped on an unexpected failure.
/// </summary>
/// <remarks>
/// <para>
/// This describes a failure of the <b>application</b>, which is a different thing from a source tree
/// that could not be read or a target that could not be used. Those are facts the scanner and the
/// probes record and report as gaps; a finding is never raised for them here, and this record is never
/// turned into a <see cref="CopyPreflight.Core.Finding"/>.
/// </para>
/// <para>
/// The exception is described, not retained, and its message is <b>not</b> copied into any finding or
/// into any stable text: it is whatever the runtime produced, in whatever language that runtime
/// speaks, and it belongs to the diagnostic side of the report. The type name and the phase are the
/// stable parts, so that the same failure can be recognised between runs.
/// </para>
/// </remarks>
public sealed record PreflightRunFailure
{
    /// <summary>Creates a failure description.</summary>
    /// <param name="phase">The stage the run was in when it stopped.</param>
    /// <param name="exceptionTypeName">Full name of the exception that stopped the run.</param>
    /// <param name="message">The message the runtime produced, kept verbatim for display.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> is not a known phase.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="exceptionTypeName"/> is null, empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public PreflightRunFailure(PreflightRunPhase phase, string exceptionTypeName, string message)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown run phase.");
        }

        Phase = phase;
        ExceptionTypeName = string.IsNullOrWhiteSpace(exceptionTypeName)
            ? throw new ArgumentException(
                "A failure must name the exception type that stopped the run.",
                nameof(exceptionTypeName))
            : exceptionTypeName;

        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>The stage the run was in when it stopped.</summary>
    public PreflightRunPhase Phase { get; }

    /// <summary>Full name of the exception that stopped the run, for example <c>System.IO.IOException</c>.</summary>
    public string ExceptionTypeName { get; }

    /// <summary>The message the runtime produced, kept as it was reported.</summary>
    public string Message { get; }
}
