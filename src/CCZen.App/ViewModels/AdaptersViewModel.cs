using System.Collections.ObjectModel;
using CCZen.App.Models;
using CCZen.Engine.Rules;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCZen.App.ViewModels;

/// <summary>Read-only view of the built-in adapter set (specs/03 首发 Adapter 集).</summary>
public sealed class AdaptersViewModel : ObservableObject
{
    public AdaptersViewModel()
    {
        foreach (Adapter adapter in BaselineAdapterPack.Load().Adapters)
        {
            Adapters.Add(AdapterRow.From(adapter));
        }
    }

    public ObservableCollection<AdapterRow> Adapters { get; } = [];
}
