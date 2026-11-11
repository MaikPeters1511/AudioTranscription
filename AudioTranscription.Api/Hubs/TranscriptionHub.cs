using Microsoft.AspNetCore.SignalR;

namespace AudioTranscription.Api.Hubs;

/// <summary>
/// Live updates (job status, progress, variants). The server pushes events per user through <c>IJobNotifier</c> to the owner and
/// the users a job is shared with (ADR 0007); clients cannot subscribe to a job. There are deliberately no group methods: a
/// group membership would outlive a revoked share, so it must never be mistaken for a permission.
/// </summary>
public class TranscriptionHub : Hub;
