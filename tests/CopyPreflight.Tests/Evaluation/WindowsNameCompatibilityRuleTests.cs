using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The Windows naming rules: characters an ordinary Windows name cannot contain, reserved device
/// names, and names ending with a space or a period.
/// </summary>
/// <remarks>
/// Every check is judged on the <b>leaf</b> component of an entry's relative path, so the tests pin
/// both what is reported and what deliberately is not: an ancestor's problem is not repeated on its
/// descendants, and a separator is a separator rather than an illegal name character.
/// </remarks>
public class WindowsNameCompatibilityRuleTests
{
    // ---------------------------------------------------------------------------------------------
    // A. Characters an ordinary Windows file or directory name cannot contain.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData('<')]
    [InlineData('>')]
    // A colon is also alternate-data-stream syntax, but this milestone does not analyse streams: as
    // part of an ordinary file or directory name it is simply one of the rejected characters.
    [InlineData(':')]
    [InlineData('"')]
    [InlineData('|')]
    [InlineData('?')]
    [InlineData('*')]
    public void CharactersWindowsRejectsInANameAreReported(char rejected)
    {
        var finding = FindingsFor(SourceEntry.File($"a{rejected}b.txt", 1))
            .Only(WindowsPreflightFindingCodes.TargetNameInvalidCharacter);

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal($"a{rejected}b.txt", finding.SourceRelativePath);

        // The offending character is named, so the finding says what to change.
        Assert.Contains($"'{rejected}'", finding.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x0001)]
    [InlineData(0x001F)]
    public void ControlCharactersAreReported(int codePoint)
    {
        var rejected = (char)codePoint;

        var finding = FindingsFor(SourceEntry.File($"a{rejected}b.txt", 1))
            .Only(WindowsPreflightFindingCodes.TargetNameInvalidCharacter);

        Assert.Equal(Severity.Error, finding.Severity);

        // A control character is rendered as a code point rather than sent to the display raw.
        Assert.Contains($"U+{codePoint:X4}", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDistinctOffendingCharacterIsListedOnceInOneFinding()
    {
        var finding = FindingsFor(SourceEntry.File("a<b>c<b.txt", 1))
            .Only(WindowsPreflightFindingCodes.TargetNameInvalidCharacter);

        Assert.Contains("'<'", finding.Explanation, StringComparison.Ordinal);
        Assert.Contains("'>'", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinarySpaceInsideANameIsNotAnIllegalCharacter()
    {
        // U+0020 is an ordinary name character. Only a space at the *end* of a name is a concern,
        // and that is the trailing-character rule's business, not this one's.
        var findings = FindingsFor(SourceEntry.File("file name.txt", 1));

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(@"folder\sub\a.txt")]
    [InlineData("folder/sub/a.txt")]
    public void SeparatorsSeparateComponentsRatherThanCountingAsNameCharacters(string relativePath)
    {
        // A backslash and a slash are path separators. Searching the whole relative path for them
        // would report every nested path in the tree, which is why only the leaf name is judged.
        var findings = FindingsFor(SourceEntry.File(relativePath, 1));

        Assert.Empty(findings);
    }

    [Fact]
    public void AnAncestorNameProblemIsNotRepeatedOnAnOrdinaryDescendant()
    {
        // "bad<dir>" is judged where it is an entry of its own; "ok.txt" carries no problem of its
        // own, so drilling into a badly named directory does not multiply the finding.
        Assert.Empty(FindingsFor(SourceEntry.File(@"bad<dir>\ok.txt", 1)));
    }

    [Fact]
    public void TheAncestorIsWhereTheProblemIsReported()
    {
        var finding = FindingsFor(
                SourceEntry.Directory("bad<dir>"),
                SourceEntry.File(@"bad<dir>\ok.txt", 1))
            .Only(WindowsPreflightFindingCodes.TargetNameInvalidCharacter);

        Assert.Equal("bad<dir>", finding.SourceRelativePath);
    }

    // ---------------------------------------------------------------------------------------------
    // B. Reserved device names.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("NUL.txt")]
    [InlineData("NUL.tar.gz")]
    [InlineData("COM1")]
    [InlineData("COM9.txt")]
    [InlineData("LPT1")]
    [InlineData("LPT9.log")]
    [InlineData("COM\u00B9")]
    [InlineData("COM\u00B2.ext")]
    [InlineData("COM\u00B3")]
    [InlineData("LPT\u00B9")]
    [InlineData("LPT\u00B2.txt")]
    [InlineData("LPT\u00B3")]
    public void ReservedDeviceNamesAreReported(string name)
    {
        var finding = FindingsFor(SourceEntry.File(name, 1))
            .Only(WindowsPreflightFindingCodes.TargetNameReservedDevice);

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal(name, finding.SourceRelativePath);
    }

    [Theory]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    [InlineData("LPT10")]
    [InlineData("CONSOLE")]
    [InlineData("NULX")]
    [InlineData("AUXILIARY")]
    public void NamesThatMerelyResembleAReservedNameAreNotReported(string name)
    {
        // Only the names Microsoft documents as reserved are reported; the rule is a fixed list from
        // the documentation, not a general DOS device name parser.
        var findings = FindingsFor(SourceEntry.File(name, 1));

        Assert.Empty(findings);
    }

    [Fact]
    public void AReservedDeviceNameIsReportedForADirectoryToo()
    {
        var finding = FindingsFor(SourceEntry.Directory("NUL"))
            .Only(WindowsPreflightFindingCodes.TargetNameReservedDevice);

        Assert.Equal(Severity.Error, finding.Severity);
    }

    [Fact]
    public void AReservedDeviceNameIsReportedForAReparsePointToo()
    {
        // Whether a name is reserved has nothing to do with knowing what a reparse point points at,
        // and CopyPreflight does not follow or classify it in order to decide this.
        var finding = FindingsFor(SourceEntry.ReparsePoint("con", SourceEntryKind.Directory))
            .Only(WindowsPreflightFindingCodes.TargetNameReservedDevice);

        Assert.Equal(Severity.Error, finding.Severity);
    }

    // ---------------------------------------------------------------------------------------------
    // C. A trailing space or period.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("file ")]
    [InlineData("file.")]
    [InlineData("folder ")]
    [InlineData("folder.")]
    public void NamesEndingWithASpaceOrPeriodAreWarned(string name)
    {
        var finding = FindingsFor(SourceEntry.File(name, 1))
            .Only(WindowsPreflightFindingCodes.TargetNameTrailingSpaceOrPeriod);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(name, finding.SourceRelativePath);
    }

    [Theory]
    [InlineData(".temp")]
    [InlineData("file.txt")]
    [InlineData("file name.txt")]
    public void NamesThatDoNotEndWithASpaceOrPeriodAreNotWarned(string name)
    {
        Assert.Empty(FindingsFor(SourceEntry.File(name, 1))
            .WithCode(WindowsPreflightFindingCodes.TargetNameTrailingSpaceOrPeriod));
    }

    [Fact]
    public void ALeadingPeriodIsAllowed()
    {
        // Only the end of a name matters, and only the end of a name is judged.
        var findings = FindingsFor(SourceEntry.File(".temp", 1));

        Assert.Empty(findings);
    }

    [Fact]
    public void TheTrailingWarningNeverClaimsThatTheCopyWillFail()
    {
        var finding = FindingsFor(SourceEntry.File("file ", 1))
            .Only(WindowsPreflightFindingCodes.TargetNameTrailingSpaceOrPeriod);

        var text = $"{finding.Explanation} {finding.Impact} {finding.Recommendation}";

        Assert.DoesNotContain("will fail", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot be copied", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guaranteed", text, StringComparison.OrdinalIgnoreCase);

        // The reader is told why it is only a risk: the shell and the UI differ from the file system,
        // and the copy program decides what actually happens.
        Assert.Contains("shell", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("underlying file system", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("copy program", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------
    // D. Severities, and a name that trips more than one check.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheThreeNameChecksCarryTheIntendedSeverities()
    {
        var findings = FindingsFor(
            SourceEntry.File("a<b.txt", 1),
            SourceEntry.File("NUL.txt", 2),
            SourceEntry.File("file ", 3));

        Assert.Equal(
            Severity.Error,
            findings.Only(WindowsPreflightFindingCodes.TargetNameInvalidCharacter).Severity);
        Assert.Equal(
            Severity.Error,
            findings.Only(WindowsPreflightFindingCodes.TargetNameReservedDevice).Severity);
        Assert.Equal(
            Severity.Warning,
            findings.Only(WindowsPreflightFindingCodes.TargetNameTrailingSpaceOrPeriod).Severity);
    }

    [Fact]
    public void ANameThatTripsTwoChecksIsReportedForBoth()
    {
        // "NUL." is both a reserved device name and a name ending with a period. The two facts are
        // independent, so both are reported rather than whichever one a check happened to reach first.
        var findings = FindingsFor(SourceEntry.File("NUL.", 1));

        Assert.Equal(
            Severity.Error,
            findings.Only(WindowsPreflightFindingCodes.TargetNameReservedDevice).Severity);
        Assert.Equal(
            Severity.Warning,
            findings.Only(WindowsPreflightFindingCodes.TargetNameTrailingSpaceOrPeriod).Severity);
    }

    // ---------------------------------------------------------------------------------------------
    // E. The extended-length namespace is not a licence for a bad name, and the target is not read.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnExtendedLengthTargetDoesNotMakeABadNameAcceptable()
    {
        // "\\?\" takes a destination path out of classic MAX_PATH handling. It says nothing about
        // whether an ordinary user can rely on a reserved device name or an illegal character.
        var target = PreflightInputs.HealthyTarget(@"\\?\C:\target");
        var source = PreflightInputs.Source(
            SourceEntry.File("NUL.txt", 1),
            SourceEntry.File("a<b.txt", 2),
            SourceEntry.File("file ", 3));

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetNameReservedDevice));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetNameInvalidCharacter));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetNameTrailingSpaceOrPeriod));
    }

    [Fact]
    public void TheNameRulesDoNotDependOnTheTargetBeingUsable()
    {
        // A name problem is a fact about the source, so it survives a target that cannot be used at
        // all — and the target's own blocking Error is still reported separately.
        var source = PreflightInputs.Source(SourceEntry.File("NUL.txt", 1));
        var target = PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathUnavailable);

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetNameReservedDevice));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetPathUnavailable));
    }

    [Fact]
    public void TheNameRulesReadNothingFromTheTargetDirectory()
    {
        var missingTargetPath = Path.Combine(
            Path.GetTempPath(),
            "copy-preflight-names-" + Guid.NewGuid().ToString("N"));

        Assert.False(Directory.Exists(missingTargetPath));
        Assert.False(File.Exists(missingTargetPath));

        var source = PreflightInputs.Source(
            SourceEntry.File("a<b.txt", 1),
            SourceEntry.File("NUL", 2));

        var findings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.HealthyTarget(missingTargetPath));

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetNameInvalidCharacter));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetNameReservedDevice));
    }

    private static IReadOnlyList<Finding> FindingsFor(params SourceEntry[] entries)
    {
        // The name checks are source-only, so the destination check is stated to have found nothing:
        // that keeps the absence of collision findings a fact rather than a missing check.
        var source = PreflightInputs.Source(entries);

        return PreflightInputs.Evaluate(
            source,
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(source));
    }
}
