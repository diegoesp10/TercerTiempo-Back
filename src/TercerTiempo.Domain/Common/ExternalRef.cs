namespace TercerTiempo.Domain.Common;

public sealed record ExternalRef
{
    public string Provider { get; }
    public string ExternalId { get; }

    private ExternalRef(string provider, string externalId)
    {
        Provider = provider;
        ExternalId = externalId;
    }

    public static ExternalRef Create(string provider, string externalId) =>
        new(Guard.NotEmpty(provider, nameof(provider), 50), Guard.NotEmpty(externalId, nameof(externalId), 200));

    public override string ToString() => $"{Provider}:{ExternalId}";
}
