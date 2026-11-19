using TercerTiempo.Domain.Squads;

namespace TercerTiempo.Domain.Tests;

// El nombre del documento lo pone el navegador de quien lo sube y luego va en la cabecera de la descarga.
public class InjuryDocumentTests
{
    [Theory]
    [InlineData(@"C:\fakepath\Informe ""urgencias"".pdf", "application/pdf", "Informe urgencias.pdf")]
    [InlineData("../../etc/passwd", "application/pdf", "passwd")]
    [InlineData("  rx\trodilla?.jpg ", "image/jpeg", "rx rodilla.jpg")]
    [InlineData("", "application/pdf", "documento.pdf")]
    [InlineData("\"\"", "image/png", "imagen")]
    public void The_file_name_keeps_only_a_safe_name(string given, string contentType, string expected) =>
        Assert.Equal(expected, InjuryDocument.SafeFileName(given, contentType));

    [Fact]
    public void A_long_name_is_cut_without_losing_its_extension()
    {
        var name = InjuryDocument.SafeFileName(new string('a', 400) + ".pdf", "application/pdf");

        Assert.Equal(InjuryDocument.FileNameMaxLength, name.Length);
        Assert.EndsWith("a.pdf", name);
    }
}
