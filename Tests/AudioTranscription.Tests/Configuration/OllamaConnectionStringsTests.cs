using AudioTranscription.Api.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AudioTranscription.Tests.Configuration;

public class OllamaConnectionStringsTests
{
    private static IConfiguration Config(params (string Name, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>($"ConnectionStrings:{e.Name}", e.Value)))
            .Build();

    [Fact]
    public void Get_PlainUrlUnderModelName_IsReturnedUnchanged() =>
        OllamaConnectionStrings.Get(Config(("llama3.2", "http://localhost:11434")), "llama3.2")
            .Should().Be("http://localhost:11434");

    [Theory]
    [InlineData("ollama-llama3-2")]
    [InlineData("ollama_llama3_2")]
    public void Get_Aspire13Name_ReturnsOnlyTheEndpoint(string name) =>
        OllamaConnectionStrings.Get(Config((name, "Endpoint=http://localhost:28956;Model=llama3.2")), "llama3.2")
            .Should().Be("http://localhost:28956");

    [Fact]
    public void Get_EmbeddingModelWithDash_FindsPrefixedName() =>
        OllamaConnectionStrings.Get(Config(("ollama-bge-m3", "Endpoint=http://localhost:28956;Model=bge-m3")), "bge-m3")
            .Should().Be("http://localhost:28956");

    [Fact]
    public void Get_NothingConfigured_ReturnsNull() =>
        OllamaConnectionStrings.Get(Config(), "llama3.2").Should().BeNull();
}
