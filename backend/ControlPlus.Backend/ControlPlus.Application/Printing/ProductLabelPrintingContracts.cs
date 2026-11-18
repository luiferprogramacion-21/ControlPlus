using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Printing;

public sealed record ProductLabelProfileDto(Guid Id, string Name, string PrinterType,
    decimal? WidthMm, decimal? HeightMm, int? ResolutionDpi, bool IsDefault,
    string LabelTemplateName);

public sealed record ProductLabelProductDto(Guid Id, string InternalCode, string? Barcode,
    string Name, decimal RetailPrice);

public sealed record ProductLabelProductsQuery(string? Search = null, int Page = 1, int PageSize = 20);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductLabelLineRequest(Guid ProductId, int Quantity);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateProductLabelJobRequest(Guid ProfileId,
    IReadOnlyCollection<ProductLabelLineRequest> Lines);

public sealed record ProductLabelJobDto(Guid JobId, Guid BatchId, Guid ProfileId,
    string ProfileName, int LabelCount, string Status, DateTimeOffset RequestedAtUtc);

public sealed record ProductLabelPrintItem(Guid ProductId, string Name, decimal RetailPrice,
    string Barcode, int Quantity);

public sealed record ProductLabelPrintPayload(int SchemaVersion,
    IReadOnlyCollection<ProductLabelPrintItem> Items);

public sealed record ProductLabelPrintLayout(decimal? WidthMm, decimal? HeightMm,
    int? ResolutionDpi);

public static class ProductLabelPayloadCodec
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Encode(IReadOnlyCollection<ProductLabelPrintItem> items) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new ProductLabelPrintPayload(CurrentSchemaVersion, items), JsonOptions));

    public static bool TryDecode(byte[] content, out ProductLabelPrintPayload? payload)
    {
        payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<ProductLabelPrintPayload>(content, JsonOptions);
            return payload is { SchemaVersion: CurrentSchemaVersion } &&
                payload.Items is { Count: > 0 } && payload.Items.All(item =>
                    item.ProductId != Guid.Empty && !string.IsNullOrWhiteSpace(item.Name) &&
                    !string.IsNullOrWhiteSpace(item.Barcode) &&
                    item.Quantity is > 0 and <= 500 &&
                    item.RetailPrice >= 0 && Code128Encoder.CanEncode(item.Barcode));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }
}

public interface IProductLabelPrintingService
{
    Task<Result<IReadOnlyCollection<ProductLabelProfileDto>>> ListProfilesAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<ProductLabelProductDto>>> ListProductsAsync(
        ActorContext actor, ProductLabelProductsQuery query,
        CancellationToken cancellationToken = default);
    Task<Result<ProductLabelJobDto>> QueueAsync(ActorContext actor, string? idempotencyKey,
        CreateProductLabelJobRequest request, CancellationToken cancellationToken = default);
}
