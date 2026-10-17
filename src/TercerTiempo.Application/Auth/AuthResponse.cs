using TercerTiempo.Application.Players;

namespace TercerTiempo.Application.Auth;

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, PlayerDto Player);
