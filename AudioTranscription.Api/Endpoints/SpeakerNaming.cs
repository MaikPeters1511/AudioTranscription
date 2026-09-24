using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>Resolves speaker display names (S11): a user-chosen name, or "Sprecher N" until renamed.</summary>
internal static class SpeakerNaming
{
    public static string DefaultName(int index) => $"Sprecher {index + 1}";

    public static async Task<Dictionary<int, string>> LoadResolvedNamesAsync(AppDbContext dbContext, Guid jobId) =>
        await dbContext.JobSpeakers
            .Where(s => s.AudioJobId == jobId)
            .ToDictionaryAsync(s => s.Index, s => s.DisplayName ?? DefaultName(s.Index));
}
