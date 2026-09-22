using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Turns the objects that already occupy the mapped destinations into findings.
/// </summary>
/// <remarks>
/// <para>
/// This is the only rule that consumes the existing-object check, and it consumes it as facts: it does
/// no I/O of its own, opens no target path and never asks whether anything exists. It reads what the
/// check established, and what the check could not establish, and states both.
/// </para>
/// <para>
/// The verdicts are deliberately narrow. Finding a file where a source file maps onto does <b>not</b>
/// mean the two files are the same, that the source can be skipped, or that a copy would overwrite
/// anything: nothing about either file's content was read, and the behaviour of the program that
/// performs the copy is not known to this tool. What is known is that the location is occupied, which
/// is why it is a Warning rather than an Error.
/// </para>
/// <para>
/// A true type conflict — a file where a directory maps onto, or a directory where a file maps onto —
/// <i>is</i> an Error, because it is decided by the objects' own kinds and needs no assumption about
/// any copy program: the object cannot arrive at that path while the other object occupies it. The
/// finding says so and stops there; it never proposes deleting, renaming or re-routing anything, and
/// the mapping is not changed to dodge the clash.
/// </para>
/// <para>
/// A location that was not examined produces no collision conclusion, and a check that did not answer
/// produces a Warning saying so rather than an absence. Those are different from each other and
/// different from an empty location, and the findings keep them apart: claiming a destination is free
/// because a check failed would be a fabrication, and quietly reporting nothing would let a gap be
/// read as an all-clear.
/// </para>
/// <para>
/// Nothing is repeated per descendant. A branch stopped by an ancestor, by a reparse point or by a
/// failed parent check is described once, at its cause, and the objects behind it are covered by a
/// single target-wide coverage finding rather than by one informational finding each.
/// </para>
/// </remarks>
internal static class TargetExistingObjectRule
{
    internal static IEnumerable<Finding> Evaluate(
        SourceScanResult source,
        TargetExistingObjectProbeResult? existingObjects)
    {
        // A check that was never supplied is a gap, not a result. It is stated explicitly so that a
        // report produced without one can never be read as "no collisions were found".
        if (existingObjects is null || existingObjects.Outcome == TargetExistingObjectProbeOutcome.NotPerformed)
        {
            yield return CreateNotPerformedFinding();

            yield break;
        }

        if (existingObjects.TargetRoot.Kind == TargetRootObservationKind.ReparsePoint)
        {
            yield return CreateTargetRootReparsePointFinding(existingObjects.TargetRoot);
        }

        var entries = IndexEntries(source);

        foreach (var observation in existingObjects.Observations)
        {
            if (!entries.TryGetValue(observation.SourceRelativePath, out var entry))
            {
                // Nothing in this inventory maps onto that path, so there is no mapping to judge.
                continue;
            }

            switch (observation.Kind)
            {
                case ExistingObjectObservationKind.Observed:
                    if (CreateCollisionFinding(entry, observation) is { } collision)
                    {
                        yield return collision;
                    }

                    break;

                case ExistingObjectObservationKind.CheckFailed:
                    yield return CreateCheckFailedFinding(observation);
                    break;

                default:
                    // An established absence needs no finding, an inferred absence needs none either,
                    // and a location that was not queried is accounted for by the coverage finding
                    // below instead of by one finding per affected object.
                    break;
            }
        }

        if (existingObjects.Outcome == TargetExistingObjectProbeOutcome.Cancelled)
        {
            yield return CreateCancelledFinding(existingObjects);
        }
        else if (existingObjects.Outcome == TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps)
        {
            yield return CreateCoverageFinding(existingObjects);
        }
    }

    /// <summary>The mapping matrix: what an observed object at a mapped location means for its source object.</summary>
    /// <remarks>
    /// <para>
    /// The target object is judged first. A reparse point is not an ordinary file or directory, and it
    /// is not classified any further than that, so it never enters the file/directory matrix below —
    /// the same precedence the attribute constants themselves impose.
    /// </para>
    /// <para>
    /// A source object that is itself a reparse point is judged next, and its kind is deliberately not
    /// used to sharpen the conflict: what a reparse point becomes when it is migrated is not
    /// established, so "the shapes look different" would not support a precise type conflict. Only
    /// that the mapping is occupied is reported.
    /// </para>
    /// <para>
    /// A directory over a directory is not reported at all. The two merge rather than clash, and one
    /// undifferentiated finding per existing directory would add noise without adding information; the
    /// objects inside them are compared on their own.
    /// </para>
    /// </remarks>
    private static Finding? CreateCollisionFinding(SourceEntry entry, ExistingObjectObservation observation)
    {
        var presence = observation.Presence!.Value;
        var mappedPath = observation.MappedTargetPath!;

        if (presence == TargetObjectPresence.ReparsePoint)
        {
            return CreateExistingReparsePointFinding(entry.RelativePath, mappedPath);
        }

        if (entry.IsReparsePoint)
        {
            return CreateSourceReparsePointCollisionFinding(entry.RelativePath, mappedPath, presence);
        }

        var sourceIsDirectory = entry.Kind == SourceEntryKind.Directory;
        var targetIsDirectory = presence == TargetObjectPresence.Directory;

        if (sourceIsDirectory && targetIsDirectory)
        {
            return null;
        }

        return sourceIsDirectory != targetIsDirectory
            ? CreateTypeCollisionFinding(entry.RelativePath, mappedPath, sourceIsDirectory)
            : CreateExistingFileFinding(entry.RelativePath, mappedPath);
    }

    private static Finding CreateExistingFileFinding(string relativePath, string mappedPath)
        => new(
            WindowsPreflightFindingCodes.TargetExistingFileCollision,
            Severity.Warning,
            "A file already exists at this destination",
            $"Following the source-to-target mapping, this source file would be created as "
            + $"'{mappedPath}', and a file of that name is already there. CopyPreflight has read "
            + "neither file, so it cannot tell whether their contents are the same, which one is newer, "
            + "or how they differ.",
            "What happens depends on the copy program you intend to use: it may replace the existing "
            + "file, skip the source file, ask you what to do, or keep both under different names. "
            + "CopyPreflight cannot tell which of those it will do, and this finding is not a statement "
            + "that the existing file would be replaced.",
            "Look at the existing file and decide what should happen to it before migrating. "
            + "CopyPreflight never overwrites, renames or deletes anything by itself.",
            sourceRelativePath: relativePath);

    private static Finding CreateTypeCollisionFinding(
        string relativePath,
        string mappedPath,
        bool sourceIsDirectory)
        => new(
            WindowsPreflightFindingCodes.TargetExistingTypeCollision,
            Severity.Error,
            "A directory and a file need the same destination",
            $"Following the source-to-target mapping, this source "
            + (sourceIsDirectory ? "directory" : "file")
            + $" would be created as '{mappedPath}', but an ordinary "
            + (sourceIsDirectory ? "file" : "directory")
            + " already exists there. An object cannot be created at a path that is already occupied "
            + "by an object of the other kind.",
            "This exact mapping is structurally blocked: nothing can arrive at that path while the "
            + "existing object is there, and no destination-dependent compatibility check for this "
            + "object can describe a migration that would happen as mapped. CopyPreflight does not "
            + "delete or rename the existing object, does not move the source object somewhere else, "
            + "and does not change the mapping to avoid the clash.",
            $"Decide how the existing object at '{mappedPath}' should be handled before migrating, "
            + "then run the preflight check again.",
            sourceRelativePath: relativePath);

    private static Finding CreateExistingReparsePointFinding(string relativePath, string mappedPath)
        => new(
            WindowsPreflightFindingCodes.TargetExistingReparsePoint,
            Severity.Warning,
            "A special file system object already exists at this destination",
            $"The location this object maps onto — '{mappedPath}' — already holds an object that "
            + "carries the reparse point attribute. CopyPreflight does not classify it as a symbolic "
            + "link, a junction, a mount point or anything else, does not resolve what it points at, "
            + "and did not examine anything beneath it because of it.",
            "What happens depends on the copy program you intend to use: it may replace the existing "
            + "reparse point, write through it into whatever it points at, skip the source object, or "
            + "behave differently again. CopyPreflight draws no conclusion about the objects below this "
            + "location, because their existence depends on where it points.",
            "Confirm what the existing object is and how your copy program handles reparse points "
            + "before migrating. CopyPreflight never replaces or follows anything by itself.",
            sourceRelativePath: relativePath);

    private static Finding CreateSourceReparsePointCollisionFinding(
        string relativePath,
        string mappedPath,
        TargetObjectPresence presence)
        => new(
            WindowsPreflightFindingCodes.SourceReparsePointExistingTargetObject,
            Severity.Warning,
            "Source reparse point maps onto an existing object",
            $"This source object carries the reparse point attribute, and the location it maps onto — "
            + $"'{mappedPath}' — already holds an ordinary "
            + (presence == TargetObjectPresence.Directory ? "directory" : "file")
            + ". CopyPreflight does not classify the source reparse point, so it cannot say whether the "
            + "two are of the same kind or whether one would replace the other.",
            "How a reparse point is migrated is not established by this check — a copy program may "
            + "copy the object itself, follow it, skip it, or do something else — and the destination "
            + "is occupied as well. The existing object may be replaced, or the source object may never "
            + "arrive under this name.",
            "Confirm how the copy program you intend to use treats reparse points, and decide what "
            + "should happen to the existing object, before migrating.",
            sourceRelativePath: relativePath);

    private static Finding CreateCheckFailedFinding(ExistingObjectObservation observation)
        => new(
            WindowsPreflightFindingCodes.TargetExistingObjectCheckFailed,
            Severity.Warning,
            "Could not check whether this destination is already occupied",
            $"CopyPreflight asked Windows what, if anything, already occupies '{observation.MappedTargetPath}', "
            + "and Windows did not answer."
            + DescribeEvidence(observation),
            "Whether an object already exists at that location stays unknown, so no conclusion about a "
            + "destination collision is drawn there. This is not a statement that the target can be "
            + "written to, and it is not a statement that the location is empty.",
            "Check that location and the access the current user has to it, then run the preflight "
            + "check again.",
            sourceRelativePath: observation.SourceRelativePath);

    private static Finding CreateCoverageFinding(TargetExistingObjectProbeResult result)
    {
        var breakdown = DescribeGapBreakdown(result);

        return new Finding(
            WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete,
            Severity.Info,
            "Only part of the inventory was checked for existing objects",
            $"The check for objects that already occupy the mapped destinations ran, but {breakdown.GapCount} "
            + $"of the {result.Observations.Count} mapped locations were not examined: {breakdown.Summary}. "
            + "A location can be left unexamined because an ancestor is a reparse point, because a "
            + "parent could not be checked, because its name cannot be queried without being "
            + "reinterpreted, or because the relative path cannot be composed into a target location "
            + "without changing the mapping.",
            "Nothing is claimed about those locations: neither that an object already occupies one of "
            + "them, nor that they are empty. The absence of a destination finding for them means only "
            + "that no check was made.",
            "Review those locations if they matter to your migration. This version of the check cannot "
            + "examine them, and CopyPreflight never changes the mapping or the names to work around "
            + "that.");
    }

    private static Finding CreateCancelledFinding(TargetExistingObjectProbeResult result)
    {
        var breakdown = DescribeGapBreakdown(result);

        return new Finding(
            WindowsPreflightFindingCodes.TargetExistingObjectCheckCancelled,
            Severity.Info,
            "The check for existing objects was cancelled",
            $"The check for objects that already occupy the mapped destinations was cancelled before it "
            + $"finished: {result.Observations.Count - breakdown.GapCount} of the "
            + $"{result.Observations.Count} mapped locations had been covered when it stopped. A "
            + "cancelled check is not a finished one.",
            "Nothing is claimed about the locations the check never reached: they are unknown, and a "
            + "location that was not reached is not an empty one. This result must not be read as a "
            + "clean finding of no collisions.",
            "Run the preflight check again and let it finish if you need those locations covered.");
    }

    private static Finding CreateNotPerformedFinding()
        => new(
            WindowsPreflightFindingCodes.TargetExistingObjectCheckNotPerformed,
            Severity.Info,
            "Existing objects at the destinations were not checked",
            "No check for objects that already occupy the mapped destinations was supplied for this "
            + "evaluation, so nothing about destination collisions was examined. This is not a report "
            + "of a clean result.",
            "The report covers no existing-object facts at all, so the absence of a destination "
            + "collision finding does not mean that the destinations are free. An object may already "
            + "occupy any of them.",
            "Carry out the existing-object check before relying on this report, or treat every "
            + "destination as one that still has to be examined.");

    private static Finding CreateTargetRootReparsePointFinding(TargetRootObservation targetRoot)
        => new(
            WindowsPreflightFindingCodes.TargetExistingReparsePoint,
            Severity.Warning,
            "The target root is a special file system object",
            "The target directory itself carries the reparse point attribute. CopyPreflight does not "
            + "classify it as a symbolic link, a junction, a mount point or anything else, does not "
            + "resolve what it points at, and did not examine any of the mapped destinations beneath "
            + "it."
            + DescribeEvidence(targetRoot.NativeErrorCode, targetRoot.Diagnostic),
            "None of the mapped destinations could be checked, because the objects below the target "
            + "root were deliberately not looked at: what is there depends on where the root points.",
            "Confirm what the target root is, and how your copy program handles it, before migrating. "
            + "Choosing a target that is an ordinary directory would let the destinations be checked.");

    /// <summary>How many locations are uncovered, and why, as a deterministic one-line summary.</summary>
    private static (int GapCount, string Summary) DescribeGapBreakdown(TargetExistingObjectProbeResult result)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var gaps = 0;

        foreach (var observation in result.Observations)
        {
            if (!observation.IsCoverageGap)
            {
                continue;
            }

            gaps++;

            var reason = DescribeGapReason(observation);
            counts[reason] = counts.TryGetValue(reason, out var count) ? count + 1 : 1;
        }

        return (
            gaps,
            counts.Count == 0
                ? "no reason was recorded"
                : string.Join(", ", counts.Select(pair => $"{pair.Value} because {pair.Key}")));
    }

    /// <summary>
    /// A short phrase for the reason one location is uncovered, phrased so that it can be read after
    /// "because" as well as counted.
    /// </summary>
    private static string DescribeGapReason(ExistingObjectObservation observation)
        => observation.Kind == ExistingObjectObservationKind.CheckFailed
            ? "the check did not answer"
            : observation.GapReason switch
            {
                ExistingObjectGapReason.BlockedByReparsePoint => "an ancestor is a reparse point",
                ExistingObjectGapReason.BlockedByFailedCheck => "an ancestor could not be checked",
                ExistingObjectGapReason.AmbiguousName =>
                    "a name cannot be queried without being reinterpreted",
                ExistingObjectGapReason.RelativePathNotQueryable =>
                    "the relative path cannot be composed into a target location",
                ExistingObjectGapReason.TargetRootUnusable => "the target is not a usable directory",
                ExistingObjectGapReason.Cancelled => "the check stopped before reaching them",
                _ => "no reason was recorded",
            };

    /// <summary>
    /// Relates each observation to its source object by the time-independent mapping identity: the
    /// relative path. The first entry for a repeated relative path is used; an inventory does not
    /// contain two different objects at one path, and matching on arrival order would make the result
    /// depend on an enumeration artifact.
    /// </summary>
    /// <remarks>
    /// A relative path that an observed entry lies below is a directory in the source, because the
    /// mapping could not have named an object below a file. Those directories are indexed as what they
    /// are, so that the structural conflict of an existing file sitting on one of them is reported even
    /// when the inventory listed only the objects inside it. Nothing is invented: an explicit entry at
    /// the same path always wins, and no other kind of source object is derived from a path shape.
    /// </remarks>
    private static Dictionary<string, SourceEntry> IndexEntries(SourceScanResult source)
    {
        var entries = new Dictionary<string, SourceEntry>(StringComparer.Ordinal);

        foreach (var entry in source.Entries)
        {
            entries.TryAdd(entry.RelativePath, entry);
        }

        foreach (var entry in source.Entries)
        {
            for (var parent = SourceRelativePathComponents.ParentDirectory(entry.RelativePath);
                 parent.Length > 0;
                 parent = SourceRelativePathComponents.ParentDirectory(parent))
            {
                entries.TryAdd(parent, SourceEntry.Directory(parent));
            }
        }

        return entries;
    }

    /// <summary>Describes the failed query using evidence that does not depend on the display language.</summary>
    private static string DescribeEvidence(ExistingObjectObservation observation)
        => observation.NativeErrorCode is { } nativeError
            ? $" (observed: GetFileAttributesW reported Windows error {nativeError})"
            : " (observed: GetFileAttributesW reported no answer)";

    private static string DescribeEvidence(int? nativeError, string diagnostic)
        => nativeError is { } code
            ? $" (observed: GetFileAttributesW reported Windows error {code})"
            : $" ({diagnostic})";
}
