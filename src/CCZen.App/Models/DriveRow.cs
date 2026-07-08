namespace CCZen.App.Models;

/// <summary>Display card for one fixed drive on the dashboard.</summary>
public sealed record DriveRow(
    string Name,
    string Label,
    string FileSystem,
    string Detail,
    double UsedPercent)
{
    public static DriveRow From(DriveInfo drive)
    {
        long total = drive.TotalSize;
        long free = drive.AvailableFreeSpace;
        long used = total - free;
        double percent = total > 0 ? used * 100.0 / total : 0;
        string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel;
        return new DriveRow(
            drive.Name.TrimEnd('\\'),
            label,
            drive.DriveFormat,
            $"已用 {SizeFormatter.Format(used)} / 共 {SizeFormatter.Format(total)} · 可用 {SizeFormatter.Format(free)}",
            percent);
    }
}
