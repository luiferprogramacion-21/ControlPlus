using System.Security.Cryptography;
using System.Text;

namespace ControlPlus.Infrastructure.Backups;

/// <summary>
/// Cifra la copia externa con AES-256-GCM. La clave se deriva de la contraseña con PBKDF2-SHA256 y una sal aleatoria
/// por archivo; GCM autentica el contenido, así que una contraseña incorrecta o un archivo alterado no se descifran.
/// Formato: "CPBK1" | sal (16) | nonce (12) | etiqueta (16) | datos cifrados.
/// </summary>
public static class BackupCrypto
{
    private static readonly byte[] Magic = "CPBK1"u8.ToArray();
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    public const int Iterations = 600_000;
    public const int MinimumPassphraseLength = 12;

    public static byte[] Encrypt(ReadOnlySpan<byte> plain, string passphrase)
    {
        RequirePassphrase(passphrase);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(passphrase, salt);
        try
        {
            var output = new byte[Magic.Length + SaltSize + NonceSize + TagSize + plain.Length];
            Magic.CopyTo(output, 0);
            salt.CopyTo(output, Magic.Length);
            nonce.CopyTo(output, Magic.Length + SaltSize);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain,
                output.AsSpan(Magic.Length + SaltSize + NonceSize + TagSize),
                output.AsSpan(Magic.Length + SaltSize + NonceSize, TagSize), Magic);
            return output;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    /// <exception cref="CryptographicException">Contraseña incorrecta, archivo alterado o formato desconocido.</exception>
    public static byte[] Decrypt(ReadOnlySpan<byte> encrypted, string passphrase)
    {
        RequirePassphrase(passphrase);
        var header = Magic.Length + SaltSize + NonceSize + TagSize;
        if (encrypted.Length < header || !encrypted[..Magic.Length].SequenceEqual(Magic))
            throw new CryptographicException("El archivo no es un respaldo cifrado de ControlPlus.");
        var salt = encrypted.Slice(Magic.Length, SaltSize);
        var nonce = encrypted.Slice(Magic.Length + SaltSize, NonceSize);
        var tag = encrypted.Slice(Magic.Length + SaltSize + NonceSize, TagSize);
        var cipher = encrypted[header..];
        var key = DeriveKey(passphrase, salt);
        try
        {
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
            return plain;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);

    private static void RequirePassphrase(string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < MinimumPassphraseLength)
            throw new ArgumentException($"La contraseña de cifrado debe tener al menos {MinimumPassphraseLength} caracteres.", nameof(passphrase));
    }
}
