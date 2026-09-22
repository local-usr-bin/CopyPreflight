using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CopyPreflight.Windows;

/// <summary>
/// Why a pair of user-entered paths cannot be made into a check request.
/// </summary>
/// <remarks>
/// <para>
/// These are <b>input format</b> facts, not facts about the file system. A path can be perfectly
/// well formed and still name nothing at all, and a path that names an unreadable or unreachable
/// directory is still a valid request — deciding those is the job of the probes, which report what
/// Windows actually answered. No kind here claims anything about existence, accessibility or
/// writability.
/// </para>
/// <para>
/// They are kept apart from <see cref="Finding"/> for the same reason the scan and probe issues are:
/// a request this tool will not build is an application-level rejection, not a migration finding.
/// </para>
/// </remarks>
public enum PreflightRequestError
{
    /// <summary>No source path was entered.</summary>
    SourcePathMissing = 0,

    /// <summary>No target path was entered.</summary>
    TargetPathMissing = 1,

    /// <summary>
    /// The source path is not fully qualified. A relative path is not resolved against the process's
    /// current directory, because that would make the check depend on where the application happened
    /// to be started from.
    /// </summary>
    SourcePathNotFullyQualified = 2,

    /// <summary>The target path is not fully qualified, for the same reason as the source path.</summary>
    TargetPathNotFullyQualified = 3,
}

/// <summary>
/// The immutable snapshot of what the user asked one check to examine.
/// </summary>
/// <remarks>
/// <para>
/// This is the identity of a run. Everything a run reports — its progress, its facts and its findings
/// — belongs to one request, and the paths are kept exactly as they were entered: nothing is trimmed,
/// repaired, expanded or normalised here. The scanner and the probes resolve the paths they are given
/// in their own documented way (<see cref="Path.GetFullPath(string)"/>), and the resolved roots are
/// reported back on their results, so the path that was <i>asked about</i> and the path that was
/// <i>used</i> can never be confused with each other.
/// </para>
/// <para>
/// Being fully qualified is the only thing the request itself checks. It is deliberately <b>not</b> a
/// statement that the path exists, is reachable, is a directory or can be written to; those are
/// observations, and they are made by the layers that observe.
/// </para>
/// </remarks>
public sealed record PreflightRequest
{
    private PreflightRequest(Guid requestId, string sourcePath, string targetPath)
    {
        RequestId = requestId;
        SourcePath = sourcePath;
        TargetPath = targetPath;
    }

    /// <summary>
    /// Identity of this request, used to tell one run's progress and results from another's. A late
    /// callback carrying the identity of a previous run cannot be mistaken for this one's.
    /// </summary>
    public Guid RequestId { get; }

    /// <summary>The source path exactly as it was entered, with no normalisation applied.</summary>
    public string SourcePath { get; }

    /// <summary>The target path exactly as it was entered, with no normalisation applied.</summary>
    public string TargetPath { get; }

    /// <summary>
    /// Builds a request from two entered paths, or reports which input the tool will not accept.
    /// </summary>
    /// <param name="sourcePath">
    /// The entered source path. It must be non-empty and fully qualified.
    /// </param>
    /// <param name="targetPath">
    /// The entered target path. It must be non-empty and fully qualified.
    /// </param>
    /// <param name="request">The built request when this method returns <see langword="true"/>.</param>
    /// <param name="error">
    /// Which input was refused, when this method returns <see langword="false"/>; otherwise
    /// <see cref="PreflightRequestError.SourcePathMissing"/>, which is meaningless in that case.
    /// </param>
    /// <returns><see langword="true"/> when a request could be built.</returns>
    /// <remarks>
    /// The order of the checks is fixed: a missing path is reported before a malformed one, so the
    /// user is never told that an empty box is "not fully qualified".
    /// </remarks>
    public static bool TryCreate(
        string? sourcePath,
        string? targetPath,
        [NotNullWhen(true)] out PreflightRequest? request,
        out PreflightRequestError error)
    {
        if (!TryValidate(sourcePath, PreflightRequestError.SourcePathMissing, out var sourcePathError, out var source))
        {
            request = null;
            error = sourcePathError;
            return false;
        }

        if (!TryValidate(targetPath, PreflightRequestError.TargetPathMissing, out var targetPathError, out var target))
        {
            request = null;
            error = targetPathError;
            return false;
        }

        request = new PreflightRequest(Guid.NewGuid(), source, target);
        error = PreflightRequestError.SourcePathMissing;
        return true;
    }

    /// <summary>
    /// Whether one entered path is usable as a request input, and which input is refused when it is
    /// not.
    /// </summary>
    /// <remarks>
    /// Only two things are checked: that something was entered, and that the entry is fully qualified.
    /// A path Windows itself cannot resolve is left to the probe, which reports the refusal as a
    /// target or source fact instead of as a UI-side guess.
    /// </remarks>
    private static bool TryValidate(
        string? path,
        PreflightRequestError missingError,
        out PreflightRequestError error,
        [NotNullWhen(true)] out string? value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            value = null;
            error = missingError;
            return false;
        }

        // A drive path such as C:\data, a UNC path such as \\server\share\data, and an
        // extended-length path such as \\?\C:\data are all fully qualified and are all accepted;
        // this check deliberately does not narrow the forms the layers below already support.
        if (!Path.IsPathFullyQualified(path))
        {
            value = null;
            error = missingError == PreflightRequestError.SourcePathMissing
                ? PreflightRequestError.SourcePathNotFullyQualified
                : PreflightRequestError.TargetPathNotFullyQualified;
            return false;
        }

        // Kept exactly as entered. Trailing spaces, unusual names and namespace prefixes are part of
        // what the user asked about, and rewriting any of them here would mean probing something
        // other than what was requested.
        value = path;
        error = missingError;
        return true;
    }
}
