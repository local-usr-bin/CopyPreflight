using CopyPreflight.App;

namespace CopyPreflight.Tests;

/// <summary>
/// How one folder dialog is configured, which is the decision that keeps the two boxes' browsing
/// apart.
/// </summary>
/// <remarks>
/// <para>
/// The dialog itself is modal and cannot be driven here, so the part that decides what it is and where
/// it starts is asserted on its own. It is kept free of file-system calls on purpose: whether a folder
/// exists is answered by the scanner and the probes, never by the interface, and the dialog ignores a
/// starting folder it cannot use.
/// </para>
/// <para>
/// Each factory takes exactly one path — its own — so "start the target where the source was" is not
/// expressible, and the tests below state that rather than leaving it to a convention.
/// </para>
/// </remarks>
public class FolderPickerTests
{
    private const string SourcePath = @"C:\data\source";

    private const string TargetPath = @"C:\data\target";

    [Fact]
    public void TheTwoDialogsAreTwoDifferentIdentities()
    {
        var source = FolderPicker.ForSource(null);
        var target = FolderPicker.ForTarget(null);

        // The identity is what Windows keys a dialog's remembered location on. One shared identity is
        // exactly the coupling this replaces, and an empty one would identify nothing.
        Assert.NotEqual(Guid.Empty, source.DialogId);
        Assert.NotEqual(Guid.Empty, target.DialogId);
        Assert.NotEqual(source.DialogId, target.DialogId);

        // And it is a constant, so the remembered location survives from one dialog to the next.
        Assert.Equal(source.DialogId, FolderPicker.ForSource(SourcePath).DialogId);
        Assert.Equal(target.DialogId, FolderPicker.ForTarget(TargetPath).DialogId);

        Assert.NotEqual(source.Title, target.Title);
        Assert.Contains("source", source.Title);
        Assert.Contains("target", target.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyBoxOffersNoStartingFolder(string? enteredPath)
    {
        // With nothing typed, the dialog's own remembered location for its own identity decides, and
        // failing that the platform's default. None of that is expressed here.
        Assert.Null(FolderPicker.ForSource(enteredPath).InitialDirectory);
        Assert.Null(FolderPicker.ForTarget(enteredPath).InitialDirectory);
    }

    [Theory]
    [InlineData("folder")]
    [InlineData(@"folder\sub")]
    [InlineData(@"..\up")]
    [InlineData("C:not-fully-qualified")]
    public void ARelativePathIsNotOfferedAsAStartingFolder(string enteredPath)
    {
        // A relative path is the same input the check itself refuses, and the dialog has nothing to
        // resolve it against that would mean the same thing to the user.
        Assert.Null(FolderPicker.ForSource(enteredPath).InitialDirectory);
        Assert.Null(FolderPicker.ForTarget(enteredPath).InitialDirectory);
    }

    [Fact]
    public void ASideOffersOnlyThePathEnteredForThatSide()
    {
        Assert.Equal(SourcePath, FolderPicker.ForSource(SourcePath).InitialDirectory);
        Assert.Equal(TargetPath, FolderPicker.ForTarget(TargetPath).InitialDirectory);

        // The other box is not consulted: with the source box filled and the target box empty, the
        // target dialog is offered nothing at all — never the source folder, and never its parent.
        var sourcePicker = FolderPicker.ForSource(SourcePath);

        Assert.Equal(SourcePath, sourcePicker.InitialDirectory);
        Assert.Null(FolderPicker.ForTarget(null).InitialDirectory);
        Assert.Null(FolderPicker.ForTarget(string.Empty).InitialDirectory);
    }

    [Theory]
    [InlineData(@"\\server\share\folder")]
    [InlineData(@"\\?\C:\very\long\folder")]
    public void EveryFullyQualifiedFormIsOfferedAsItWasEntered(string enteredPath)
    {
        // The forms the layers below already support are not narrowed here, and nothing is rewritten:
        // the folder offered is the folder that was typed.
        Assert.Equal(enteredPath, FolderPicker.ForSource(enteredPath).InitialDirectory);
        Assert.Equal(enteredPath, FolderPicker.ForTarget(enteredPath).InitialDirectory);
    }
}
