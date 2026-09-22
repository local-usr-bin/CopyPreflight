using System.Runtime.Versioning;
using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Discovers what Windows can reliably report about a target directory, and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// This probe answers one question: <i>"for the target directory the user picked, what is Windows
/// actually able to tell me?"</i> It establishes the three capabilities
/// <see cref="TargetCapabilities"/> currently models — the file system name, the space available to
/// the calling user, and the maximum length of a single path component — and it produces no
/// <see cref="Finding"/>. Deciding what those facts mean for a migration is the rule layer's job.
/// </para>
/// <para>
/// Each capability is established independently and stays <see cref="CapabilityValue{T}.Unknown"/>
/// when it cannot be established. A failure never discards a capability that was already obtained:
/// a network target whose free space is known but whose file system cannot be described is a normal,
/// expected outcome, not a failure of the probe.
/// </para>
/// <para>
/// Whether the target is a usable directory is decided from <b>positive evidence only</b>: a
/// directory Windows actually reported, or a free-space answer that only a directory can produce.
/// A query that fails to answer is never taken as proof that the target is unusable, because Windows
/// is documented not to answer the attribute query for a network share root such as
/// <c>\\Server\Share</c> — a target this probe must support. That failure therefore holds nothing
/// back; the remaining questions are still asked.
/// </para>
/// <para>
/// The probe is strictly read-only. It never creates a file, creates a directory, writes an
/// alternate data stream or changes an ACL, so it cannot and does not claim that a target is
/// writable — only that Windows answered these particular queries.
/// </para>
/// <para>
/// The type is Windows-only and holds no state, so one instance can be reused for any number of
/// targets.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsTargetCapabilityProbe
{
    /// <summary>
    /// The Win32 error Windows reports when a path's name syntax is invalid, as opposed to a
    /// well-formed name that resolves to nothing. Verified against
    /// <c>GetFileAttributesW</c> on a path containing reserved characters.
    /// </summary>
    private const int ErrorInvalidName = 123;

    private const string GetFileAttributesOperation = "GetFileAttributesW";
    private const string GetDiskFreeSpaceExOperation = "GetDiskFreeSpaceExW";
    private const string GetVolumePathNameOperation = "GetVolumePathNameW";
    private const string GetVolumeInformationOperation = "GetVolumeInformationW";

    private const string NotADirectoryReason =
        "The target path names an existing object that is not a directory.";

    private const string MalformedNameReason =
        "Windows rejected the target path as a malformed name.";

    private const string UnreachableTargetReason =
        "No usable object could be reached at the target path.";

    private const string UnavailableTargetReason =
        "The target path is not an available directory, so nothing about its file system is known.";

    private const string VolumeNotDeterminedReason =
        "The volume the target belongs to could not be determined, so this could not be observed.";

    private const string VolumeNotDescribedReason =
        "Windows identified the target volume but did not describe the file system on it.";

    private const string FreeSpaceNotReportedReason =
        "Windows did not report the space available to the current user on the target.";

    private readonly IWindowsFileSystemApi _fileSystemApi;

    /// <summary>Creates a probe that queries the real Windows file system.</summary>
    public WindowsTargetCapabilityProbe()
        : this(new Kernel32FileSystemApi())
    {
    }

    /// <summary>Creates a probe over a supplied Windows query seam. For tests only.</summary>
    internal WindowsTargetCapabilityProbe(IWindowsFileSystemApi fileSystemApi)
    {
        ArgumentNullException.ThrowIfNull(fileSystemApi);

        _fileSystemApi = fileSystemApi;
    }

    /// <summary>Probes one target directory and reports what Windows could say about it.</summary>
    /// <param name="targetDirectoryPath">
    /// The destination directory of a migration. Relative inputs are resolved against the process's
    /// current directory. The path is expected to name a directory.
    /// </param>
    /// <returns>
    /// The normalized target path, the capabilities that could be established, and the failures that
    /// kept any of them unknown.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="targetDirectoryPath"/> is null, empty, whitespace, or a malformed path that
    /// <see cref="Path.GetFullPath(string)"/> itself refuses (which also surfaces
    /// <see cref="NotSupportedException"/> or <see cref="PathTooLongException"/> for inputs the
    /// platform cannot resolve at all).
    /// </exception>
    /// <remarks>
    /// <para>
    /// A target that is well formed but unusable — missing, not a directory, unreachable over the
    /// network, or refused by permissions — is <b>not</b> an exception. It is reported as a
    /// <see cref="TargetProbeIssueKind.TargetPathUnavailable"/> (or
    /// <see cref="TargetProbeIssueKind.TargetPathInvalid"/>) issue, and every capability stays
    /// unknown.
    /// </para>
    /// <para>
    /// A target Windows will not describe by attribute — notably a network share root — is only
    /// reported as unusable if no other query could reach a directory through it either. The
    /// free-space question in particular is asked before that conclusion is drawn, so a share root
    /// that answers it is probed normally.
    /// </para>
    /// <para>
    /// Nothing is written to the target at any point, including to check whether it is writable.
    /// </para>
    /// </remarks>
    public TargetProbeResult Probe(string targetDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(targetDirectoryPath))
        {
            throw new ArgumentException(
                "A target directory path must not be null, empty or whitespace.",
                nameof(targetDirectoryPath));
        }

        var targetPath = Path.GetFullPath(targetDirectoryPath);
        var issues = new List<TargetProbeIssue>();

        // Step 1 — what kind of object the target names. A failure to answer is recorded as an error
        // code but is deliberately not treated as unavailability: Windows does not answer this
        // question for a network share root, which is a target this probe must support.
        var identity = ProbeTargetIdentity(targetPath, out var identityError);

        if (identity == TargetIdentity.NotADirectory)
        {
            // A positive observation: Windows found an object there and it is not a directory, so
            // the target cannot be used and there is no point asking anything else about it.
            issues.Add(CreateIssue(
                TargetProbeIssueKind.TargetPathUnavailable,
                targetPath,
                GetFileAttributesOperation,
                nativeError: 0,
                NotADirectoryReason));

            return new TargetProbeResult(targetPath, UnavailableTargetCapabilities(), issues);
        }

        // Step 2 — the space available to the caller. This is asked of every target that might be a
        // directory, and its answer doubles as evidence that a directory is reachable: Windows
        // answers it for a directory (including a share root) and refuses it for a file.
        var freeSpace = ProbeAvailableFreeBytes(targetPath);

        if (identity != TargetIdentity.Directory && !freeSpace.Answered)
        {
            // No query produced positive evidence of a usable directory, so there is nothing to
            // describe. The volume questions are not asked, because a volume path can be resolved
            // from a name alone and would describe a volume the target may not even reach.
            issues.Add(CreateUnavailableTargetIssue(targetPath, identityError, freeSpace.NativeError));

            return new TargetProbeResult(targetPath, UnavailableTargetCapabilities(), issues);
        }

        // From here the target is a reachable directory, and each capability is established on its
        // own so that one failure cannot discard another capability that was obtained.
        if (freeSpace.Issue is not null)
        {
            issues.Add(freeSpace.Issue);
        }

        var volume = ProbeVolume(targetPath, issues);

        return new TargetProbeResult(
            targetPath,
            new TargetCapabilities(volume.FileSystemName, freeSpace.Value, volume.MaxPathComponentLength),
            issues);
    }

    /// <summary>
    /// Establishes what kind of object the target names, through a read-only attribute query: it
    /// observes the path, it does not write to it.
    /// </summary>
    /// <param name="targetPath">The normalized target directory path.</param>
    /// <param name="nativeError">
    /// The error Windows reported when it did not answer, or <c>0</c> when it did.
    /// </param>
    private TargetIdentity ProbeTargetIdentity(string targetPath, out int nativeError)
    {
        if (!_fileSystemApi.TryGetFileAttributes(targetPath, out var attributes, out nativeError))
        {
            // Windows is documented not to answer this query for a network share root, so a failure
            // proves nothing about whether the target is usable. The error code is kept so that an
            // unusable target can still be classified once the other queries have had their say.
            return TargetIdentity.Undetermined;
        }

        nativeError = 0;

        return (attributes & FileAttributes.Directory) != 0
            ? TargetIdentity.Directory
            : TargetIdentity.NotADirectory;
    }

    /// <summary>Establishes how much space the calling user still has on the target's volume.</summary>
    private FreeSpaceProbe ProbeAvailableFreeBytes(string targetPath)
    {
        // Windows documents that a UNC name must carry a trailing backslash, and a directory path is
        // valid with one. The separator is therefore normalized once for every target rather than
        // special-cased for UNC, which keeps one code path for local, mounted and network targets and
        // avoids translating a UNC path into anything else.
        var queryPath = EnsureTrailingDirectorySeparator(targetPath);

        if (!_fileSystemApi.TryGetFreeBytesAvailableToCaller(
                queryPath,
                out var availableToCaller,
                out var nativeError))
        {
            return new FreeSpaceProbe(
                Answered: false,
                NativeError: nativeError,
                Value: CapabilityValue<long>.Unknown(FreeSpaceNotReportedReason),
                Issue: CreateIssue(
                    TargetProbeIssueKind.FreeSpaceQueryFailed,
                    queryPath,
                    GetDiskFreeSpaceExOperation,
                    nativeError,
                    FreeSpaceNotReportedReason));
        }

        if (availableToCaller > long.MaxValue)
        {
            // The native quantity is unsigned. Wrapping or clamping it would turn a real number into
            // a wrong one, so the amount is left unknown and the limitation is stated instead. The
            // query itself did answer, so it is still evidence of a reachable directory.
            return new FreeSpaceProbe(
                Answered: true,
                NativeError: 0,
                Value: CapabilityValue<long>.Unknown(
                    $"Windows reported {availableToCaller} bytes available to the current user on the target, " +
                    $"which exceeds the largest value the capability model can represent ({long.MaxValue}). " +
                    "The amount is left unknown rather than truncated."),
                Issue: null);
        }

        return new FreeSpaceProbe(
            Answered: true,
            NativeError: 0,
            Value: CapabilityValue<long>.Known((long)availableToCaller),
            Issue: null);
    }

    /// <summary>
    /// Identifies the target's volume through Windows' own mount point resolution and describes the
    /// file system on it.
    /// </summary>
    /// <remarks>
    /// The volume is resolved with the volume path query rather than by taking the path root: a
    /// target can sit on a mounted folder or a junction, where the volume it actually belongs to is
    /// not the drive-letter root, and a network target has share semantics that a path root does not
    /// express. If the volume cannot be resolved, no root is guessed in its place.
    /// </remarks>
    private (CapabilityValue<FileSystemName> FileSystemName, CapabilityValue<int> MaxPathComponentLength) ProbeVolume(
        string targetPath,
        List<TargetProbeIssue> issues)
    {
        if (!_fileSystemApi.TryGetVolumePathName(targetPath, out var volumePathName, out var volumePathError))
        {
            issues.Add(CreateIssue(
                TargetProbeIssueKind.VolumePathQueryFailed,
                targetPath,
                GetVolumePathNameOperation,
                volumePathError,
                "Windows did not report a volume path for the target, so its file system could not be described."));

            return (
                CapabilityValue<FileSystemName>.Unknown(VolumeNotDeterminedReason),
                CapabilityValue<int>.Unknown(VolumeNotDeterminedReason));
        }

        // Windows requires the root of a volume for the volume information query and rejects a path
        // inside one, so the path Windows reported is passed on as a volume root rather than
        // re-derived from the target.
        var volumeRootPath = EnsureTrailingDirectorySeparator(volumePathName);

        if (!_fileSystemApi.TryGetVolumeInformation(
                volumeRootPath,
                out var reportedName,
                out var reportedMaxComponentLength,
                out var nativeError))
        {
            issues.Add(CreateIssue(
                TargetProbeIssueKind.VolumeInformationQueryFailed,
                volumeRootPath,
                GetVolumeInformationOperation,
                nativeError,
                VolumeNotDescribedReason));

            return (
                CapabilityValue<FileSystemName>.Unknown(VolumeNotDescribedReason),
                CapabilityValue<int>.Unknown(VolumeNotDescribedReason));
        }

        // The two capabilities answered by one call stay independent of each other: a volume that
        // reports a name but not a usable component length is described exactly that way.
        return (
            DescribeFileSystemName(volumeRootPath, reportedName),
            DescribeMaxPathComponentLength(volumeRootPath, reportedMaxComponentLength));
    }

    /// <summary>Describes the reported file system name, or explains why it cannot be a known value.</summary>
    private static CapabilityValue<FileSystemName> DescribeFileSystemName(string volumeRootPath, string reportedName)
        => string.IsNullOrWhiteSpace(reportedName)
            ? CapabilityValue<FileSystemName>.Unknown(
                $"Windows reported no file system name for the volume at '{volumeRootPath}'.")
            : CapabilityValue<FileSystemName>.Known(new FileSystemName(reportedName));

    /// <summary>
    /// Describes the reported maximum component length, refusing to turn a value that cannot be a
    /// usable length into a known one.
    /// </summary>
    private static CapabilityValue<int> DescribeMaxPathComponentLength(string volumeRootPath, uint reportedLength)
    {
        if (reportedLength == 0)
        {
            // Zero is not a usable length. Reporting it as known would state a fact that cannot hold.
            return CapabilityValue<int>.Unknown(
                $"Windows reported a maximum path component length of 0 for the volume at '{volumeRootPath}', " +
                "which is not a usable length.");
        }

        if (reportedLength > int.MaxValue)
        {
            return CapabilityValue<int>.Unknown(
                $"Windows reported a maximum path component length of {reportedLength} for the volume at " +
                $"'{volumeRootPath}', which exceeds the largest value the capability model can represent " +
                $"({int.MaxValue}). The length is left unknown rather than truncated.");
        }

        return CapabilityValue<int>.Known((int)reportedLength);
    }

    /// <summary>
    /// Builds the single issue that explains why no usable directory could be reached, keeping the
    /// error Windows actually gave instead of classifying it from the shape of the path.
    /// </summary>
    /// <remarks>
    /// A name Windows refuses outright is a different fact from a name that is well formed but leads
    /// nowhere, and the two are reported apart. The issue is attributed to whichever query reported
    /// an error, so the evidence and its source stay together.
    /// </remarks>
    private static TargetProbeIssue CreateUnavailableTargetIssue(
        string targetPath,
        int identityError,
        int freeSpaceError)
    {
        var (operation, nativeError) = identityError != 0
            ? (GetFileAttributesOperation, identityError)
            : (GetDiskFreeSpaceExOperation, freeSpaceError);

        return CreateIssue(
            nativeError == ErrorInvalidName
                ? TargetProbeIssueKind.TargetPathInvalid
                : TargetProbeIssueKind.TargetPathUnavailable,
            targetPath,
            operation,
            nativeError,
            nativeError == ErrorInvalidName ? MalformedNameReason : UnreachableTargetReason);
    }

    /// <summary>The capability set of a target that could not be reached as a directory.</summary>
    private static TargetCapabilities UnavailableTargetCapabilities()
        => new(
            CapabilityValue<FileSystemName>.Unknown(UnavailableTargetReason),
            CapabilityValue<long>.Unknown(UnavailableTargetReason),
            CapabilityValue<int>.Unknown(UnavailableTargetReason));

    /// <summary>
    /// Appends the platform's directory separator when the path does not already end with one.
    /// Windows requires it for a UNC name and accepts it for any directory.
    /// </summary>
    private static string EnsureTrailingDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    /// <summary>Builds an issue, keeping the native error code only when there was one.</summary>
    private static TargetProbeIssue CreateIssue(
        TargetProbeIssueKind kind,
        string affectedPath,
        string operation,
        int nativeError,
        string summary)
        => new(
            kind,
            affectedPath,
            operation,
            // ERROR_SUCCESS is not an error: it is the state of a call that reported nothing to
            // report, so it becomes no error code rather than a fabricated one.
            nativeError == 0 ? null : nativeError,
            nativeError == 0 ? summary : $"{summary} (native error {nativeError})");

    /// <summary>
    /// What the attribute query established about the object the target names.
    /// </summary>
    /// <remarks>
    /// A failure to answer is deliberately kept apart from a negative answer. Windows does not answer
    /// this query for a network share root, so treating its failure as "unusable" would reject a
    /// perfectly good target.
    /// </remarks>
    private enum TargetIdentity
    {
        /// <summary>Windows did not answer, which is not evidence that the target is unusable.</summary>
        Undetermined = 0,

        /// <summary>The target is an existing directory.</summary>
        Directory = 1,

        /// <summary>The target is an existing object that is not a directory.</summary>
        NotADirectory = 2,
    }

    /// <summary>
    /// The outcome of the free-space query: whether Windows answered it, what the answer was worth,
    /// and the issue to report if it did not answer.
    /// </summary>
    /// <remarks>
    /// "Answered" is kept apart from "the amount is known" because a reported amount that cannot be
    /// represented still proves that a directory is reachable, which is what the availability
    /// decision reads this for.
    /// </remarks>
    private readonly record struct FreeSpaceProbe(
        bool Answered,
        int NativeError,
        CapabilityValue<long> Value,
        TargetProbeIssue? Issue);
}
