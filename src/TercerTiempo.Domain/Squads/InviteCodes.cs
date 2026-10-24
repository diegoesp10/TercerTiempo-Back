using System.Security.Cryptography;

namespace TercerTiempo.Domain.Squads;

public static class InviteCodes
{
    public const int Length = 8;
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate() => Generate(Length);

    public static string Generate(int length) => new(RandomNumberGenerator.GetItems<char>(Alphabet, length));

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
}
