using AudioTranscription.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Tests.Data;

/// <summary>
/// Full-text population runs in the background after CREATE FULLTEXT INDEX and after every insert; tests that
/// search must wait for it. On a busy CI runner (several containers at once) this has taken longer than 30 seconds,
/// so the limit is generous: a healthy run returns after a few hundred milliseconds either way.
/// </summary>
internal static class FullTextPopulation
{
    public static readonly string[] AllTables = ["AudioJobs", "TranscriptSegments", "TranscriptVariants"];

    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    public static async Task WaitAsync(AppDbContext db, params string[] tableNames)
    {
        var tables = tableNames.Length > 0 ? tableNames : AllTables;
        var deadline = DateTime.UtcNow + Limit;
        var pending = new Dictionary<string, int>();
        while (true)
        {
            pending.Clear();
            foreach (var table in tables)
            {
                // 0 = idle; anything else is a population in progress (1 full, 6 incremental, 9 change tracking, ...)
                var status = await db.Database.SqlQuery<int>(
                    $"SELECT OBJECTPROPERTYEX(OBJECT_ID({table}), 'TableFulltextPopulateStatus') AS Value").SingleAsync();
                if (status != 0)
                    pending[table] = status;
            }

            if (pending.Count == 0)
                return;
            if (DateTime.UtcNow >= deadline)
            {
                var detail = string.Join(", ", pending.Select(p => $"{p.Key}: status {p.Value}"));
                throw new TimeoutException($"Full-text population did not finish within {Limit.TotalSeconds:F0} s ({detail}).");
            }

            await Task.Delay(200);
        }
    }
}
