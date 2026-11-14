using Salio.Domain.Services;

namespace Salio.Tests
{
    public class InvoiceMatchRulesTests
    {
        private static readonly Guid BrakePad = Guid.NewGuid();
        private static readonly Guid BrakeShoe = Guid.NewGuid();
        private static readonly Guid SparkPlug = Guid.NewGuid();
        private static readonly Guid Chain = Guid.NewGuid();
        private static readonly Guid Mirror = Guid.NewGuid();

        private static List<InvoiceMatchCandidate> Shelf() => new()
        {
            new() { ProductId = BrakePad, Name = "Brake Pad - Boxer BM150", Sku = "BRK-001" },
            new() { ProductId = BrakeShoe, Name = "Brake shoe TVS", Sku = "BRK-002" },
            new() { ProductId = SparkPlug, Name = "Spark plug NGK", Sku = "SPK-010" },
            new() { ProductId = Chain, Name = "Drive chain 428H", Sku = "CHN-428" },
            new() { ProductId = Mirror, Name = "Side mirror left", Sku = "MIR-L" }
        };

        [Fact]
        public void Case_and_punctuation_do_not_matter()
        {
            Assert.Equal("brake pad boxer bm150", InvoiceMatchRules.Normalise("BRAKE PAD - Boxer, BM150."));
        }

        [Fact]
        public void The_same_name_written_differently_is_the_top_suggestion_with_a_high_score()
        {
            var suggestions = InvoiceMatchRules.Suggest("BRAKE PAD BOXER BM150", "BRAKE PAD BOXER BM150 x10 @ 450", Shelf());

            Assert.Equal(BrakePad, suggestions[0].ProductId);
            Assert.True(suggestions[0].Score >= 0.9, "score was " + suggestions[0].Score);
        }

        [Fact]
        public void A_small_spelling_slip_still_finds_the_part()
        {
            var suggestions = InvoiceMatchRules.Suggest("Spark plugs NKG", "", Shelf());

            Assert.Equal(SparkPlug, suggestions[0].ProductId);
        }

        [Fact]
        public void The_sku_in_the_raw_text_is_a_strong_match_even_when_the_name_is_different()
        {
            var suggestions = InvoiceMatchRules.Suggest("Chain", "ITEM CHN428 CHAIN HEAVY x2 @ 900", Shelf());

            Assert.Equal(Chain, suggestions[0].ProductId);
            Assert.Equal(InvoiceMatchRules.SkuSeenScore, suggestions[0].Score);
        }

        [Fact]
        public void At_most_three_suggestions_best_first_and_scores_between_0_and_1()
        {
            var shelf = Shelf();
            shelf.Add(new() { ProductId = Guid.NewGuid(), Name = "Brake lever", Sku = "BRK-003" });
            shelf.Add(new() { ProductId = Guid.NewGuid(), Name = "Brake cable", Sku = "BRK-004" });

            var suggestions = InvoiceMatchRules.Suggest("Brake pad", "", shelf);

            Assert.True(suggestions.Count <= 3);
            Assert.Equal(BrakePad, suggestions[0].ProductId);

            for (int i = 0; i < suggestions.Count; i++)
            {
                Assert.InRange(suggestions[i].Score, 0, 1);

                if (i > 0)
                {
                    Assert.True(suggestions[i - 1].Score >= suggestions[i].Score);
                }
            }
        }

        [Fact]
        public void Nothing_alike_gives_no_suggestion()
        {
            var suggestions = InvoiceMatchRules.Suggest("Headlamp bulb 12V 35W", "", Shelf());

            Assert.DoesNotContain(suggestions, s => s.ProductId == Mirror);
            Assert.All(suggestions, s => Assert.True(s.Score >= InvoiceMatchRules.MinimumScore));
        }

        [Fact]
        public void An_empty_proposed_name_falls_back_to_the_raw_text()
        {
            var suggestions = InvoiceMatchRules.Suggest("", "SIDE MIRROR LEFT x2 @ 300", Shelf());

            Assert.Equal(Mirror, suggestions[0].ProductId);
        }

        [Fact]
        public void An_empty_shop_gives_no_suggestions()
        {
            Assert.Empty(InvoiceMatchRules.Suggest("Brake pad", "", new List<InvoiceMatchCandidate>()));
        }
    }
}
