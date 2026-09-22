using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The invariants of the existing-object facts: the states that must not be conflated, and the outcomes
/// a result may not claim.
/// </summary>
/// <remarks>
/// These matter because the model is what keeps a gap from being read as an absence. If a result could
/// claim completion while carrying an unexamined location, every downstream conclusion would inherit
/// that claim, so the contradictions are rejected at construction.
/// </remarks>
public class ExistingObjectModelTests
{
    [Fact]
    public void AnObservationOfAnObjectCarriesItsKindAndTheMappingLocation()
    {
        var observation = ExistingObjectObservation.Observed(
            "report.txt",
            ExistingObjectInputs.Mapped("report.txt"),
            TargetObjectPresence.File,
            "Scripted observation.");

        Assert.Equal(ExistingObjectObservationKind.Observed, observation.Kind);
        Assert.Equal(TargetObjectPresence.File, observation.Presence);
        Assert.Equal(ExistingObjectInputs.Mapped("report.txt"), observation.MappedTargetPath);
        Assert.Null(observation.GapReason);
        Assert.Null(observation.BlockingAncestorRelativePath);
        Assert.Null(observation.NativeErrorCode);
        Assert.False(observation.IsCoverageGap);
    }

    [Fact]
    public void OnlyAnObservationOfAnObjectCarriesAPresence()
    {
        Assert.Null(MissingObservation().Presence);
        Assert.Null(FailedObservation().Presence);
        Assert.Null(UnqueriedObservation().Presence);
        Assert.NotNull(
            ExistingObjectObservation.Observed(
                "a.txt",
                ExistingObjectInputs.Mapped("a.txt"),
                TargetObjectPresence.Directory,
                "Scripted observation.").Presence);
    }

    [Fact]
    public void AnObservationDoesNotCarryAPresenceItWasNotGiven()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExistingObjectObservation.Observed(
            "a.txt",
            ExistingObjectInputs.Mapped("a.txt"),
            (TargetObjectPresence)99,
            "Scripted observation."));
    }

    [Fact]
    public void AnAbsentAncestorIsAnInferenceRatherThanAnUnqueriedLocation()
    {
        // Category E and category G are different facts, and the model refuses to record one as the
        // other: a location inferred absent from a missing ancestor is not merely "not queried".
        Assert.Throws<ArgumentOutOfRangeException>(() => ExistingObjectObservation.NotQueried(
            "a.jpg",
            ExistingObjectGapReason.MissingAncestor,
            blockingAncestorRelativePath: null,
            nativeErrorCode: null,
            "Scripted.",
            ExistingObjectInputs.Mapped("a.jpg")));
    }

    [Fact]
    public void AnUnknownGapReasonIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExistingObjectObservation.NotQueried(
            "a.jpg",
            (ExistingObjectGapReason)99,
            blockingAncestorRelativePath: null,
            nativeErrorCode: null,
            "Scripted.",
            ExistingObjectInputs.Mapped("a.jpg")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ObservationTextCannotBeBlank(string blank)
    {
        Assert.Throws<ArgumentException>(() => ExistingObjectObservation.Observed(
            "a.txt",
            ExistingObjectInputs.Mapped("a.txt"),
            TargetObjectPresence.File,
            blank));

        Assert.Throws<ArgumentException>(() => ExistingObjectObservation.NotQueried(
            blank,
            ExistingObjectGapReason.Cancelled,
            blockingAncestorRelativePath: null,
            nativeErrorCode: null,
            "Scripted.",
            ExistingObjectInputs.Mapped("a.txt")));
    }

    [Fact]
    public void ABranchStoppedByAFileAncestorIsNotCountedAsACoverageGap()
    {
        // Nothing can exist below a file, and the conflict that says so is reported at the ancestor.
        // Counting every descendant as unchecked would bury that one real problem.
        var blocked = ExistingObjectObservation.NotQueried(
            @"photos\a.jpg",
            ExistingObjectGapReason.BlockedByExistingObject,
            "photos",
            nativeErrorCode: null,
            "Scripted.",
            ExistingObjectInputs.Mapped(@"photos\a.jpg"));

        Assert.False(blocked.IsCoverageGap);
        Assert.True(UnqueriedObservation().IsCoverageGap);
        Assert.True(FailedObservation().IsCoverageGap);
        Assert.False(MissingObservation().IsCoverageGap);
    }

    [Fact]
    public void AFailedCheckIsNotAnAbsence()
    {
        var failed = FailedObservation();

        Assert.Equal(ExistingObjectObservationKind.CheckFailed, failed.Kind);
        Assert.NotEqual(ExistingObjectObservationKind.ObservedMissing, failed.Kind);
        Assert.Equal(ScriptedFileSystemApi.ErrorAccessDenied, failed.NativeErrorCode);
    }

    [Fact]
    public void ANonPerformedResultCannotCarryObservations()
    {
        Assert.Throws<ArgumentException>(() => new TargetExistingObjectProbeResult(
            TargetRootObservation.NotAttempted("Scripted."),
            [MissingObservation()],
            TargetExistingObjectProbeOutcome.NotPerformed));
    }

    [Fact]
    public void ACompletedResultCannotLeaveALocationUncovered()
    {
        Assert.Throws<ArgumentException>(() => new TargetExistingObjectProbeResult(
            DirectoryRoot(),
            [UnqueriedObservation()],
            TargetExistingObjectProbeOutcome.Completed));
    }

    [Fact]
    public void ACompletedResultCannotHaveAnUnexaminableTargetRoot()
    {
        foreach (var kind in new[]
                 {
                     TargetRootObservationKind.File,
                     TargetRootObservationKind.ReparsePoint,
                     TargetRootObservationKind.NotAttempted,
                 })
        {
            var root = kind switch
            {
                TargetRootObservationKind.NotAttempted => TargetRootObservation.NotAttempted("Scripted."),
                TargetRootObservationKind.NotAnswered => TargetRootObservation.NotAnswered(53, "Scripted."),
                _ => TargetRootObservation.Answered(kind, "Scripted."),
            };

            Assert.Throws<ArgumentException>(() => new TargetExistingObjectProbeResult(
                root,
                [MissingObservation()],
                TargetExistingObjectProbeOutcome.Completed));
        }
    }

    [Fact]
    public void APartialResultMustActuallyLeaveSomethingUncovered()
    {
        Assert.Throws<ArgumentException>(() => new TargetExistingObjectProbeResult(
            DirectoryRoot(),
            [MissingObservation()],
            TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps));
    }

    [Fact]
    public void AShareRootThatDoesNotAnswerStillAllowsAFullCoverageClaim()
    {
        // The target's usability was settled from positive evidence before this check ran, and Microsoft
        // documents that a network share root does not answer the attribute query. Its silence is
        // therefore not a gap — as long as every mapped location itself was covered.
        var result = new TargetExistingObjectProbeResult(
            TargetRootObservation.NotAnswered(ScriptedFileSystemApi.ErrorBadNetPath, "Scripted."),
            [MissingObservation()],
            TargetExistingObjectProbeOutcome.Completed);

        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ACancelledResultIsNotTreatedAsCompletion()
    {
        var cancelled = ExistingObjectInputs.CancelledProbeResult(UnqueriedObservation());

        Assert.Equal(TargetExistingObjectProbeOutcome.Cancelled, cancelled.Outcome);
        Assert.False(cancelled.IsComplete);
    }

    [Fact]
    public void TheNotPerformedResultIsExplicitAndEmpty()
    {
        var result = TargetExistingObjectProbeResult.NotPerformed();

        Assert.Equal(TargetExistingObjectProbeOutcome.NotPerformed, result.Outcome);
        Assert.Empty(result.Observations);
        Assert.Equal(TargetRootObservationKind.NotAttempted, result.TargetRoot.Kind);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void AnObservationIsAValue()
    {
        Assert.Equal(
            ExistingObjectObservation.Observed(
                "a.txt",
                ExistingObjectInputs.Mapped("a.txt"),
                TargetObjectPresence.File,
                "Scripted observation."),
            ExistingObjectObservation.Observed(
                "a.txt",
                ExistingObjectInputs.Mapped("a.txt"),
                TargetObjectPresence.File,
                "Scripted observation."));
    }

    internal static TargetRootObservation DirectoryRoot()
        => TargetRootObservation.Answered(TargetRootObservationKind.Directory, "Scripted root.");

    private static ExistingObjectObservation MissingObservation()
        => ExistingObjectObservation.ObservedMissing(
            "a.txt",
            ExistingObjectInputs.Mapped("a.txt"),
            ScriptedFileSystemApi.ErrorFileNotFound,
            "Scripted absence.");

    private static ExistingObjectObservation FailedObservation()
        => ExistingObjectObservation.CheckFailed(
            "a.txt",
            ExistingObjectInputs.Mapped("a.txt"),
            ScriptedFileSystemApi.ErrorAccessDenied,
            "Scripted failure.");

    private static ExistingObjectObservation UnqueriedObservation()
        => ExistingObjectObservation.NotQueried(
            "a.txt",
            ExistingObjectGapReason.BlockedByReparsePoint,
            "link",
            nativeErrorCode: null,
            "Scripted boundary.",
            ExistingObjectInputs.Mapped("a.txt"));
}
