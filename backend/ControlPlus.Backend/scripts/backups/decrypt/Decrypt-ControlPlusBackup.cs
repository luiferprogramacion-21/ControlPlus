// Descifra una copia externa de ControlPlus (*.dump.cpbk) para la restauración de emergencia.
// Uso: dotnet run Decrypt-ControlPlusBackup.cs -- <copia.cpbk> <salida.dump>
// La contraseña de cifrado se lee de la entrada estándar (UTF-8, una línea); nunca se recibe como argumento.
// Formato (igual que ControlPlus.Infrastructure.Backups.BackupCrypto): "CPBK1" | sal 16 | nonce 12 | etiqueta 16 | datos,
// clave AES-256 derivada con PBKDF2-SHA256 de 600 000 iteraciones y el encabezado "CPBK1" como dato asociado.
#:property PublishAot=false

using System.Security.Cryptography;
using System.Text;

if (args.Length != 2) { Console.Error.WriteLine("Uso: <copia.cpbk> <salida.dump>"); return 2; }
var magic = "CPBK1"u8.ToArray();
const int saltSize = 16, nonceSize = 12, tagSize = 16, iterations = 600_000;

string passphrase;
using (var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
    passphrase = (reader.ReadLine() ?? string.Empty).TrimStart('﻿');
if (passphrase.Length < 12) { Console.Error.WriteLine("La contraseña de cifrado debe tener al menos 12 caracteres."); return 3; }

var encrypted = File.ReadAllBytes(args[0]);
var header = magic.Length + saltSize + nonceSize + tagSize;
if (encrypted.Length < header || !encrypted.AsSpan(0, magic.Length).SequenceEqual(magic))
{ Console.Error.WriteLine("El archivo no es un respaldo cifrado de ControlPlus."); return 4; }

var salt = encrypted.AsSpan(magic.Length, saltSize);
var nonce = encrypted.AsSpan(magic.Length + saltSize, nonceSize);
var tag = encrypted.AsSpan(magic.Length + saltSize + nonceSize, tagSize);
var cipher = encrypted.AsSpan(header);
var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
try
{
    var plain = new byte[cipher.Length];
    using (var aes = new AesGcm(key, tagSize)) aes.Decrypt(nonce, cipher, tag, plain, magic);
    File.WriteAllBytes(args[1], plain);
    Console.WriteLine("DECRYPT_OK sha256=" + Convert.ToHexString(SHA256.HashData(plain)));
    return 0;
}
catch (CryptographicException)
{
    Console.Error.WriteLine("No se pudo descifrar: la contraseña es distinta o el archivo está alterado.");
    return 5;
}
finally { CryptographicOperations.ZeroMemory(key); }
