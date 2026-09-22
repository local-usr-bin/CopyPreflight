using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Stable identities of the preflight rules this milestone implements.
/// </summary>
/// <remarks>
/// <para>
/// A code identifies a <b>rule</b>, not a message. The user-facing title, explanation, impact and
/// recommendation attached to a finding may be reworded freely without changing the code, and a
/// code can be matched in logs and tests without depending on any display text.
/// </para>
/// <para>
/// Once a code has been carried by a finding its spelling is permanent: codes are added, never
/// renamed and never reused for a different rule. They are declared here, rather than written out
/// at each rule, so that their uniqueness and spelling can be asserted in one place.
/// </para>
/// <para>
/// <see cref="All"/> exists so that tests and diagnostics can enumerate the codes. It is
/// deliberately <b>not</b> a rule registry: rules are invoked directly by
/// <see cref="WindowsPreflightEvaluator"/>, and nothing in this type takes part in discovery,
/// ordering or dispatch.
/// </para>
/// </remarks>
public static class WindowsPreflightFindingCodes
{
    /// <summary>
    /// A source directory could not be enumerated, so what is inside it was never observed and the
    /// inventory is incomplete.
    /// </summary>
    public static readonly FindingCode SourceDirectoryEnumerationFailed = new("SOURCE_DIRECTORY_ENUMERATION_FAILED");

    /// <summary>
    /// A source object was listed but could not be described, so it is missing from the inventory.
    /// </summary>
    public static readonly FindingCode SourceEntryMetadataFailed = new("SOURCE_ENTRY_METADATA_FAILED");

    /// <summary>Windows rejected the target path as a malformed name.</summary>
    public static readonly FindingCode TargetPathInvalid = new("TARGET_PATH_INVALID");

    /// <summary>The target path is well formed, but no usable directory could be reached through it.</summary>
    public static readonly FindingCode TargetPathUnavailable = new("TARGET_PATH_UNAVAILABLE");

    /// <summary>A regular file is larger than a single FAT32 file can be.</summary>
    public static readonly FindingCode Fat32FileTooLarge = new("FAT32_FILE_TOO_LARGE");

    /// <summary>A name this entry would create at the target is longer than the target's limit.</summary>
    public static readonly FindingCode TargetComponentTooLong = new("TARGET_COMPONENT_TOO_LONG");

    /// <summary>The composed destination path is long enough to be a classic Windows path risk.</summary>
    public static readonly FindingCode TraditionalPathLengthRisk = new("TRADITIONAL_PATH_LENGTH_RISK");

    /// <summary>An observed object carries the reparse point attribute.</summary>
    public static readonly FindingCode ReparsePointPresent = new("REPARSE_POINT_PRESENT");

    /// <summary>The observed source size is larger than the space Windows reported as available.</summary>
    public static readonly FindingCode TargetSpaceMayBeInsufficient = new("TARGET_SPACE_MAY_BE_INSUFFICIENT");

    /// <summary>The target file system could not be determined, so file system specific checks were skipped.</summary>
    public static readonly FindingCode TargetFileSystemUnknown = new("TARGET_FILESYSTEM_UNKNOWN");

    /// <summary>The target's maximum name length could not be determined, so name length was not checked.</summary>
    public static readonly FindingCode TargetComponentLimitUnknown = new("TARGET_COMPONENT_LIMIT_UNKNOWN");

    /// <summary>The target's free space could not be determined, so no space comparison was made.</summary>
    public static readonly FindingCode TargetFreeSpaceUnknown = new("TARGET_FREE_SPACE_UNKNOWN");

    /// <summary>A name this entry would create contains characters an ordinary Windows name cannot.</summary>
    public static readonly FindingCode TargetNameInvalidCharacter = new("TARGET_NAME_INVALID_CHARACTER");

    /// <summary>A name this entry would create is a reserved Windows device name.</summary>
    public static readonly FindingCode TargetNameReservedDevice = new("TARGET_NAME_RESERVED_DEVICE");

    /// <summary>A name this entry would create ends with a space or a period.</summary>
    public static readonly FindingCode TargetNameTrailingSpaceOrPeriod = new("TARGET_NAME_TRAILING_SPACE_OR_PERIOD");

    /// <summary>Sibling source objects have names that differ only in letter case.</summary>
    public static readonly FindingCode SourceCaseCollisionRisk = new("SOURCE_CASE_COLLISION_RISK");

    /// <summary>A regular file already occupies the destination a regular source file maps onto.</summary>
    public static readonly FindingCode TargetExistingFileCollision = new("TARGET_EXISTING_FILE_COLLISION");

    /// <summary>
    /// The destination a source object maps onto already holds an object of the other kind, so the
    /// exact mapping is structurally blocked.
    /// </summary>
    public static readonly FindingCode TargetExistingTypeCollision = new("TARGET_EXISTING_TYPE_COLLISION");

    /// <summary>The destination a source object maps onto already holds a reparse point.</summary>
    public static readonly FindingCode TargetExistingReparsePoint = new("TARGET_EXISTING_REPARSE_POINT");

    /// <summary>
    /// A source object that is itself a reparse point maps onto a location that already holds an
    /// ordinary file or directory.
    /// </summary>
    public static readonly FindingCode SourceReparsePointExistingTargetObject =
        new("SOURCE_REPARSE_POINT_WITH_EXISTING_TARGET_OBJECT");

    /// <summary>
    /// Whether an object already occupies a mapped destination could not be established, so existence
    /// and kind remain unknown there.
    /// </summary>
    public static readonly FindingCode TargetExistingObjectCheckFailed =
        new("TARGET_EXISTING_OBJECT_CHECK_FAILED");

    /// <summary>
    /// The existing-object check ran but left part of the inventory unexamined, so no conclusion about
    /// collisions covers those locations.
    /// </summary>
    public static readonly FindingCode TargetExistingObjectCoverageIncomplete =
        new("TARGET_EXISTING_OBJECT_COVERAGE_INCOMPLETE");

    /// <summary>
    /// No existing-object check was supplied, so nothing about collisions was examined.
    /// </summary>
    public static readonly FindingCode TargetExistingObjectCheckNotPerformed =
        new("TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED");

    /// <summary>
    /// The existing-object check was cancelled before it finished, so the locations it never reached
    /// are unknown rather than absent.
    /// </summary>
    public static readonly FindingCode TargetExistingObjectCheckCancelled =
        new("TARGET_EXISTING_OBJECT_CHECK_CANCELLED");

    /// <summary>
    /// Every code declared above, in declaration order. For tests and diagnostics only; nothing
    /// iterates this to decide which rules run.
    /// </summary>
    public static readonly IReadOnlyList<FindingCode> All =
    [
        SourceDirectoryEnumerationFailed,
        SourceEntryMetadataFailed,
        TargetPathInvalid,
        TargetPathUnavailable,
        Fat32FileTooLarge,
        TargetComponentTooLong,
        TraditionalPathLengthRisk,
        ReparsePointPresent,
        TargetSpaceMayBeInsufficient,
        TargetFileSystemUnknown,
        TargetComponentLimitUnknown,
        TargetFreeSpaceUnknown,
        TargetNameInvalidCharacter,
        TargetNameReservedDevice,
        TargetNameTrailingSpaceOrPeriod,
        SourceCaseCollisionRisk,
        TargetExistingFileCollision,
        TargetExistingTypeCollision,
        TargetExistingReparsePoint,
        SourceReparsePointExistingTargetObject,
        TargetExistingObjectCheckFailed,
        TargetExistingObjectCoverageIncomplete,
        TargetExistingObjectCheckNotPerformed,
        TargetExistingObjectCheckCancelled,
    ];
}
