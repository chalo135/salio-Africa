using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Salio.Infrastructure.Invoices;

// Reads a supplier invoice with Claude, through Anthropic's Messages API.
// Chosen when InvoiceReader:Provider is "anthropic" (see ConfiguredInvoiceReader).
//
//   https://platform.claude.com/docs/en/build-with-claude/vision
//   https://platform.claude.com/docs/en/build-with-claude/pdf-support
//   https://platform.claude.com/docs/en/build-with-claude/structured-outputs
//
// It only PROPOSES lines. Nothing reaches stock or the books until the owner
// has checked every line on the review screen and pressed the button that
// receives them. The commit already refuses a quantity or unit cost of zero,
// so a number Claude could not read comes back as 0, and the owner has to
// type it before anything posts.
//
// Two rules about numbers:
//   - Money comes back as text ("1,500/="), never as a JSON number, and is
//     turned into minor units here with decimal. No amount ever passes
//     through floating point.
//   - When an invoice prints a line total but no unit price, the unit price
//     is worked out HERE from the line total and the quantity. A model reads
//     numbers well and still does sums badly, so it is never asked to divide.
//
// Every failure throws InvoiceReadFailedException with a sentence the owner
// can act on. It never falls back to demo lines.
public class ClaudeInvoiceReader : IInvoiceReader
{
    // Haiku is enough for printed and handwritten invoices and costs well
    // under a shilling a page. InvoiceReader:Model changes it, for example to
    // claude-sonnet-5-5 if a supplier's handwriting defeats Haiku.
    public const string DefaultModel = "claude-haiku-5-5";

    public const string ApiVersion = "2026-10-21";

    public static readonly Uri MessagesUri = new("https://api.anthropic.com/v1/messages");

    // A 60-line invoice is about 4,000 tokens of answer. Twice that leaves
    // room; an answer cut off at this limit is refused as "too many lines".
    public const int MaxTokens = 8192;

    // What the owner reads when something goes wrong. Short, and each one
    // says what to do next.
    public const string Unreadable = "Couldn't read this invoice. Take the photo again: flat, in good light, the whole page in view.";
    public const string NoGoods = "Couldn't find any goods on this invoice. Take the photo again, closer and flat.";
    public const string TooManyLines = "This invoice has too many lines to read at once. Photograph it in parts and send each part.";
    public const string TookTooLong = "Reading the invoice took too long. Try again.";
    public const string CannotReach = "Salio couldn't reach the invoice reader. Try again, or add the stock by hand.";
    public const string KeyRefused = "The invoice reader's key was refused. Add this stock by hand, and ask whoever set up Salio to check the key.";
    public const string Busy = "The invoice reader is busy. Wait a minute and try again.";
    public const string TooBig = "This file is too big to read. Take the photo closer, or send fewer pages.";
    public const string CouldNotTake = "The invoice reader couldn't take this file. Add this stock by hand.";

    private const string SystemPrompt =
        "You read supplier invoices and delivery notes for a motorcycle spare parts shop in Kenya, "
        + "so the owner does not have to type them in. You copy what is printed. "
        + "You never guess a number you cannot read.";

    private const string Instructions = """
        Read this supplier invoice or delivery note and fill in the JSON.

        lines: one entry for every item of goods, in the order printed. Skip anything that is not goods: subtotals, totals, VAT, discounts, delivery or transport charges, payments, balances, and blank or crossed-out lines.
        - raw_text: the whole line exactly as printed, abbreviations and all.
        - name: the part as a shopkeeper would say it, in plain words. Keep the bike model and the brand if they are printed, for example "Clutch cable Boxer" or "Battery 12V Datco". Spell out obvious short forms, but add nothing that is not on the line.
        - quantity: the number of units, as printed.
        - unit_price: the price of ONE unit, as printed. Write "" if the invoice does not print a unit price.
        - line_total: the amount for the whole line, as printed. Write "" if it is not printed.
        Do not calculate anything. Copy the numbers only.

        Numbers: digits with at most one decimal point. No commas, no currency, no "/=" or "/-". Write "" for any number you cannot read with certainty, rather than guessing. Handwritten invoices are common: read them carefully, and if one digit is unclear, write "" for that number.

        supplier_name, invoice_number: as printed, or "".
        invoice_date: as YYYY-MM-DD, or "". Kenyan invoices write the day first: 05/11/2026 is 5 November 2026.
        total: the grand total payable, as printed, or "".
        readable: false if this is not an invoice, delivery note or receipt for goods, or if it is too blurred, dark or cut off to read. Otherwise true.
        """;

    private readonly Func<HttpClient> _http;
    private readonly string _apiKey;
    private readonly string _model;

    public ClaudeInvoiceReader(Func<HttpClient> http, string apiKey, string? model)
    {
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
    }

    public async Task<InvoiceExtraction> ReadAsync(Stream image, string contentType, CancellationToken cancellationToken)
    {
        if (contentType != "image/png" && contentType != "image/jpeg" && contentType != "application/pdf")
        {
            throw new InvoiceReadFailedException(InvoiceFile.WrongTypeMessage);
        }

        byte[] file;
        using (var copy = new MemoryStream())
        {
            await image.CopyToAsync(copy, cancellationToken);
            file = copy.ToArray();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUri)
        {
            Content = new StringContent(BuildRequest(file, contentType, _model).ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        HttpResponseMessage response;

        try
        {
            response = await _http().SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout, not the owner leaving the page.
            throw new InvoiceReadFailedException(TookTooLong);
        }
        catch (HttpRequestException)
        {
            throw new InvoiceReadFailedException(CannotReach);
        }

        using (response)
        {
            string answer = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvoiceReadFailedException(ForStatus(response.StatusCode, answer));
            }

            return ReadAnswer(answer);
        }
    }

    // The whole request body. Public so a test can see exactly what is sent.
    public static JsonObject BuildRequest(byte[] file, string contentType, string model)
    {
        var fileBlock = new JsonObject
        {
            // A PDF is a "document" block; a photo is an "image" block.
            ["type"] = contentType == "application/pdf" ? "document" : "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = contentType,
                ["data"] = Convert.ToBase64String(file)
            }
        };

        return new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["system"] = SystemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        fileBlock,
                        new JsonObject { ["type"] = "text", ["text"] = Instructions }
                    }
                }
            },

            // Structured outputs: the answer is guaranteed to match this
            // schema. Everything is required and nothing else is allowed, as
            // the API asks. Numbers are strings on purpose (see the top).
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["schema"] = Schema()
                }
            }
        };
    }

    private static JsonObject Schema()
    {
        static JsonObject Text() => new() { ["type"] = "string" };

        var line = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["raw_text"] = Text(),
                ["name"] = Text(),
                ["quantity"] = Text(),
                ["unit_price"] = Text(),
                ["line_total"] = Text()
            },
            ["required"] = new JsonArray("raw_text", "name", "quantity", "unit_price", "line_total"),
            ["additionalProperties"] = false
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["readable"] = new JsonObject { ["type"] = "boolean" },
                ["supplier_name"] = Text(),
                ["invoice_number"] = Text(),
                ["invoice_date"] = Text(),
                ["total"] = Text(),
                ["lines"] = new JsonObject { ["type"] = "array", ["items"] = line }
            },
            ["required"] = new JsonArray("readable", "supplier_name", "invoice_number", "invoice_date", "total", "lines"),
            ["additionalProperties"] = false
        };
    }

    // The API's reply, turned into an InvoiceExtraction or refused.
    public static InvoiceExtraction ReadAnswer(string responseJson)
    {
        JsonNode? message = ParseOrNull(responseJson);

        if (message is null)
        {
            throw new InvoiceReadFailedException(Unreadable);
        }

        string? stopReason = Words(message["stop_reason"]);

        if (stopReason == "max_tokens")
        {
            throw new InvoiceReadFailedException(TooManyLines);
        }

        if (stopReason == "refusal")
        {
            throw new InvoiceReadFailedException(Unreadable);
        }

        // The JSON is in the text block. Found by type, not by position.
        string? text = null;

        foreach (JsonNode? block in message["content"] as JsonArray ?? new JsonArray())
        {
            if (Words(block?["type"]) == "text")
            {
                text = Words(block?["text"]);
                break;
            }
        }

        JsonNode? read = text is null ? null : ParseOrNull(text);

        if (read is null || !(read["readable"] is JsonValue readable && readable.TryGetValue(out bool ok) && ok))
        {
            throw new InvoiceReadFailedException(Unreadable);
        }

        var extraction = new InvoiceExtraction
        {
            SupplierName = Words(read["supplier_name"]),
            InvoiceNumber = Words(read["invoice_number"]),
            InvoiceDate = Date(Words(read["invoice_date"])),
            TotalMinor = ToMinor(ToNumber(Words(read["total"]))),
            IsDemo = false
        };

        foreach (JsonNode? line in read["lines"] as JsonArray ?? new JsonArray())
        {
            string raw = Words(line?["raw_text"]) ?? "";
            string name = Words(line?["name"]) ?? raw;

            if (raw.Length == 0 && name.Length == 0)
            {
                continue;
            }

            decimal? quantity = ToNumber(Words(line?["quantity"]));
            decimal? unit = ToNumber(Words(line?["unit_price"]));
            decimal? lineTotal = ToNumber(Words(line?["line_total"]));

            // Only a line total printed: the unit price is worked out here.
            if (unit is null && lineTotal is not null && quantity is > 0)
            {
                unit = decimal.Round(lineTotal.Value / quantity.Value, 2, MidpointRounding.AwayFromZero);
            }

            extraction.Lines.Add(new InvoiceExtractionLine
            {
                RawText = raw.Length > 0 ? raw : name,
                ProposedName = name,
                // Unreadable is 0: the owner must fill it in before receiving.
                Quantity = quantity ?? 0,
                UnitCostMinor = ToMinor(unit) ?? 0
            });
        }

        if (extraction.Lines.Count == 0)
        {
            throw new InvoiceReadFailedException(NoGoods);
        }

        return extraction;
    }

    // Kenyan invoices write money many ways: "1,500/=", "KSh 1,500.00",
    // "Kshs. 450", "450/-". All of those are plain numbers once the
    // decorations are gone. Anything else, including a minus sign, is null:
    // unreadable, never guessed.
    private static readonly Regex Decorations = new(@"kshs?\.?|kes\.?|/=|/-|,|\s", RegexOptions.IgnoreCase);
    private static readonly Regex PlainNumber = new(@"^\d+(\.\d+)?$");

    public static decimal? ToNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string plain = Decorations.Replace(text, "");

        if (!PlainNumber.IsMatch(plain))
        {
            return null;
        }

        return decimal.TryParse(plain, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    // Shillings to minor units, rounded to the cent. Amounts no shop invoice
    // could carry are treated as unreadable rather than overflowing.
    public static long? ToMinor(decimal? shillings)
    {
        if (shillings is null || shillings.Value > 1_000_000_000m)
        {
            return null;
        }

        return (long)decimal.Round(shillings.Value * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static DateOnly? Date(string? text)
    {
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }

    // A string value, trimmed, or null when it is missing, empty or not a string.
    private static string? Words(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            text = text?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

    private static JsonNode? ParseOrNull(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ForStatus(HttpStatusCode status, string body)
    {
        int code = (int)status;

        if (code == 401 || code == 403)
        {
            return KeyRefused;
        }

        if (code == 413)
        {
            return TooBig;
        }

        // 429 too many requests, 529 overloaded, and the 5xx family.
        if (code == 429 || code >= 500)
        {
            return Busy;
        }

        // Anything else: say so, with the API's own error type for support.
        string? type = Words(ParseOrNull(body)?["error"]?["type"]);
        return CouldNotTake + " (" + (type ?? code.ToString(CultureInfo.InvariantCulture)) + ")";
    }
}
