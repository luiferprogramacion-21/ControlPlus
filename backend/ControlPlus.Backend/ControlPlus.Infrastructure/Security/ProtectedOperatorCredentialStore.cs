using System.Security.Cryptography;
using System.Text;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ControlPlus.Infrastructure.Security;

/// <summary>Protects tokens using a distinct key derived from the installation's existing signing secret.</summary>
public sealed class OperatorCredentialProtector(IOptions<JwtOptions> options)
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const string Prefix = "v1:";

    public string Protect(Guid userId, Guid credentialId, string token)
    {
        var plain = Encoding.UTF8.GetBytes(token);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLength];
        var key = Key();
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(userId, credentialId));
            return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
        }
        finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); }
    }

    public string? Unprotect(Guid userId, Guid credentialId, string? protectedToken)
    {
        if (protectedToken is null || !protectedToken.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        byte[]? plain = null;
        var key = Key();
        try
        {
            var bytes = Convert.FromBase64String(protectedToken[Prefix.Length..]);
            if (bytes.Length <= NonceLength + TagLength) return null;
            plain = new byte[bytes.Length - NonceLength - TagLength];
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(bytes.AsSpan(0, NonceLength), bytes.AsSpan(NonceLength + TagLength),
                bytes.AsSpan(NonceLength, TagLength), plain, AssociatedData(userId, credentialId));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException) { return null; }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] Key()
    {
        var value = options.Value;
        value.EnsureValid();
        // Domain separation: this key is never the raw JWT key and cannot mint JWT signatures.
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(value.SigningKey),
            Encoding.UTF8.GetBytes("ControlPlus.OperatorCredential.Encryption.v1"));
    }

    private static byte[] AssociatedData(Guid userId, Guid credentialId) =>
        Encoding.UTF8.GetBytes($"ControlPlus.OperatorCredential.v1|{userId:N}|{credentialId:N}");
}

public sealed class ProtectedOperatorCredentialStore(
    OfficialControlPlusDbContext db, OperatorCredentialProtector protector) : IOperatorCredentialStore
{
    public const string Provider = "ControlPlus.OperatorCredential";

    public async Task<string?> ReadAsync(Guid userId, Guid credentialId, CancellationToken cancellationToken = default)
    {
        var name = credentialId.ToString("N");
        var row = await db.UsuarioToken.AsNoTracking().SingleOrDefaultAsync(x =>
            x.UsuarioId == userId && x.ProveedorLogin == Provider && x.NombreToken == name, cancellationToken);
        return protector.Unprotect(userId, credentialId, row?.ValorToken);
    }

    public async Task StoreAsync(Guid userId, Guid credentialId, string token, CancellationToken cancellationToken = default)
    {
        await db.UsuarioToken.AddAsync(new UsuarioToken
        {
            UsuarioId = userId, ProveedorLogin = Provider, NombreToken = credentialId.ToString("N"),
            ValorToken = protector.Protect(userId, credentialId, token)
        }, cancellationToken);
    }

    public async Task RemoveAsync(Guid userId, Guid credentialId, CancellationToken cancellationToken = default)
    {
        var name = credentialId.ToString("N");
        var row = await db.UsuarioToken.SingleOrDefaultAsync(x =>
            x.UsuarioId == userId && x.ProveedorLogin == Provider && x.NombreToken == name, cancellationToken);
        if (row is not null) db.UsuarioToken.Remove(row);
    }
}
