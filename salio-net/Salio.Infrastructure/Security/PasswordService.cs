using Microsoft.AspNetCore.Identity;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Security;

/// <summary>
/// Turns a password into a hash, and checks a password against a stored hash.
/// Locked rule 11: the hashing is ASP.NET Core Identity's, not ours. Nothing
/// in this file invents cryptography, and no plain password is ever logged,
/// printed, returned or stored.
/// </summary>
public class PasswordService
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string Hash(AppUser user, string plainPassword)
    {
        return _hasher.HashPassword(user, plainPassword);
    }

    public bool Check(AppUser user, string plainPassword)
    {
        // A user with no hash yet can never match. Asking the hasher to read
        // an empty hash is pointless.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return false;
        }

        PasswordVerificationResult result =
            _hasher.VerifyHashedPassword(user, user.PasswordHash, plainPassword);

        // SuccessRehashNeeded means the password is correct, but the stored
        // hash was made by an older, weaker setting than today's. The person
        // is let in either way; re-saving the hash is a separate job.
        return result == PasswordVerificationResult.Success
            || result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}
