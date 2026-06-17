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

    public static string? ValidateCardSaveCredentials(MercadoPagoSettings? settings, string accessToken)
    {
        var publicKey = settings?.PublicKey;
        var publicKeyIsTest = IsTestCredential(publicKey);
        var publicKeyIsLive = IsLiveCredential(publicKey);
        var accessTokenIsTest = IsTestCredential(accessToken);
        var accessTokenIsLive = IsLiveCredential(accessToken);

        if (publicKeyIsTest && accessTokenIsLive)
        {
            return "La Public Key es de prueba y el Access Token es de produccion. Usa credenciales del mismo ambiente en Pagos.";
        }

        if (publicKeyIsLive && accessTokenIsTest)
        {
            return "La Public Key es de produccion y el Access Token es de prueba. Usa credenciales del mismo ambiente en Pagos.";
        }

        return null;
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
