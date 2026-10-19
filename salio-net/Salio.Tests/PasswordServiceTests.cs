using Salio.Domain.Entities;
using Salio.Infrastructure.Security;

namespace Salio.Tests
{
    // Locked rule 11: the hashing is Identity's. These tests check we use it
    // correctly — never that we invented anything.
    public class PasswordServiceTests
    {
        private const string Password = "correct horse battery";

        [Fact]
        public void The_hash_is_not_the_password()
        {
            PasswordService passwords = new();
            AppUser user = NewUser();

            string hash = passwords.Hash(user, Password);

            Assert.NotEqual(Password, hash);
            Assert.DoesNotContain(Password, hash);
        }

        [Fact]
        public void The_right_password_is_accepted()
        {
            PasswordService passwords = new();
            AppUser user = NewUser();
            user.PasswordHash = passwords.Hash(user, Password);

            Assert.True(passwords.Check(user, Password));
        }

        [Fact]
        public void A_wrong_password_is_refused()
        {
            PasswordService passwords = new();
            AppUser user = NewUser();
            user.PasswordHash = passwords.Hash(user, Password);

            Assert.False(passwords.Check(user, "correct horse battery!"));
            Assert.False(passwords.Check(user, ""));
        }

        [Fact]
        public void A_user_with_no_hash_is_refused()
        {
            PasswordService passwords = new();
            AppUser user = NewUser();

            // PasswordHash starts empty. No password may ever match it.
            Assert.False(passwords.Check(user, Password));
            Assert.False(passwords.Check(user, ""));
        }

        [Fact]
        public void The_same_password_hashes_differently_each_time()
        {
            PasswordService passwords = new();
            AppUser user = NewUser();

            // A random salt per hash, so two people with the same password do
            // not have the same hash, and a stolen hash list reveals nothing.
            string first = passwords.Hash(user, Password);
            string second = passwords.Hash(user, Password);

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void A_hash_from_one_user_does_not_open_another()
        {
            PasswordService passwords = new();
            AppUser owner = NewUser();
            AppUser cashier = NewUser();

            owner.PasswordHash = passwords.Hash(owner, Password);
            cashier.PasswordHash = passwords.Hash(cashier, "a different password");

            Assert.False(passwords.Check(cashier, Password));
        }

        private static AppUser NewUser() => new()
        {
            Id = Guid.NewGuid(),
            FullName = "Wanjiru",
            Email = "wanjiru@shop.co.ke",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            SecurityStamp = Guid.NewGuid()
        };
    }
}
