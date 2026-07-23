using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed partial class ShoppingListTransferService
{
    private readonly ItemCatalogService itemCatalogService;

    public ShoppingListTransferService(ItemCatalogService itemCatalogService)
    {
        this.itemCatalogService = itemCatalogService;
    }

    public ShoppingListImportResult Parse(string text, ImportTextFormat format)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ShoppingListImportResult { Errors = ["没有可导入的内容。"] };

        var resolvedFormat = format == ImportTextFormat.Auto ? DetectFormat(text) : format;
        return resolvedFormat switch
        {
            ImportTextFormat.Json => ParseJson(text),
            ImportTextFormat.Csv => ParseDelimited(text),
            _ => ParsePlainText(text),
        };
    }

    public string ExportCsv(IEnumerable<ShoppingListEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("itemId,name,quantity,quality");
        foreach (var entry in entries)
        {
            builder.Append(entry.ItemId.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(EscapeCsv(entry.DisplayName));
            builder.Append(',');
            builder.Append(entry.Quantity.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.AppendLine(GetQualityText(entry.Quality));
        }
        return builder.ToString();
    }

    public string ExportJson(IEnumerable<ShoppingListEntry> entries)
    {
        var values = entries.Select(static entry => new TransferEntry
        {
            ItemId = entry.ItemId,
            Name = entry.DisplayName,
            Quantity = entry.Quantity,
            Quality = GetQualityText(entry.Quality),
        });
        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
    }

    private ShoppingListImportResult ParseJson(string text)
    {
        try
        {
            var values = JsonSerializer.Deserialize<List<TransferEntry>>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? [];

            var entries = new List<ShoppingListEntry>();
            var errors = new List<string>();
            for (var index = 0; index < values.Count; index++)
                Resolve(values[index], index + 1, entries, errors);

            return BuildResult(values.Count, entries, errors);
        }
        catch (JsonException exception)
        {
            return new ShoppingListImportResult { Errors = [$"JSON 解析失败：{exception.Message}"] };
        }
    }

    private ShoppingListImportResult ParseDelimited(string text)
    {
        var lines = SplitLines(text);
        var entries = new List<ShoppingListEntry>();
        var errors = new List<string>();
        var parsed = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
                continue;
            if (index == 0 && line.Contains("quantity", StringComparison.OrdinalIgnoreCase))
                continue;

            parsed++;
            var cells = SplitDelimitedLine(line);
            if (cells.Count < 2)
            {
                errors.Add($"第 {index + 1} 行无法识别。 ");
                continue;
            }

            var transfer = new TransferEntry();
            if (uint.TryParse(cells[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
            {
                transfer.ItemId = itemId;
                transfer.Name = cells.Count > 1 ? cells[1] : string.Empty;
                transfer.Quantity = cells.Count > 2 && uint.TryParse(cells[2], out var quantity) ? quantity : 1;
                transfer.Quality = cells.Count > 3 ? cells[3] : "任意";
            }
            else
            {
                transfer.Name = cells[0];
                transfer.Quantity = cells.Count > 1 && uint.TryParse(cells[1], out var quantity) ? quantity : 1;
                transfer.Quality = cells.Count > 2 ? cells[2] : "任意";
            }
            Resolve(transfer, index + 1, entries, errors);
        }

        return BuildResult(parsed, entries, errors);
    }

    private ShoppingListImportResult ParsePlainText(string text)
    {
        var lines = SplitLines(text);
        var entries = new List<ShoppingListEntry>();
        var errors = new List<string>();
        var parsed = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
                continue;

            parsed++;
            var match = PlainLinePattern().Match(line);
            var transfer = new TransferEntry();
            if (match.Success)
            {
                transfer.Name = match.Groups["name"].Value.Trim();
                transfer.Quantity = uint.TryParse(match.Groups["quantity"].Value, out var quantity) ? quantity : 1;
                transfer.Quality = match.Groups["quality"].Success ? match.Groups["quality"].Value : "任意";
            }
            else
            {
                transfer.Name = line;
                transfer.Quantity = 1;
                transfer.Quality = "任意";
            }
            Resolve(transfer, index + 1, entries, errors);
        }

        return BuildResult(parsed, entries, errors);
    }

    private void Resolve(TransferEntry transfer, int lineNumber, List<ShoppingListEntry> entries, List<string> errors)
    {
        ItemSearchResult? item = null;
        if (transfer.ItemId > 0 && itemCatalogService.TryGetById(transfer.ItemId, out var byId))
            item = byId;
        else if (!string.IsNullOrWhiteSpace(transfer.Name) && itemCatalogService.TryGetExactName(transfer.Name, out var byName))
            item = byName;

        if (item is null)
        {
            errors.Add($"第 {lineNumber} 行未找到可交易物品：{transfer.Name}");
            return;
        }

        var quantity = transfer.Quantity == 0 ? 1u : transfer.Quantity;
        var quality = ParseQuality(transfer.Quality);
        if (!item.SupportsHighQuality)
            quality = PurchaseQuality.Any;

        entries.Add(new ShoppingListEntry
        {
            EntryId = Guid.NewGuid(),
            ItemId = item.ItemId,
            DisplayName = item.Name,
            Quantity = quantity,
            Quality = quality,
            SupportsHighQuality = item.SupportsHighQuality,
        });
    }

    private static ShoppingListImportResult BuildResult(int parsedRows, List<ShoppingListEntry> entries, List<string> errors)
    {
        var merged = entries
            .GroupBy(entry => new { entry.ItemId, entry.Quality })
            .Select(group =>
            {
                var first = group.First();
                first.Quantity = checked((uint)group.Sum(static item => (long)item.Quantity));
                return first;
            })
            .ToList();

        return new ShoppingListImportResult
        {
            ParsedRows = parsedRows,
            Entries = merged,
            Errors = errors,
        };
    }

    private static ImportTextFormat DetectFormat(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == '[' || trimmed[0] == '{'))
            return ImportTextFormat.Json;
        if (trimmed.Contains(',') || trimmed.Contains('\t'))
            return ImportTextFormat.Csv;
        return ImportTextFormat.PlainText;
    }

    private static PurchaseQuality ParseQuality(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized switch
        {
            "HQ" or "高品质" => PurchaseQuality.HighQuality,
            "NQ" or "普通品质" => PurchaseQuality.NormalQuality,
            _ => PurchaseQuality.Any,
        };
    }

    private static string GetQualityText(PurchaseQuality quality)
    {
        return quality switch
        {
            PurchaseQuality.HighQuality => "HQ",
            PurchaseQuality.NormalQuality => "NQ",
            _ => "任意",
        };
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static List<string> SplitDelimitedLine(string line)
    {
        if (line.Contains('\t'))
            return line.Split('\t').Select(static item => item.Trim()).ToList();

        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        foreach (var character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (character == ',' && !quoted)
            {
                values.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }
            builder.Append(character);
        }
        values.Add(builder.ToString().Trim());
        return values;
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"'))
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    [GeneratedRegex(@"^(?<name>.+?)(?:\s*[xX×*]\s*|\s+)(?<quantity>\d+)(?:\s+(?<quality>HQ|NQ|任意|高品质|普通品质))?$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainLinePattern();

    private sealed class TransferEntry
    {
        public TransferEntry()
        {
        }

        public uint ItemId { get; set; }

        public string Name { get; set; } = string.Empty;

        public uint Quantity { get; set; } = 1;

        public string Quality { get; set; } = "任意";
    }
}
