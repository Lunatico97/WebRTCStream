using Microsoft.AspNetCore.SignalR;

public class ConnectionHub : Hub
{
    public string GetConnectionId()
    {
        return Context.ConnectionId;
    }

    public async Task SendCount(int count)
    {
        await Clients.All.SendAsync("ReceiveCount", count);
    }

    public async Task JoinStream(string lobby)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, lobby);
    }

    public async Task RequestFeed(string lobby)
    {
        await Clients.OthersInGroup(lobby).SendAsync("ReceiveFeedRequest", Context.ConnectionId);
    }

    public async Task SendSignal(string receiverId, string signal)
    {
        await Clients.Client(receiverId).SendAsync("ReceiveSignal", Context.ConnectionId, signal);
    }

    public async Task SendAcknowledgement(string receiverId, string sdp)
    {
        await Clients.Client(receiverId).SendAsync("ReceiveAcknowledgment", Context.ConnectionId, sdp);
    }

    public async Task SendIceCandidate(string receiverId, string candidate)
    {
        await Clients.Client(receiverId).SendAsync("ReceiveIceCandidate", Context.ConnectionId, candidate);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.All.SendAsync("RemoveConnection", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}