using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Padel.Api.Contracts;
using Padel.Api.Domain;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

[Authorize]
public sealed class PaymentsController(UserManager<ApplicationUser> userManager, IMercadoPagoService mercadoPago) : ApiControllerBase
{
    [HttpPost("preferences")]
    [Authorize(Roles = "Player")]
    public async Task<ActionResult<PaymentPreferenceResponse>> CreatePreference(PaymentPreferenceRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await mercadoPago.CreatePreferenceAsync(user, request.MatchId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("mercadopago/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> MercadoPagoWebhook(
        [FromQuery] Guid? paymentId,
        [FromBody] MercadoPagoWebhookRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var providerPaymentId = request?.Data?.Id
                ?? request?.ProviderPaymentId
                ?? Request.Query["data.id"].FirstOrDefault()
                ?? Request.Query["id"].FirstOrDefault();

            if (paymentId.HasValue && !string.IsNullOrWhiteSpace(providerPaymentId))
            {
                await mercadoPago.SyncPaymentFromProviderAsync(paymentId.Value, providerPaymentId, cancellationToken);
                return NoContent();
            }

            if (!string.IsNullOrWhiteSpace(request?.ProviderPaymentId) && request.Status.HasValue)
            {
                await mercadoPago.UpdatePaymentAsync(request.ProviderPaymentId, request.Status.Value, cancellationToken);
                return NoContent();
            }

            return BadRequest("No se pudo identificar el pago de Mercado Pago.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("mercadopago/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> MercadoPagoWebhookGet([FromQuery] Guid? paymentId, CancellationToken cancellationToken)
    {
        try
        {
            var providerPaymentId = Request.Query["data.id"].FirstOrDefault()
                ?? Request.Query["id"].FirstOrDefault();

            if (!paymentId.HasValue || string.IsNullOrWhiteSpace(providerPaymentId))
            {
                return BadRequest("No se pudo identificar el pago de Mercado Pago.");
            }

            await mercadoPago.SyncPaymentFromProviderAsync(paymentId.Value, providerPaymentId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
