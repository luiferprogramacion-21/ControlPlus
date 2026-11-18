using System.Security.Cryptography;
using System.Text;
using ControlPlus.Application.Security.Ports;
using Microsoft.Extensions.Options;

namespace ControlPlus.Infrastructure.Security;

/// <summary>
/// Códigos de recuperación de 6 dígitos sin tabla: HMAC-SHA256 del usuario, su sello de seguridad y una ventana de
/// 5 minutos, con una clave derivada de la de firma JWT. Se aceptan la ventana actual y las dos anteriores, así que un
/// código recién enviado vale entre 10 y 15 minutos. Al cambiar la contraseña el sello cambia y el código deja de servir.
/// </summary>
public sealed class PasswordRecoveryCodes(IOptions<JwtOptions> jwtOptions) : IPasswordRecoveryCodes
{
    private const long WindowSeconds = 300;
    private const int AcceptedPreviousWindows = 2;

    public TimeSpan MinimumValidity => TimeSpan.FromSeconds(WindowSeconds * AcceptedPreviousWindows);

    public string Create(Guid userId, string securityStamp, DateTimeOffset now) =>
        Compute(userId, securityStamp, Window(now));

    public bool Verify(Guid userId, string securityStamp, string code, DateTimeOffset now)
    {
        if (code is not { Length: 6 } || !code.All(char.IsAsciiDigit)) return false;
        var given = Encoding.ASCII.GetBytes(code);
        var window = Window(now);
        var matched = false;
        for (var offset = 0; offset <= AcceptedPreviousWindows; offset++)
        {
            // Se comparan todas las ventanas, en tiempo constante, para no revelar cuál coincidió.
            matched |= CryptographicOperations.FixedTimeEquals(
                given, Encoding.ASCII.GetBytes(Compute(userId, securityStamp, window - offset)));
        }

        return matched;
    }

    private static long Window(DateTimeOffset now) => now.ToUnixTimeSeconds() / WindowSeconds;

    private string Compute(Guid userId, string securityStamp, long window)
    {
        var jwt = jwtOptions.Value;
        jwt.EnsureValid();
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("controlplus-password-recovery:" + jwt.SigningKey));
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{userId:N}|{securityStamp}|{window}"));
        var number = (BitConverter.ToUInt32(mac, 0) & 0x7FFFFFFF) % 1_000_000;
        return number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
