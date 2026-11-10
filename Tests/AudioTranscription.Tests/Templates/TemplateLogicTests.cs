using System.Text.Json;
using System.Text.Json.Nodes;
using AudioTranscription.Domain.Templates;
using FluentAssertions;

namespace AudioTranscription.Tests.Templates;

/// <summary>S26-T1/T2: schema check of the structured output, rendering as Markdown, owners only for named speakers, prompt rules.</summary>
public class TemplateLogicTests
{
    private const string TaskSchema = """
        {"type":"object","required":["tasks"],"properties":{"tasks":{"type":"array","title":"Aufgaben","items":{"type":"object","required":["task"],
          "properties":{"task":{"type":"string"},"owner":{"type":"string","title":"Verantwortlich"},"due":{"type":"string","title":"Fällig"}}}}}}
        """;

    // --- schema validation -------------------------------------------------------

    [Fact]
    public void Validate_AcceptsAMatchingDocument() =>
        TemplateSchemaValidator.Validate(TaskSchema, """{"tasks":[{"task":"Budget freigeben","owner":"Anna"}]}""").Should().BeEmpty();

    [Theory]
    [InlineData("""{}""", "tasks")]
    [InlineData("""{"tasks":"keine Liste"}""", "tasks")]
    [InlineData("""{"tasks":[{"owner":"Anna"}]}""", "task")]
    [InlineData("""{"tasks":[{"task":42}]}""", "task")]
    [InlineData("""[1,2]""", "object")]
    public void Validate_ReportsWhatDoesNotMatch(string document, string mentions) =>
        TemplateSchemaValidator.Validate(TaskSchema, document).Should().Contain(e => e.Contains(mentions));

    [Fact]
    public void Validate_ChecksEnumsAndBasicTypes()
    {
        const string schema = """{"type":"object","properties":{"priority":{"type":"string","enum":["hoch","niedrig"]},"count":{"type":"integer"},"done":{"type":"boolean"}}}""";

        TemplateSchemaValidator.Validate(schema, """{"priority":"hoch","count":3,"done":true}""").Should().BeEmpty();
        TemplateSchemaValidator.Validate(schema, """{"priority":"mittel"}""").Should().NotBeEmpty();
        TemplateSchemaValidator.Validate(schema, """{"count":1.5}""").Should().NotBeEmpty();
        TemplateSchemaValidator.Validate(schema, """{"done":"ja"}""").Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_AllowsAdditionalProperties() =>
        TemplateSchemaValidator.Validate(TaskSchema, """{"tasks":[],"extra":1}""").Should().BeEmpty();

    [Theory]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string"}}}""", true)]
    [InlineData("nicht json", false)]
    [InlineData("""[1]""", false)]
    [InlineData("""{"properties":{}}""", false)]
    [InlineData("""{"type":"banane"}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"banane"}}}""", false)]
    public void ValidateDefinition_ReportsUnusableSchemas(string schema, bool valid) =>
        (TemplateSchemaValidator.ValidateDefinition(schema).Count == 0).Should().Be(valid);

    // --- extracting the document -------------------------------------------------

    [Fact]
    public void ExtractJson_FindsTheDocumentInChattyAnswers()
    {
        var node = TemplateOutput.ExtractJson("Gern!\n```json\n{\"tasks\":[]}\n```\nFertig.");

        node!["tasks"]!.AsArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Kein JSON hier.")]
    [InlineData("{\"tasks\": [")]
    public void ExtractJson_ReturnsNullForUnusableAnswers(string answer) =>
        TemplateOutput.ExtractJson(answer).Should().BeNull();

    // --- owners ------------------------------------------------------------------

    [Fact]
    public void KeepOwners_KeepsNamedSpeakersAndRemovesEveryoneElse()
    {
        var document = JsonNode.Parse("""{"tasks":[{"task":"A","owner":"anna"},{"task":"B","owner":"Herr Erfundener"},{"task":"C","owner":"Sprecher 1"},{"task":"D"}]}""")!;

        TemplateOutput.KeepOwners(document, ["Anna", "Ben"]);

        var tasks = document["tasks"]!.AsArray();
        tasks[0]!["owner"]!.GetValue<string>().Should().Be("Anna", "the spelling of the named speaker wins");
        tasks[1]!.AsObject().ContainsKey("owner").Should().BeFalse();
        tasks[2]!.AsObject().ContainsKey("owner").Should().BeFalse();
        tasks[3]!.AsObject().ContainsKey("owner").Should().BeFalse();
    }

    [Fact]
    public void KeepOwners_WithoutNamedSpeakers_RemovesAllOwners()
    {
        var document = JsonNode.Parse("""{"tasks":[{"task":"A","owner":"Anna"}]}""")!;

        TemplateOutput.KeepOwners(document, []);

        document["tasks"]![0]!.AsObject().ContainsKey("owner").Should().BeFalse();
    }

    // --- Markdown ------------------------------------------------------------------

    [Fact]
    public void ToMarkdown_RendersTitlesParagraphsAndLists()
    {
        const string schema = """
            {"type":"object","properties":{"title":{"type":"string"},"summary":{"type":"string","title":"Zusammenfassung"},
             "decisions":{"type":"array","title":"Beschlüsse","items":{"type":"string"}},
             "tasks":{"type":"array","title":"Aufgaben","items":{"type":"object","properties":{"task":{"type":"string"},"owner":{"type":"string","title":"Verantwortlich"},"due":{"type":"string","title":"Fällig"}}}}}}
            """;
        const string document = """
            {"title":"Wochenmeeting","summary":"Das Budget wurde besprochen.","decisions":["Budget freigeben","Termin verschieben"],
             "tasks":[{"task":"Angebot einholen","owner":"Anna","due":"2026-11-20"},{"task":"Raum buchen"}]}
            """;

        var markdown = TemplateOutput.ToMarkdown(schema, JsonNode.Parse(document)!);

        markdown.Should().Be(
            "# Wochenmeeting\n\n## Zusammenfassung\n\nDas Budget wurde besprochen.\n\n## Beschlüsse\n\n- Budget freigeben\n- Termin verschieben\n\n" +
            "## Aufgaben\n\n- Angebot einholen — Verantwortlich: Anna — Fällig: 2026-11-20\n- Raum buchen\n");
    }

    [Fact]
    public void ToMarkdown_SkipsEmptyValues()
    {
        const string schema = """{"type":"object","properties":{"summary":{"type":"string"},"decisions":{"type":"array","items":{"type":"string"}}}}""";

        var markdown = TemplateOutput.ToMarkdown(schema, JsonNode.Parse("""{"summary":"  ","decisions":[]}""")!);

        markdown.Should().BeEmpty();
    }

    // --- prompt rules --------------------------------------------------------------

    [Fact]
    public void PromptRules_AcceptAKnownPlaceholder() =>
        TemplatePromptRules.Validate("Erstelle ein Protokoll. Bekannte Sprecher: {{SPEAKERS}}. Sprache: {{LANGUAGE}}.").Should().BeEmpty();

    [Fact]
    public void PromptRules_RejectAnUnknownPlaceholder() =>
        TemplatePromptRules.Validate("Hallo {{PASSWORD}}").Should().ContainSingle().Which.Should().Contain("PASSWORD");

    [Fact]
    public void PromptRules_RejectEmptyAndTooLongPrompts()
    {
        TemplatePromptRules.Validate("  ").Should().NotBeEmpty();
        TemplatePromptRules.Validate(new string('x', TemplatePromptRules.MaxPromptLength + 1)).Should().NotBeEmpty();
        TemplatePromptRules.Validate(new string('x', TemplatePromptRules.MaxPromptLength)).Should().BeEmpty();
    }

    [Fact]
    public void PromptRules_Fill_ReplacesThePlaceholders()
    {
        var filled = TemplatePromptRules.Fill("Sprecher: {{SPEAKERS}}", ["Anna", "Ben"]);

        filled.Should().Be("Sprecher: Anna, Ben");
        TemplatePromptRules.Fill("Sprecher: {{SPEAKERS}}", []).Should().Be("Sprecher: (none named)");
    }

    // --- built-in templates ----------------------------------------------------------

    [Fact]
    public void BuiltInTemplates_AreValid_HaveStableIds_AndAreMarkedBuiltIn()
    {
        BuiltInTemplates.All.Should().HaveCount(3);
        BuiltInTemplates.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        foreach (var template in BuiltInTemplates.All)
        {
            template.IsBuiltIn.Should().BeTrue();
            TemplatePromptRules.Validate(template.Prompt).Should().BeEmpty(template.Name);
            TemplateSchemaValidator.ValidateDefinition(template.OutputSchema).Should().BeEmpty(template.Name);
        }
    }
}
