using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Padel.Api.Contracts;
using Padel.Api.Data;
using Padel.Api.Domain;
using Padel.Api.Services;

namespace Padel.Api.Controllers;

[Authorize(Roles = "Player")]
public sealed class MatchesController(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IMatchService matches,
    ISkillMatcher matcher) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<MatchResponse>>> Search([FromQuery] bool all, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        var result = await matches.SearchAsync(user, all, cancellationToken);
        return Ok(result.Select(match => ToResponse(match, user)).ToList());
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyCollection<MatchResponse>>> Mine(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        var result = await db.Matches
            .Include(x => x.Court)
            .ThenInclude(x => x.Club)
            .Include(x => x.Players)
            .ThenInclude(x => x.User)
            .Include(x => x.Payments)
            .Where(match => match.Players.Any(player => player.UserId == CurrentUserId))
            .OrderByDescending(match => match.StartsAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(result.Select(match => ToResponse(match, user)).ToList());
    }

    [HttpGet("{matchId:guid}")]
    public async Task<ActionResult<MatchResponse>> Get(Guid matchId, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        var match = await db.Matches
            .Include(x => x.Court)
            .ThenInclude(x => x.Club)
            .Include(x => x.Players)
            .ThenInclude(x => x.User)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken);

        return match is null ? NotFound() : Ok(ToResponse(match, user));
    }

    [HttpPost]
    public async Task<ActionResult<MatchResponse>> Create(CreateMatchRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        try
        {
            var match = await matches.CreateAsync(user, request, cancellationToken);
            var loaded = await LoadMatchAsync(match.Id, cancellationToken);
            return CreatedAtAction(nameof(Get), new { matchId = match.Id }, ToResponse(loaded, user));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{matchId:guid}/join")]
    public async Task<IActionResult> Join(Guid matchId, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        try
        {
            await matches.JoinAsync(matchId, user, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{matchId:guid}/leave")]
    public async Task<IActionResult> Leave(Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await matches.LeaveAsync(matchId, CurrentUserId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{matchId:guid}/requests")]
    public async Task<ActionResult<JoinRequestDto>> RequestJoin(Guid matchId, CreateJoinRequestRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        try
        {
            var joinRequest = await matches.RequestJoinAsync(matchId, user, request.Message, cancellationToken);
            return Ok(new JoinRequestDto(joinRequest.Id, joinRequest.MatchId, joinRequest.UserId, user.FullName, joinRequest.Status, joinRequest.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("requests/pending")]
    public async Task<ActionResult<IReadOnlyCollection<JoinRequestDto>>> PendingRequests(CancellationToken cancellationToken)
    {
        var requests = await db.JoinRequests
            .Include(request => request.User)
            .Include(request => request.Match)
            .Where(request => request.Match.CreatorId == CurrentUserId && request.Status == JoinRequestStatus.Pending)
            .Select(request => new JoinRequestDto(request.Id, request.MatchId, request.UserId, request.User.FullName, request.Status, request.Message))
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpPost("requests/{requestId:guid}/accept")]
    public async Task<IActionResult> Accept(Guid requestId, CancellationToken cancellationToken)
    {
        return await Decide(requestId, accept: true, cancellationToken);
    }

    [HttpPost("requests/{requestId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid requestId, CancellationToken cancellationToken)
    {
        return await Decide(requestId, accept: false, cancellationToken);
    }

    [HttpPost("{matchId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await matches.CancelAsync(matchId, CurrentUserId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<IActionResult> Decide(Guid requestId, bool accept, CancellationToken cancellationToken)
    {
        try
        {
            await matches.DecideRequestAsync(requestId, CurrentUserId, accept, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<ApplicationUser> GetCurrentUserAsync()
    {
        return await userManager.FindByIdAsync(CurrentUserId)
            ?? throw new InvalidOperationException("Usuario no autenticado.");
    }

    private async Task<PadelMatch> LoadMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        return await db.Matches
            .Include(x => x.Court)
            .ThenInclude(x => x.Club)
            .Include(x => x.Players)
            .ThenInclude(x => x.User)
            .Include(x => x.Payments)
            .SingleAsync(x => x.Id == matchId, cancellationToken);
    }

    private MatchResponse ToResponse(PadelMatch match, ApplicationUser user)
    {
        var isParticipant = match.Players.Any(player => player.UserId == user.Id);
        var currentUserPayment = match.Payments
            .Where(payment => payment.UserId == user.Id)
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .FirstOrDefault();

        return new MatchResponse(
            match.Id,
            match.Court.Club.Name,
            match.Court.Name,
            match.StartsAtUtc,
            match.EndsAtUtc,
            match.Status,
            match.RequiredCategory,
            match.RequiredLevel,
            match.Players.Count,
            !isParticipant && matcher.IsCompatible(match.RequiredCategory, match.RequiredLevel, user.Category, user.Level),
            match.CreatorId == user.Id,
            currentUserPayment?.Id,
            currentUserPayment?.Status,
            currentUserPayment?.CheckoutUrl,
            match.Players
                .OrderBy(player => player.TeamNumber)
                .Select(player => new MatchPlayerResponse(player.UserId, player.User.FullName, player.TeamNumber, player.User.Category, player.User.Level))
                .ToList());
    }
}
