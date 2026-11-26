using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Salio.Infrastructure.Invoices;

namespace Salio.Tests
{
    // ClaudeInvoiceReader, with the API played by a stand-in. No test here
    // ever calls Anthropic or needs a key.
    public class ClaudeInvoiceReaderTests
    {
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        private static readonly byte[] Pdf = { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };

        // Plays the Messages API: answers with a fixed reply, and keeps what
        // was sent so a test can look at it.
        private sealed class StandIn : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _reply;
            private readonly Exception? _throw;

            public HttpRequestMessage? Sent { get; private set; }
            public string SentBody { get; private set; } = "";

            public StandIn(HttpStatusCode status, string reply, Exception? throws = null)
            {
                _status = status;
                _reply = reply;
                _throw = throws;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Sent = request;
                SentBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

                if (_throw is not null)
                {
                    throw _throw;
                }

                return new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_reply, Encoding.UTF8, "application/json")
                };
            }
        }

        // What the API sends back: the reading as JSON text inside a message.
        private static string Reply(object reading, string stopReason = "end_turn")
        {
            return JsonSerializer.Serialize(new
            {
                id = "msg_test",
                type = "message",
                role = "assistant",
                content = new object[] { new { type = "text", text = JsonSerializer.Serialize(reading) } },
                stop_reason = stopReason
            });
        }

        private static object Line(string raw, string name, string quantity, string unitPrice, string lineTotal)
        {
            return new { raw_text = raw, name, quantity, unit_price = unitPrice, line_total = lineTotal };
        }

        private static object Reading(bool readable, params object[] lines)
        {
            return new
            {
                readable,
                supplier_name = "Kirinyaga Road Moto Spares",
                invoice_number = "INV-88",
                invoice_date = "2026-11-21",
                total = "2,580/=",
                lines
            };
        }

        private static (ClaudeInvoiceReader Reader, StandIn Api) With(StandIn api, string? model = null)
        {
            return (new ClaudeInvoiceReader(() => new HttpClient(api), "test-key", model), api);
        }

        private static Task<InvoiceExtraction> Read(ClaudeInvoiceReader reader, byte[] file, string contentType)
        {
            return reader.ReadAsync(new MemoryStream(file), contentType, CancellationToken.None);
        }

        [Fact]
        public async Task A_good_reading_becomes_lines_in_minor_units_and_is_not_a_demo()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true,
                Line("CLUTCH CABLE BOXER 6 @ 380", "Clutch cable Boxer", "6", "380", "2280"),
                Line("SPARK PLUG NGK x2 300", "Spark plug NGK", "2", "", "300")))));

            var extraction = await Read(reader, Png, "image/png");

            Assert.False(extraction.IsDemo);
            Assert.Equal("Kirinyaga Road Moto Spares", extraction.SupplierName);
            Assert.Equal("INV-88", extraction.InvoiceNumber);
            Assert.Equal(new DateOnly(2026, 11, 21), extraction.InvoiceDate);
            Assert.Equal(258_000, extraction.TotalMinor);
            Assert.Equal(2, extraction.Lines.Count);

            Assert.Equal("CLUTCH CABLE BOXER 6 @ 380", extraction.Lines[0].RawText);
            Assert.Equal("Clutch cable Boxer", extraction.Lines[0].ProposedName);
            Assert.Equal(6m, extraction.Lines[0].Quantity);
            Assert.Equal(38_000, extraction.Lines[0].UnitCostMinor);

            // Only a line total was printed: 300 for 2 is 150 each, worked out
            // by Salio, not by the model.
            Assert.Equal(15_000, extraction.Lines[1].UnitCostMinor);
        }

        [Fact]
        public async Task The_request_carries_the_key_the_version_the_photo_and_the_schema()
        {
            var (reader, api) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true,
                Line("X", "X", "1", "1", "1")))));

            await Read(reader, Png, "image/png");

            Assert.NotNull(api.Sent);
            Assert.Equal(ClaudeInvoiceReader.MessagesUri, api.Sent!.RequestUri);
            Assert.Equal("test-key", api.Sent.Headers.GetValues("x-api-key").Single());
            Assert.Equal(ClaudeInvoiceReader.ApiVersion, api.Sent.Headers.GetValues("anthropic-version").Single());

            JsonNode body = JsonNode.Parse(api.SentBody)!;
            Assert.Equal(ClaudeInvoiceReader.DefaultModel, body["model"]!.GetValue<string>());

            JsonNode photo = body["messages"]![0]!["content"]![0]!;
            Assert.Equal("image", photo["type"]!.GetValue<string>());
            Assert.Equal("base64", photo["source"]!["type"]!.GetValue<string>());
            Assert.Equal("image/png", photo["source"]!["media_type"]!.GetValue<string>());
            Assert.Equal(Convert.ToBase64String(Png), photo["source"]!["data"]!.GetValue<string>());

            JsonNode format = body["output_config"]!["format"]!;
            Assert.Equal("json_schema", format["type"]!.GetValue<string>());
            Assert.False(format["schema"]!["additionalProperties"]!.GetValue<bool>());
            Assert.False(format["schema"]!["properties"]!["lines"]!["items"]!["additionalProperties"]!.GetValue<bool>());
        }

        [Fact]
        public async Task A_pdf_is_sent_as_a_document()
        {
            var (reader, api) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true,
                Line("X", "X", "1", "1", "1")))));

            await Read(reader, Pdf, "application/pdf");

            JsonNode file = JsonNode.Parse(api.SentBody)!["messages"]![0]!["content"]![0]!;
            Assert.Equal("document", file["type"]!.GetValue<string>());
            Assert.Equal("application/pdf", file["source"]!["media_type"]!.GetValue<string>());
        }

        [Fact]
        public async Task The_model_can_be_changed_in_configuration()
        {
            var (reader, api) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true,
                Line("X", "X", "1", "1", "1")))), model: " claude-sonnet-5-5 ");

            await Read(reader, Png, "image/png");

            Assert.Equal("claude-sonnet-5-5", JsonNode.Parse(api.SentBody)!["model"]!.GetValue<string>());
        }

        [Fact]
        public async Task A_number_it_could_not_read_comes_back_as_zero_for_the_owner_to_fill_in()
        {
            // The commit refuses a quantity or cost of zero, so nothing posts
            // until the owner has typed the real number.
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true,
                Line("BRAKE PADS ?? @ ??", "Brake pads", "", "", "")))));

            var line = (await Read(reader, Png, "image/png")).Lines.Single();

            Assert.Equal(0m, line.Quantity);
            Assert.Equal(0, line.UnitCostMinor);
        }

        [Fact]
        public async Task A_date_it_could_not_read_is_left_empty_not_guessed()
        {
            var reading = new
            {
                readable = true,
                supplier_name = "",
                invoice_number = "",
                invoice_date = "21/11/2026",
                total = "",
                lines = new[] { Line("X", "X", "1", "1", "1") }
            };
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, Reply(reading)));

            var extraction = await Read(reader, Png, "image/png");

            Assert.Null(extraction.InvoiceDate);
            Assert.Null(extraction.SupplierName);
            Assert.Null(extraction.TotalMinor);
        }

        [Fact]
        public async Task A_line_with_neither_text_nor_name_is_dropped()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true,
                Line("", "", "3", "100", "300"),
                Line("CHAIN COVER", "", "1", "150", "150")))));

            var lines = (await Read(reader, Png, "image/png")).Lines;

            Assert.Single(lines);
            Assert.Equal("CHAIN COVER", lines[0].ProposedName);
        }

        [Theory]
        [InlineData("1,500/=", "1500")]
        [InlineData("KSh 1,500.00", "1500.00")]
        [InlineData("Kshs. 450", "450")]
        [InlineData("450/-", "450")]
        [InlineData(" 80.5 ", "80.5")]
        public void Money_is_read_the_way_Kenyan_invoices_write_it(string printed, string expected)
        {
            Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ClaudeInvoiceReader.ToNumber(printed));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("-450")]
        [InlineData("1.2.3")]
        [InlineData("about 500")]
        public void A_number_that_is_not_plainly_a_number_is_unreadable(string printed)
        {
            Assert.Null(ClaudeInvoiceReader.ToNumber(printed));
        }

        [Fact]
        public void Minor_units_round_to_the_cent_without_floating_point()
        {
            Assert.Equal(33_333, ClaudeInvoiceReader.ToMinor(1000m / 3m));
            Assert.Equal(1_999_999, ClaudeInvoiceReader.ToMinor(19_999.99m));
            Assert.Null(ClaudeInvoiceReader.ToMinor(null));
        }

        [Fact]
        public async Task A_photo_it_says_it_cannot_read_is_refused_never_guessed()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(false))));

            var ex = await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));

            Assert.Equal(ClaudeInvoiceReader.Unreadable, ex.Message);
        }

        [Fact]
        public async Task An_invoice_with_no_goods_on_it_is_refused()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true))));

            var ex = await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));

            Assert.Equal(ClaudeInvoiceReader.NoGoods, ex.Message);
        }

        [Fact]
        public async Task An_answer_cut_off_part_way_is_refused_as_too_many_lines()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, Reply(Reading(true,
                Line("X", "X", "1", "1", "1")), stopReason: "max_tokens")));

            var ex = await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));

            Assert.Equal(ClaudeInvoiceReader.TooManyLines, ex.Message);
        }

        [Fact]
        public async Task An_answer_that_is_not_json_is_refused()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK,
                "{\"content\":[{\"type\":\"text\",\"text\":\"Sorry, I cannot\"}],\"stop_reason\":\"end_turn\"}"));

            await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, ClaudeInvoiceReader.KeyRefused)]
        [InlineData(HttpStatusCode.Forbidden, ClaudeInvoiceReader.KeyRefused)]
        [InlineData(HttpStatusCode.TooManyRequests, ClaudeInvoiceReader.Busy)]
        [InlineData((HttpStatusCode)529, ClaudeInvoiceReader.Busy)]
        [InlineData(HttpStatusCode.InternalServerError, ClaudeInvoiceReader.Busy)]
        [InlineData(HttpStatusCode.RequestEntityTooLarge, ClaudeInvoiceReader.TooBig)]
        public async Task A_refusal_from_the_api_is_told_to_the_owner_in_words(HttpStatusCode status, string expected)
        {
            var (reader, _) = With(new StandIn(status, "{\"type\":\"error\",\"error\":{\"type\":\"some_error\",\"message\":\"x\"}}"));

            var ex = await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));

            Assert.Equal(expected, ex.Message);
        }

        [Fact]
        public async Task Any_other_refusal_names_the_api_error_type_for_support()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.BadRequest,
                "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"x\"}}"));

            var ex = await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));

            Assert.Equal(ClaudeInvoiceReader.CouldNotTake + " (invalid_request_error)", ex.Message);
        }

        [Fact]
        public async Task No_internet_on_the_server_says_so()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, "", new HttpRequestException("no route")));

            var ex = await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));

            Assert.Equal(ClaudeInvoiceReader.CannotReach, ex.Message);
        }

        [Fact]
        public async Task A_timeout_says_so()
        {
            var (reader, _) = With(new StandIn(HttpStatusCode.OK, "", new TaskCanceledException("timed out")));

            var ex = await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/png"));

            Assert.Equal(ClaudeInvoiceReader.TookTooLong, ex.Message);
        }

        [Fact]
        public async Task A_file_that_is_not_a_photo_or_pdf_is_never_sent()
        {
            var (reader, api) = With(new StandIn(HttpStatusCode.OK, ""));

            await Assert.ThrowsAsync<InvoiceReadFailedException>(() => Read(reader, Png, "image/gif"));

            Assert.Null(api.Sent);
        }

        [Theory]
        [InlineData("anthropic")]
        [InlineData("Anthropic")]
        [InlineData(" claude ")]
        public void With_an_anthropic_key_the_configured_reader_is_real(string provider)
        {
            var settings = new Dictionary<string, string?>
            {
                ["InvoiceReader:Provider"] = provider,
                ["InvoiceReader:ApiKey"] = "secret-key"
            };

            var reader = new ConfiguredInvoiceReader(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

            Assert.True(reader.IsReal);
        }

        [Fact]
        public async Task The_configured_reader_sends_through_the_client_it_was_given()
        {
            var api = new StandIn(HttpStatusCode.OK, Reply(Reading(true, Line("X", "X", "1", "1", "1"))));
            var settings = new Dictionary<string, string?>
            {
                ["InvoiceReader:Provider"] = "anthropic",
                ["InvoiceReader:ApiKey"] = "secret-key",
                ["InvoiceReader:Model"] = "claude-sonnet-5-5"
            };
            var reader = new ConfiguredInvoiceReader(
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                () => new HttpClient(api));

            var extraction = await reader.ReadAsync(new MemoryStream(Png), "image/png", CancellationToken.None);

            Assert.False(extraction.IsDemo);
            Assert.Equal("secret-key", api.Sent!.Headers.GetValues("x-api-key").Single());
            Assert.Equal("claude-sonnet-5-5", JsonNode.Parse(api.SentBody)!["model"]!.GetValue<string>());
        }
    }
}
