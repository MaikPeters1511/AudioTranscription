namespace AudioTranscription.Domain.Entities;

/// <summary>
/// A template for structured post-processing (S26): instructions for the model and the JSON schema its answer
/// must follow. Templates are global; the built-in ones cannot be changed or deleted, others are managed by admins.
/// </summary>
public class PostprocessingTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Instructions to the model; may use the placeholders {{SPEAKERS}} and {{LANGUAGE}}.</summary>
    public string Prompt { get; set; } = string.Empty;
    /// <summary>JSON schema (subset, see TemplateSchemaValidator) of the answer.</summary>
    public string OutputSchema { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
}
