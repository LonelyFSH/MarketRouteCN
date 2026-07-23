namespace MarketRouteCN.Models;

public enum ImportTextFormat
{
    Auto = 0,
    PlainText = 1,
    Csv = 2,
    Json = 3,
}

public enum ShoppingListImportMode
{
    Append = 0,
    Replace = 1,
    CreateNew = 2,
}

public sealed class ShoppingListImportResult
{
    public List<ShoppingListEntry> Entries { get; init; } = [];

    public List<string> Errors { get; init; } = [];

    public int ParsedRows { get; init; }

    public int ValidRows => Entries.Count;
}
