using AudioTranscription.Api.Hubs;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Access;

/// <summary>
/// Pushes live updates about a job only to the people who may see it: its owner and the users it is
/// shared with (ADR 0007). Replaces broadcasting to every connected client.
/// </summary>
public interface IJobNotifier
{
    /// <summary>Owner and shared users of a job. Resolve this <em>before</em> deleting the job.</summary>
    Task<IReadOnlyList<string>> GetRecipientsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Sends an event to the given users. Does nothing for an empty list.</summary>
    Task SendAsync(IReadOnlyList<string> recipients, string method, object payload, CancellationToken cancellationToken = default);

    /// <summary>Sends an event to everyone who may see the job.</summary>
    Task NotifyAsync(Guid jobId, string method, object payload, CancellationToken cancellationToken = default);
}

public class JobNotifier(IHubContext<TranscriptionHub> hub, AppDbContext db) : IJobNotifier
{
    public async Task<IReadOnlyList<string>> GetRecipientsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var owner = await db.AudioJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.OwnerId)
            .SingleOrDefaultAsync(cancellationToken);
        var shared = await db.JobShares
            .AsNoTracking()
            .Where(s => s.AudioJobId == jobId)
            .Select(s => s.UserId)
            .ToListAsync(cancellationToken);

        return (owner is null ? shared : shared.Prepend(owner)).Distinct().ToList();
    }

    public Task SendAsync(IReadOnlyList<string> recipients, string method, object payload, CancellationToken cancellationToken = default) =>
        recipients.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Users(recipients).SendAsync(method, payload, cancellationToken);

    public async Task NotifyAsync(Guid jobId, string method, object payload, CancellationToken cancellationToken = default) =>
        await SendAsync(await GetRecipientsAsync(jobId, cancellationToken), method, payload, cancellationToken);
}
