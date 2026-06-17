using Padel.Api.Domain;

namespace Padel.Api.Services;

public static class MercadoPagoPlatformCredentials
{
    public static string? ResolveAccessToken(MercadoPagoSettings? settings, MercadoPagoOptions options)
    {
        return FirstNotEmpty(settings?.AccessToken, options.AccessToken);
    }

    public static bool IsTestCredential(string? credential)
    {
        return !string.IsNullOrWhiteSpace(credential) &&
            credential.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLiveCredential(string? credential)
    {
        return !string.IsNullOrWhiteSpace(credential) &&
            credential.StartsWith("APP_USR-", StringComparison.OrdinalIgnoreCase);
    }

    public static MercadoPagoEnvironment ResolveEffectiveEnvironment(
        MercadoPagoSettings? settings,
        string? accessToken,
        string? publicKey)
    {
        if (IsLiveCredential(publicKey) && IsLiveCredential(accessToken))
        {
            return MercadoPagoEnvironment.Production;
        }

        if (IsTestCredential(publicKey) && IsTestCredential(accessToken))
        {
            return MercadoPagoEnvironment.Sandbox;
        }

        return settings?.Environment ?? MercadoPagoEnvironment.Sandbox;
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
