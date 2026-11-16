using System.Globalization;

namespace ControlPlus.Application.Backups;

/// <summary>Una copia existente en un destino, identificada por su archivo.</summary>
public sealed record RetainedCopy(string FileName, DateTime CreatedAtUtc, bool PreUpdate);

/// <summary>Retención configurada en configuracion.destino_respaldo (Fase 4: 7 diarias, 4 semanales y 12 mensuales).</summary>
public sealed record RetentionRules(int Daily, int Weekly, int Monthly, bool KeepPreUpdate);

/// <summary>
/// Decide qué copias se borran. Se conservan siempre las de las últimas 24 horas (la meta de pérdida máxima es de
/// 30 minutos), la más reciente de cada uno de los últimos días, semanas y meses configurados y, si así se configura,
/// las previas a una actualización. Nunca borra la copia más reciente, aunque la retención sea mínima.
/// </summary>
public static class BackupRetentionPolicy
{
    public static IReadOnlyList<RetainedCopy> SelectForDeletion(
        IReadOnlyCollection<RetainedCopy> copies, RetentionRules rules, DateTime nowUtc)
    {
        if (copies.Count == 0) return [];
        var ordered = copies.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.FileName, StringComparer.Ordinal).ToArray();
        var keep = new HashSet<string>(StringComparer.Ordinal) { ordered[0].FileName };

        foreach (var copy in ordered.Where(x => nowUtc - x.CreatedAtUtc <= TimeSpan.FromHours(24)))
            keep.Add(copy.FileName);
        if (rules.KeepPreUpdate)
            foreach (var copy in ordered.Where(x => x.PreUpdate)) keep.Add(copy.FileName);

        KeepLatestPerPeriod(ordered, rules.Daily, x => x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), keep);
        KeepLatestPerPeriod(ordered, rules.Weekly,
            x => $"{ISOWeek.GetYear(x)}-W{ISOWeek.GetWeekOfYear(x):00}", keep);
        KeepLatestPerPeriod(ordered, rules.Monthly, x => x.ToString("yyyy-MM", CultureInfo.InvariantCulture), keep);

        return ordered.Where(x => !keep.Contains(x.FileName)).ToArray();
    }

    // Recorre de la más reciente a la más antigua y conserva la primera copia de cada período nuevo,
    // hasta completar la cantidad de períodos configurada.
    private static void KeepLatestPerPeriod(IEnumerable<RetainedCopy> newestFirst, int periods,
        Func<DateTime, string> periodOf, HashSet<string> keep)
    {
        if (periods <= 0) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var copy in newestFirst)
        {
            if (!seen.Add(periodOf(copy.CreatedAtUtc))) continue;
            keep.Add(copy.FileName);
            if (seen.Count == periods) break;
        }
    }
}
