using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AudioTranscription.Api.Automation;

/// <summary>
/// Describes how a client authenticates in the OpenAPI document (S28): the browser session cookie, or an API key sent as
/// header <c>X-Api-Key</c> or as bearer token.
/// </summary>
public sealed class OpenApiSecuritySchemes : IOpenApiDocumentTransformer
{
    public const string Cookie = "SessionCookie";
    public const string ApiKeyHeader = "ApiKey";
    public const string ApiKeyBearer = "BearerApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Cookie] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = ".AspNetCore.Identity.Application",
            Description = "Browser session after POST /api/auth/login. Needed to manage API keys and webhooks.",
        };
        document.Components.SecuritySchemes[ApiKeyHeader] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-Api-Key",
            Description = "API key created under /api/api-keys. Reaches /api/audio-jobs, /api/uploads and /api/search; the scopes jobs:read and jobs:write decide per HTTP method.",
        };
        document.Components.SecuritySchemes[ApiKeyBearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "The same API key as 'Authorization: Bearer at_…'.",
        };

        document.Security ??= [];
        foreach (var name in new[] { Cookie, ApiKeyHeader, ApiKeyBearer })
            document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(name, document)] = [] });
        return Task.CompletedTask;
    }
}
