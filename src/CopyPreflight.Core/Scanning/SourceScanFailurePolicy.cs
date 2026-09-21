using System.IO;
using System.Security;

namespace CopyPreflight.Core;

/// <summary>
/// Decides which failures of a file-system read describe a gap in the inventory, rather than a bug
/// in CopyPreflight.
/// </summary>
/// <remarks>
/// <para>
/// Only the failures that mean "this could not be looked at" are recoverable: the scan records them
/// and carries on with what it can still reach. Everything else — an invalid argument, a programming
/// error, or an exhausted process — propagates, because swallowing it would turn a defect into a
/// falsely reassuring result.
/// </para>
/// <para>
/// Cancellation is deliberately <b>not</b> recoverable. A cancelled scan must surface
/// <see cref="OperationCanceledException"/>; it must not be quietly folded into a partial result
/// that looks like a finished one.
/// </para>
/// </remarks>
internal static class SourceScanFailurePolicy
{
    /// <summary>Whether a failure describes an unobservable part of the source tree.</summary>
    /// <param name="exception">The failure to classify.</param>
    /// <returns><see langword="true"/> when the scan should record an issue and continue.</returns>
    internal static bool IsRecoverableFileSystemFailure(Exception exception)
        => exception is UnauthorizedAccessException
            or IOException
            or SecurityException;
}
