namespace Salio.Infrastructure.Invoices;

// Canned lines from a made-up motorcycle spares supplier. It does not look
// at the file at all, and always says IsDemo = true, so the whole flow can be
// tried with no provider and no key without anyone mistaking it for a real
// reading.
//
// The printed total is deliberately KES 600.00 more than the lines add up to
// (30,840.00 against 30,240.00), so the totals check has something to show.
public class FakeInvoiceReader : IInvoiceReader
{
    public Task<InvoiceExtraction> ReadAsync(Stream image, string contentType, CancellationToken cancellationToken)
    {
        var extraction = new InvoiceExtraction
        {
            SupplierName = "Kirinyaga Road Moto Spares Ltd",
            InvoiceNumber = "INV-2026-0457",
            InvoiceDate = new DateOnly(2026, 11, 7),
            TotalMinor = 3_084_000,
            IsDemo = true,
            Lines =
            {
                Line("BRAKE PAD BOXER BM150 x10 @ 450", "Brake pad Boxer BM150", 10, 45_000),
                Line("CHAIN SPROCKET KIT TVS HLX125 x4 @ 2,350", "Chain sprocket kit TVS HLX125", 4, 235_000),
                Line("ENGINE OIL 20W50 1L x12 @ 680", "Engine oil 20W50 1L", 12, 68_000),
                Line("SPARK PLUG NGK C7HSA x20 @ 220", "Spark plug NGK C7HSA", 20, 22_000),
                Line("CLUTCH CABLE BAJAJ BOXER x6 @ 380", "Clutch cable Bajaj Boxer", 6, 38_000),
                Line("HEADLAMP BULB 12V 35W x10 @ 150", "Headlamp bulb 12V 35W", 10, 15_000)
            }
        };

        return Task.FromResult(extraction);
    }

    private static InvoiceExtractionLine Line(string rawText, string name, decimal quantity, long unitCostMinor)
    {
        return new InvoiceExtractionLine
        {
            RawText = rawText,
            ProposedName = name,
            Quantity = quantity,
            UnitCostMinor = unitCostMinor
        };
    }
}
