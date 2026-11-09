using AudioTranscription.Api.Access;
using Microsoft.AspNetCore.SignalR;

namespace AudioTranscription.Api.Hubs;

/// <summary>
/// Live updates (job status, progress, variants). Events are pushed per user by <see cref="IJobNotifier"/>
/// to the owner and the users a job is shared with (ADR 0007); clients cannot subscribe to someone else's job.
/// </summary>
public class TranscriptionHub(JobAccessService access) : Hub
{
    /// <summary>
    /// Clients can join a group for a specific job to receive targeted updates. Only users who may
    /// read the job can join.
    /// </summary>
    public async Task JoinJobGroup(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id) || await access.GetAccessAsync(id, Context.UserIdentifier) == JobAccess.None)
            throw new HubException("Job not found.");

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
