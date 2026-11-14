namespace Salio.Api.Dtos;

// GET /api/products/quick-add/rules: what the quick-add form may ask for.
public class QuickAddRulesResponse
{
    // True only while the shop has never recorded a sale. Then quantity and
    // cost may be sent, and are posted as opening stock. After the first
    // sale, quick-add takes name and selling price only; stock goes through
    // Stock in.
    public bool StockAllowed { get; set; }
}
