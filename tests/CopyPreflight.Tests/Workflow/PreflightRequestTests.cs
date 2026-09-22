using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The rules for turning two entered paths into a request: what is refused, and what is deliberately
/// left alone.
/// </summary>
public class PreflightRequestTests
{
    // ------------------------------------------------------------------ accepted forms

    [Theory]
    [InlineData(@"C:\data")]
    [InlineData(@"C:\data\deeper")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\folder")]
    [InlineData(@"\\?\C:\data")]
    [InlineData(@"\\?\UNC\server\share\data")]
    [InlineData("c:/data")]
    public void TryCreate_AcceptsEveryFormTheLayersBelowSupport(string path)
    {
        // None of these is narrowed by a list of "allowed" shapes: the target probe and the scanner
        // already decide what they can do with a path, and the request must not refuse a form they
        // support.
        var created = PreflightRequest.TryCreate(path, path, out var request, out _);

        Assert.True(created);
        Assert.Equal(path, request!.SourcePath);
        Assert.Equal(path, request.TargetPath);
    }

    [Fact]
    public void TryCreate_KeepsAnUnusualPathExactlyAsItWasEntered()
    {
        // A trailing space is part of what the user typed. Win32 may or may not act on it later, but
        // silently trimming it here would probe a different location than the one on screen.
        const string Entered = @"C:\data ";

        var created = PreflightRequest.TryCreate(Entered, @"C:\target", out var request, out _);

        Assert.True(created);
        Assert.Equal(Entered, request!.SourcePath);
    }

    // ------------------------------------------------------------------ refused forms

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_RefusesAnAbsentSourcePath(string? sourcePath)
    {
        var created = PreflightRequest.TryCreate(sourcePath, @"C:\target", out var request, out var error);

        Assert.False(created);
        Assert.Null(request);
        Assert.Equal(PreflightRequestError.SourcePathMissing, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\t")]
    public void TryCreate_RefusesAnAbsentTargetPath(string? targetPath)
    {
        var created = PreflightRequest.TryCreate(@"C:\source", targetPath, out var request, out var error);

        Assert.False(created);
        Assert.Null(request);
        Assert.Equal(PreflightRequestError.TargetPathMissing, error);
    }

    [Theory]
    [InlineData("data")]
    [InlineData(@"data\deeper")]
    [InlineData(".")]
    [InlineData(@"..\data")]
    public void TryCreate_RefusesARelativeSourcePath(string sourcePath)
    {
        // A relative path would have to be resolved against whatever folder the application happened
        // to be started from, which is not something the user chose.
        var created = PreflightRequest.TryCreate(sourcePath, @"C:\target", out var request, out var error);

        Assert.False(created);
        Assert.Null(request);
        Assert.Equal(PreflightRequestError.SourcePathNotFullyQualified, error);
    }

    [Fact]
    public void TryCreate_RefusesARelativeTargetPath()
    {
        var created = PreflightRequest.TryCreate(@"C:\source", "target", out var request, out var error);

        Assert.False(created);
        Assert.Null(request);
        Assert.Equal(PreflightRequestError.TargetPathNotFullyQualified, error);
    }

    [Fact]
    public void TryCreate_ReportsTheSourceBeforeTheTarget()
    {
        // With both inputs wrong, the user is told about the first box rather than about the last
        // thing that happened to be checked.
        var created = PreflightRequest.TryCreate(string.Empty, "also-relative", out _, out var error);

        Assert.False(created);
        Assert.Equal(PreflightRequestError.SourcePathMissing, error);
    }

    // ------------------------------------------------------------------ identity

    [Fact]
    public void TryCreate_GivesEveryRequestItsOwnIdentity()
    {
        Assert.True(PreflightRequest.TryCreate(@"C:\source", @"C:\target", out var first, out _));
        Assert.True(PreflightRequest.TryCreate(@"C:\source", @"C:\target", out var second, out _));

        Assert.NotEqual(first.RequestId, second.RequestId);
    }

    [Fact]
    public void TryCreate_DoesNotNormaliseTheEnteredPaths()
    {
        // The resolved path is reported by the scanner and the probe, not invented here, so that the
        // path asked about and the path used can be told apart.
        const string Messy = @"C:\source\.\..\source\";

        Assert.True(PreflightRequest.TryCreate(Messy, Messy, out var request, out _));

        Assert.Equal(Messy, request.SourcePath);
        Assert.Equal(Messy, request.TargetPath);
    }
}
