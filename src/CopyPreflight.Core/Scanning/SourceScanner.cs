using System.IO;

namespace CopyPreflight.Core;

/// <summary>
/// Observes what is actually inside a source directory, and reports it without drawing conclusions.
/// </summary>
/// <remarks>
/// <para>
/// The scanner answers one question: <i>"what did I actually observe in the source tree?"</i> It
/// builds an inventory of files, directories and reparse points and records the places it could not
/// look at. It does not decide what any of that means for a migration — that is the rule layer's
/// job — and it produces no <see cref="Finding"/>.
/// </para>
/// <para>
/// The scan is strictly read-only and does not verify that file contents can be read: it never
/// opens a file, never reads a byte, never writes anything into the source or target, and never
/// probes a target. Successfully reading an object's metadata is not evidence that its contents can
/// be read.
/// </para>
/// <para>
/// Traversal is a plain synchronous walk driven by an explicit stack, so a deeply nested tree
/// cannot exhaust the call stack. Each directory is listed lazily, and only directories that are
/// still pending are held in memory, so the whole tree is never materialised as a list of paths
/// before the walk starts.
/// </para>
/// <para>
/// A directory reparse point is recorded and then left alone: it is not entered, and its target is
/// never resolved or read.
/// </para>
/// <para>
/// The scanner holds no state, so one instance can be reused for any number of scans.
/// </para>
/// </remarks>
public sealed class SourceScanner
{
    private readonly ISourceDirectoryReader _directoryReader;

    /// <summary>Creates a scanner that reads the real file system.</summary>
    public SourceScanner()
        : this(new FileSystemDirectoryReader())
    {
    }

    /// <summary>Creates a scanner over a supplied directory-listing seam. For tests only.</summary>
    internal SourceScanner(ISourceDirectoryReader directoryReader)
    {
        ArgumentNullException.ThrowIfNull(directoryReader);

        _directoryReader = directoryReader;
    }

    /// <summary>Scans one source tree and returns everything that was observed.</summary>
    /// <param name="sourceRoot">
    /// The source root directory. Relative inputs are resolved against the process's current
    /// directory. Source entry paths in the result are always relative to this root, and the root
    /// itself never becomes an entry.
    /// </param>
    /// <param name="progress">
    /// Optional receiver of progress snapshots. It is called once per directory whose children were
    /// observed, on the calling thread. A snapshot may be reported synchronously; implementations
    /// are expected to return promptly.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the scan. Cancellation is observed before the walk starts, once per directory and
    /// once per observed child, so it stays responsive even in a directory holding a very large
    /// number of entries.
    /// </param>
    /// <returns>The inventory and the recoverable failures recorded while building it.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourceRoot"/> is null, empty or whitespace.</exception>
    /// <exception cref="InvalidSourceRootException">
    /// Nothing exists at <paramref name="sourceRoot"/>, or it exists but is not a directory.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The scan was cancelled. Cancellation is never reported as a scan issue, and no partial result
    /// is returned in its place.
    /// </exception>
    /// <remarks>
    /// A directory that exists but cannot be listed is not a failure of this method: the scan
    /// records a <see cref="SourceScanIssue"/> and continues with whatever else it can reach, so the
    /// caller gets an explicitly incomplete result instead of an exception.
    /// </remarks>
    public SourceScanResult Scan(
        string sourceRoot,
        IProgress<SourceScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new ArgumentException(
                "A source root must not be null, empty or whitespace.",
                nameof(sourceRoot));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullSourceRoot = Path.GetFullPath(sourceRoot);

        if (!Directory.Exists(fullSourceRoot))
        {
            throw new InvalidSourceRootException(
                sourceRoot,
                File.Exists(fullSourceRoot)
                    ? InvalidSourceRootReason.NotADirectory
                    : InvalidSourceRootReason.NotFound);
        }

        var entries = new List<SourceEntry>();
        var issues = new List<SourceScanIssue>();
        var pending = new Stack<PendingDirectory>();

        // The root is walked but is never itself an entry, so it has no relative path.
        pending.Push(new PendingDirectory(fullSourceRoot, RelativePath: null));

        long entriesObserved = 0;
        long filesObserved = 0;
        long directoriesObserved = 0;
        long reparsePointsObserved = 0;
        long logicalBytesObserved = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();

            using (var enumerator = TryBeginEnumeration(current, issues))
            {
                if (enumerator is null)
                {
                    // The directory could not be listed at all; the issue is already recorded and
                    // its siblings, which are still reachable, keep being walked.
                }
                else
                {
                    while (TryMoveNext(enumerator, current, issues, cancellationToken, out var child))
                    {
                        var entry = TryObserveChild(child, current, issues, pending);
                        if (entry is null)
                        {
                            continue;
                        }

                        entries.Add(entry);
                        entriesObserved++;

                        if (entry.IsReparsePoint)
                        {
                            reparsePointsObserved++;
                        }
                        else if (entry.Kind == SourceEntryKind.File)
                        {
                            filesObserved++;
                            logicalBytesObserved += entry.LengthInBytes!.Value;
                        }
                        else
                        {
                            directoriesObserved++;
                        }
                    }
                }
            }

            // Counters are complete for this directory, so the last snapshot of a scan always
            // equals the counters of the returned result.
            progress?.Report(new SourceScanProgress(
                entriesObserved,
                filesObserved,
                directoriesObserved,
                reparsePointsObserved,
                logicalBytesObserved,
                issues.Count,
                current.RelativePath));
        }

        return new SourceScanResult(fullSourceRoot, entries, issues);
    }

    /// <summary>Starts listing one directory, recording an issue if the listing cannot be started.</summary>
    private IEnumerator<ObservedDirectoryChild>? TryBeginEnumeration(
        PendingDirectory directory,
        List<SourceScanIssue> issues)
    {
        try
        {
            return _directoryReader.ReadChildren(directory.AbsolutePath).GetEnumerator();
        }
        catch (Exception exception) when (SourceScanFailurePolicy.IsRecoverableFileSystemFailure(exception))
        {
            issues.Add(SourceScanIssue.FromException(
                SourceScanIssueKind.CannotEnumerateDirectory,
                directory.RelativePath,
                exception));

            return null;
        }
    }

    /// <summary>
    /// Advances the listing of one directory, recording an issue and stopping that directory if the
    /// listing itself fails part-way through.
    /// </summary>
    private static bool TryMoveNext(
        IEnumerator<ObservedDirectoryChild> enumerator,
        PendingDirectory directory,
        List<SourceScanIssue> issues,
        CancellationToken cancellationToken,
        out ObservedDirectoryChild child)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (enumerator.MoveNext())
            {
                child = enumerator.Current;
                return true;
            }
        }
        catch (Exception exception) when (SourceScanFailurePolicy.IsRecoverableFileSystemFailure(exception))
        {
            issues.Add(SourceScanIssue.FromException(
                SourceScanIssueKind.CannotEnumerateDirectory,
                directory.RelativePath,
                exception));
        }

        child = default;
        return false;
    }

    /// <summary>
    /// Turns one listed child into an entry, or records why it could not be described. A directory
    /// that is not a reparse point is queued for walking; nothing else is.
    /// </summary>
    private SourceEntry? TryObserveChild(
        ObservedDirectoryChild child,
        PendingDirectory directory,
        List<SourceScanIssue> issues,
        Stack<PendingDirectory> pending)
    {
        var relativePath = ComposeRelativePath(directory.RelativePath, child.Name);

        if (child.Attributes is not { } attributes)
        {
            issues.Add(child.MetadataFailure is { } failure
                ? SourceScanIssue.FromException(
                    SourceScanIssueKind.CannotReadEntryMetadata,
                    relativePath,
                    failure)
                : new SourceScanIssue(
                    SourceScanIssueKind.CannotReadEntryMetadata,
                    relativePath,
                    exceptionTypeName: null,
                    hResult: null,
                    "The entry was listed, but its basic metadata could not be read."));

            return null;
        }

        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;

        if (isReparsePoint)
        {
            // The encounter is kept as a fact. The object is not classified, not entered, and its
            // target is not resolved, so no length can honestly be attributed to it either.
            return SourceEntry.ReparsePoint(
                relativePath,
                isDirectory ? SourceEntryKind.Directory : SourceEntryKind.File);
        }

        if (isDirectory)
        {
            pending.Push(new PendingDirectory(
                Path.Combine(directory.AbsolutePath, child.Name),
                relativePath));

            return SourceEntry.Directory(relativePath);
        }

        if (child.LengthInBytes is { } lengthInBytes)
        {
            return SourceEntry.File(relativePath, lengthInBytes);
        }

        // A file whose length was not established must not be reported as a zero-length file.
        issues.Add(new SourceScanIssue(
            SourceScanIssueKind.CannotReadEntryMetadata,
            relativePath,
            exceptionTypeName: null,
            hResult: null,
            "The entry was listed as a file, but its length could not be established."));

        return null;
    }

    /// <summary>
    /// Builds a child's path relative to the source root, keeping the separator the platform uses
    /// and changing nothing about the observed names.
    /// </summary>
    private static string ComposeRelativePath(string? parentRelativePath, string name)
        => parentRelativePath is null
            ? name
            : parentRelativePath + Path.DirectorySeparatorChar + name;

    /// <summary>One directory still waiting to be walked, with its path relative to the source root.</summary>
    private readonly record struct PendingDirectory(string AbsolutePath, string? RelativePath);
}
