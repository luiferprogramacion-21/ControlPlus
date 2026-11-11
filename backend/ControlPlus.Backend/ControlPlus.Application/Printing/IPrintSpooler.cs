namespace ControlPlus.Application.Printing;

/// <summary>Submitting succeeds only when the operating system accepts the entire job.</summary>
public interface IPrintSpooler
{
    Task SubmitAsync(string queueName, byte[] utf8Text, string documentName,
        CancellationToken cancellationToken = default);
}

public sealed class PrintSubmissionException(bool mayHaveBeenAccepted, Exception innerException)
    : Exception("La cola no confirmó el trabajo de impresión.", innerException)
{
    public bool MayHaveBeenAccepted { get; } = mayHaveBeenAccepted;
}
