using System.IO;
using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// The scan issue: a recorded observation failure that carries no verdict.
/// </summary>
public class SourceScanIssueTests
{
    [Fact]
    public void DescribesTheExceptionThatCausedItWithoutRetainingIt()
    {
        var exception = new IOException("The device is not ready.");

        var issue = SourceScanIssue.FromException(
            SourceScanIssueKind.CannotEnumerateDirectory,
            @"sub\inner",
            exception);

        Assert.Equal(SourceScanIssueKind.CannotEnumerateDirectory, issue.Kind);
        Assert.Equal(@"sub\inner", issue.RelativePath);
        Assert.Equal("System.IO.IOException", issue.ExceptionTypeName);
        Assert.Equal(exception.HResult, issue.HResult);
        Assert.Equal(exception.Message, issue.Diagnostic);
    }

    [Fact]
    public void AllowsAFailureAtTheSourceRootItself()
    {
        var issue = SourceScanIssue.FromException(
            SourceScanIssueKind.CannotEnumerateDirectory,
            relativePath: null,
            new UnauthorizedAccessException("Access to the path is denied."));

        Assert.Null(issue.RelativePath);
    }

    [Fact]
    public void AllowsAFailureDetectedWithoutAnException()
    {
        var issue = new SourceScanIssue(
            SourceScanIssueKind.CannotReadEntryMetadata,
            "mystery",
            exceptionTypeName: null,
            hResult: null,
            "The entry was listed, but its basic metadata could not be read.");

        Assert.Null(issue.ExceptionTypeName);
        Assert.Null(issue.HResult);
        Assert.False(string.IsNullOrWhiteSpace(issue.Diagnostic));
    }

    [Fact]
    public void CarriesNoVerdictOrAdvice()
    {
        // A scan issue states a fact about the observation, so it has no Severity and no
        // recommendation; those belong to the rule layer, which M2 does not implement.
        var properties = typeof(SourceScanIssue)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Severity", properties);
        Assert.DoesNotContain("Recommendation", properties);
        Assert.DoesNotContain("Title", properties);
    }

    [Fact]
    public void RejectsAnUnknownKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceScanIssue(
            (SourceScanIssueKind)42,
            "sub",
            exceptionTypeName: null,
            hResult: null,
            "Something happened."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsAMissingDiagnostic(string? diagnostic)
    {
        Assert.Throws<ArgumentException>(() => new SourceScanIssue(
            SourceScanIssueKind.CannotEnumerateDirectory,
            "sub",
            exceptionTypeName: null,
            hResult: null,
            diagnostic!));
    }

    [Fact]
    public void RejectsABlankRelativePathOrExceptionTypeName()
    {
        Assert.Throws<ArgumentException>(() => new SourceScanIssue(
            SourceScanIssueKind.CannotEnumerateDirectory,
            " ",
            exceptionTypeName: null,
            hResult: null,
            "Something happened."));

        Assert.Throws<ArgumentException>(() => new SourceScanIssue(
            SourceScanIssueKind.CannotEnumerateDirectory,
            "sub",
            exceptionTypeName: " ",
            hResult: null,
            "Something happened."));
    }

    [Fact]
    public void RejectsANullExceptionInTheConvenienceFactory()
    {
        Assert.Throws<ArgumentNullException>(() => SourceScanIssue.FromException(
            SourceScanIssueKind.CannotEnumerateDirectory,
            "sub",
            null!));
    }
}
