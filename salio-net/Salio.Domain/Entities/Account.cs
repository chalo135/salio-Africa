using Salio.Domain.Enums;

namespace Salio.Domain.Entities;

public class Account
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountClass AccountClass { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;

    public void Deactivate()
    {
        IsActive = false;
    }
}