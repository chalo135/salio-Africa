using Salio.Domain.Enums;

namespace Salio.Api.Contracts;

public class AccountResponse
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AccountClass AccountClass { get; set; }

    public bool IsActive { get; set; }
}
