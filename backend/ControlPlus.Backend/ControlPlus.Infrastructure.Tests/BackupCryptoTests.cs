using System.Security.Cryptography;
using ControlPlus.Infrastructure.Backups;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class BackupCryptoTests
{
    private const string Passphrase = "clave-de-prueba-larga-2026";

    [Fact]
    public void EncryptsAndDecryptsTheSameBytes_WithADifferentResultEachTime()
    {
        var plain = RandomNumberGenerator.GetBytes(100_000);
        var first = BackupCrypto.Encrypt(plain, Passphrase);
        var second = BackupCrypto.Encrypt(plain, Passphrase);
        Assert.NotEqual(first, second); // sal y nonce aleatorios
        Assert.False(first.AsSpan().IndexOf(plain.AsSpan(0, 64)) >= 0);
        Assert.Equal(plain, BackupCrypto.Decrypt(first, Passphrase));
        Assert.Equal(plain, BackupCrypto.Decrypt(second, Passphrase));
    }

    [Fact]
    public void WrongPassphrase_OrTamperedFile_AreRejected()
    {
        var encrypted = BackupCrypto.Encrypt("contenido del respaldo"u8, Passphrase);
        Assert.ThrowsAny<CryptographicException>(() => BackupCrypto.Decrypt(encrypted, "otra-clave-larga-2026"));
        var tampered = (byte[])encrypted.Clone();
        tampered[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => BackupCrypto.Decrypt(tampered, Passphrase));
        Assert.ThrowsAny<CryptographicException>(() => BackupCrypto.Decrypt("no es un respaldo"u8, Passphrase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    public void ShortPassphrases_AreRefused(string passphrase) =>
        Assert.Throws<ArgumentException>(() => BackupCrypto.Encrypt("x"u8, passphrase));
}
