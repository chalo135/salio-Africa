using Salio.Domain.Services;

namespace Salio.Tests
{
    public class ImportRowRulesTests
    {
        private static Dictionary<string, string> Row(
            string sku = "BRK-01",
            string name = "Brake pad",
            string price = "450",
            string? cost = null,
            string? quantity = null)
        {
            var cells = new Dictionary<string, string>
            {
                [ImportRowRules.Sku] = sku,
                [ImportRowRules.Name] = name,
                [ImportRowRules.SellingPrice] = price
            };

            if (cost is not null)
            {
                cells[ImportRowRules.UnitCost] = cost;
            }

            if (quantity is not null)
            {
                cells[ImportRowRules.Quantity] = quantity;
            }

            return cells;
        }

        private static ImportRowResult Check(Dictionary<string, string> cells, ImportRowContext? context = null)
        {
            return ImportRowRules.Validate(7, cells, context ?? new ImportRowContext());
        }

        private static ImportIssue OnlyError(ImportRowResult result, string column)
        {
            ImportIssue error = Assert.Single(result.Errors);
            Assert.Equal(column, error.Column);
            Assert.Equal(7, error.Row);
            Assert.StartsWith("Row 7: ", error.Message);
            Assert.Null(result.Row);
            return error;
        }

        // ---- A good row ----

        [Fact]
        public void A_complete_new_row_passes_and_is_read_into_minor_units()
        {
            var result = Check(Row(cost: "300.50", quantity: "4"));

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
            Assert.NotNull(result.Row);
            Assert.Equal("BRK-01", result.Row.Sku);
            Assert.Equal(45000, result.Row.SellPriceMinor);
            Assert.Equal(30050, result.Row.UnitCostMinor);
            Assert.Equal(4, result.Row.Quantity);
        }

        // ---- SKU ----

        [Fact]
        public void A_missing_sku_is_an_error()
        {
            OnlyError(Check(Row(sku: "   ")), ImportRowRules.Sku);
        }

        [Fact]
        public void A_sku_longer_than_40_characters_is_an_error()
        {
            OnlyError(Check(Row(sku: new string('A', 41))), ImportRowRules.Sku);
            Assert.Empty(Check(Row(sku: new string('A', 40))).Errors);
        }

        [Fact]
        public void A_sku_repeated_earlier_in_the_file_is_an_error_naming_the_first_row()
        {
            var context = new ImportRowContext { SkusSeenEarlier = new() { ["BRK-01"] = 3 } };

            var error = OnlyError(Check(Row(sku: " brk-01 "), context), ImportRowRules.Sku);

            Assert.Contains("row 3", error.Message);
            Assert.Equal(" brk-01 ", error.Value);
        }

        [Fact]
        public void A_lower_case_sku_is_accepted_trimmed_and_made_capitals()
        {
            var result = Check(Row(sku: "  brk-01\t"));

            Assert.Empty(result.Errors);
            Assert.Equal("BRK-01", result.Row!.Sku);
            Assert.Equal("BRK-01", ImportRowRules.NormaliseSku(" brk-01 "));
        }

        [Fact]
        public void A_sku_of_a_switched_off_part_is_an_error()
        {
            var context = new ImportRowContext { SkuAlreadyExists = false, SkuIsSwitchedOff = true };

            var error = OnlyError(Check(Row(), context), ImportRowRules.Sku);

            Assert.Contains("belongs to a part that was switched off", error.Message);
            Assert.Contains("Switch it back on, or use a different SKU.", error.Message);
        }

        // ---- Name ----

        [Fact]
        public void A_missing_name_is_an_error()
        {
            OnlyError(Check(Row(name: "")), ImportRowRules.Name);
        }

        [Fact]
        public void A_name_longer_than_64_characters_is_an_error()
        {
            OnlyError(Check(Row(name: new string('x', 65))), ImportRowRules.Name);
            Assert.Empty(Check(Row(name: new string('x', 64))).Errors);
        }

        // ---- Selling price ----

        [Fact]
        public void A_missing_selling_price_is_an_error()
        {
            OnlyError(Check(Row(price: " ")), ImportRowRules.SellingPrice);
        }

        [Fact]
        public void A_selling_price_that_is_not_a_number_is_an_error_showing_the_cell()
        {
            var error = OnlyError(Check(Row(price: "abc")), ImportRowRules.SellingPrice);

            Assert.Equal("abc", error.Value);
            Assert.Contains("(abc) is not a number", error.Message);
        }

        [Fact]
        public void A_negative_selling_price_is_an_error()
        {
            var error = OnlyError(Check(Row(price: "-5")), ImportRowRules.SellingPrice);

            Assert.Contains("below zero", error.Message);
        }

        [Fact]
        public void A_selling_price_of_zero_is_allowed()
        {
            Assert.Empty(Check(Row(price: "0")).Errors);
        }

        // ---- Quantity and unit cost ----

        [Fact]
        public void A_quantity_without_a_unit_cost_is_an_error_in_plain_words()
        {
            var error = OnlyError(Check(Row(quantity: "5")), ImportRowRules.UnitCost);

            Assert.Equal(
                "Row 7: there is a quantity (5) but no unit cost. Salio needs to know what the stock cost you, "
                + "or your profit will look bigger than it is. Add the unit cost, or clear the quantity.",
                error.Message);
        }

        [Fact]
        public void A_quantity_with_a_unit_cost_of_zero_is_an_error()
        {
            var error = OnlyError(Check(Row(cost: "0", quantity: "5")), ImportRowRules.UnitCost);

            Assert.Contains("a unit cost of zero", error.Message);
        }

        [Fact]
        public void A_quantity_of_zero_needs_no_cost()
        {
            var result = Check(Row(quantity: "0"));

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.Row!.Quantity);
        }

        [Fact]
        public void A_quantity_that_is_not_a_whole_number_is_an_error()
        {
            var error = OnlyError(Check(Row(cost: "100", quantity: "2.5")), ImportRowRules.Quantity);

            Assert.Contains("not a whole number", error.Message);
        }

        [Fact]
        public void A_quantity_written_as_five_point_zero_is_still_five()
        {
            var result = Check(Row(cost: "100", quantity: "5.0"));

            Assert.Empty(result.Errors);
            Assert.Equal(5, result.Row!.Quantity);
        }

        [Fact]
        public void A_negative_quantity_is_an_error()
        {
            var error = OnlyError(Check(Row(cost: "100", quantity: "-3")), ImportRowRules.Quantity);

            Assert.Contains("below zero", error.Message);
        }

        [Fact]
        public void A_negative_unit_cost_is_an_error()
        {
            var error = OnlyError(Check(Row(cost: "-100")), ImportRowRules.UnitCost);

            Assert.Contains("below zero", error.Message);
        }

        [Fact]
        public void Several_problems_on_one_row_are_all_reported()
        {
            var result = Check(Row(sku: "", name: "", price: "abc", quantity: "5"));

            Assert.Equal(4, result.Errors.Count);
            Assert.Null(result.Row);
        }

        // ---- Warnings ----

        [Fact]
        public void A_selling_price_below_unit_cost_is_a_warning_not_an_error()
        {
            var result = Check(Row(price: "100", cost: "150", quantity: "2"));

            Assert.Empty(result.Errors);
            ImportIssue warning = Assert.Single(result.Warnings);
            Assert.Equal(ImportRowRules.SellingPrice, warning.Column);
            Assert.Contains("below the unit cost", warning.Message);
            Assert.NotNull(result.Row);
        }

        [Fact]
        public void A_quantity_for_a_sku_that_exists_is_ignored_with_a_warning()
        {
            var context = new ImportRowContext { SkuAlreadyExists = true };

            var result = Check(Row(quantity: "5"), context);

            Assert.Empty(result.Errors);
            ImportIssue warning = Assert.Single(result.Warnings);
            Assert.Equal(ImportRowRules.Quantity, warning.Column);
            Assert.Contains("use a stock receipt to add stock to a part you already have", warning.Message);
            Assert.Null(result.Row!.Quantity);
            Assert.Null(result.Row.UnitCostMinor);
        }

        [Fact]
        public void A_unit_cost_for_a_sku_that_exists_is_ignored_with_a_warning()
        {
            var context = new ImportRowContext { SkuAlreadyExists = true };

            var result = Check(Row(cost: "200"), context);

            Assert.Empty(result.Errors);
            Assert.Single(result.Warnings);
            Assert.Null(result.Row!.UnitCostMinor);
        }

        [Fact]
        public void An_unknown_column_is_a_warning_and_is_ignored()
        {
            var headers = new List<string> { "SKU", "Name", "SellingPrice", "Colour" };

            var result = ImportRowRules.MapHeaders(headers, 1);

            Assert.Empty(result.Errors);
            ImportIssue warning = Assert.Single(result.Warnings);
            Assert.Equal("Colour", warning.Column);
            Assert.Equal(1, warning.Row);
            Assert.Equal(3, result.Columns.Count);
            Assert.False(result.Columns.ContainsKey(3));
        }

        // ---- Numbers ----

        [Theory]
        [InlineData("1250", 125000)]
        [InlineData("1,250.50", 125050)]
        [InlineData(" 1 250.5 ", 125050)]
        [InlineData("12,345,678", 1234567800)]
        [InlineData("0.99", 99)]
        [InlineData(".5", 50)]
        [InlineData("007", 700)]
        public void Money_accepts_digits_a_full_stop_spaces_and_thousands_commas(string raw, long minor)
        {
            Assert.Equal(ImportRowRules.NumberProblem.None, ImportRowRules.TryParseMoney(raw, out long parsed));
            Assert.Equal(minor, parsed);
        }

        [Theory]
        [InlineData("KES 100", ImportRowRules.NumberProblem.NotANumber)]
        [InlineData("1.2.3", ImportRowRules.NumberProblem.NotANumber)]
        [InlineData("1e5", ImportRowRules.NumberProblem.NotANumber)]
        [InlineData("-10", ImportRowRules.NumberProblem.Negative)]
        [InlineData("10.005", ImportRowRules.NumberProblem.TooManyDecimals)]
        [InlineData("9999999999999", ImportRowRules.NumberProblem.TooLarge)]
        public void Money_refuses_anything_else(string raw, ImportRowRules.NumberProblem problem)
        {
            Assert.Equal(problem, ImportRowRules.TryParseMoney(raw, out _));
        }

        [Fact]
        public void More_than_two_decimals_on_a_price_is_an_error()
        {
            var error = OnlyError(Check(Row(price: "10.005")), ImportRowRules.SellingPrice);

            Assert.Contains("more than two decimal places", error.Message);
        }

        [Fact]
        public void A_quantity_with_thousands_commas_is_read()
        {
            var result = Check(Row(cost: "10", quantity: "1,200"));

            Assert.Equal(1200, result.Row!.Quantity);
        }

        // ---- Headers ----

        [Theory]
        [InlineData("Selling Price")]
        [InlineData("sellingprice")]
        [InlineData("SELLING_PRICE")]
        [InlineData(" selling-price ")]
        public void Headers_match_ignoring_case_spaces_underscores_and_hyphens(string header)
        {
            Assert.Equal(ImportRowRules.SellingPrice, ImportRowRules.MatchHeader(header));
        }

        [Fact]
        public void Every_known_header_is_matched_in_its_own_spelling()
        {
            foreach (string column in ImportRowRules.KnownColumns)
            {
                Assert.Equal(column, ImportRowRules.MatchHeader(column));
            }

            Assert.Equal(ImportRowRules.ReorderLevel, ImportRowRules.MatchHeader("Reorder Level"));
            Assert.Equal(ImportRowRules.UnitCost, ImportRowRules.MatchHeader("unit_cost"));
        }

        [Fact]
        public void A_missing_required_column_is_an_error()
        {
            var result = ImportRowRules.MapHeaders(new List<string> { "SKU", "Name" }, 1);

            ImportIssue error = Assert.Single(result.Errors);
            Assert.Equal(ImportRowRules.SellingPrice, error.Column);
        }

        [Fact]
        public void A_column_given_twice_is_an_error()
        {
            var result = ImportRowRules.MapHeaders(
                new List<string> { "SKU", "Name", "Selling Price", "SELLING_PRICE" }, 1);

            ImportIssue error = Assert.Single(result.Errors);
            Assert.Equal(ImportRowRules.SellingPrice, error.Column);
        }

        // ---- Optional columns ----

        [Fact]
        public void Category_barcode_and_reorder_level_are_read_and_an_empty_cell_clears()
        {
            var cells = Row();
            cells[ImportRowRules.Category] = " Brakes ";
            cells[ImportRowRules.Barcode] = "";
            cells[ImportRowRules.ReorderLevel] = "3";

            var row = Check(cells).Row!;

            Assert.True(row.HasCategory);
            Assert.Equal("Brakes", row.Category);
            Assert.True(row.HasBarcode);
            Assert.Null(row.Barcode);
            Assert.Equal(3, row.ReorderLevel);
        }

        [Fact]
        public void A_reorder_level_that_is_not_a_whole_number_is_an_error()
        {
            var cells = Row();
            cells[ImportRowRules.ReorderLevel] = "2.5";

            OnlyError(Check(cells), ImportRowRules.ReorderLevel);
        }

        [Fact]
        public void The_exports_formula_guard_is_taken_off_again()
        {
            Assert.Equal("=SUM", ImportRowRules.CleanText("'=SUM"));
            Assert.Equal("'quoted", ImportRowRules.CleanText("'quoted"));
        }
    }
}
