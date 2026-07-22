using Dalamud.Configuration;
using Dalamud.Plugin;
using MarketRouteCN.Models;

namespace MarketRouteCN;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 1;

    public PurchaseScope Scope { get; set; } = PurchaseScope.CompareAllDataCenters;

    public string SelectedDataCenter { get; set; } = "陆行鸟";

    public int AutoRefreshMinutes { get; set; } = 10;

    public List<ShoppingListEntry> ShoppingList { get; set; } = [];

    // 读取插件配置
    public void Initialize(IDalamudPluginInterface value)
    {
        pluginInterface = value;
        ShoppingList ??= [];
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
