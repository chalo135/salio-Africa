namespace Salio.Domain.Enums
{
    // Stored as a number. New values go at the end.
    public enum ImportChangeType
    {
        // The import made a new product.
        Created,

        // The import changed a product that already existed.
        Updated
    }
}
