namespace NbTcgTrader.Api.Common.Domain;

/// <summary>One CSV/XLSX upload and its reconciliation progress (CLAUDE.md §9).</summary>
public sealed class ImportJob
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public AppUser? User { get; set; }

    public required string FileName { get; set; }

    public ImportStatus Status { get; set; }

    public int RowsTotal { get; set; }

    public int RowsMatched { get; set; }

    public int RowsUnmatched { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
}
