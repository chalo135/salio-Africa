using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    public class Organization
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } ="KES";
        public DateTimeOffset CreatedAt{ get; set; } 

        // "Sales" or "Services": what the client said the business mainly
        // does. It sets the default wording and which screens appear first.
        // It does NOT switch stock off: a Services business can still add
        // stock later without re-running setup. Never gate stock on it.
        public string BusinessKind { get; set; } = "Sales";

        // The trade the client chose at setup, exactly as the option read
        // (e.g. "Motorcycle or car spare parts"), or the words they typed
        // for "Something else".
        public string Trade { get; set; } = string.Empty;

        // What the shop calls the things it sells, in the plural: "Parts",
        // "Products". Worked out from the trade; the client is never asked.
        // The till uses it wherever it would have said "Parts".
        public string SellableNoun { get; set; } = "Parts";

        // What the owner said at setup. Not a ruling on what they owe: "not
        // sure" is a real answer, kept so it can be asked again later.
        public TaxPosition TaxPosition { get; set; }
    }
}
