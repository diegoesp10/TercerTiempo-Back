using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Application.Tests;

// Los tipos de lesión con los que nace el catálogo (en la base de datos los siembra la migración).
internal static class TestInjuryTypes
{
    private static readonly Dictionary<string, (string Label, int? Days)> Seed = new()
    {
        [InjuryTypeCodes.MuscleTear] = ("Rotura fibrilar", 21),
        [InjuryTypeCodes.AnkleSprain] = ("Esguince de tobillo", 21),
        [InjuryTypeCodes.Contusion] = ("Contusión / golpe", 5),
        [InjuryTypeCodes.Other] = ("Otra", null),
    };

    public static InjuryTypeDefinition Of(string code)
    {
        var (label, days) = Seed.GetValueOrDefault(code, (code, null));
        return InjuryTypeDefinition.Create(code, label, days, 0, new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
    }
}
