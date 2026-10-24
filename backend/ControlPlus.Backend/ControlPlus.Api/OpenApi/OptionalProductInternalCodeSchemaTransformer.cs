using ControlPlus.Application.Catalog.Contracts;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ControlPlus.Api.OpenApi;

/// <summary>Preserves the creation-only omission contract for the server-assigned internal code.</summary>
internal sealed class OptionalProductInternalCodeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Positional record constructor parameters are otherwise marked required, even when nullable.
        if (context.JsonTypeInfo.Type == typeof(CreateProductRequest))
            schema.Required?.Remove("internalCode");
        return Task.CompletedTask;
    }
}
