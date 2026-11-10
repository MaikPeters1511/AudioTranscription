using AudioTranscription.Domain.Entities;

namespace AudioTranscription.Api.BackgroundServices;

/// <summary>The transcript with the name of the speaker in front of what they said (S26); only speakers with a chosen name are named.</summary>
public static class SpeakerTranscript
{
    public static string Build(IEnumerable<TranscriptSegment> segments, IReadOnlyDictionary<int, string> namedSpeakers)
    {
        var lines = new List<string>();
        int? lastSpeaker = null;
        foreach (var segment in segments.OrderBy(s => s.Index))
        {
            var text = segment.EffectiveText.Trim();
            if (text.Length == 0)
                continue;

            var name = segment.SpeakerIndex is { } index && namedSpeakers.TryGetValue(index, out var n) ? n : null;
            if (name is not null && segment.SpeakerIndex == lastSpeaker && lines.Count > 0)
                lines[^1] += " " + text;
            else
                lines.Add(name is null ? text : $"{name}: {text}");
            lastSpeaker = name is null ? null : segment.SpeakerIndex;
        }
        return string.Join("\n", lines);
    }
}
