namespace AudioTranscription.Domain.Enums;

/// <summary>What an on-demand transcript variant (S10) is generated for.</summary>
public enum PostProcessingMode
{
    /// <summary>Punctuation, filler words, paragraphs; wording unchanged (formerly AudioJob.ProcessedTranscript, see S04).</summary>
    Cleanup = 0,
    Summary = 1,
    BulletPoints = 2,
    ActionItems = 3,
    /// <summary>Requires <see cref="Entities.TranscriptVariant.TargetLanguage"/>.</summary>
    Translate = 4,
    /// <summary>Structured result of a <see cref="Entities.PostprocessingTemplate"/> (S26); requires <see cref="Entities.TranscriptVariant.TemplateId"/>.</summary>
    Template = 5,
}
