using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Padel.Api.Contracts;
using Padel.Api.Domain;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokens,
    IEmailSender emails,
    IGoogleAuthService googleAuth) : ApiControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Category = request.Category,
            Level = request.Level,
            City = request.City,
            Phone = request.Phone
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        await userManager.AddToRoleAsync(user, "Player");
        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await emails.SendConfirmationAsync(user, confirmationToken, cancellationToken);

        return Ok(new AuthResponse(await tokens.CreateTokenAsync(user), ToSummary(user)));
    }

    [HttpPost("register-club-owner")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserSummary>> RegisterClubOwner(RegisterClubOwnerRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Phone = request.Phone,
            Category = SkillCategory.Octava,
            Level = SkillLevel.Bajo
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return ValidationProblem(string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        await userManager.AddToRoleAsync(user, "ClubOwner");
        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await emails.SendConfirmationAsync(user, confirmationToken, cancellationToken);

        return Ok(ToSummary(user));
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return NotFound();
        }

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        return result.Succeeded ? NoContent() : ValidationProblem("No se pudo confirmar el email.");
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            await emails.SendPasswordResetAsync(user, resetToken, cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Unauthorized();
        }

        return Ok(new AuthResponse(await tokens.CreateTokenAsync(user), ToSummary(user)));
    }

    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> GoogleLogin(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var payload = await googleAuth.ValidateAsync(request.IdToken, cancellationToken);
        var user = await userManager.FindByEmailAsync(payload.Email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = payload.Email,
                Email = payload.Email,
                EmailConfirmed = payload.EmailVerified,
                FullName = payload.Name ?? payload.Email,
                ProfilePhotoUrl = payload.Picture,
                Category = request.Category ?? SkillCategory.Octava,
                Level = request.Level ?? SkillLevel.Bajo
            };

            var result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                return ValidationProblem(string.Join("; ", result.Errors.Select(error => error.Description)));
            }

            await userManager.AddToRoleAsync(user, "Player");
        }

        return Ok(new AuthResponse(await tokens.CreateTokenAsync(user), ToSummary(user)));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserSummary>> Me()
    {
        var user = await userManager.FindByIdAsync(CurrentUserId);
        return user is null ? NotFound() : Ok(ToSummary(user));
    }

    private static UserSummary ToSummary(ApplicationUser user)
    {
        return new UserSummary(user.Id, user.Email ?? string.Empty, user.FullName, user.Category, user.Level, user.ProfilePhotoUrl);
    }
}
