using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Padel.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Usuario no autenticado.");
}
