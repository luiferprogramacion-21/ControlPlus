using System.Security.Cryptography;
using System.Text;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Security.Services;

public sealed class CurrentUserSessionValidator : ICurrentUserSessionValidator
{
    private readonly IUserRepository _userRepository;

    public CurrentUserSessionValidator(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<bool> ValidateAsync(
        Guid userId,
        string securityStamp,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(securityStamp))
        {
            return false;
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive || user.IsLocked)
        {
            return false;
        }

        return FixedTimeEquals(user.SecurityStamp, securityStamp);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
