using Microsoft.Extensions.Configuration;
using Salio.Infrastructure.Invoices;

namespace Salio.Tests
{
    public class InvoiceReaderTests
    {
        private static Stream AnyFile() => new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        private static ConfiguredInvoiceReader ReaderWith(string? provider, string? apiKey)
        {
            var settings = new Dictionary<string, string?>
            {
                ["InvoiceReader:Provider"] = provider,
                ["InvoiceReader:ApiKey"] = apiKey
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            return new ConfiguredInvoiceReader(configuration);
        }

        [Fact]
        public async Task The_fake_reader_says_it_is_a_demo_and_gives_the_same_answer_every_time()
        {
            var reader = new FakeInvoiceReader();

            var first = await reader.ReadAsync(AnyFile(), "image/png", CancellationToken.None);
            var second = await reader.ReadAsync(AnyFile(), "application/pdf", CancellationToken.None);

            Assert.True(first.IsDemo);
            Assert.Equal(6, first.Lines.Count);
            Assert.Equal(first.SupplierName, second.SupplierName);
            Assert.Equal(first.InvoiceNumber, second.InvoiceNumber);
            Assert.Equal(first.InvoiceDate, second.InvoiceDate);
            Assert.Equal(first.TotalMinor, second.TotalMinor);
            Assert.Equal(
                first.Lines.Select(l => (l.RawText, l.ProposedName, l.Quantity, l.UnitCostMinor)),
                second.Lines.Select(l => (l.RawText, l.ProposedName, l.Quantity, l.UnitCostMinor)));
        }

        [Fact]
        public async Task The_fake_total_is_600_shillings_more_than_its_lines_so_the_check_shows()
        {
            var extraction = await new FakeInvoiceReader().ReadAsync(AnyFile(), "image/png", CancellationToken.None);

            long lines = extraction.Lines.Sum(l => (long)(l.Quantity * l.UnitCostMinor));

            Assert.Equal(3_024_000, lines);
            Assert.Equal(3_084_000, extraction.TotalMinor);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("someprovider", "")]
        [InlineData("someprovider", "   ")]
        public async Task With_no_key_the_configured_reader_uses_the_demo_and_says_so(string? provider, string? apiKey)
        {
            var extraction = await ReaderWith(provider, apiKey)
                .ReadAsync(AnyFile(), "image/png", CancellationToken.None);

            Assert.True(extraction.IsDemo);
            Assert.NotEmpty(extraction.Lines);
        }

        [Fact]
        public async Task With_a_key_for_a_provider_nobody_built_it_refuses_rather_than_pretend()
        {
            var reader = ReaderWith("someprovider", "secret-key");

            var ex = await Assert.ThrowsAsync<InvoiceReaderNotBuiltException>(
                () => reader.ReadAsync(AnyFile(), "image/png", CancellationToken.None));

            Assert.Equal(
                "InvoiceReader:Provider 'someprovider' is configured but no reader for it has been built yet",
                ex.Message);
        }

        [Fact]
        public async Task With_a_key_but_no_provider_named_it_refuses()
        {
            var reader = ReaderWith("", "secret-key");

            await Assert.ThrowsAsync<InvoiceReaderNotBuiltException>(
                () => reader.ReadAsync(AnyFile(), "image/png", CancellationToken.None));
        }

        [Theory]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
        [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
        public void A_file_is_known_by_its_first_bytes(byte[] content, string expected)
        {
            Assert.Equal(expected, InvoiceFile.Sniff(content).ContentType);
        }

        [Fact]
        public void A_file_that_only_claims_to_be_a_picture_is_refused()
        {
            // "GIF89a" and plain text named .png are both unknown.
            Assert.Null(InvoiceFile.Sniff(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }).ContentType);
            Assert.Null(InvoiceFile.Sniff(System.Text.Encoding.UTF8.GetBytes("hello.png")).ContentType);
            Assert.Null(InvoiceFile.Sniff(Array.Empty<byte>()).ContentType);
        }
    }
}
