using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

[Authorize(Roles = "Player")]
[Route("api/player-payments")]
public sealed class PlayerPaymentsController(
    AppDbContext db,
    IOptions<MercadoPagoOptions> mercadoPagoOptions) : ApiControllerBase
{
    private const string PlayerOAuthPurpose = "Player";
    private const string MercadoPagoAccountMethod = "mercadopago_account";

    [HttpGet("/api/player-payments/config")]
    public async Task<ActionResult<PlayerPaymentConfigResponse>> GetConfig(CancellationToken cancellationToken)
    {
        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var publicKey = settings?.PublicKey;
        var accessToken = MercadoPagoPlatformCredentials.ResolveAccessToken(settings, mercadoPagoOptions.Value);
        var environment = MercadoPagoPlatformCredentials.ResolveEffectiveEnvironment(settings, accessToken, publicKey);
        var canTokenizeCards = !string.IsNullOrWhiteSpace(publicKey);

        return Ok(new PlayerPaymentConfigResponse(
            environment,
            publicKey,
            canTokenizeCards,
            canTokenizeCards,
            !string.IsNullOrWhiteSpace(settings?.OAuthClientId) &&
            !string.IsNullOrWhiteSpace(settings.OAuthClientSecret) &&
            !string.IsNullOrWhiteSpace(settings.OAuthRedirectUrl)));
    }

    [HttpGet("/api/player-payments/method")]
    public async Task<ActionResult<PlayerPaymentMethodResponse>> GetMethod(CancellationToken cancellationToken)
    {
        var method = await db.PlayerPaymentMethods
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId && x.IsActive, cancellationToken);

        return Ok(ToResponse(method));
    }

    [HttpPost("/api/player-payments/method")]
    public async Task<ActionResult<PlayerPaymentMethodResponse>> UpsertMethod(UpsertPlayerPaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentMethodId))
        {
            return ValidationProblem("Debes indicar el metodo de pago de la tarjeta.");
        }

        if (string.IsNullOrWhiteSpace(request.LastFourDigits))
        {
            return ValidationProblem("Debes indicar los ultimos digitos de la tarjeta.");
        }

        var method = await db.PlayerPaymentMethods
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId, cancellationToken);

        if (method is null || string.IsNullOrWhiteSpace(method.MercadoPagoAccountEmail))
        {
            return BadRequest("Vincula tu cuenta de Mercado Pago antes de guardar una tarjeta.");
        }

        method.MercadoPagoCardId = null;
        method.CardToken = null;
        method.PaymentMethodId = request.PaymentMethodId;
        method.CardBrand = request.CardBrand;
        method.LastFourDigits = request.LastFourDigits;
        method.CardholderName = request.CardholderName?.Trim();
        method.IdentificationType = request.IdentificationType?.Trim();
        method.IdentificationNumber = request.IdentificationNumber?.Trim();
        method.IsActive = true;
        method.LinkedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(method));
    }

    [HttpDelete("/api/player-payments/method/card")]
    public async Task<ActionResult<PlayerPaymentMethodResponse>> DeleteCard(CancellationToken cancellationToken)
    {
        var method = await db.PlayerPaymentMethods
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId && x.IsActive, cancellationToken);
        if (method is null || !HasSavedCard(method))
        {
            return Ok(ToResponse(method));
        }

        method.MercadoPagoCardId = null;
        method.CardToken = null;
        method.CardholderName = null;
        method.IdentificationType = null;
        method.IdentificationNumber = null;
        method.PaymentMethodId = MercadoPagoAccountMethod;
        method.CardBrand = "Mercado Pago";
        method.LastFourDigits = null;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(method));
    }

    [HttpDelete("/api/player-payments/method")]
    public async Task<ActionResult<PlayerPaymentMethodResponse>> DeleteMethod(CancellationToken cancellationToken)
    {
        var method = await db.PlayerPaymentMethods
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId, cancellationToken);
        if (method is not null)
        {
            method.IsActive = false;
            method.CardToken = null;
            method.MercadoPagoCustomerId = null;
            method.MercadoPagoAccountEmail = null;
            method.MercadoPagoCardId = null;
            method.CardholderName = null;
            method.IdentificationType = null;
            method.IdentificationNumber = null;
            method.PaymentMethodId = string.Empty;
            method.CardBrand = null;
            method.LastFourDigits = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(ToResponse(null));
    }

    [HttpPost("/api/player-payments/mercadopago/connect")]
    public async Task<ActionResult<MercadoPagoConnectResponse>> CreateMercadoPagoConnectUrl(CancellationToken cancellationToken)
    {
        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings?.OAuthClientId) ||
            string.IsNullOrWhiteSpace(settings.OAuthClientSecret) ||
            string.IsNullOrWhiteSpace(settings.OAuthRedirectUrl))
        {
            return BadRequest("El administrador debe configurar Mercado Pago OAuth antes de permitir vinculaciones.");
        }

        if (settings.OAuthClientId.StartsWith("APP_USR-", StringComparison.OrdinalIgnoreCase) ||
            settings.OAuthClientId.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("La configuracion de Mercado Pago no es valida: Application ID / Client ID debe ser el N° de aplicación.");
        }

        var state = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        db.MercadoPagoOAuthStates.Add(new MercadoPagoOAuthState
        {
            State = state,
            UserId = CurrentUserId,
            Purpose = PlayerOAuthPurpose
        });
        await db.SaveChangesAsync(cancellationToken);

        var authorizationUrl =
            "https://auth.mercadopago.com/authorization" +
            $"?client_id={Uri.EscapeDataString(settings.OAuthClientId)}" +
            "&response_type=code" +
            "&platform_id=mp" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&redirect_uri={Uri.EscapeDataString(settings.OAuthRedirectUrl)}";

        return Ok(new MercadoPagoConnectResponse(authorizationUrl));
    }

    private static bool HasSavedCard(PlayerPaymentMethod method)
    {
        return !string.IsNullOrWhiteSpace(method.LastFourDigits) &&
            !string.Equals(method.PaymentMethodId, MercadoPagoAccountMethod, StringComparison.OrdinalIgnoreCase);
    }

    private static PlayerPaymentMethodResponse ToResponse(PlayerPaymentMethod? method)
    {
        var hasMercadoPagoAccountLinked = method?.IsActive == true &&
            !string.IsNullOrWhiteSpace(method.MercadoPagoAccountEmail);
        var hasSavedCard = method?.IsActive == true && method is not null && HasSavedCard(method);

        return new PlayerPaymentMethodResponse(
            hasMercadoPagoAccountLinked || hasSavedCard,
            hasMercadoPagoAccountLinked,
            hasSavedCard,
            method?.MercadoPagoAccountEmail,
            method?.MercadoPagoCustomerId,
            hasSavedCard ? method?.PaymentMethodId : null,
            hasSavedCard ? method?.CardBrand : null,
            hasSavedCard ? method?.LastFourDigits : null,
            method?.LinkedAtUtc);
    }
}
