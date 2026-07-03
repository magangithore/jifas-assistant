using Microsoft.AspNetCore.SignalR;
using Jifas.Assistant.Hubs;

namespace Jifas.Assistant.Tests;

/// <summary>
/// Helper untuk test - membuat fake IHubContext.
/// MonitoringHub : HubIClientProxy.
/// IHubContextMonitoringHub.Clients = IHubClients (non-generic).
/// FakeHubClients implements IHubClients AND IHubClientsIClientProxy (AllExcept).
/// </summary>
public static class TestHelpers
{
    public static IHubContext<MonitoringHub> CreateFakeHubContext()
    {
        return new FakeHubContext();
    }

    private sealed class FakeHubContext : IHubContext<MonitoringHub>
    {
        // Interface requires IHubClients, implementation returns FakeHubClients
        public IHubClients Clients { get; } = new FakeHubClients();
        public IGroupManager Groups => new FakeGroupManager();
    }

    // Implements BOTH IHubClients (non-generic) and IHubClientsIClientProxy
    // so Clients property can be assigned from IHubClients or IHubClientsIClientProxy
    private sealed class FakeHubClients : IHubClients, IHubClients<IClientProxy>
    {
        // IHubClients non-generic
        public IClientProxy All => null!;
        public IClientProxy? Client(string connectionId) => null;
        public IClientProxy? Clients(IReadOnlyList<string> connectionIds) => null;
        public IClientProxy? Group(string groupName) => null;
        public IClientProxy? Groups(IReadOnlyList<string> groupNames) => null;
        public IClientProxy? GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => null;
        public IClientProxy? User(string userId) => null;
        public IClientProxy? Users(IReadOnlyList<string> userIds) => null;

        // IHubClientsIClientProxy
        IClientProxy IHubClients<IClientProxy>.AllExcept(IReadOnlyList<string> excludedConnectionIds) => null!;
        IClientProxy IHubClients<IClientProxy>.All => null!;
        IClientProxy? IHubClients<IClientProxy>.Client(string connectionId) => null;
        IClientProxy? IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => null;
        IClientProxy? IHubClients<IClientProxy>.Group(string groupName) => null;
        IClientProxy? IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => null;
        IClientProxy? IHubClients<IClientProxy>.GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => null;
        IClientProxy? IHubClients<IClientProxy>.User(string userId) => null;
        IClientProxy? IHubClients<IClientProxy>.Users(IReadOnlyList<string> userIds) => null;
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public IReadOnlyList<string> GroupConnections(string groupName) =>
            Array.Empty<string>();
    }
}
