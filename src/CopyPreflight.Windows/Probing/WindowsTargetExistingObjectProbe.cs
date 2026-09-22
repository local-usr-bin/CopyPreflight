using System.Runtime.Versioning;
using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Examines, read-only, whether an object already exists at the locations a source inventory maps
/// onto, and reports what was found — or why nothing could be found out.
/// </summary>
/// <remarks>
/// <para>
/// This probe answers one question per mapped location: <i>"is there already an object at
/// <c>target\relative-path</c>, and what kind of object is it?"</i> It establishes facts only. It
/// produces no <see cref="Finding"/>, carries no severity, and never decides what an occupied
/// destination means for a migration — that is the rule layer's job.
/// </para>
/// <para>
/// It is strictly read-only, and read-only in the strong sense: it calls one attribute query and
/// nothing else. It creates no file, creates no directory, writes no probe file, writes no alternate
/// data stream, changes no ACL and reads no file content. It therefore cannot and does not claim that
/// the target is writable, and it compares nothing but object kinds: two files of the same name are
/// not compared, because reading them is out of scope and a size or name match proves nothing about
/// their content.
/// </para>
/// <para>
/// Only the inventory it is given is examined. Paths are queried on demand, oldest-mapped ancestor
/// first; the target tree is never enumerated, unrelated target content is never listed, and the
/// source is never re-scanned. A location is only ever queried because some source object maps onto
/// it.
/// </para>
/// <para>
/// <b>Ancestors are resolved before their children, and never in reliance on input order.</b> The
/// inventory is a set of facts, not a traversal, so entries may arrive in any order; each entry's own
/// ancestor chain is walked from the shallowest component downwards, and resolved locations are
/// remembered for the duration of this run only. Nothing is reused across runs.
/// </para>
/// <para>
/// <b>A branch stops where a fact says it must.</b> If an ancestor already holds an ordinary file, no
/// object can exist below it and the branch is not queried further; if an ancestor is a reparse point,
/// what lies beneath it depends on where it points, which this milestone does not resolve; if an
/// ancestor could not be checked, nothing below it can be claimed; and if an ancestor is absent,
/// everything below it is absent by inference. In every case the affected objects keep a reference to
/// the ancestor that explains them, so one root cause is reported once instead of once per descendant.
/// </para>
/// <para>
/// <b>A name is queried as the mapping spells it, or not at all.</b> A component whose name cannot be
/// queried without being reinterpreted — an illegal character, a reserved device name, a trailing
/// space or period — would have to be normalised into a different name, and the answer would then
/// describe an object the mapping never mentioned. Such locations are recorded as unexamined, and the
/// naming rule's own judgement of them is left untouched.
/// </para>
/// <para>
/// <b>The target root keeps the boundary the capability probe established for it.</b> That the root's
/// own attribute query does not answer is normal for a network share root and is documented by
/// Microsoft; it is recorded as such and the mapped locations are still examined individually, each
/// answering for itself. Only a root Windows positively reported as a file or as a reparse point stops
/// the examination, and even then the fact is recorded rather than turned into a claim about the
/// target.
/// </para>
/// <para>
/// <b>The observation is not a snapshot.</b> Ancestors and children are separate queries, and the file
/// system may change between them and afterwards. Remembering an answer is an optimisation within one
/// run, not an isolation guarantee, and this milestone does not promise to detect concurrent
/// replacement.
/// </para>
/// <para>
/// The type is Windows-only and holds no state, so one instance can be reused for any number of
/// inventories.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsTargetExistingObjectProbe
{
    /// <summary>
    /// The Win32 error Windows reports when a well-formed path resolves to nothing at its last
    /// component. It is the only failure this probe treats as evidence of absence.
    /// </summary>
    private const int ErrorFileNotFound = 2;

    /// <summary>
    /// The classic Win32 <c>MAX_PATH</c>, counted in characters including the terminating null.
    /// Above it a path is queried in its extended-length spelling.
    /// </summary>
    private const int MaxPath = 260;

    /// <summary>The Win32 extended-length file namespace prefix.</summary>
    private const string ExtendedLengthPrefix = @"\\?\";

    /// <summary>The Win32 device namespace prefix, which is <b>not</b> a long-path exemption.</summary>
    private const string DeviceNamespacePrefix = @"\\.\";

    /// <summary>The start of a network (UNC) path.</summary>
    private const string NetworkPrefix = @"\\";

    private static readonly char[] Separators = ['\\', '/'];

    private const string DirectoryDiagnostic =
        "Windows reported an ordinary directory at this location.";

    private const string FileDiagnostic =
        "Windows reported an ordinary file at this location.";

    private const string ReparsePointDiagnostic =
        "Windows reported the reparse point attribute at this location. It is not classified and not "
        + "followed.";

    private const string MissingDiagnostic =
        "The attribute query answered that the last component of this location does not exist, and no "
        + "ancestor blocked or made this query ambiguous, so no object is currently there.";

    private const string NotAnsweredDiagnostic =
        "The attribute query did not answer for this location, so whether an object exists there, and "
        + "what kind it is, remain unknown.";

    private const string RejectedTargetRootDiagnostic =
        "The target had already been rejected as a destination, so no mapped location under it was "
        + "queried.";

    private const string UnusableTargetRootDiagnostic =
        "Windows reported an ordinary file at the target root, so the target cannot be a destination "
        + "root and no mapped location under it was queried.";

    private const string ReparsePointTargetRootDiagnostic =
        "The target root itself carries the reparse point attribute, which is not looked through, so no "
        + "mapped location under it was queried.";

    private const string UnansweredTargetRootDiagnostic =
        "The attribute query did not answer for the target root. Microsoft documents that it does not "
        + "answer for a network share root, so this is not treated as the target being unusable and the "
        + "mapped locations are still examined one by one.";

    private const string DirectoryTargetRootDiagnostic =
        "Windows reported an ordinary directory at the target root, so the mapped locations under it "
        + "were examined.";

    private const string FileTargetRootDiagnostic =
        "Windows reported an ordinary file at the target root, which cannot be the destination root of "
        + "a migration.";

    private const string ReparsePointTargetRootReportedDiagnostic =
        "Windows reported the reparse point attribute at the target root. It is not classified and not "
        + "followed.";

    private const string RelativePathNotQueryableDiagnostic =
        "The relative path cannot be composed into a target location without changing the mapping — it "
        + "is rooted, or a component is empty or walks out of the target — so nothing was queried for "
        + "it.";

    private readonly IWindowsFileSystemApi _fileSystemApi;

    /// <summary>Creates a probe that queries the real Windows file system.</summary>
    public WindowsTargetExistingObjectProbe()
        : this(new Kernel32FileSystemApi())
    {
    }

    /// <summary>Creates a probe over a supplied Windows query seam. For tests only.</summary>
    internal WindowsTargetExistingObjectProbe(IWindowsFileSystemApi fileSystemApi)
    {
        ArgumentNullException.ThrowIfNull(fileSystemApi);

        _fileSystemApi = fileSystemApi;
    }

    /// <summary>
    /// Examines the mapped destination location of every source object in the inventory.
    /// </summary>
    /// <param name="source">What the scanner observed in the source tree. Only its entries are read.</param>
    /// <param name="target">
    /// The target the source maps onto. A target that was already rejected as a destination is not
    /// queried at all: its rejection is a fact the rule layer already reports.
    /// </param>
    /// <param name="cancellationToken">
    /// Checked at every source object and every ancestor before a query is made. A signalled token
    /// stops the run by throwing, exactly as the rest of this codebase does, so an interrupted run can
    /// never present itself as a finished one. It cannot interrupt a native call already in progress.
    /// </param>
    /// <returns>
    /// One observation per source entry, together with what was established about the target root and
    /// how far the run got.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="target"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    /// <remarks>
    /// A source entry whose relative path cannot be composed, whose name cannot be queried as written,
    /// or whose branch was stopped by an ancestor is reported as unexamined rather than answered. A
    /// query that did not answer leaves its location unknown and is never recorded as absent, because a
    /// check that failed is not evidence about what is there.
    /// </remarks>
    public TargetExistingObjectProbeResult Probe(
        SourceScanResult source,
        TargetProbeResult target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var observations = new List<ExistingObjectObservation>(source.Entries.Count);

        if (TargetPathBlockerRule.IsTargetBlocked(target))
        {
            // The target is not a destination at all. That is already a blocking finding of its own, so
            // nothing is queried here and no second conclusion is drawn about the same fact; the result
            // records only that no mapped location was covered.
            foreach (var entry in source.Entries)
            {
                observations.Add(UnqueriedLocation(
                    entry.RelativePath,
                    ExistingObjectGapReason.TargetRootUnusable,
                    RejectedTargetRootDiagnostic,
                    target.TargetDirectoryPath));
            }

            return new TargetExistingObjectProbeResult(
                TargetRootObservation.NotAttempted(RejectedTargetRootDiagnostic),
                observations,
                TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps);
        }

        var targetRoot = ObserveTargetRoot(target.TargetDirectoryPath);

        if (targetRoot.Kind is TargetRootObservationKind.ReparsePoint or TargetRootObservationKind.File)
        {
            var reason = targetRoot.Kind == TargetRootObservationKind.ReparsePoint
                ? ExistingObjectGapReason.BlockedByReparsePoint
                : ExistingObjectGapReason.TargetRootUnusable;

            foreach (var entry in source.Entries)
            {
                observations.Add(UnqueriedLocation(
                    entry.RelativePath,
                    reason,
                    targetRoot.Diagnostic,
                    target.TargetDirectoryPath));
            }

            return new TargetExistingObjectProbeResult(
                targetRoot,
                observations,
                TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps);
        }

        // Keyed by the composed path's exact identity, and reused only for this run. Two mappings that
        // differ only in letter case stay two separate questions, because a target may resolve them
        // differently and deciding that is not this milestone's to do.
        var resolvedLocations = new Dictionary<string, MappedLocation>(StringComparer.Ordinal);

        foreach (var entry in source.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            observations.Add(ObserveMappedLocation(
                entry.RelativePath,
                target.TargetDirectoryPath,
                resolvedLocations,
                cancellationToken));
        }

        var outcome = TargetExistingObjectProbeResult.HasFullCoverage(targetRoot, observations)
            ? TargetExistingObjectProbeOutcome.Completed
            : TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps;

        return new TargetExistingObjectProbeResult(targetRoot, observations, outcome);
    }

    /// <summary>Establishes what the target root itself is, without treating a failure to answer as a verdict.</summary>
    private TargetRootObservation ObserveTargetRoot(string targetPath)
    {
        if (!_fileSystemApi.TryGetFileAttributes(ToQueryPath(targetPath), out var attributes, out var nativeError))
        {
            // Not a gate. Microsoft documents that this query does not answer for a network share root,
            // and such a target is still a perfectly good destination; the mapped locations below it are
            // therefore examined anyway, and each one answers for itself.
            return TargetRootObservation.NotAnswered(nativeError, UnansweredTargetRootDiagnostic);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return TargetRootObservation.Answered(
                TargetRootObservationKind.ReparsePoint,
                ReparsePointTargetRootReportedDiagnostic);
        }

        return (attributes & FileAttributes.Directory) != 0
            ? TargetRootObservation.Answered(TargetRootObservationKind.Directory, DirectoryTargetRootDiagnostic)
            : TargetRootObservation.Answered(TargetRootObservationKind.File, FileTargetRootDiagnostic);
    }

    /// <summary>Examines the location one source object maps onto, ancestors first.</summary>
    private ExistingObjectObservation ObserveMappedLocation(
        string relativePath,
        string targetRoot,
        Dictionary<string, MappedLocation> resolvedLocations,
        CancellationToken cancellationToken)
    {
        if (!IsComposableRelativePath(relativePath))
        {
            return ExistingObjectObservation.NotQueried(
                relativePath,
                ExistingObjectGapReason.RelativePathNotQueryable,
                blockingAncestorRelativePath: null,
                nativeErrorCode: null,
                RelativePathNotQueryableDiagnostic,
                mappedTargetPath: null);
        }

        var mappedPath = Path.Join(targetRoot, relativePath);

        if (FirstAmbiguousComponent(relativePath) is { } ambiguousComponent)
        {
            // Querying a normalised spelling of this name would answer about a different object than the
            // mapping names, so nothing is asked at all. The naming rule still reports the name itself;
            // this milestone only declines to query through it.
            return ExistingObjectObservation.NotQueried(
                relativePath,
                ExistingObjectGapReason.AmbiguousName,
                blockingAncestorRelativePath: null,
                nativeErrorCode: null,
                $"The name component '{ambiguousComponent}' cannot be queried without being "
                + "reinterpreted, so nothing was asked about this location.",
                mappedPath);
        }

        foreach (var ancestorRelativePath in AncestorsOf(relativePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ancestor = Resolve(Path.Join(targetRoot, ancestorRelativePath), resolvedLocations);

            switch (ancestor.Kind)
            {
                case MappedLocationKind.Directory:
                    continue;

                case MappedLocationKind.File:
                    // A file cannot contain anything, so no object can exist at this location. The
                    // structural conflict belongs to the ancestor; repeating it for every descendant
                    // would report one problem a great many times.
                    return ExistingObjectObservation.NotQueried(
                        relativePath,
                        ExistingObjectGapReason.BlockedByExistingObject,
                        ancestorRelativePath,
                        nativeErrorCode: null,
                        $"The ancestor '{ancestorRelativePath}' is an ordinary file, so no object can "
                        + "exist at this location.",
                        mappedPath);

                case MappedLocationKind.ReparsePoint:
                    return ExistingObjectObservation.NotQueried(
                        relativePath,
                        ExistingObjectGapReason.BlockedByReparsePoint,
                        ancestorRelativePath,
                        nativeErrorCode: null,
                        $"The ancestor '{ancestorRelativePath}' is a reparse point, which is not looked "
                        + "through, so nothing beneath it was queried.",
                        mappedPath);

                case MappedLocationKind.Missing:
                    return ExistingObjectObservation.InferredMissing(
                        relativePath,
                        mappedPath,
                        ancestorRelativePath,
                        ancestor.NativeErrorCode,
                        $"The ancestor '{ancestorRelativePath}' is currently absent, so nothing can exist "
                        + "at this location. This was derived from that ancestor and was not queried "
                        + "here.");

                default:
                    return ExistingObjectObservation.NotQueried(
                        relativePath,
                        ExistingObjectGapReason.BlockedByFailedCheck,
                        ancestorRelativePath,
                        ancestor.NativeErrorCode,
                        $"The ancestor '{ancestorRelativePath}' could not be checked, so nothing beneath "
                        + "it was queried and nothing is claimed about this location.",
                        mappedPath);
            }
        }

        var location = Resolve(mappedPath, resolvedLocations);

        return location.Kind switch
        {
            MappedLocationKind.Directory => ExistingObjectObservation.Observed(
                relativePath, mappedPath, TargetObjectPresence.Directory, DirectoryDiagnostic),
            MappedLocationKind.File => ExistingObjectObservation.Observed(
                relativePath, mappedPath, TargetObjectPresence.File, FileDiagnostic),
            MappedLocationKind.ReparsePoint => ExistingObjectObservation.Observed(
                relativePath, mappedPath, TargetObjectPresence.ReparsePoint, ReparsePointDiagnostic),
            MappedLocationKind.Missing => ExistingObjectObservation.ObservedMissing(
                relativePath, mappedPath, location.NativeErrorCode, MissingDiagnostic),
            _ => ExistingObjectObservation.CheckFailed(
                relativePath, mappedPath, location.NativeErrorCode, NotAnsweredDiagnostic),
        };
    }

    /// <summary>Queries one location, remembering the answer for the rest of this run.</summary>
    private MappedLocation Resolve(string mappedPath, Dictionary<string, MappedLocation> resolvedLocations)
    {
        if (resolvedLocations.TryGetValue(mappedPath, out var remembered))
        {
            return remembered;
        }

        var resolved = _fileSystemApi.TryGetFileAttributes(
            ToQueryPath(mappedPath),
            out var attributes,
            out var nativeError)
            ? DescribeAttributes(attributes)
            : DescribeFailure(nativeError);

        resolvedLocations[mappedPath] = resolved;

        return resolved;
    }

    /// <summary>
    /// Interprets reported attributes, judging the reparse attribute before the directory attribute.
    /// </summary>
    /// <remarks>
    /// Windows reports both attributes for a junction or a mount point. Reading such an object as an
    /// ordinary directory would describe something this milestone deliberately does not look through,
    /// and would then let a "the destination is already a directory" conclusion be drawn about an
    /// object whose target is unknown.
    /// </remarks>
    private static MappedLocation DescribeAttributes(FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return new MappedLocation(MappedLocationKind.ReparsePoint, null, ReparsePointDiagnostic);
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return new MappedLocation(MappedLocationKind.Directory, null, DirectoryDiagnostic);
        }

        return new MappedLocation(MappedLocationKind.File, null, FileDiagnostic);
    }

    /// <summary>Interprets a failed attribute query, keeping the native error as the evidence.</summary>
    /// <remarks>
    /// Only "the last component of this path does not exist" is treated as evidence of absence. Every
    /// other failure — a path that cannot be resolved, access denied, a sharing violation, an
    /// unreachable network name, a query length limit — leaves existence and kind unknown, because
    /// Windows did not answer the question that was asked. A failed check is not evidence about what is
    /// there.
    /// </remarks>
    private static MappedLocation DescribeFailure(int nativeError)
    {
        // ERROR_SUCCESS is not an error, so a failed call that reported no code at all yields no code
        // rather than a fabricated one.
        var reportedError = nativeError == 0 ? (int?)null : nativeError;

        return nativeError == ErrorFileNotFound
            ? new MappedLocation(MappedLocationKind.Missing, reportedError, MissingDiagnostic)
            : new MappedLocation(MappedLocationKind.NotAnswered, reportedError, NotAnsweredDiagnostic);
    }

    /// <summary>
    /// The relative paths of every ancestor of an entry, shallowest first.
    /// </summary>
    /// <remarks>
    /// Derived from the observed relative path by plain string splitting, so no ancestor is invented and
    /// no normalisation is applied. Resolving them in this order is what makes the result independent of
    /// the order the inventory arrived in.
    /// </remarks>
    private static IReadOnlyList<string> AncestorsOf(string relativePath)
    {
        var ancestors = new List<string>();

        for (var parent = SourceRelativePathComponents.ParentDirectory(relativePath);
             parent.Length > 0;
             parent = SourceRelativePathComponents.ParentDirectory(parent))
        {
            ancestors.Add(parent);
        }

        ancestors.Reverse();

        return ancestors;
    }

    /// <summary>Records a location that was not queried, naming the mapping location where one can be composed.</summary>
    private static ExistingObjectObservation UnqueriedLocation(
        string relativePath,
        ExistingObjectGapReason reason,
        string diagnostic,
        string targetRoot)
        => ExistingObjectObservation.NotQueried(
            relativePath,
            reason,
            blockingAncestorRelativePath: null,
            nativeErrorCode: null,
            diagnostic,
            IsComposableRelativePath(relativePath) ? Path.Join(targetRoot, relativePath) : null);

    /// <summary>
    /// Whether the relative path can be composed into a target location without changing what the
    /// mapping means.
    /// </summary>
    /// <remarks>
    /// A rooted relative path, an empty component (from a repeated separator) or a traversal component
    /// would all resolve somewhere other than the location the mapping names. None of them is repaired
    /// into something queryable: the mapping is kept as observed and the location is left unexamined.
    /// A trailing separator is not part of a name, so it is not treated as an empty component.
    /// </remarks>
    private static bool IsComposableRelativePath(string relativePath)
    {
        if (relativePath.Length == 0 || IsRooted(relativePath))
        {
            return false;
        }

        foreach (var component in SplitComponents(relativePath))
        {
            if (component.Length == 0 || component == "." || component == "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The first name component that cannot be queried as written, or <see langword="null"/> when every
    /// component can.
    /// </summary>
    /// <remarks>
    /// The whole path is checked, not only its last component: a mapping whose parent directory carries
    /// such a name cannot be queried truthfully either, so a normal leaf name does not license walking
    /// through an ambiguous ancestor.
    /// </remarks>
    private static string? FirstAmbiguousComponent(string relativePath)
    {
        foreach (var component in SplitComponents(relativePath))
        {
            if (IsAmbiguouslyNamed(component))
            {
                return component;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a name cannot be queried without being reinterpreted.
    /// </summary>
    /// <remarks>
    /// The three checks are the naming-compatibility rule's own, reused rather than restated, so the set
    /// of names this probe declines to query can never drift away from the set that rule reports. Their
    /// severity is not re-decided here: whether such a name is an Error or a Warning for a migration
    /// remains the naming rule's business.
    /// </remarks>
    private static bool IsAmbiguouslyNamed(string component)
        => WindowsNameCompatibilityRule.InvalidCharactersIn(component).Count > 0
            || WindowsNameCompatibilityRule.ReservedDeviceNameIn(component) is not null
            || WindowsNameCompatibilityRule.EndsWithSpaceOrPeriod(component);

    /// <summary>
    /// The form in which a composed path is handed to Windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A path already expressed in a namespace of its own is passed through unchanged: it must not
    /// acquire a second prefix, and the <c>\\.\</c> device namespace is not an extended-length namespace
    /// to be converted into.
    /// </para>
    /// <para>
    /// A short path is queried exactly as the mapping composes it, so the query and the mapping agree.
    /// A long one is queried in the extended-length spelling of the very same name, which lets a long
    /// but unambiguous location still be examined instead of being skipped wholesale.
    /// </para>
    /// <para>
    /// This is an implementation detail of the query only. The mapping location recorded on an
    /// observation is always the composed path, never this form, and the rule layer's own notion of the
    /// destination path is untouched by it.
    /// </para>
    /// </remarks>
    private static string ToQueryPath(string mappedPath)
    {
        if (mappedPath.StartsWith(ExtendedLengthPrefix, StringComparison.OrdinalIgnoreCase)
            || mappedPath.StartsWith(DeviceNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return mappedPath;
        }

        if (mappedPath.Length < MaxPath)
        {
            return mappedPath;
        }

        if (mappedPath.StartsWith(NetworkPrefix, StringComparison.Ordinal))
        {
            // \\server\share\… becomes \\?\UNC\server\share\…, the extended-length spelling of the same
            // network location rather than a different one.
            return ExtendedLengthPrefix + "UNC" + mappedPath[1..];
        }

        return IsDriveRooted(mappedPath)
            ? ExtendedLengthPrefix + mappedPath
            : mappedPath;
    }

    /// <summary>The name components of a relative path, ignoring a trailing separator.</summary>
    private static IReadOnlyList<string> SplitComponents(string relativePath)
        => relativePath.TrimEnd(Separators).Split(Separators, StringSplitOptions.None);

    /// <summary>Whether a relative path starts at a root of its own rather than below the target root.</summary>
    private static bool IsRooted(string relativePath)
        => relativePath.Length > 0
            && (relativePath[0] is '\\' or '/'
                || (relativePath.Length >= 2 && relativePath[1] == ':' && IsAsciiLetter(relativePath[0])));

    /// <summary>Whether a composed path begins with a drive root, the only drive form worth prefixing.</summary>
    private static bool IsDriveRooted(string path)
        => path.Length >= 3 && path[1] == ':' && IsAsciiLetter(path[0]) && path[2] is '\\' or '/';

    private static bool IsAsciiLetter(char value)
        => (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');

    /// <summary>What one queried location turned out to be, or why the query did not settle it.</summary>
    /// <remarks>
    /// <see cref="NativeErrorCode"/> is <see langword="null"/> both for a location that was answered and
    /// for a failed call that reported no error code at all, because <c>0</c> is <c>ERROR_SUCCESS</c>
    /// and is not an error to report.
    /// </remarks>
    private readonly record struct MappedLocation(
        MappedLocationKind Kind,
        int? NativeErrorCode,
        string Diagnostic);

    private enum MappedLocationKind
    {
        Directory,
        File,
        ReparsePoint,
        Missing,
        NotAnswered,
    }
}
