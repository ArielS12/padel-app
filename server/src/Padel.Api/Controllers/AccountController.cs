using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;
using System.Text.Json.Serialization;

namespace Padel.Api.Controllers;

[Authorize]
public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    HttpClient httpClient) : ApiControllerBase
{
    private const string MercadoPagoAccountMethod = "mercadopago_account";
    private const string ClubOwnerOAuthPurpose = "ClubOwner";
    private const string PlayerOAuthPurpose = "Player";

    [HttpGet("club-owner")]
    [Authorize(Roles = "ClubOwner,Admin")]
    public async Task<ActionResult<OwnerAccountResponse>> GetClubOwnerAccount()
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    [HttpPut("club-owner")]
    [Authorize(Roles = "ClubOwner,Admin")]
    public async Task<ActionResult<OwnerAccountResponse>> UpdateClubOwnerAccount(UpdateOwnerAccountRequest request)
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        if (user is null)
        {
            return Unauthorized();
        }

        user.FullName = request.FullName;
        user.Phone = request.Phone;
        user.MercadoPagoAccountEmail = request.MercadoPagoAccountEmail;
        user.MercadoPagoPublicKey = request.MercadoPagoPublicKey;
        if (!string.IsNullOrWhiteSpace(request.MercadoPagoAccessToken))
        {
            user.MercadoPagoAccessToken = request.MercadoPagoAccessToken;
        }

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ValidationProblem(string.Join("; ", updateResult.Errors.Select(error => error.Description)));
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return ValidationProblem("Debes indicar la contraseña actual para cambiarla.");
            }

            var passwordResult = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!passwordResult.Succeeded)
            {
                return ValidationProblem(string.Join("; ", passwordResult.Errors.Select(error => error.Description)));
            }
        }

        return Ok(ToResponse(user));
    }

    [HttpPost("club-owner/mercadopago/connect")]
    [Authorize(Roles = "ClubOwner")]
    public async Task<ActionResult<MercadoPagoConnectResponse>> CreateMercadoPagoConnectUrl(CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!string.IsNullOrWhiteSpace(user.MercadoPagoAccessToken))
        {
            return BadRequest("Ya tienes una cuenta de Mercado Pago vinculada. Desvinculala antes de conectar otra cuenta.");
        }

        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings?.OAuthClientId) || string.IsNullOrWhiteSpace(settings.OAuthRedirectUrl))
        {
            return BadRequest("El administrador debe configurar Application ID y Redirect URL de Mercado Pago.");
        }

        if (settings.OAuthClientId.StartsWith("APP_USR-", StringComparison.OrdinalIgnoreCase) ||
            settings.OAuthClientId.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("La configuracion de Mercado Pago no es valida: Application ID / Client ID debe ser el N° de aplicación, no la Public Key ni el Access Token.");
        }

        var state = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        db.MercadoPagoOAuthStates.Add(new MercadoPagoOAuthState
        {
            State = state,
            UserId = CurrentUserId,
            Purpose = ClubOwnerOAuthPurpose
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

    [HttpDelete("club-owner/mercadopago")]
    [Authorize(Roles = "ClubOwner")]
    public async Task<ActionResult<OwnerAccountResponse>> DisconnectMercadoPagoAccount()
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        if (user is null)
        {
            return Unauthorized();
        }

        user.MercadoPagoAccessToken = null;
        user.MercadoPagoAccountEmail = null;
        user.MercadoPagoRefreshToken = null;
        user.MercadoPagoPublicKey = null;
        user.MercadoPagoUserId = null;
        user.MercadoPagoTokenExpiresAtUtc = null;
        user.MercadoPagoLinkedAtUtc = null;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return ValidationProblem(string.Join("; ", updateResult.Errors.Select(error => error.Description)));
        }

        return Ok(ToResponse(user));
    }

    [HttpGet("club-owner/mercadopago/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> MercadoPagoOAuthCallback(string? code, string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest("Mercado Pago no devolvio codigo de autorizacion.");
        }

        var oauthState = await db.MercadoPagoOAuthStates
            .SingleOrDefaultAsync(x => x.State == state && x.UsedAtUtc == null, cancellationToken);
        if (oauthState is null)
        {
            return BadRequest("La solicitud de vinculacion no es valida o ya fue utilizada.");
        }

        var settings = await db.MercadoPagoSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings?.OAuthClientId) ||
            string.IsNullOrWhiteSpace(settings.OAuthClientSecret) ||
            string.IsNullOrWhiteSpace(settings.OAuthRedirectUrl))
        {
            return BadRequest("La aplicacion de Mercado Pago no esta configurada.");
        }

        var response = await httpClient.PostAsJsonAsync("https://api.mercadopago.com/oauth/token", new
        {
            client_id = settings.OAuthClientId,
            client_secret = settings.OAuthClientSecret,
            grant_type = "authorization_code",
            code,
            redirect_uri = settings.OAuthRedirectUrl,
            test_token = settings.Environment == MercadoPagoEnvironment.Sandbox ? "true" : "false"
        }, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return BadRequest($"Mercado Pago rechazo la vinculacion ({(int)response.StatusCode}). {body}");
        }

        var token = await response.Content.ReadFromJsonAsync<MercadoPagoOAuthTokenResponse>(cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return BadRequest("Mercado Pago no devolvio credenciales validas.");
        }

        var user = await userManager.FindByIdAsync(oauthState.UserId);
        if (user is null)
        {
            return BadRequest("No se encontro el dueño de cancha vinculado a la solicitud.");
        }

        if (oauthState.Purpose == PlayerOAuthPurpose)
        {
            if (token.UserId is null)
            {
                return BadRequest("Mercado Pago no devolvio una cuenta valida.");
            }

            var method = await db.PlayerPaymentMethods.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
            if (method is null)
            {
                method = new PlayerPaymentMethod { UserId = user.Id };
                db.PlayerPaymentMethods.Add(method);
            }

            method.MercadoPagoCustomerId = token.UserId.Value.ToString();
            method.MercadoPagoCardId = null;
            method.CardToken = null;
            method.PaymentMethodId = MercadoPagoAccountMethod;
            method.CardBrand = "Mercado Pago";
            method.LastFourDigits = null;
            method.IsActive = true;
            method.LinkedAtUtc = DateTime.UtcNow;
            oauthState.UsedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return Content("""
                <html>
                  <body>
                    <h1>Cuenta de Mercado Pago vinculada</h1>
                    <p>Ya puedes volver a la aplicacion de Padel.</p>
                  </body>
                </html>
                """, "text/html");
        }

        if (oauthState.Purpose != ClubOwnerOAuthPurpose)
        {
            return BadRequest("La solicitud de vinculacion de Mercado Pago no tiene un proposito valido.");
        }

        if (!await userManager.IsInRoleAsync(user, "ClubOwner"))
        {
            return BadRequest("El usuario vinculado a la solicitud no puede conectar Mercado Pago.");
        }

        user.MercadoPagoAccessToken = token.AccessToken;
        user.MercadoPagoRefreshToken = token.RefreshToken;
        user.MercadoPagoPublicKey = token.PublicKey;
        user.MercadoPagoUserId = token.UserId?.ToString();
        user.MercadoPagoLinkedAtUtc = DateTime.UtcNow;
        user.MercadoPagoTokenExpiresAtUtc = token.ExpiresIn.HasValue
            ? DateTime.UtcNow.AddSeconds(token.ExpiresIn.Value)
            : null;
        oauthState.UsedAtUtc = DateTime.UtcNow;

        await userManager.UpdateAsync(user);
        await db.SaveChangesAsync(cancellationToken);

        return Content("""
            <html>
              <body>
                <h1>Cuenta de Mercado Pago vinculada</h1>
                <p>Ya puedes volver a la aplicacion de Padel.</p>
              </body>
            </html>
            """, "text/html");
    }

    private static OwnerAccountResponse ToResponse(ApplicationUser user)
    {
        return new OwnerAccountResponse(
            user.Email ?? string.Empty,
            user.FullName,
            user.Phone,
            user.MercadoPagoAccountEmail,
            user.MercadoPagoPublicKey,
            user.MercadoPagoUserId,
            user.MercadoPagoLinkedAtUtc,
            !string.IsNullOrWhiteSpace(user.MercadoPagoAccessToken));
    }

    private sealed class MercadoPagoOAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("public_key")]
        public string? PublicKey { get; set; }

        [JsonPropertyName("user_id")]
        public long? UserId { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }
}
