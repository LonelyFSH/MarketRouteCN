namespace MarketRouteCN.Services;

public static class DataCenterCatalog
{
    public static readonly string[] ChinaDataCenters =
    [
        "陆行鸟",
        "莫古力",
        "猫小胖",
        "豆豆柴",
    ];

    public static bool IsKnown(string name)
    {
        return ChinaDataCenters.Contains(name, StringComparer.Ordinal);
    }
}
