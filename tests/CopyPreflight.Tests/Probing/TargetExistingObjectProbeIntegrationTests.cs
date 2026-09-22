using System.IO;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The existing-object check against the real Windows file system, and the rule layer connected to it.
/// </summary>
/// <remarks>
/// <para>
/// These tests use only paths the test owns: a throwaway tree created under an ignored directory inside
/// this repository (see <see cref="TemporarySourceTree"/>), which needs no network share, no quota, no
/// mounted volume, no elevation and no Developer Mode, and never hardcodes which file system the test
/// machine happens to use or where it puts its temp files.
/// </para>
/// <para>
/// The directory junctions used here are created with <c>mklink /J</c>, which any Windows user can
/// create, so a genuine reparse point is exercised on disk without elevation or Developer Mode. What
/// cannot be produced here at all — a share root that refuses the attribute query, an access-denied
/// parent — is covered deterministically through the query seam instead; see
/// <see cref="TargetExistingObjectProbeTests"/>.
/// </para>
/// </remarks>
public class TargetExistingObjectProbeIntegrationTests
{
    [Fact]
    public void ReportsWhatAnExistingTargetDirectoryHoldsAndFeedsItToTheRuleLayer()
    {
        using var tree = new TemporarySourceTree();
        var target = tree.CreateDirectory("target");

        tree.CreateFile(@"target\report.txt", lengthInBytes: 4);
        tree.CreateDirectory(@"target\media");
        tree.CreateFile(@"target\blocked", lengthInBytes: 8);
        tree.CreateDirectory(@"target\incoming");
        tree.CreateDirectory(@"target\photos\2026");

        var source = new SourceScanResult(
            @"C:\source",
            [
                SourceEntry.File("report.txt", 10),
                SourceEntry.Directory("media"),
                SourceEntry.Directory("blocked"),
                SourceEntry.File("incoming", 10),
                SourceEntry.Directory("photos"),
                SourceEntry.Directory(@"photos\2026"),
                SourceEntry.File(@"photos\2026\a.jpg", 20),
                SourceEntry.File("absent.txt", 30),
            ],
            []);

        var probeResult = new WindowsTargetExistingObjectProbe().Probe(
            source,
            new WindowsTargetCapabilityProbe().Probe(target));

        Assert.Equal(TargetRootObservationKind.Directory, probeResult.TargetRoot.Kind);
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, probeResult.Outcome);

        Assert.Equal(TargetObjectPresence.File, probeResult.ObservationFor("report.txt").Presence);
        Assert.Equal(TargetObjectPresence.Directory, probeResult.ObservationFor("media").Presence);
        Assert.Equal(TargetObjectPresence.File, probeResult.ObservationFor("blocked").Presence);
        Assert.Equal(TargetObjectPresence.Directory, probeResult.ObservationFor("incoming").Presence);
        Assert.Equal(TargetObjectPresence.Directory, probeResult.ObservationFor("photos").Presence);
        Assert.Equal(
            TargetObjectPresence.Directory,
            probeResult.ObservationFor(@"photos\2026").Presence);

        // A real absence: the parent directory exists, so Windows answers that the last component is not
        // there, which is the one answer this check reads as evidence of absence.
        Assert.Equal(
            ExistingObjectObservationKind.ObservedMissing,
            probeResult.ObservationFor(@"photos\2026\a.jpg").Kind);
        Assert.Equal(
            ExistingObjectObservationKind.ObservedMissing,
            probeResult.ObservationFor("absent.txt").Kind);
        Assert.Equal(
            ScriptedFileSystemApi.ErrorFileNotFound,
            probeResult.ObservationFor("absent.txt").NativeErrorCode);

        var findings = new WindowsPreflightEvaluator().Evaluate(
            source,
            new WindowsTargetCapabilityProbe().Probe(target),
            probeResult);

        var fileCollision = findings.Only(WindowsPreflightFindingCodes.TargetExistingFileCollision);
        Assert.Equal(Severity.Warning, fileCollision.Severity);
        Assert.Equal("report.txt", fileCollision.SourceRelativePath);

        var typeConflicts = findings.WithCode(WindowsPreflightFindingCodes.TargetExistingTypeCollision);
        Assert.Equal(2, typeConflicts.Count);
        Assert.All(typeConflicts, finding => Assert.Equal(Severity.Error, finding.Severity));

        // Both directions of the type conflict are exercised on the real file system: a source
        // directory that maps onto an existing file, and a source file that maps onto an existing
        // directory.
        Assert.Contains(typeConflicts, finding => finding.SourceRelativePath == "blocked");
        Assert.Contains(typeConflicts, finding => finding.SourceRelativePath == "incoming");

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCheckFailed));
        Assert.Equal(3, findings.Count);
    }

    /// <summary>
    /// The check leaves the tree it examined as it found it.
    /// </summary>
    /// <remarks>
    /// What this compares is the observable shape of the target: every entry below it, and the length of
    /// every file in it. That is evidence that the check created nothing and wrote through nothing. It is
    /// not a system-level audit — it says nothing about metadata such as timestamps, nothing about the
    /// source tree, and nothing about anything outside this fixture. The stronger evidence for the same
    /// claim is the call chain itself, which reaches one attribute query and no write entry point at all.
    /// </remarks>
    [Fact]
    public void WritesNothingIntoTheSourceOrTheTarget()
    {
        using var tree = new TemporarySourceTree();
        var target = tree.CreateDirectory("target");
        var targetFile = tree.CreateFile(@"target\report.txt", lengthInBytes: 4);

        var before = EnumerateTree(target);

        var source = new SourceScanResult(
            @"C:\source",
            [
                SourceEntry.File("report.txt", 10),
                SourceEntry.File(@"never\created.txt", 10),
            ],
            []);

        var probe = new WindowsTargetExistingObjectProbe();
        probe.Probe(source, new WindowsTargetCapabilityProbe().Probe(target));

        Assert.Equal(before, EnumerateTree(target));

        // The existing file is still exactly the file it was: nothing was written through it, and no
        // probe file was put beside it.
        Assert.Equal(4, new FileInfo(targetFile).Length);
    }

    [Fact]
    public void ReportsARealJunctionAtAMappedLocationAndDoesNotLookThroughIt()
    {
        using var tree = new TemporarySourceTree();
        var target = tree.CreateDirectory("target");
        var external = tree.CreateExternalDirectory();
        var junction = tree.CreateDirectoryJunction(@"target\link", external);
        var externalFile = tree.CreateExternalFile(external, "inside.txt", lengthInBytes: 4);

        var source = new SourceScanResult(
            @"C:\source",
            [
                SourceEntry.Directory("link"),
                SourceEntry.File(@"link\inside.txt", 10),
            ],
            []);

        var probeResult = new WindowsTargetExistingObjectProbe().Probe(
            source,
            new WindowsTargetCapabilityProbe().Probe(target));

        // The junction carries the directory attribute as well, and the reparse attribute is judged
        // first: it is reported as a reparse point, never as an ordinary directory.
        Assert.Equal(
            TargetObjectPresence.ReparsePoint,
            probeResult.ObservationFor("link").Presence);

        // Nothing beneath it was examined, even though a real file is reachable through it.
        var inside = probeResult.ObservationFor(@"link\inside.txt");
        Assert.Equal(ExistingObjectObservationKind.NotQueried, inside.Kind);
        Assert.Equal(ExistingObjectGapReason.BlockedByReparsePoint, inside.GapReason);
        Assert.Equal("link", inside.BlockingAncestorRelativePath);
        Assert.Equal(TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps, probeResult.Outcome);

        var findings = new WindowsPreflightEvaluator().Evaluate(
            source,
            new WindowsTargetCapabilityProbe().Probe(target),
            probeResult);

        var reparsePoint = findings.Only(WindowsPreflightFindingCodes.TargetExistingReparsePoint);
        Assert.Equal(Severity.Warning, reparsePoint.Severity);
        Assert.Equal("link", reparsePoint.SourceRelativePath);

        // The coverage of what lies beyond the boundary is stated once, not once per object.
        var coverage = findings.Only(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete);
        Assert.Equal(Severity.Info, coverage.Severity);

        // The junction and the directory it points at are both still there, untouched: the check does
        // not follow a reparse point and does not remove one.
        Assert.True(Directory.Exists(junction));
        Assert.True(File.Exists(externalFile));
    }

    [Fact]
    public void ReportsAStructuralBlockAtAPathTheInventoryNeverLists()
    {
        using var tree = new TemporarySourceTree();
        var target = tree.CreateDirectory("target");

        // An ordinary file where the source path expects a directory, and no source entry for that
        // directory itself: the block has to be established from the anomaly alone, not from a
        // directory entry happening to be in the inventory.
        var blockingFile = tree.CreateFile(@"target\archive", lengthInBytes: 6);

        var source = new SourceScanResult(
            @"C:\source",
            [SourceEntry.File(@"archive\2026\report.txt", 10)],
            []);

        var probeResult = new WindowsTargetExistingObjectProbe().Probe(
            source,
            new WindowsTargetCapabilityProbe().Probe(target));

        var blockingAncestor = probeResult.ObservationFor("archive");

        Assert.Equal(TargetObjectPresence.File, blockingAncestor.Presence);

        var descendant = probeResult.ObservationFor(@"archive\2026\report.txt");

        Assert.Equal(ExistingObjectObservationKind.NotQueried, descendant.Kind);
        Assert.Equal(ExistingObjectGapReason.BlockedByExistingObject, descendant.GapReason);
        Assert.Equal("archive", descendant.BlockingAncestorRelativePath);
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, probeResult.Outcome);

        var findings = new WindowsPreflightEvaluator().Evaluate(
            source,
            new WindowsTargetCapabilityProbe().Probe(target),
            probeResult);

        var conflict = findings.Only(WindowsPreflightFindingCodes.TargetExistingTypeCollision);

        Assert.Equal(Severity.Error, conflict.Severity);
        Assert.Equal("archive", conflict.SourceRelativePath);

        // The blocked mapping was neither created nor repaired, and the file that blocks it is untouched.
        Assert.Equal(6, new FileInfo(blockingFile).Length);
        Assert.False(Directory.Exists(tree.PathOf(@"target\archive\2026")));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete));
    }

    /// <summary>
    /// A stable, order-independent listing of everything below a directory, with each file's length, so
    /// that a check can be shown to have changed nothing at all.
    /// </summary>
    private static IReadOnlyList<string> EnumerateTree(string directory)
        => [.. Directory
            .EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Select(path => File.Exists(path)
                ? $"{path}|{new FileInfo(path).Length}"
                : $"{path}|<directory>")
            .OrderBy(text => text, StringComparer.Ordinal)];
}
