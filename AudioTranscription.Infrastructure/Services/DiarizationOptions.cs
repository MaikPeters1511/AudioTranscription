namespace AudioTranscription.Infrastructure.Services;

/// <summary>Configuration section "Diarization" (S11): speaker recognition via sherpa-onnx, see ADR 0004.</summary>
public class DiarizationOptions
{
    public const string SectionName = "Diarization";

    /// <summary>Master switch; without it, uploads cannot request diarization even with model paths set.</summary>
    public bool Enabled { get; set; }

    /// <summary>Path to a pyannote-style ONNX segmentation model. Required when <see cref="Enabled"/>.</summary>
    public string? SegmentationModelPath { get; set; }

    /// <summary>Path to an ONNX speaker-embedding model. Required when <see cref="Enabled"/>.</summary>
    public string? EmbeddingModelPath { get; set; }

    /// <summary>Clustering similarity threshold (0–1); used only when a job does not specify a known speaker count.</summary>
    public float Threshold { get; set; } = 0.5f;

    public int NumThreads { get; set; } = 1;
}
