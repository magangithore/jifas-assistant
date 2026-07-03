using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Jifas.Assistant.Hubs;

/// <summary>
/// SignalR hub used by the monitoring dashboard.
/// Clients connect here to receive real-time AI usage events (NewMetric).
/// HubIClientProxy agar IHubContext MonitoringHub sesuai dengan test helper.
/// </summary>
public class MonitoringHub : Hub<IClientProxy>
{
    /// <summary>Called when a dashboard client connects.</summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    /// <summary>Called when a dashboard client disconnects.</summary>
    public override async Task OnDisconnectedAsync(System.Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
