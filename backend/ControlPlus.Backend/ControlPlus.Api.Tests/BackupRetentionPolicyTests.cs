using ControlPlus.Application.Backups;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class BackupRetentionPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc);
    private static readonly RetentionRules Official = new(Daily: 7, Weekly: 4, Monthly: 12, KeepPreUpdate: true);

    private static RetainedCopy Copy(DateTime at, bool preUpdate = false) =>
        new($"controlplus-{at:yyyyMMddTHHmmssZ}-PROGRAMADO.dump", at, preUpdate);

    [Fact]
    public void KeepsEveryCopyOfTheLast24Hours()
    {
        // Cada 30 minutos durante el último día: ninguna se borra.
        var copies = Enumerable.Range(0, 48).Select(i => Copy(Now.AddMinutes(-30 * i))).ToArray();
        Assert.Empty(BackupRetentionPolicy.SelectForDeletion(copies, Official, Now));
    }

    [Fact]
    public void OlderThanADay_KeepsOnlyTheLatestOfEachDay()
    {
        var yesterday = Now.Date.AddDays(-2);
        var copies = new[] { Copy(yesterday.AddHours(8)), Copy(yesterday.AddHours(12)), Copy(yesterday.AddHours(20)), Copy(Now) };
        var deleted = BackupRetentionPolicy.SelectForDeletion(copies, Official, Now);
        Assert.Equal([yesterday.AddHours(12), yesterday.AddHours(8)], deleted.Select(x => x.CreatedAtUtc).ToArray());
    }

    [Fact]
    public void KeepsSevenDailyFourWeeklyAndTwelveMonthly()
    {
        // Una copia diaria a las 21:00 durante 500 días.
        var copies = Enumerable.Range(1, 500).Select(i => Copy(Now.Date.AddDays(-i).AddHours(21))).Append(Copy(Now)).ToArray();
        var deleted = BackupRetentionPolicy.SelectForDeletion(copies, Official, Now);
        var kept = copies.Except(deleted).Select(x => x.CreatedAtUtc).OrderByDescending(x => x).ToArray();
        // Últimos 7 días (ahora + 6 anteriores) quedan; luego solo representantes de semanas y meses.
        Assert.All(Enumerable.Range(1, 6), i => Assert.Contains(Now.Date.AddDays(-i).AddHours(21), kept));
        Assert.True(kept.Length <= 1 + 7 + 4 + 12, $"Se conservaron {kept.Length}");
        Assert.True(kept.Length >= 12, $"Se conservaron {kept.Length}");
        // Nada anterior a 12 meses sobrevive.
        Assert.All(kept, at => Assert.True(at > Now.AddMonths(-13)));
    }

    [Fact]
    public void PreUpdateCopies_AreKeptWhenConfigured_AndPrunedOtherwise()
    {
        var old = Copy(Now.AddYears(-2), preUpdate: true);
        var copies = new[] { old, Copy(Now) };
        Assert.Empty(BackupRetentionPolicy.SelectForDeletion(copies, Official, Now));
        Assert.Equal([old], BackupRetentionPolicy.SelectForDeletion(copies, Official with { KeepPreUpdate = false, Monthly = 0, Weekly = 0, Daily = 1 }, Now));
    }

    [Fact]
    public void NeverDeletesTheNewestCopy_EvenIfItIsOld()
    {
        var onlyOld = new[] { Copy(Now.AddYears(-3)) };
        Assert.Empty(BackupRetentionPolicy.SelectForDeletion(onlyOld, new RetentionRules(1, 0, 0, false), Now));
        Assert.Empty(BackupRetentionPolicy.SelectForDeletion([], Official, Now));
    }
}
