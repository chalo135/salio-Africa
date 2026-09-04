namespace Salio.Domain.Entities
{
    public class Organization
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } ="KES";
        public DateTimeOffset CreatedAt{ get; set; } 
    }
}