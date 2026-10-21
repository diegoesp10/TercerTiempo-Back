using System.Text.RegularExpressions;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Common.Errors;

namespace TercerTiempo.Domain.Tests;

/// <summary>El catálogo de errores (semilla de ErrorMessages) cubre todos los códigos y las plantillas son coherentes.</summary>
public class ErrorCatalogTests
{
    [Fact]
    public void Every_error_code_has_a_spanish_message()
    {
        var missing = ErrorCatalog.Codes.Where(code => !ErrorCatalog.Contains(code)).ToList();

        Assert.Empty(missing);
        Assert.Equal(ErrorCatalog.Codes.Count, ErrorCatalog.Messages.Count(m => m.Language == ErrorMessage.DefaultLanguage));
    }

    [Fact]
    public void Codes_are_unique_and_lowercase_dotted()
    {
        Assert.Equal(ErrorCatalog.Codes.Count, ErrorCatalog.Codes.Distinct().Count());
        Assert.All(ErrorCatalog.Codes, code => Assert.Matches(@"^[a-z_]+\.[A-Za-z_]+$", code));
    }

    [Fact]
    public void Messages_fill_their_placeholders()
    {
        Assert.Equal("'nombre' no puede superar 120 caracteres.", ErrorCatalog.Format(ErrorCode.CommonTooLong, "nombre", 120));
        Assert.Equal("La cuenta 'abc' no existe.", ErrorCatalog.Format(ErrorCode.AccountNotFound, "abc"));

        // Sin argumentos la plantilla se devuelve tal cual (nunca se pierde el error por un formato).
        Assert.Equal("El equipo '{0}' no existe.", ErrorCatalog.Format(ErrorCode.TeamNotFound));
    }

    [Fact]
    public void Placeholders_never_exceed_the_arguments_the_code_documents()
    {
        // Ninguna plantilla usa un marcador por encima de {2} (los Guard llevan hasta tres argumentos).
        foreach (var message in ErrorCatalog.Messages)
        {
            var max = Regex.Matches(message.Message, @"\{(\d)\}").Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max();
            Assert.True(max <= 2, $"{message.Code} usa un marcador {{{max}}}");
        }
    }

    [Fact]
    public void Domain_exceptions_carry_code_and_default_message()
    {
        var ex = Assert.Throws<DomainException>(() => Guard.NotEmpty("", "nombre"));

        Assert.Equal(ErrorCode.CommonRequired, ex.Code);
        Assert.Equal(["nombre"], ex.Args);
        Assert.Equal("'nombre' no puede estar vacío.", ex.Message);
    }
}
