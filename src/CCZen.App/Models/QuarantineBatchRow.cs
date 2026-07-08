namespace CCZen.App.Models;

/// <summary>One executed cleanup batch shown in the quarantine/undo center.</summary>
public sealed record QuarantineBatchRow(
    string BatchId,
    string VolumeRoot,
    string Summary,
    string TimeLabel);
