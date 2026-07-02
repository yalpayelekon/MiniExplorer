namespace MiniExplorer.Models;

public enum AlertSeverity
{
    Informational,
    Advisory,
    Warning,
    Critical
}

public enum AssetHealthStatus
{
    Healthy,
    Attention,
    Degraded,
    Offline
}

public sealed record OperationSummary(
    string Title,
    string Value,
    string Detail,
    AssetHealthStatus Status);

public sealed record ActiveOperationAlert(
    string AssetName,
    AlertSeverity Severity,
    string Message,
    DateTimeOffset RaisedAt);

public sealed record OperationalEvent(
    DateTimeOffset OccurredAt,
    string Area,
    string Description);

public sealed record OperationsOverview(
    IReadOnlyList<OperationSummary> SummaryCards,
    IReadOnlyList<ActiveOperationAlert> ActiveAlerts,
    IReadOnlyList<OperationalEvent> RecentEvents);
