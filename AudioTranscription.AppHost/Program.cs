var builder = DistributedApplication.CreateBuilder(args);

// MSSQL Server with persistent volume; built from docker/mssql-fts to include Full-Text Search (S13, ADR 0005),
// which the stock mssql/server image does not ship.
var sql = builder.AddSqlServer("sql")
    .WithDockerfile("../docker/mssql-fts")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("audio-transcription-sqldata");

var db = sql.AddDatabase("transcriptiondb");

// First login of the app (ADR 0003). Values come from AppHost user-secrets
// (Parameters:initial-user-email / Parameters:initial-user-password) or are prompted in the dashboard.
var initialUserEmail = builder.AddParameter("initial-user-email");
var initialUserPassword = builder.AddParameter("initial-user-password", secret: true);

// Qdrant vector database for the semantic search (S23, ADR 0010), with a persistent volume
var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume("audio-transcription-qdrant")
    .WithLifetime(ContainerLifetime.Persistent);

// Ollama (optional, activated via configuration)
var ollamaEnabled = builder.Configuration["Features:OllamaPostProcessing"] == "true";

if (ollamaEnabled)
{
    var ollama = builder.AddOllama("ollama")
        .WithDataVolume("audio-transcription-ollama")
        .WithLifetime(ContainerLifetime.Persistent);

    var ollamaModel = ollama.AddModel("llama3.2");
    // Local embedding model of the semantic search (ADR 0010); its connection string is "bge-m3"
    var embeddingModel = ollama.AddModel("bge-m3");

    var api = builder.AddProject<Projects.AudioTranscription_Api>("api")
        .WithReference(db)
        .WaitFor(db)
        .WithReference(ollamaModel)
        .WaitFor(ollamaModel)
        .WithReference(embeddingModel)
        .WithReference(qdrant)
        .WaitFor(qdrant)
        .WithEnvironment("Auth__InitialUser__Email", initialUserEmail)
        .WithEnvironment("Auth__InitialUser__Password", initialUserPassword);

    builder.AddJavaScriptApp("web", "../AudioTranscription.Web")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints();
}
else
{
    // Without Ollama the semantic search stays off (no embedding model), Qdrant is still started for later use
    var api = builder.AddProject<Projects.AudioTranscription_Api>("api")
        .WithReference(db)
        .WaitFor(db)
        .WithEnvironment("Auth__InitialUser__Email", initialUserEmail)
        .WithEnvironment("Auth__InitialUser__Password", initialUserPassword);

    builder.AddJavaScriptApp("web", "../AudioTranscription.Web")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
