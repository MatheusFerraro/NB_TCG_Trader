using System.Text;
using ClosedXML.Excel;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Features.Import;
using Shouldly;

namespace NbTcgTrader.Tests;

// Unit coverage of the import parsing/typing logic (BACKLOG #14): header contract,
// per-field rules shared by CSV and XLSX, row cap, and the clear-errors AC. Pure
// in-memory streams — no host, network, or database.
public sealed class ImportFileParserTests
{
    private const int MaxRows = 1000;

    private static Stream Csv(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    [Fact]
    public void Csv_with_all_columns_parses_typed_rows()
    {
        using var stream = Csv(
            "card_name,set,card_number,quantity,condition,price,for_sale",
            "Charizard,Base,4,2,LP,49.99,true",
            "Machamp,Base,8,1,NM,,false");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.HasErrors.ShouldBeFalse();
        result.Rows.Count.ShouldBe(2);

        var first = result.Rows[0];
        first.RawName.ShouldBe("Charizard");
        first.RawSet.ShouldBe("Base");
        first.RawNumber.ShouldBe("4");
        first.Quantity.ShouldBe(2);
        first.Condition.ShouldBe(CardCondition.LP);
        first.Price.ShouldBe(49.99m);
        first.IsForSale.ShouldBeTrue();
        first.MatchStatus.ShouldBe(MatchStatus.Unmatched);

        var second = result.Rows[1];
        second.Price.ShouldBeNull();
        second.IsForSale.ShouldBeFalse();
    }

    [Fact]
    public void Csv_with_only_card_name_column_defaults_the_optionals()
    {
        using var stream = Csv("card_name", "Charizard");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.HasErrors.ShouldBeFalse();
        var row = result.Rows.ShouldHaveSingleItem();
        row.RawName.ShouldBe("Charizard");
        row.Quantity.ShouldBe(1);
        row.Condition.ShouldBeNull();
        row.Price.ShouldBeNull();
        row.IsForSale.ShouldBeFalse();
    }

    [Fact]
    public void Csv_header_is_case_insensitive_and_order_free()
    {
        using var stream = Csv(
            "FOR_SALE,Price,Card_Name,quantity",
            "true,10.00,Charizard,3");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.HasErrors.ShouldBeFalse();
        var row = result.Rows.ShouldHaveSingleItem();
        row.RawName.ShouldBe("Charizard");
        row.Quantity.ShouldBe(3);
        row.Price.ShouldBe(10.00m);
        row.IsForSale.ShouldBeTrue();
    }

    [Fact]
    public void Csv_skips_blank_rows()
    {
        using var stream = Csv(
            "card_name,quantity",
            "Charizard,1",
            ",",
            "Machamp,2");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.HasErrors.ShouldBeFalse();
        result.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public void Csv_without_card_name_column_is_a_file_error()
    {
        using var stream = Csv("set,quantity", "Base,1");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        result.FileErrors.ShouldContain(e => e.Contains("card_name"));
    }

    [Fact]
    public void Csv_with_unknown_column_is_a_file_error()
    {
        using var stream = Csv("card_name,language", "Charizard,en");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        result.FileErrors.ShouldContain(e => e.Contains("language"));
    }

    [Fact]
    public void Csv_with_duplicated_column_is_a_file_error()
    {
        using var stream = Csv("card_name,card_name", "Charizard,Blastoise");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        result.FileErrors.ShouldContain(e => e.Contains("more than once"));
    }

    [Fact]
    public void Empty_csv_is_a_file_error()
    {
        using var stream = new MemoryStream();

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.FileErrors.ShouldContain(e => e.Contains("empty"));
    }

    [Fact]
    public void Csv_with_headers_only_reports_no_data_rows()
    {
        using var stream = Csv("card_name,set,card_number,quantity,condition,price,for_sale");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.FileErrors.ShouldContain(e => e.Contains("no data rows"));
    }

    [Fact]
    public void Csv_parser_leaves_the_caller_stream_open()
    {
        using var stream = Csv("card_name", "Charizard");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.HasErrors.ShouldBeFalse();
        stream.CanRead.ShouldBeTrue();
        stream.Position = 0;
        using var reader = new StreamReader(stream, leaveOpen: true);
        reader.ReadLine().ShouldBe("card_name");
    }

    [Theory]
    [InlineData("Charizard,0,,,", "quantity")]
    [InlineData("Charizard,1000,,,", "quantity")]
    [InlineData("Charizard,two,,,", "quantity")]
    [InlineData("Charizard,1,EX,,", "condition")]
    [InlineData("Charizard,1,,free,", "price")]
    [InlineData("Charizard,1,,-5,", "price")]
    [InlineData("Charizard,1,,9.999,", "price")]
    [InlineData("Charizard,1,,,maybe", "for_sale")]
    [InlineData(",1,,,", "card_name")]
    public void Invalid_field_values_are_row_errors_with_the_row_number(
        string dataRow, string expectedField)
    {
        using var stream = Csv("card_name,quantity,condition,price,for_sale", dataRow);

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        var error = result.RowErrors.ShouldHaveSingleItem();
        error.ShouldStartWith("Row 2:");
        error.ShouldContain(expectedField);
    }

    [Fact]
    public void For_sale_without_price_is_a_row_error()
    {
        using var stream = Csv("card_name,for_sale", "Charizard,true");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        var error = result.RowErrors.ShouldHaveSingleItem();
        error.ShouldContain("price is required when for_sale is true");
    }

    [Fact]
    public void One_bad_row_rejects_the_file_but_reports_only_that_row()
    {
        using var stream = Csv(
            "card_name,quantity",
            "Charizard,1",
            "Machamp,zero",
            "Blastoise,2");

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.HasErrors.ShouldBeTrue();
        var error = result.RowErrors.ShouldHaveSingleItem();
        error.ShouldStartWith("Row 3:");
        // Valid rows still parse — the handler rejects the upload as a whole.
        result.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public void Row_errors_are_capped_but_still_reject_every_bad_row()
    {
        var lines = new List<string> { "card_name,quantity" };
        for (var i = 0; i < ImportParseResult.MaxReportedRowErrors + 10; i++)
        {
            lines.Add($"Card {i},bad");
        }

        using var stream = Csv([.. lines]);

        var result = ImportFileParser.ParseCsv(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        result.RowErrors.Count.ShouldBe(ImportParseResult.MaxReportedRowErrors + 1);
        result.RowErrors[^1].ShouldContain("More than");
    }

    [Fact]
    public void Csv_over_the_row_cap_is_a_file_error()
    {
        using var stream = Csv(
            "card_name",
            "Charizard",
            "Machamp",
            "Blastoise");

        var result = ImportFileParser.ParseCsv(stream, maxRows: 2);

        result.FileErrors.ShouldContain(e => e.Contains("more than 2 data rows"));
    }

    [Fact]
    public void Xlsx_parses_typed_rows()
    {
        using var stream = Xlsx(sheet =>
        {
            sheet.Cell(1, 1).Value = "card_name";
            sheet.Cell(1, 2).Value = "quantity";
            sheet.Cell(1, 3).Value = "price";
            sheet.Cell(1, 4).Value = "for_sale";
            sheet.Cell(2, 1).Value = "Charizard";
            sheet.Cell(2, 2).Value = 2;          // numeric cell
            sheet.Cell(2, 3).Value = 49.99;      // numeric cell
            sheet.Cell(2, 4).Value = true;       // boolean cell
        });

        var result = ImportFileParser.ParseXlsx(stream, MaxRows);

        result.HasErrors.ShouldBeFalse();
        var row = result.Rows.ShouldHaveSingleItem();
        row.RawName.ShouldBe("Charizard");
        row.Quantity.ShouldBe(2);
        row.Price.ShouldBe(49.99m);
        row.IsForSale.ShouldBeTrue();
    }

    [Fact]
    public void Xlsx_without_card_name_column_is_a_file_error()
    {
        using var stream = Xlsx(sheet =>
        {
            sheet.Cell(1, 1).Value = "set";
            sheet.Cell(2, 1).Value = "Base";
        });

        var result = ImportFileParser.ParseXlsx(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        result.FileErrors.ShouldContain(e => e.Contains("card_name"));
    }

    [Fact]
    public void Xlsx_row_errors_carry_the_worksheet_row_number()
    {
        using var stream = Xlsx(sheet =>
        {
            sheet.Cell(1, 1).Value = "card_name";
            sheet.Cell(1, 2).Value = "quantity";
            sheet.Cell(2, 1).Value = "Charizard";
            sheet.Cell(2, 2).Value = 1;
            sheet.Cell(3, 1).Value = "Machamp";
            sheet.Cell(3, 2).Value = "zero";
        });

        var result = ImportFileParser.ParseXlsx(stream, MaxRows);

        var error = result.RowErrors.ShouldHaveSingleItem();
        error.ShouldStartWith("Row 3:");
    }

    [Fact]
    public void Non_xlsx_bytes_are_a_file_error()
    {
        using var stream = Csv("card_name", "Charizard");

        var result = ImportFileParser.ParseXlsx(stream, MaxRows);

        result.Rows.ShouldBeEmpty();
        result.FileErrors.ShouldContain(e => e.Contains("not a valid .xlsx"));
    }

    [Fact]
    public void Empty_xlsx_is_a_file_error()
    {
        using var stream = Xlsx(_ => { });

        var result = ImportFileParser.ParseXlsx(stream, MaxRows);

        result.FileErrors.ShouldContain(e => e.Contains("empty"));
    }

    private static Stream Xlsx(Action<IXLWorksheet> fill)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Cards");
        fill(sheet);

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
