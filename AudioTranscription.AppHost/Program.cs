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

// LM Studio (optional): runs on the host, not in a container, and serves chat, post-processing and embeddings through its
// OpenAI-compatible API. Start its local server (default http://localhost:1234/v1) and load the models named below.
// An empty LmStudio:Endpoint switches the language-model features (and with them the semantic search) off.
var lmStudioEndpoint = builder.Configuration["LmStudio:Endpoint"];
var chatModel = builder.Configuration["LmStudio:Model"] ?? "llama-3.2-3b-instruct";
var embeddingModel = builder.Configuration["LmStudio:EmbeddingModel"] ?? "text-embedding-bge-m3";

var api = builder.AddProject<Projects.AudioTranscription_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(qdrant)
    .WaitFor(qdrant)
    .WithEnvironment("Auth__InitialUser__Email", initialUserEmail)
    .WithEnvironment("Auth__InitialUser__Password", initialUserPassword);

if (!string.IsNullOrWhiteSpace(lmStudioEndpoint))
{
    api.WithEnvironment("LmStudio__Endpoint", lmStudioEndpoint)
        .WithEnvironment("LmStudio__Model", chatModel)
        .WithEnvironment("Chat__Model", chatModel)
        .WithEnvironment("Embedding__Model", embeddingModel);
}

builder.AddJavaScriptApp("web", "../AudioTranscription.Web")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints();

builder.Build().Run();
