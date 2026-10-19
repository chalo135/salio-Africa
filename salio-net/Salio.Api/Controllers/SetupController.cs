using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Controllers;

/// <summary>
/// Locked rule 14: the developer creates each organisation and its first
/// Owner, by hand, until the setup screens have been used on a real
/// shopkeeper. There is nobody to sign in as yet when a shop is created, so
/// this is the one endpoint that allows anonymous callers — and it answers
/// 404 outside Development, so it does not exist in a deployed API.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly UserCreator _userCreator;
    private readonly IWebHostEnvironment _environment;

    public SetupController(
        SalioDbContext db,
        UserCreator userCreator,
        IWebHostEnvironment environment)
    {
        _db = db;
        _userCreator = userCreator;
        _environment = environment;
    }

    [HttpPost("organization")]
    public async Task<ActionResult<CreateOrganizationResponse>> CreateOrganization(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.ShopName,
            Currency = "KES",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // A shop, its books, its tax rate and its first Owner arrive together
        // or not at all. Half a shop would be worse than none: no chart of
        // accounts means the till cannot sell.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        AppUser owner;

        try
        {
            _db.Organizations.Add(organization);
            await _db.SaveChangesAsync(cancellationToken);

            await ChartOfAccountsSeeder.SeedAsync(_db, organization.Id, cancellationToken);
            await TaxRateSeeder.SeedAsync(_db, organization.Id, cancellationToken);

            owner = await _userCreator.CreateAsync(
                organization.Id,
                request.OwnerFullName,
                request.OwnerEmail,
                request.OwnerPassword,
                MembershipRole.Owner,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // A short password or an email already in use. Nothing is left
            // behind: no shop, no accounts, no tax rate.
            await transaction.RollbackAsync(cancellationToken);

            return BadRequest(new ProblemDetails
            {
                Title = "The shop was not created",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        int accountsCreated = await _db.Accounts
            .CountAsync(a => a.OrganizationId == organization.Id, cancellationToken);

        return new CreateOrganizationResponse
        {
            OrganizationId = organization.Id,
            ShopName = organization.Name,
            OwnerId = owner.Id,
            OwnerEmail = owner.Email,
            AccountsCreated = accountsCreated
        };
    }
}
