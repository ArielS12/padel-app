using Padel.Api.Domain;

namespace Padel.Api.Services;

public static class MercadoPagoPlatformCredentials
{
    public static string? ResolveAccessToken(MercadoPagoSettings? settings, MercadoPagoOptions options)
    {
        return FirstNotEmpty(settings?.AccessToken, options.AccessToken);
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
