using System.Collections.ObjectModel;
using CCZen.App.Models;
using CCZen.App.Services;
using CCZen.Engine.Index;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCZen.App.ViewModels;

/// <summary>
/// View model for conditional large file/directory search (SCAN-FR-025):
/// scans on first use, then queries the in-memory index instantly.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int MaxResults = 100;

    private readonly IEngineClient _engine;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _minSizeMb = "100";

    [ObservableProperty]
    private string _nameFilter = string.Empty;

    [ObservableProperty]
    private int _kindIndex = (int)SearchKind.All;

    [ObservableProperty]
    private string _status = string.Empty;

    public SearchViewModel(IEngineClient engine)
    {
        _engine = engine;
    }

    public ObservableCollection<SearchResultRow> Results { get; } = [];

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsBusy = true;
        try
        {
            await EnsureIndexAsync();
            await RunSearchAsync();
        }
        catch (Exception ex)
        {
            Status = $"出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnsureIndexAsync()
    {
        if (await _engine.GetStatusAsync() is not null)
        {
            return;
        }

        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        Status = $"首次搜索：正在扫描 {root} …";
        var summary = await _engine.ScanAsync(root);
        Status = $"已索引 {summary.FileCount:N0} 个文件（{summary.ElapsedSeconds:0.00} s）。";
    }

    private async Task RunSearchAsync()
    {
        var query = new SearchQuery(
            (SearchKind)KindIndex,
            ParseMinSizeBytes(),
            string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter.Trim(),
            MaxResults);

        IReadOnlyList<FileEntry> entries = await _engine.SearchAsync(query);

        Results.Clear();
        foreach (FileEntry entry in entries)
        {
            Results.Add(SearchResultRow.From(entry));
        }

        Status = $"命中 {entries.Count} 项（按占用空间降序，最多 {MaxResults} 项）。";
    }

    private long ParseMinSizeBytes() =>
        double.TryParse(MinSizeMb, out double mb) && mb > 0
            ? (long)(mb * 1024 * 1024)
            : 0;
}
