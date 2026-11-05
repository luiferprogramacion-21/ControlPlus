namespace ControlPlus.Domain.OfficialModel;

public static class ProductCode
{
    public static string GenerateOwnedBarcode(string reference)
    {
        var separator = reference.LastIndexOf('-');
        if (separator < 1 || separator == reference.Length - 1 ||
            reference[(separator + 1)..].Any(c => c is < '0' or > '9'))
            throw new ArgumentException("La referencia no contiene un consecutivo válido.", nameof(reference));
        return "CP" + reference[(separator + 1)..];
    }
}
