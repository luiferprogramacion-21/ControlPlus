using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ControlPlus.Api.OpenApi;

/// <summary>
/// Describes the API-wide fallback authorization policy in the generated OpenAPI document.
/// </summary>
internal sealed class ControlPlusOpenApiSecurityTransformer(IAuthenticationSchemeProvider schemes)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!(await schemes.GetAllSchemesAsync()).Any(scheme =>
                string.Equals(
                    scheme.Name,
                    JwtBearerDefaults.AuthenticationScheme,
                    StringComparison.Ordinal)))
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.AddComponent(
            JwtBearerDefaults.AuthenticationScheme,
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

        var anonymousOperations = context.DescriptionGroups
            .SelectMany(group => group.Items)
            .Where(description => description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .Select(description => OperationKey(description.HttpMethod, description.RelativePath))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (method, operation) in pathItem.Operations ?? [])
            {
                if (anonymousOperations.Contains(OperationKey(method.Method, path)))
                {
                    continue;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = []
                });

                operation.Responses ??= [];
                operation.Responses.TryAdd(
                    StatusCodes.Status401Unauthorized.ToString(),
                    new OpenApiResponse { Description = "Authentication is required." });
                operation.Responses.TryAdd(
                    StatusCodes.Status403Forbidden.ToString(),
                    new OpenApiResponse { Description = "The authenticated user lacks the required permission." });
            }
        }
    }

    private static string OperationKey(string? method, string? path) =>
        $"{method?.ToUpperInvariant()}:{NormalizePath(path)}";

    private static string NormalizePath(string? path)
    {
        var route = path?.Split('?', 2)[0].Trim() ?? string.Empty;
        return route.StartsWith("/", StringComparison.Ordinal) ? route : $"/{route}";
    }
}
