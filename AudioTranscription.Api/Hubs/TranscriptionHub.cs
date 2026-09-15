using Microsoft.AspNetCore.SignalR;

namespace AudioTranscription.Api.Hubs;

public class TranscriptionHub : Hub
{
    /// <summary>
    /// Clients can join a group for a specific job to receive targeted updates.
    /// </summary>
    public async Task JoinJobGroup(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
    }

    /// <summary>
    /// Clients can leave a job group.
    /// </summary>
    public async Task LeaveJobGroup(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
    }
}
