namespace TercerTiempo.Application.Auth;

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, AccountDto Account);
