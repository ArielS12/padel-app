using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;

namespace Padel.Api.Controllers;

[Authorize(Roles = "Player")]
[Route("api/player-payments")]
public sealed class PlayerPaymentsController(AppDbContext db) : ApiControllerBase
{
    private const string MercadoPagoAccountMethod = "mercadopago_account";
    private const string PlayerOAuthPurpose = "Player";

    [HttpGet("/api/player-payments/config")]
    public async Task<ActionResult<PlayerPaymentConfigResponse>> GetConfig(CancellationToken cancellationToken)
    {
        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var publicKey = settings?.PublicKey;

        return Ok(new PlayerPaymentConfigResponse(
            settings?.Environment ?? MercadoPagoEnvironment.Sandbox,
            publicKey,
            !string.IsNullOrWhiteSpace(publicKey),
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
            return ValidationProblem("Debes indicar el metodo de pago de Mercado Pago.");
        }

        if (string.IsNullOrWhiteSpace(request.MercadoPagoCardId) && string.IsNullOrWhiteSpace(request.CardToken))
        {
            return ValidationProblem("Debes indicar un token o una tarjeta guardada de Mercado Pago.");
        }

        var method = await db.PlayerPaymentMethods
            .SingleOrDefaultAsync(x => x.UserId == CurrentUserId, cancellationToken);

        if (method is null)
        {
            method = new PlayerPaymentMethod
            {
                UserId = CurrentUserId
            };
            db.PlayerPaymentMethods.Add(method);
        }

        method.MercadoPagoCustomerId = request.MercadoPagoCustomerId;
        method.MercadoPagoCardId = request.MercadoPagoCardId;
        method.CardToken = request.CardToken;
        method.PaymentMethodId = request.PaymentMethodId;
        method.CardBrand = request.CardBrand;
        method.LastFourDigits = request.LastFourDigits;
        method.IsActive = true;
        method.LinkedAtUtc = DateTime.UtcNow;

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
        var canReserveAutomatically = method?.IsActive == true &&
            (method.PaymentMethodId == MercadoPagoAccountMethod ||
                !string.IsNullOrWhiteSpace(method.MercadoPagoCardId) ||
                !string.IsNullOrWhiteSpace(method.CardToken));

        return new PlayerPaymentMethodResponse(
            method?.IsActive == true,
            canReserveAutomatically,
            method?.PaymentMethodId == MercadoPagoAccountMethod ? "Cuenta Mercado Pago" : method is null ? null : "Tarjeta",
            method?.MercadoPagoCustomerId,
            method?.MercadoPagoCardId,
            method?.PaymentMethodId,
            method?.CardBrand,
            method?.LastFourDigits,
            method?.LinkedAtUtc);
    }

}
