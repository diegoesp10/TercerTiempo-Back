using TercerTiempo.Domain.Common;

namespace TercerTiempo.Domain.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("José Ángel Muñiz", "jose angel muniz")]
    [InlineData("  PELAYO   fernández-Llaneza ", "pelayo fernandez llaneza")]
    [InlineData("Xuan A. Noval", "xuan a noval")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalizes_case_accents_and_separators(string? input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }
}
