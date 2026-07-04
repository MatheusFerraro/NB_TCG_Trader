using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Outcome of parsing an uploaded file (BACKLOG #14). Errors are user-facing strings:
/// file-level problems (bad header, too many rows, not parseable) and row-level
/// problems prefixed with the spreadsheet row number, so the user can fix the exact
/// cell. Any error rejects the whole upload — no row is ever silently dropped
/// (AGENTS.md §9).
/// </summary>
public sealed class ImportParseResult
{
    /// <summary>Cap on reported row errors so a fully broken file yields a bounded response.</summary>
    public const int MaxReportedRowErrors = 25;

    private bool _rowErrorsTruncated;

    public List<ImportRow> Rows { get; } = [];

    public List<string> FileErrors { get; } = [];

    public List<string> RowErrors { get; } = [];

    public bool HasErrors => FileErrors.Count > 0 || RowErrors.Count > 0;

    public void AddFileError(string message) => FileErrors.Add(message);

    public void AddRowError(int rowNumber, string message)
    {
        if (RowErrors.Count >= MaxReportedRowErrors)
        {
            if (!_rowErrorsTruncated)
            {
                _rowErrorsTruncated = true;
                RowErrors.Add(
                    $"More than {MaxReportedRowErrors} rows have errors; " +
                    "fix the rows reported above and upload again.");
            }

            return;
        }

        RowErrors.Add($"Row {rowNumber}: {message}");
    }
}

/// <summary>
/// Parses an uploaded template file into <see cref="ImportRow"/>s: CsvHelper for
/// <c>.csv</c>, ClosedXML for <c>.xlsx</c> (AGENTS.md §3/§9). Both formats share the
/// same header contract (<see cref="ImportTemplate.Header"/>, any order,
/// case-insensitive, columns other than <c>card_name</c> optional) and the same
/// per-field rules, so a spreadsheet saved either way behaves identically. Rows come
/// out <see cref="MatchStatus.Unmatched"/>; catalog matching is a separate step (#15).
/// </summary>
public static class ImportFileParser
{
    public static ImportParseResult ParseCsv(Stream stream, int maxRows)
    {
        var result = new ImportParseResult();

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = args =>
                result.AddRowError(args.Context.Parser!.Row, "the row is not valid CSV."),
        };

        try
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            using var csv = new CsvReader(reader, configuration);

            if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 })
            {
                result.AddFileError("The file is empty.");
                return result;
            }

            var columns = MapHeader(csv.HeaderRecord, result);
            if (columns is null)
            {
                return result;
            }

            var dataRows = 0;
            while (csv.Read())
            {
                var rowNumber = csv.Parser.Row;
                var record = ReadRecord(rowNumber, columns, csv.GetField);

                if (record.IsEmpty)
                {
                    continue;
                }

                if (++dataRows > maxRows)
                {
                    AddRowCapError(result, maxRows);
                    return result;
                }

                MapRecord(record, result);
            }
        }
        catch (CsvHelperException)
        {
            result.AddFileError("The file could not be parsed as CSV.");
            return result;
        }

        RequireDataRows(result);
        return result;
    }

    public static ImportParseResult ParseXlsx(Stream stream, int maxRows)
    {
        var result = new ImportParseResult();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            // ClosedXML throws a mix of exception types for corrupt/non-zip input;
            // they all mean the same thing to the user.
            result.AddFileError("The file is not a valid .xlsx workbook.");
            return result;
        }

        using (workbook)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault();
            var range = worksheet?.RangeUsed();
            if (worksheet is null || range is null)
            {
                result.AddFileError("The file is empty.");
                return result;
            }

            var headerRow = range.FirstRow();
            var headerCells = new string?[range.ColumnCount()];
            for (var i = 0; i < headerCells.Length; i++)
            {
                headerCells[i] = CellText(headerRow.Cell(i + 1));
            }

            var columns = MapHeader(headerCells, result);
            if (columns is null)
            {
                return result;
            }

            var dataRows = 0;
            foreach (var row in range.Rows().Skip(1))
            {
                var record = ReadRecord(
                    row.RowNumber(), columns, index => CellText(row.Cell(index + 1)));

                if (record.IsEmpty)
                {
                    continue;
                }

                if (++dataRows > maxRows)
                {
                    AddRowCapError(result, maxRows);
                    return result;
                }

                MapRecord(record, result);
            }
        }

        RequireDataRows(result);
        return result;
    }

    /// <summary>A data row's cells as raw text, before any typing/validation.</summary>
    private sealed record RawImportRecord(
        int RowNumber,
        string? CardName,
        string? Set,
        string? CardNumber,
        string? Quantity,
        string? Condition,
        string? Price,
        string? ForSale)
    {
        public bool IsEmpty =>
            CardName is null && Set is null && CardNumber is null && Quantity is null
            && Condition is null && Price is null && ForSale is null;
    }

    /// <summary>
    /// Maps header cells to template columns (case-insensitive, any order). Unknown or
    /// duplicated columns and a missing <c>card_name</c> are file errors (§15: reject
    /// unexpected fields) — returns null so parsing stops before any rows are read.
    /// </summary>
    private static Dictionary<string, int>? MapHeader(
        IReadOnlyList<string?> headerCells, ImportParseResult result)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();

        for (var index = 0; index < headerCells.Count; index++)
        {
            var name = headerCells[index]?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!ImportTemplate.Header.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                unknown.Add(name);
            }
            else if (!columns.TryAdd(name, index))
            {
                result.AddFileError($"The column '{name.ToLowerInvariant()}' appears more than once.");
            }
        }

        if (unknown.Count > 0)
        {
            result.AddFileError(
                $"Unknown column(s): {string.Join(", ", unknown)}. " +
                $"Expected columns: {string.Join(", ", ImportTemplate.Header)}.");
        }

        if (!columns.ContainsKey(ImportTemplate.Columns.CardName))
        {
            result.AddFileError(
                $"The required column '{ImportTemplate.Columns.CardName}' is missing.");
        }

        return result.HasErrors ? null : columns;
    }

    private static RawImportRecord ReadRecord(
        int rowNumber, Dictionary<string, int> columns, Func<int, string?> cellAt)
    {
        string? Get(string column) =>
            columns.TryGetValue(column, out var index) ? Normalize(cellAt(index)) : null;

        return new RawImportRecord(
            rowNumber,
            Get(ImportTemplate.Columns.CardName),
            Get(ImportTemplate.Columns.Set),
            Get(ImportTemplate.Columns.CardNumber),
            Get(ImportTemplate.Columns.Quantity),
            Get(ImportTemplate.Columns.Condition),
            Get(ImportTemplate.Columns.Price),
            Get(ImportTemplate.Columns.ForSale));
    }

    /// <summary>
    /// Types and validates one raw record. Field bounds mirror the ImportRow columns
    /// (RawName/RawSet 200, RawNumber 50, numeric(18,2) price) and the collection
    /// slice's rules (quantity 1–999, price positive with max 2 decimals, price
    /// required when for sale), so an imported row can always become a binder item.
    /// </summary>
    private static void MapRecord(RawImportRecord record, ImportParseResult result)
    {
        // Collected locally: the row is valid only if this list stays empty. (The
        // result's reported-error cap only bounds messages, never validity.)
        var errors = new List<string>();

        if (record.CardName is null)
        {
            errors.Add("card_name is required.");
        }
        else if (record.CardName.Length > 200)
        {
            errors.Add("card_name must be 200 characters or fewer.");
        }

        if (record.Set is { Length: > 200 })
        {
            errors.Add("set must be 200 characters or fewer.");
        }

        if (record.CardNumber is { Length: > 50 })
        {
            errors.Add("card_number must be 50 characters or fewer.");
        }

        var quantity = 1;
        if (record.Quantity is not null
            && (!int.TryParse(record.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out quantity)
                || quantity is < 1 or > 999))
        {
            errors.Add("quantity must be a whole number between 1 and 999.");
        }

        CardCondition? condition = null;
        if (record.Condition is not null)
        {
            var match = Enum.GetValues<CardCondition>()
                .Cast<CardCondition?>()
                .FirstOrDefault(c => string.Equals(
                    c!.ToString(), record.Condition, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                errors.Add(
                    $"condition must be one of {string.Join(", ", Enum.GetNames<CardCondition>())}.");
            }

            condition = match;
        }

        decimal? price = null;
        if (record.Price is not null)
        {
            if (decimal.TryParse(
                    record.Price,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                && parsed > 0
                && decimal.Round(parsed, 2) == parsed)
            {
                price = parsed;
            }
            else
            {
                errors.Add("price must be a positive number with at most 2 decimals, e.g. 12.50.");
            }
        }

        var forSale = false;
        if (record.ForSale is not null && !TryParseForSale(record.ForSale, out forSale))
        {
            errors.Add("for_sale must be true or false.");
        }

        // Same soft rule as editing a binder item (#12): a listing needs an asking price.
        if (forSale && price is null && errors.Count == 0)
        {
            errors.Add("price is required when for_sale is true.");
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                result.AddRowError(record.RowNumber, error);
            }

            return;
        }

        result.Rows.Add(new ImportRow
        {
            RawName = record.CardName!,
            RawSet = record.Set,
            RawNumber = record.CardNumber,
            Quantity = quantity,
            Price = price,
            Condition = condition,
            IsForSale = forSale,
            MatchStatus = MatchStatus.Unmatched,
        });
    }

    private static bool TryParseForSale(string value, out bool forSale)
    {
        switch (value.ToLowerInvariant())
        {
            case "true" or "1" or "yes":
                forSale = true;
                return true;
            case "false" or "0" or "no":
                forSale = false;
                return true;
            default:
                forSale = false;
                return false;
        }
    }

    private static void AddRowCapError(ImportParseResult result, int maxRows) =>
        result.AddFileError(
            $"The file has more than {maxRows} data rows; split it and upload in parts.");

    private static void RequireDataRows(ImportParseResult result)
    {
        if (!result.HasErrors && result.Rows.Count == 0)
        {
            result.AddFileError("The file contains no data rows.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Cell text via explicit per-type conversion with the invariant culture, so a
    /// numeric price cell reads "12.5" regardless of the server locale and a boolean
    /// cell reads "true"/"false".
    /// </summary>
    private static string? CellText(IXLCell cell)
    {
        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => value.GetBoolean() ? "true" : "false",
            XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.Text => value.GetText(),
            XLDataType.DateTime => value.GetDateTime().ToString(CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => value.GetTimeSpan().ToString(),
            _ => null,
        };
    }
}
