using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The existing-object check over a scripted file-system seam: how each answer is read, and what the
/// check does when the answer is "no answer".
/// </summary>
/// <remarks>
/// The seam is strict here. A path the check asks about but the test did not script fails the test,
/// because the interesting property of this check is which questions it asks — and a silently defaulted
/// answer would hide a question that should never have been asked.
/// </remarks>
public class TargetExistingObjectProbeTests
{
    [Fact]
    public void ReportsAnOrdinaryFileAtAMappedLocation()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("report.txt"), FileAttributes.Normal));

        var observation = result.ObservationFor("report.txt");

        Assert.Equal(ExistingObjectObservationKind.Observed, observation.Kind);
        Assert.Equal(TargetObjectPresence.File, observation.Presence);
        Assert.Equal(ExistingObjectInputs.Mapped("report.txt"), observation.MappedTargetPath);
        Assert.Null(observation.NativeErrorCode);
        Assert.False(observation.IsCoverageGap);
    }

    [Fact]
    public void ReportsAnOrdinaryDirectoryAtAMappedLocation()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.Directory("media"));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("media"), FileAttributes.Directory));

        Assert.Equal(TargetObjectPresence.Directory, result.ObservationFor("media").Presence);
    }

    [Fact]
    public void ReadsTheReparseAttributeBeforeTheDirectoryAttribute()
    {
        // Windows reports both attributes for a junction or a mount point. Reading such an object as an
        // ordinary directory would describe something this milestone deliberately does not look through.
        var source = ExistingObjectInputs.Source(SourceEntry.File("link", 1));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(
                    ExistingObjectInputs.Mapped("link"),
                    FileAttributes.Directory | FileAttributes.ReparsePoint));

        Assert.Equal(
            TargetObjectPresence.ReparsePoint,
            result.ObservationFor("link").Presence);
    }

    [Fact]
    public void TreatsOnlyFileNotFoundAsEvidenceOfAbsence()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped("report.txt"),
                    ScriptedFileSystemApi.ErrorFileNotFound));

        var observation = result.ObservationFor("report.txt");

        Assert.Equal(ExistingObjectObservationKind.ObservedMissing, observation.Kind);
        Assert.Equal(ScriptedFileSystemApi.ErrorFileNotFound, observation.NativeErrorCode);
        Assert.Null(observation.Presence);
        Assert.False(observation.IsCoverageGap);
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, result.Outcome);
    }

    [Theory]
    [InlineData(ScriptedFileSystemApi.ErrorPathNotFound)]
    [InlineData(ScriptedFileSystemApi.ErrorAccessDenied)]
    [InlineData(ScriptedFileSystemApi.ErrorSharingViolation)]
    [InlineData(ScriptedFileSystemApi.ErrorBadNetPath)]
    [InlineData(ScriptedFileSystemApi.ErrorBadNetName)]
    [InlineData(ScriptedFileSystemApi.ErrorInvalidName)]
    [InlineData(ScriptedFileSystemApi.ErrorFilenameExceededRange)]
    public void KeepsEveryOtherFailureUnknownAndKeepsItsErrorCode(int nativeError)
    {
        // None of these says that the last component is absent — the path may not resolve at all, the
        // caller may not be allowed to look, the name may be one Windows will not accept, or the query
        // may be too long. The check records the code it was given and draws no conclusion from it.
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(ExistingObjectInputs.Mapped("report.txt"), nativeError));

        var observation = result.ObservationFor("report.txt");

        Assert.Equal(ExistingObjectObservationKind.CheckFailed, observation.Kind);
        Assert.Equal(nativeError, observation.NativeErrorCode);
        Assert.Null(observation.Presence);
        Assert.True(observation.IsCoverageGap);
        Assert.Equal(TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps, result.Outcome);
    }

    [Fact]
    public void FailureSkipAndAbsenceAreThreeDifferentFacts()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("answered.txt", 1),
            SourceEntry.File("failed.txt", 1),
            SourceEntry.File("skipped.txt", 1));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("answered.txt"), FileAttributes.Normal)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped("failed.txt"),
                    ScriptedFileSystemApi.ErrorAccessDenied)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped("skipped.txt"),
                    ScriptedFileSystemApi.ErrorFileNotFound));

        Assert.Equal(
            ExistingObjectObservationKind.Observed,
            result.ObservationFor("answered.txt").Kind);
        Assert.Equal(
            ExistingObjectObservationKind.CheckFailed,
            result.ObservationFor("failed.txt").Kind);
        Assert.Equal(
            ExistingObjectObservationKind.ObservedMissing,
            result.ObservationFor("skipped.txt").Kind);
    }

    [Fact]
    public void AnAncestorThatIsAnOrdinaryFileStopsTheBranch()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.Directory("photos"),
            SourceEntry.File(@"photos\a.jpg", 1));

        // The descendant is deliberately not scripted: a strict seam turns any query about it into a
        // failure, which is how this test proves the check did not ask.
        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("photos"), FileAttributes.Normal));

        Assert.Equal(TargetObjectPresence.File, result.ObservationFor("photos").Presence);

        var descendant = result.ObservationFor(@"photos\a.jpg");

        Assert.Equal(ExistingObjectObservationKind.NotQueried, descendant.Kind);
        Assert.Equal(ExistingObjectGapReason.BlockedByExistingObject, descendant.GapReason);
        Assert.Equal("photos", descendant.BlockingAncestorRelativePath);
        Assert.Null(descendant.NativeErrorCode);

        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot, ExistingObjectInputs.Mapped("photos"));
    }

    [Fact]
    public void AnAbsentAncestorMakesTheBranchInferredMissingRatherThanQueried()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.Directory("photos"),
            SourceEntry.File(@"photos\a.jpg", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped("photos"),
                    ScriptedFileSystemApi.ErrorFileNotFound));

        Assert.Equal(ExistingObjectObservationKind.ObservedMissing, result.ObservationFor("photos").Kind);

        var descendant = result.ObservationFor(@"photos\a.jpg");

        Assert.Equal(ExistingObjectObservationKind.InferredMissing, descendant.Kind);
        Assert.Equal(ExistingObjectGapReason.MissingAncestor, descendant.GapReason);
        Assert.Equal("photos", descendant.BlockingAncestorRelativePath);

        // Inferred, not asked: the location itself was never queried, and it is not recorded as though
        // a per-location query had answered.
        Assert.NotEqual(ExistingObjectObservationKind.ObservedMissing, descendant.Kind);
        Assert.False(descendant.IsCoverageGap);
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, result.Outcome);

        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot, ExistingObjectInputs.Mapped("photos"));
    }

    [Fact]
    public void AReparseAncestorStopsTheBranchWithoutLookingThroughIt()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"link\inside.txt", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(
                    ExistingObjectInputs.Mapped("link"),
                    FileAttributes.Directory | FileAttributes.ReparsePoint));

        var observation = result.ObservationFor(@"link\inside.txt");

        Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
        Assert.Equal(ExistingObjectGapReason.BlockedByReparsePoint, observation.GapReason);
        Assert.Equal("link", observation.BlockingAncestorRelativePath);
        Assert.True(observation.IsCoverageGap);
        Assert.Equal(TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps, result.Outcome);

        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot, ExistingObjectInputs.Mapped("link"));
    }

    [Fact]
    public void AnUnanswerableAncestorStopsTheBranchAndKeepsTheDescendantUnknown()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"photos\a.jpg", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped("photos"),
                    ScriptedFileSystemApi.ErrorAccessDenied));

        var observation = result.ObservationFor(@"photos\a.jpg");

        Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
        Assert.Equal(ExistingObjectGapReason.BlockedByFailedCheck, observation.GapReason);
        Assert.Equal("photos", observation.BlockingAncestorRelativePath);

        // The code belongs to the ancestor that failed, and the ancestor is named, so the evidence is
        // never presented as though this location had been asked about itself.
        Assert.Equal(ScriptedFileSystemApi.ErrorAccessDenied, observation.NativeErrorCode);

        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot, ExistingObjectInputs.Mapped("photos"));
    }

    [Fact]
    public void OneFailedBranchDoesNotStopAnIndependentOne()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.File(@"a\x", 1),
            SourceEntry.File(@"b\y.txt", 1));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped("a"),
                    ScriptedFileSystemApi.ErrorAccessDenied)
                .WithAttribute(ExistingObjectInputs.Mapped("b"), FileAttributes.Directory)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped(@"b\y.txt"),
                    ScriptedFileSystemApi.ErrorFileNotFound));

        Assert.Equal(ExistingObjectObservationKind.NotQueried, result.ObservationFor(@"a\x").Kind);
        Assert.Equal(
            ExistingObjectObservationKind.ObservedMissing,
            result.ObservationFor(@"b\y.txt").Kind);
    }

    [Fact]
    public void ResolvesAncestorsThatAreNotInTheInventoryParentsFirst()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"photos\2026\a.jpg", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("photos"), FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped(@"photos\2026"), FileAttributes.Directory)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped(@"photos\2026\a.jpg"),
                    ScriptedFileSystemApi.ErrorFileNotFound));

        api.AssertAttributeQueries(
            ExistingObjectInputs.TargetRoot,
            ExistingObjectInputs.Mapped("photos"),
            ExistingObjectInputs.Mapped(@"photos\2026"),
            ExistingObjectInputs.Mapped(@"photos\2026\a.jpg"));

        Assert.Equal(
            ExistingObjectObservationKind.ObservedMissing,
            result.ObservationFor(@"photos\2026\a.jpg").Kind);
    }

    [Fact]
    public void TheConclusionDoesNotDependOnTheInventoryOrder()
    {
        var entries = new SourceEntry[]
        {
            SourceEntry.File(@"photos\2026\a.jpg", 1),
            SourceEntry.Directory(@"photos\2026"),
            SourceEntry.Directory("photos"),
            SourceEntry.File("report.txt", 1),
        };

        static void Script(ScriptedFileSystemApi api) => api
            .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
            .WithAttribute(ExistingObjectInputs.Mapped("photos"), FileAttributes.Directory)
            .WithAttribute(ExistingObjectInputs.Mapped(@"photos\2026"), FileAttributes.Directory)
            .WithAttributeFailureFor(
                ExistingObjectInputs.Mapped(@"photos\2026\a.jpg"),
                ScriptedFileSystemApi.ErrorFileNotFound)
            .WithAttributeFailureFor(
                ExistingObjectInputs.Mapped("report.txt"),
                ScriptedFileSystemApi.ErrorAccessDenied);

        var forward = ExistingObjectInputs.Run(ExistingObjectInputs.Source(entries), Script);
        var backward = ExistingObjectInputs.Run(
            ExistingObjectInputs.Source([.. entries.Reverse()]),
            Script);

        Assert.NotEmpty(forward.Result.Observations);
        Assert.Equal(forward.Result.ObservationsShape(), backward.Result.ObservationsShape());
        Assert.Equal(forward.Result.Outcome, backward.Result.Outcome);
    }

    [Fact]
    public void ARejectedTargetQueriesNothingAtAll()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1));

        var (result, api) = ExistingObjectInputs.RunAgainstRejectedTarget(source);

        Assert.Empty(api.AttributePaths);
        Assert.Equal(TargetRootObservationKind.NotAttempted, result.TargetRoot.Kind);

        var observation = result.ObservationFor("report.txt");

        Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
        Assert.Equal(ExistingObjectGapReason.TargetRootUnusable, observation.GapReason);
        Assert.Equal(TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps, result.Outcome);
    }

    [Fact]
    public void ATargetRootThatIsAnOrdinaryFileStopsEveryBranch()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"photos\a.jpg", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api.WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Normal));

        Assert.Equal(TargetRootObservationKind.File, result.TargetRoot.Kind);

        var observation = result.ObservationFor(@"photos\a.jpg");
        Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
        Assert.Equal(ExistingObjectGapReason.TargetRootUnusable, observation.GapReason);

        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot);
    }

    [Fact]
    public void ATargetRootThatIsAReparsePointStopsEveryBranch()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.Directory("photos"),
            SourceEntry.File(@"photos\a.jpg", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api.WithAttribute(
                ExistingObjectInputs.TargetRoot,
                FileAttributes.Directory | FileAttributes.ReparsePoint));

        Assert.Equal(TargetRootObservationKind.ReparsePoint, result.TargetRoot.Kind);

        foreach (var observation in result.Observations)
        {
            Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
            Assert.Equal(ExistingObjectGapReason.BlockedByReparsePoint, observation.GapReason);
        }

        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot);
    }

    [Fact]
    public void QueriesAnOrdinaryPathExactlyAsTheMappingComposesIt()
    {
        // The ancestors the mapping requires are asked about first — parent before child — and every
        // question names exactly the composed location, never a normalised spelling of it.
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"docs\a.txt", 1));

        var (_, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("docs"), FileAttributes.Directory)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped(@"docs\a.txt"),
                    ScriptedFileSystemApi.ErrorFileNotFound));

        api.AssertAttributeQueries(
            ExistingObjectInputs.TargetRoot,
            ExistingObjectInputs.Mapped("docs"),
            ExistingObjectInputs.Mapped(@"docs\a.txt"));
    }

    [Fact]
    public void AShareRootThatRefusesTheAttributeQueryDoesNotGateTheLocationsBelowIt()
    {
        // Microsoft documents that the attribute query does not answer for a network share root. That
        // failure is not a verdict on the share, so the locations below it are still examined — and what
        // each of them holds is decided by that location's own answer.
        const string ShareRoot = @"\\server\share";
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("present.txt", 1),
            SourceEntry.File("absent.txt", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttributeFailureFor(ShareRoot, ScriptedFileSystemApi.ErrorBadNetPath)
                .WithAttribute(Path.Join(ShareRoot, "present.txt"), FileAttributes.Normal)
                .WithAttributeFailureFor(
                    Path.Join(ShareRoot, "absent.txt"),
                    ScriptedFileSystemApi.ErrorFileNotFound),
            targetRoot: ShareRoot);

        Assert.Equal(TargetRootObservationKind.NotAnswered, result.TargetRoot.Kind);
        Assert.Equal(ScriptedFileSystemApi.ErrorBadNetPath, result.TargetRoot.NativeErrorCode);

        Assert.Equal(TargetObjectPresence.File, result.ObservationFor("present.txt").Presence);
        Assert.Equal(
            ExistingObjectObservationKind.ObservedMissing,
            result.ObservationFor("absent.txt").Kind);

        // Every mapped location was covered, so the root's own silence does not make the run a partial
        // one: the target's usability had already been settled from positive evidence.
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, result.Outcome);

        api.AssertAttributeQueries(
            ShareRoot,
            Path.Join(ShareRoot, "present.txt"),
            Path.Join(ShareRoot, "absent.txt"));
    }

    [Fact]
    public void AnOrdinaryNetworkFailureOnALocationBelowAShareStaysUnknown()
    {
        // The documented share-root exception applies to the share root, not to every network error
        // anywhere. A path below the share that fails on its own is an unanswered question.
        const string ShareRoot = @"\\server\share";
        var source = ExistingObjectInputs.Source(SourceEntry.File("gone.txt", 1));

        var (result, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttributeFailureFor(ShareRoot, ScriptedFileSystemApi.ErrorBadNetPath)
                .WithAttributeFailureFor(
                    Path.Join(ShareRoot, "gone.txt"),
                    ScriptedFileSystemApi.ErrorBadNetName),
            targetRoot: ShareRoot);

        var observation = result.ObservationFor("gone.txt");

        Assert.Equal(ExistingObjectObservationKind.CheckFailed, observation.Kind);
        Assert.Equal(ScriptedFileSystemApi.ErrorBadNetName, observation.NativeErrorCode);
    }

    [Fact]
    public void DoesNotQueryThroughANameThatCannotBeWrittenAsItStands()
    {
        // A trailing period is silently dropped by Windows, so querying this name as written would
        // answer about a different object. The location is left unexamined instead.
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api.WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory));

        var observation = result.ObservationFor("report.");

        Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
        Assert.Equal(ExistingObjectGapReason.AmbiguousName, observation.GapReason);
        Assert.Contains("report.", observation.Diagnostic, StringComparison.Ordinal);

        // Only the target root was ever asked about: neither the name as written nor a normalised
        // spelling of it was queried.
        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot);
        Assert.Equal(ExistingObjectInputs.Mapped("report."), observation.MappedTargetPath);
    }

    [Fact]
    public void ProtectsAnAmbiguousAncestorComponentToo()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.File(@"NUL\a.txt", 1),
            SourceEntry.File(@"Photos \a.txt", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api.WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory));

        foreach (var relativePath in new[] { @"NUL\a.txt", @"Photos \a.txt" })
        {
            var observation = result.ObservationFor(relativePath);

            Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
            Assert.Equal(ExistingObjectGapReason.AmbiguousName, observation.GapReason);
        }

        // A normal leaf name underneath an ambiguous directory does not license walking through it.
        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot);
    }

    [Fact]
    public void QueriesALongDrivePathInItsExtendedLengthSpelling()
    {
        const string TargetRoot = @"C:\t";
        var longName = new string('d', 260);
        var mappedPath = Path.Join(TargetRoot, longName);

        Assert.True(mappedPath.Length >= 260);

        var source = ExistingObjectInputs.Source(SourceEntry.File(longName, 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(
                    @"\\?\" + mappedPath,
                    ScriptedFileSystemApi.ErrorFileNotFound),
            targetRoot: TargetRoot);

        api.AssertAttributeQueries(TargetRoot, @"\\?\" + mappedPath);

        // The observation records the mapping location, never the form the query happened to use.
        Assert.Equal(mappedPath, result.ObservationFor(longName).MappedTargetPath);
        Assert.Equal(ExistingObjectObservationKind.ObservedMissing, result.ObservationFor(longName).Kind);
    }

    [Fact]
    public void QueriesALongNetworkPathInTheUncSpelling()
    {
        const string TargetRoot = @"\\server\share";
        var longName = new string('d', 260);
        var mappedPath = Path.Join(TargetRoot, longName);

        var source = ExistingObjectInputs.Source(SourceEntry.File(longName, 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(
                    @"\\?\UNC\server\share\" + longName,
                    ScriptedFileSystemApi.ErrorFileNotFound),
            targetRoot: TargetRoot);

        api.AssertAttributeQueries(TargetRoot, @"\\?\UNC\server\share\" + longName);
        Assert.Equal(mappedPath, result.ObservationFor(longName).MappedTargetPath);
    }

    [Fact]
    public void DoesNotTreatTheDeviceNamespaceAsAnExtendedLengthNamespace()
    {
        // \\.\ is the device namespace, not a long-path exemption. Prefixing it with \\?\ would name
        // something else entirely, so the path is handed to Windows as it stands.
        const string TargetRoot = @"\\.\C:\target";
        var longName = new string('d', 260);
        var mappedPath = Path.Join(TargetRoot, longName);

        var source = ExistingObjectInputs.Source(SourceEntry.File(longName, 1));

        var (_, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(TargetRoot, FileAttributes.Directory)
                .WithAttributeFailureFor(mappedPath, ScriptedFileSystemApi.ErrorFileNotFound),
            targetRoot: TargetRoot);

        api.AssertAttributeQueries(TargetRoot, mappedPath);
    }

    [Fact]
    public void DoesNotMergeTwoMappingsThatDifferOnlyInLetterCase()
    {
        // A target may resolve letter case differently from the source, and deciding that is not this
        // milestone's job. Two mappings that differ only in case are two questions.
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("A.txt", 1),
            SourceEntry.File("a.txt", 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("A.txt"), FileAttributes.Normal)
                .WithAttribute(ExistingObjectInputs.Mapped("a.txt"), FileAttributes.Directory));

        api.AssertAttributeQueries(
            ExistingObjectInputs.TargetRoot,
            ExistingObjectInputs.Mapped("A.txt"),
            ExistingObjectInputs.Mapped("a.txt"));

        Assert.Equal(TargetObjectPresence.File, result.ObservationFor("A.txt").Presence);
        Assert.Equal(TargetObjectPresence.Directory, result.ObservationFor("a.txt").Presence);
    }

    [Theory]
    [InlineData(@"\absolute.txt")]
    [InlineData(@"..\escape.txt")]
    [InlineData(@"a\\b.txt")]
    [InlineData(@"a\.\b.txt")]
    public void DoesNotComposeOrQueryARelativePathThatWouldChangeTheMapping(string relativePath)
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File(relativePath, 1));

        var (result, api) = ExistingObjectInputs.Run(
            source,
            api => api.WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory));

        var observation = result.ObservationFor(relativePath);

        Assert.Equal(ExistingObjectObservationKind.NotQueried, observation.Kind);
        Assert.Equal(ExistingObjectGapReason.RelativePathNotQueryable, observation.GapReason);
        Assert.Null(observation.MappedTargetPath);
        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot);
    }

    [Fact]
    public void CancellationPropagatesInsteadOfProducingAResult()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1));
        var api = new ScriptedFileSystemApi()
            .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new WindowsTargetExistingObjectProbe(api)
            .Probe(source, ExistingObjectInputs.UsableTarget(), cancellation.Token));

        // Nothing is returned for a cancelled run, so no caller can mistake it for a finished one.
        api.AssertAttributeQueries(ExistingObjectInputs.TargetRoot);
    }

    [Fact]
    public void DeclaresItsWindowsPlatformContract()
    {
        var platforms = typeof(WindowsTargetExistingObjectProbe)
            .GetCustomAttributes<SupportedOSPlatformAttribute>()
            .Select(attribute => attribute.PlatformName)
            .ToList();

        Assert.Contains("windows", platforms);
    }
}
