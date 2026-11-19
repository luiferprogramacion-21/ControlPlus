namespace ControlPlus.Application.Printing;

/// <summary>
/// Sends one ESC/POS drawer pulse as a separate RAW printer job. Completion means that
/// Windows accepted the job, not that a physical drawer opened.
/// </summary>
public interface ICashDrawerPulseSender
{
    Task SendPulseAsync(string queueName, CashDrawerPulse pulse,
        CancellationToken cancellationToken = default);
}

/// <param name="Pin">Drawer connector pin 2 or 5.</param>
/// <param name="OnTimeUnits">Pulse on time in two-millisecond units.</param>
/// <param name="OffTimeUnits">Pulse off time in two-millisecond units.</param>
public sealed record CashDrawerPulse
{
    public CashDrawerPulse(int pin, byte onTimeUnits, byte offTimeUnits)
    {
        if (pin is not (2 or 5)) throw new ArgumentOutOfRangeException(nameof(pin));
        if (onTimeUnits == 0) throw new ArgumentOutOfRangeException(nameof(onTimeUnits));
        if (offTimeUnits == 0) throw new ArgumentOutOfRangeException(nameof(offTimeUnits));
        Pin = pin;
        OnTimeUnits = onTimeUnits;
        OffTimeUnits = offTimeUnits;
    }

    public int Pin { get; }
    public byte OnTimeUnits { get; }
    public byte OffTimeUnits { get; }

    /// <summary>ESC p m t1 t2. Pin 2 maps to m=0; pin 5 maps to m=1.</summary>
    public byte[] ToEscPosBytes() => [0x1b, 0x70, Pin == 2 ? (byte)0 : (byte)1,
        OnTimeUnits, OffTimeUnits];
}

public sealed class CashDrawerPulseSubmissionException(bool mayHaveBeenAccepted,
    Exception innerException)
    : Exception("La cola no confirmó la orden de apertura del cajón.", innerException)
{
    /// <summary>
    /// True once Windows started the RAW job. The hardware outcome is then uncertain,
    /// and this attempt must not be retried automatically.
    /// </summary>
    public bool MayHaveBeenAccepted { get; } = mayHaveBeenAccepted;
}
