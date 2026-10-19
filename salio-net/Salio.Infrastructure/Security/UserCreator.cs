using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;

namespace Salio.Infrastructure.Security;

/// <summary>
/// Creates a person and their place in one organisation. Both rows are saved
/// together, so locked rule 10 holds: there is no user without a membership,
/// and therefore no user who can log in and see nothing.
/// </summary>
public class UserCreator
{
    private readonly SalioDbContext _db;
    private readonly PasswordService _passwords;

    public UserCreator(SalioDbContext db, PasswordService passwords)
    {
        _db = db;
        _passwords = passwords;
    }

    public async Task<AppUser> CreateAsync(
        Guid organizationId,
        string fullName,
        string email,
        string plainPassword,
        MembershipRole role,
        CancellationToken cancellationToken)
    {
        // Checked before anything touches the database: a short password is
        // the caller's mistake, not the database's problem.
        if (plainPassword.Length < 8)
        {
            throw new InvalidOperationException(
                "The password must be at least 8 characters");
        }

        bool organizationExists = await _db.Organizations
            .AnyAsync(o => o.Id == organizationId, cancellationToken);

        if (!organizationExists)
        {
            throw new InvalidOperationException(
                "That organisation does not exist: " + organizationId);
        }

        // Compared without case, so nobody creates ann@ next to Ann@ and
        // leaves two people wondering which account is theirs.
        string emailLower = email.ToLower();

        bool emailTaken = await _db.AppUsers
            .AnyAsync(u => u.Email.ToLower() == emailLower, cancellationToken);

        if (emailTaken)
        {
            throw new InvalidOperationException(
                "An account already exists for " + email);
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,

            // A fresh stamp, so this account's cookies are its own.
            SecurityStamp = Guid.NewGuid()
        };

        // The only place the plain password is used, and it leaves as a hash.
        user.PasswordHash = _passwords.Hash(user, plainPassword);

        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppUserId = user.Id,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.AppUsers.Add(user);
        _db.Memberships.Add(membership);

        // One save is one transaction: both rows land, or neither does.
        await _db.SaveChangesAsync(cancellationToken);

        return user;
    }
}
