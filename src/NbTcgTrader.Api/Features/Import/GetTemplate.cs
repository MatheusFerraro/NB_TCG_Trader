using System.Text;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// The CSV import contract (AGENTS.md §9, BACKLOG #13): the template users download,
/// fill in, and upload. The column names live here so the upload parser (#14) matches
/// against the same constants the template serves — the two can never drift apart.
/// Only <c>card_name</c> is required; everything else is best-effort matching input.
/// </summary>
public static class ImportTemplate
{
    public const string FileName = "nb-tcg-import-template.csv";
    public const string ContentType = "text/csv";

    public static class Columns
    {
        public const string CardName = "card_name";
        public const string Set = "set";
        public const string CardNumber = "card_number";
        public const string Quantity = "quantity";
        public const string Condition = "condition";
        public const string Price = "price";
        public const string ForSale = "for_sale";
    }

    public static readonly IReadOnlyList<string> Header =
    [
        Columns.CardName,
        Columns.Set,
        Columns.CardNumber,
        Columns.Quantity,
        Columns.Condition,
        Columns.Price,
        Columns.ForSale,
    ];

    /// <summary>
    /// The template body: the header row only, CRLF-terminated per RFC 4180. No cell
    /// starts with <c>=</c> <c>+</c> <c>-</c> <c>@</c>, so no CSV-injection escaping
    /// is needed here (AGENTS.md §15); any future export of user data must escape.
    /// </summary>
    public static string Csv { get; } = string.Join(',', Header) + "\r\n";

    /// <summary>UTF-8 bytes served by the endpoint, encoded once.</summary>
    private static readonly byte[] CsvBytes = Encoding.UTF8.GetBytes(Csv);

    /// <summary>Returns a fresh byte array so callers cannot mutate the cached template.</summary>
    public static byte[] GetCsvBytes() => [.. CsvBytes];
}
