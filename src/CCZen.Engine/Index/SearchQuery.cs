namespace CCZen.Engine.Index;

/// <summary>What kinds of entries a search returns.</summary>
public enum SearchKind
{
    Files,
    Directories,
    All,
}

/// <summary>
/// Conditional query over a scanned index (SCAN-FR-025): minimum allocated
/// size, optional case-insensitive name fragment (file name / extension for
/// files, full path fragment for directories), and result cap.
/// </summary>
public sealed record SearchQuery(
    SearchKind Kind,
    long MinSizeBytes,
    string? NameContains,
    int MaxResults);
