using MiniExplorer.Models;

namespace MiniExplorer.Services;

public sealed class OperationsOverviewService
{
    public OperationsOverview GetOverview()
    {
        var now = DateTimeOffset.Now;

        return new OperationsOverview(
            new[]
            {
                new OperationSummary("Fleet readiness", "96%", "24 of 25 assets reporting", AssetHealthStatus.Healthy),
                new OperationSummary("Energy reserve", "78%", "Average reserve across active routes", AssetHealthStatus.Healthy),
                new OperationSummary("Service queue", "3", "Assets waiting for scheduled inspection", AssetHealthStatus.Attention),
                new OperationSummary("Offline assets", "1", "Last heartbeat outside tolerance", AssetHealthStatus.Degraded)
            },
            new[]
            {
                new ActiveOperationAlert("Candela C-8 Demo North", AlertSeverity.Warning, "Battery temperature trend above nominal range", now.AddMinutes(-18)),
                new ActiveOperationAlert("Dockside charger 02", AlertSeverity.Advisory, "Charge cycle delayed by marina load shedding", now.AddMinutes(-42)),
                new ActiveOperationAlert("Service bay gateway", AlertSeverity.Critical, "Telemetry heartbeat missed for 12 minutes", now.AddHours(-1).AddMinutes(-7))
            },
            new[]
            {
                new OperationalEvent(now.AddMinutes(-5), "Stockholm route", "Foil calibration profile synchronized."),
                new OperationalEvent(now.AddMinutes(-21), "Service bay", "Inspection checklist completed for Candela P-12 shuttle."),
                new OperationalEvent(now.AddMinutes(-34), "Charging", "Dockside charger 01 completed balancing cycle."),
                new OperationalEvent(now.AddHours(-1).AddMinutes(-12), "Fleet", "Morning readiness snapshot archived locally.")
            });
    }
}
