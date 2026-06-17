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
    IMercadoPagoCustomerCardService customerCardService,
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

        return Ok(new PlayerPaymentConfigResponse(
            settings?.Environment ?? MercadoPagoEnvironment.Sandbox,
            publicKey,
            !string.IsNullOrWhiteSpace(publicKey),
            !string.IsNullOrWhiteSpace(accessToken),
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
        if (string.IsNullOrWhiteSpace(request.CardToken))
        {
            return ValidationProblem("Debes indicar el token de la tarjeta.");
        }

        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var accessToken = MercadoPagoPlatformCredentials.ResolveAccessToken(settings, mercadoPagoOptions.Value);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return BadRequest("El administrador debe configurar el Access Token de Mercado Pago para guardar tarjetas.");
        }

        var method = await db.PlayerPaymentMethods
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId, cancellationToken);

        if (method is null || string.IsNullOrWhiteSpace(method.MercadoPagoAccountEmail))
        {
            return BadRequest("Vincula tu cuenta de Mercado Pago antes de guardar una tarjeta.");
        }

        MercadoPagoSavedCardResult savedCard;
        try
        {
            savedCard = await customerCardService.SaveCardAsync(
                method.MercadoPagoAccountEmail,
                method.MercadoPagoCustomerId,
                request.CardToken,
                accessToken,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        method.MercadoPagoCustomerId = savedCard.CustomerId;
        method.MercadoPagoCardId = savedCard.CardId;
        method.CardToken = null;
        method.PaymentMethodId = savedCard.PaymentMethodId;
        method.CardBrand = request.CardBrand ?? savedCard.CardBrand;
        method.LastFourDigits = request.LastFourDigits ?? savedCard.LastFourDigits;
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
        if (method is null || string.IsNullOrWhiteSpace(method.MercadoPagoCardId))
        {
            return Ok(ToResponse(method));
        }

        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var accessToken = MercadoPagoPlatformCredentials.ResolveAccessToken(settings, mercadoPagoOptions.Value);
        if (!string.IsNullOrWhiteSpace(accessToken) &&
            !string.IsNullOrWhiteSpace(method.MercadoPagoCustomerId))
        {
            try
            {
                await customerCardService.DeleteCardAsync(
                    method.MercadoPagoCustomerId,
                    method.MercadoPagoCardId,
                    accessToken,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Si Mercado Pago ya no tiene la tarjeta, igual limpiamos el registro local.
            }
        }

        method.MercadoPagoCardId = null;
        method.CardToken = null;
        if (string.IsNullOrWhiteSpace(method.MercadoPagoAccountEmail))
        {
            method.PaymentMethodId = string.Empty;
            method.CardBrand = null;
            method.LastFourDigits = null;
        }
        else
        {
            method.PaymentMethodId = MercadoPagoAccountMethod;
            method.CardBrand = "Mercado Pago";
            method.LastFourDigits = null;
        }

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
            var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
            var accessToken = MercadoPagoPlatformCredentials.ResolveAccessToken(settings, mercadoPagoOptions.Value);
            if (!string.IsNullOrWhiteSpace(method.MercadoPagoCardId) &&
                !string.IsNullOrWhiteSpace(method.MercadoPagoCustomerId) &&
                !string.IsNullOrWhiteSpace(accessToken))
            {
                try
                {
                    await customerCardService.DeleteCardAsync(
                        method.MercadoPagoCustomerId,
                        method.MercadoPagoCardId,
                        accessToken,
                        cancellationToken);
                }
                catch (InvalidOperationException)
                {
                }
            }

            method.IsActive = false;
            method.CardToken = null;
            method.MercadoPagoCustomerId = null;
            method.MercadoPagoAccountEmail = null;
            method.MercadoPagoCardId = null;
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

    private static PlayerPaymentMethodResponse ToResponse(PlayerPaymentMethod? method)
    {
        var hasMercadoPagoAccountLinked = method?.IsActive == true &&
            !string.IsNullOrWhiteSpace(method.MercadoPagoAccountEmail);
        var hasSavedCard = method?.IsActive == true &&
            !string.IsNullOrWhiteSpace(method.MercadoPagoCardId);

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
