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

        if (settings?.Environment == MercadoPagoEnvironment.Sandbox &&
            publicKeyIsLive &&
            accessTokenIsLive)
        {
            return "El ambiente esta en Sandbox pero las credenciales son de produccion. Cambia a credenciales TEST o configura el ambiente en Production.";
        }

        if (settings?.Environment == MercadoPagoEnvironment.Production &&
            publicKeyIsTest &&
            accessTokenIsTest)
        {
            return "El ambiente esta en Production pero las credenciales son de prueba. Usa credenciales APP_USR de produccion.";
        }

        return null;
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
