namespace ControlPlus.Application.Security.Ports;

/// <summary>
/// Password hashing is an infrastructure concern; plaintext passwords never leave application use cases.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
