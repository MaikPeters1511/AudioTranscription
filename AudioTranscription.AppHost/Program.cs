var builder = DistributedApplication.CreateBuilder(args);

// MSSQL Server with persistent volume
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("audio-transcription-sqldata");

var db = sql.AddDatabase("transcriptiondb");

// Ollama (optional, activated via configuration)
var ollamaEnabled = builder.Configuration["Features:OllamaPostProcessing"] == "true";

if (ollamaEnabled)
{
    var ollama = builder.AddOllama("ollama")
        .WithDataVolume("audio-transcription-ollama")
        .WithLifetime(ContainerLifetime.Persistent);

    var ollamaModel = ollama.AddModel("llama3.2");

    var api = builder.AddProject<Projects.AudioTranscription_Api>("api")
        .WithReference(db)
        .WaitFor(db)
        .WithReference(ollamaModel)
        .WaitFor(ollamaModel);

    builder.AddJavaScriptApp("web", "../AudioTranscription.Web")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints();
}
else
{
    var api = builder.AddProject<Projects.AudioTranscription_Api>("api")
        .WithReference(db)
        .WaitFor(db);

    builder.AddJavaScriptApp("web", "../AudioTranscription.Web")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints();
}

builder.Build().Run();
