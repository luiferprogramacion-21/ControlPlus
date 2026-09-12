namespace ControlPlus.Application.Security.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
