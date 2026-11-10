namespace AudioTranscription.Domain.Chapters;

/// <summary>A timed transcript segment as the chapter generator sees it (the corrected text if there is one, S32).</summary>
public record ChapterSegment(int Index, long StartMs, long EndMs, string Text);

/// <summary>A chapter as proposed by the LLM: it starts at a segment and runs until the next chapter starts.</summary>
public record ChapterDraft(int StartSegmentIndex, string Title, string Summary);

/// <summary>The answer of the LLM could not be turned into chapters.</summary>
public class ChapterParseException(string message) : Exception(message);
