using AudioTranscription.Api.Auth;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Templates;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>
/// Post-processing templates (S26): every signed-in user can list and use them, admins manage the custom ones.
/// The built-in templates cannot be changed or deleted.
/// </summary>
public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/templates").WithTags("Templates");

        group.MapGet("", List)
            .WithName("ListTemplates")
            .WithSummary("List post-processing templates")
            .WithDescription("Built-in and custom templates, built-in ones first");

        var admin = group.MapGroup("").RequireAuthorization(Roles.AdminPolicy);
        admin.MapPost("", Create).WithName("CreateTemplate").WithSummary("Create a custom template (admin)");
        admin.MapPut("{id:guid}", Update).WithName("UpdateTemplate").WithSummary("Change a custom template (admin)");
        admin.MapDelete("{id:guid}", Delete)
            .WithName("DeleteTemplate")
            .WithSummary("Delete a custom template (admin)")
            .WithDescription("Variants generated from it stay, they just lose the link; built-in templates cannot be deleted (409)");
    }

    private static async Task<Ok<List<TemplateDto>>> List(AppDbContext db)
    {
        var templates = await db.PostprocessingTemplates.AsNoTracking()
            .OrderByDescending(t => t.IsBuiltIn).ThenBy(t => t.Name)
            .ToListAsync();
        return TypedResults.Ok(templates.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<TemplateDto>, ValidationProblem>> Create(SaveTemplateRequest request, AppDbContext db)
    {
        var errors = await ValidateAsync(request, db, ignoreId: null);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var template = new PostprocessingTemplate
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Prompt = request.Prompt.Trim(),
            OutputSchema = request.OutputSchema.Trim(),
        };
        db.PostprocessingTemplates.Add(template);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/templates/{template.Id}", ToDto(template));
    }

    private static async Task<Results<Ok<TemplateDto>, ValidationProblem, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> Update(
        Guid id, SaveTemplateRequest request, AppDbContext db)
    {
        var template = await db.PostprocessingTemplates.FindAsync(id);
        if (template is null)
            return NotFound(id);
        if (template.IsBuiltIn)
            return BuiltInConflict();

        var errors = await ValidateAsync(request, db, ignoreId: id);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        template.Name = request.Name.Trim();
        template.Description = request.Description?.Trim() ?? string.Empty;
        template.Prompt = request.Prompt.Trim();
        template.OutputSchema = request.OutputSchema.Trim();
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToDto(template));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> Delete(Guid id, AppDbContext db)
    {
        var template = await db.PostprocessingTemplates.FindAsync(id);
        if (template is null)
            return NotFound(id);
        if (template.IsBuiltIn)
            return BuiltInConflict();

        // Variants made from it keep their text but lose the link
        foreach (var variant in await db.TranscriptVariants.Where(v => v.TemplateId == id).ToListAsync())
            variant.TemplateId = null;
        db.PostprocessingTemplates.Remove(template);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(SaveTemplateRequest request, AppDbContext db, Guid? ignoreId)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > TemplatePromptRules.MaxNameLength)
            errors["name"] = [$"The name must not be empty and at most {TemplatePromptRules.MaxNameLength} characters long."];
        else if (await db.PostprocessingTemplates.AnyAsync(t => t.Name == name && t.Id != ignoreId))
            errors["name"] = ["A template with this name already exists."];

        if ((request.Description?.Trim().Length ?? 0) > TemplatePromptRules.MaxDescriptionLength)
            errors["description"] = [$"The description must be at most {TemplatePromptRules.MaxDescriptionLength} characters long."];

        var promptErrors = TemplatePromptRules.Validate(request.Prompt?.Trim());
        if (promptErrors.Count > 0)
            errors["prompt"] = [.. promptErrors];

        var schemaErrors = TemplateSchemaValidator.ValidateDefinition(request.OutputSchema?.Trim() ?? string.Empty);
        if (schemaErrors.Count > 0)
            errors["outputSchema"] = [.. schemaErrors];
        return errors;
    }

    private static TemplateDto ToDto(PostprocessingTemplate t) => new(t.Id, t.Name, t.Description, t.Prompt, t.OutputSchema, t.IsBuiltIn);

    private static NotFound<ProblemDetails> NotFound(Guid id) =>
        TypedResults.NotFound(new ProblemDetails { Title = "Template not found", Detail = $"Template {id} does not exist.", Status = StatusCodes.Status404NotFound });

    private static Conflict<ProblemDetails> BuiltInConflict() =>
        TypedResults.Conflict(new ProblemDetails { Title = "Built-in template", Detail = "Built-in templates cannot be changed or deleted.", Status = StatusCodes.Status409Conflict });
}
