using AudioTranscription.Api.Access;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Glossary;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>
/// Personal glossaries of names and technical terms (S22). A glossary chosen at upload becomes Whisper's
/// initial prompt. Glossaries are private: someone else's id is answered like a missing one.
/// </summary>
public static class GlossaryEndpoints
{
    private const int MaxNameLength = 100;
    private const int MaxTermLength = 100;
    private const int MaxTerms = 200;

    public static void MapGlossaryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/glossaries").WithTags("Glossaries");

        group.MapGet("/", GetGlossaries)
            .WithName("GetGlossaries").WithSummary("The user's glossaries").WithDescription("All glossaries of the signed-in user with their terms");
        group.MapPost("/", CreateGlossary)
            .WithName("CreateGlossary").WithSummary("Create a glossary")
            .WithDescription($"Name (unique per user, case-insensitive) and terms (at most {MaxTerms}, {MaxTermLength} characters each, no duplicates). Rejected if the terms exceed Whisper's {GlossaryPrompt.MaxTokens}-token prompt window.");
        group.MapPut("/{id:guid}", UpdateGlossary)
            .WithName("UpdateGlossary").WithSummary("Replace name and terms of a glossary").WithDescription("Same rules as creating; jobs that already used the glossary keep their prompt");
        group.MapDelete("/{id:guid}", DeleteGlossary)
            .WithName("DeleteGlossary").WithSummary("Delete a glossary").WithDescription("Jobs that used it keep their stored prompt");
    }

    private static async Task<Results<Ok<List<GlossaryDto>>, UnauthorizedHttpResult>> GetGlossaries(HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var glossaries = await db.Glossaries.Include(g => g.Terms).Where(g => g.OwnerId == userId).OrderBy(g => g.Name).ToListAsync();
        return TypedResults.Ok(glossaries.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<GlossaryDto>, ValidationProblem, Conflict<string>, UnauthorizedHttpResult>> CreateGlossary(
        SaveGlossaryRequest request, HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();
        if (Validate(request, out var name, out var terms) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var lower = name.ToLower();
        if (await db.Glossaries.AnyAsync(g => g.OwnerId == userId && g.Name.ToLower() == lower))
            return TypedResults.Conflict("A glossary with this name already exists.");

        var glossary = new Glossary { OwnerId = userId, Name = name };
        SetTerms(glossary, terms);
        db.Glossaries.Add(glossary);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The same name created in parallel: the unique index decided
            return TypedResults.Conflict("A glossary with this name already exists.");
        }
        return TypedResults.Created($"/api/glossaries/{glossary.Id}", ToDto(glossary));
    }

    private static async Task<Results<Ok<GlossaryDto>, ValidationProblem, NotFound, Conflict<string>, UnauthorizedHttpResult>> UpdateGlossary(
        Guid id, SaveGlossaryRequest request, HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();
        if (Validate(request, out var name, out var terms) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var glossary = await db.Glossaries.Include(g => g.Terms).SingleOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
        if (glossary is null)
            return TypedResults.NotFound();

        var lower = name.ToLower();
        if (await db.Glossaries.AnyAsync(g => g.OwnerId == userId && g.Id != id && g.Name.ToLower() == lower))
            return TypedResults.Conflict("A glossary with this name already exists.");

        glossary.Name = name;
        db.GlossaryTerms.RemoveRange(glossary.Terms);
        glossary.Terms = [];
        SetTerms(glossary, terms);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return TypedResults.Conflict("A glossary with this name already exists.");
        }
        return TypedResults.Ok(ToDto(glossary));
    }

    private static async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> DeleteGlossary(Guid id, HttpContext http, AppDbContext db)
    {
        if (JobAccessService.GetUserId(http.User) is not { Length: > 0 } userId)
            return TypedResults.Unauthorized();

        var glossary = await db.Glossaries.Include(g => g.Terms).SingleOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
        if (glossary is null)
            return TypedResults.NotFound();

        db.GlossaryTerms.RemoveRange(glossary.Terms);
        db.Glossaries.Remove(glossary);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    /// <summary>Returns the validation errors, or null and the normalized name and terms.</summary>
    private static Dictionary<string, string[]>? Validate(SaveGlossaryRequest request, out string name, out List<string> terms)
    {
        var errors = new Dictionary<string, string[]>();
        name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaxNameLength)
            errors["name"] = [$"Must be 1 to {MaxNameLength} characters."];

        terms = (request.Terms ?? []).Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0).ToList();
        if (terms.Count > MaxTerms)
            errors["terms"] = [$"At most {MaxTerms} terms."];
        else if (terms.Any(t => t.Length > MaxTermLength))
            errors["terms"] = [$"Each term may have at most {MaxTermLength} characters."];
        else if (terms.Distinct(StringComparer.OrdinalIgnoreCase).Count() != terms.Count)
            errors["terms"] = ["Terms must be unique (case-insensitive)."];
        else if (!GlossaryPrompt.Fits(terms))
        {
            errors["terms"] =
            [
                $"Too long for Whisper's prompt: about {GlossaryPrompt.EstimateTokens(GlossaryPrompt.Build(terms))} of {GlossaryPrompt.MaxTokens} tokens. Remove terms."
            ];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static void SetTerms(Glossary glossary, List<string> terms) =>
        glossary.Terms = terms.Select((term, i) => new GlossaryTerm { GlossaryId = glossary.Id, Position = i, Term = term }).ToList();

    private static GlossaryDto ToDto(Glossary glossary)
    {
        var terms = glossary.Terms.OrderBy(t => t.Position).Select(t => t.Term).ToList();
        return new GlossaryDto(glossary.Id, glossary.Name, terms, GlossaryPrompt.EstimateTokens(GlossaryPrompt.Build(terms)), GlossaryPrompt.MaxTokens);
    }
}
