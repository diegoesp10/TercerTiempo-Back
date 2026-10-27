using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using TercerTiempo.Application.Auth;

namespace TercerTiempo.Infrastructure.Identity;

// Reglas que Identity no trae: longitud máxima, nada del email, nada de lo primero que se prueba al adivinar una
// contraseña y sin el mismo carácter repetido. Las de mayúscula, minúscula, número, símbolo y caracteres distintos van
// en las opciones de Identity.
internal sealed partial class PasswordRulesValidator : IPasswordValidator<ApplicationUser>
{
    // Series que aparecen en las contraseñas más usadas; se buscan dentro de la contraseña.
    private static readonly string[] CommonSequences =
    [
        "password", "contrasena", "qwerty", "asdfgh", "zxcvbn", "123456", "654321", "abcdef", "abc123", "111111", "000000",
        "iloveyou", "letmein", "welcome", "tercertiempo",
    ];

    // Palabras que con números y símbolos alrededor siguen siendo lo primero que se prueba («Futbol2026!», «P4ssw0rd!»).
    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "password", "contrasena", "qwerty", "admin", "administrador", "usuario", "user", "bienvenido", "welcome", "hola",
        "clave", "secreto", "acceso", "futbol", "football", "soccer", "balon", "gol", "goles", "campeon", "liga", "temporada",
        "equipo", "club", "entrenador", "jugador", "portero", "asturias", "oviedo", "realoviedo", "gijon", "sporting", "aviles",
        "tercertiempo", "madrid", "realmadrid", "barcelona", "barca", "athletic", "valencia", "betis", "sevilla", "atleti",
        "liverpool", "manchester", "messi", "ronaldo", "cristiano", "teamo", "amor", "princesa", "dragon", "monkey", "master",
        "superman", "batman", "pokemon", "sunshine", "shadow", "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio",
        "agosto", "septiembre", "octubre", "noviembre", "diciembre", "verano", "invierno", "primavera", "otono",
    };

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        password ??= string.Empty;
        var errors = new List<IdentityError>();

        if (password.Length > PasswordPolicy.MaxLength)
            errors.Add(Error("PasswordTooLong", $"La contraseña no puede tener más de {PasswordPolicy.MaxLength} caracteres."));

        var folded = Fold(password);
        if (EmailPieces(user.Email ?? user.UserName).Any(folded.Contains))
            errors.Add(Error("PasswordContainsEmail", "La contraseña no puede contener tu email."));

        var decoded = Fold(Unleet(password));
        if (CommonSequences.Any(s => folded.Contains(s) || decoded.Contains(s))
            || CommonWords.Contains(Letters(folded)) || CommonWords.Contains(Letters(decoded)))
            errors.Add(Error("PasswordTooCommon", "La contraseña es demasiado fácil de adivinar."));

        if (RepeatedChars().IsMatch(password))
            errors.Add(Error("PasswordRepeatedChars", $"La contraseña no puede repetir el mismo carácter más de {PasswordPolicy.MaxRepeatedChars} veces seguidas."));

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    // El usuario del email entero y sus trozos («diego.espina» → diego, espina) de cuatro letras o más.
    private static IEnumerable<string> EmailPieces(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) yield break;
        var local = Fold(email.Split('@')[0]);
        if (local.Length >= 4) yield return local;
        foreach (var piece in local.Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries))
            if (piece.Length >= 4 && piece != local) yield return piece;
    }

    private static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
        return builder.ToString();
    }

    private static string Letters(string value) => new([.. value.Where(char.IsLetter)]);

    private static string Unleet(string value) => string.Concat(value.Select(ch => ch switch
    {
        '0' => 'o',
        '1' or '!' => 'i',
        '3' => 'e',
        '4' or '@' => 'a',
        '5' or '$' => 's',
        '7' => 't',
        _ => ch,
    }));

    [GeneratedRegex(@"(.)\1{3,}")]
    private static partial Regex RepeatedChars();
}
