using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniExplorer.Models;
using MiniExplorer.Services;

namespace MiniExplorer.ViewModels;

public sealed partial class OperationsDashboardViewModel : ObservableObject
{
    private readonly OperationsOverviewService _overviewService;

    public OperationsDashboardViewModel(OperationsOverviewService overviewService)
    {
        _overviewService = overviewService;
        SummaryCards = new ObservableCollection<OperationSummary>();
        ActiveAlerts = new ObservableCollection<ActiveOperationAlert>();
        RecentEvents = new ObservableCollection<OperationalEvent>();
        Refresh();
    }

    public ObservableCollection<OperationSummary> SummaryCards { get; }

    public ObservableCollection<ActiveOperationAlert> ActiveAlerts { get; }

    public ObservableCollection<OperationalEvent> RecentEvents { get; }

    [ObservableProperty]
    private DateTimeOffset _lastUpdated;

    public void Refresh()
    {
        var overview = _overviewService.GetOverview();
        ReplaceItems(SummaryCards, overview.SummaryCards);
        ReplaceItems(ActiveAlerts, overview.ActiveAlerts);
        ReplaceItems(RecentEvents, overview.RecentEvents);
        LastUpdated = DateTimeOffset.Now;
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
