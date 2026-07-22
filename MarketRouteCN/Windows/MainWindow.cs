using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MarketRouteCN.Models;
using MarketRouteCN.Services;

namespace MarketRouteCN.Windows;

public sealed class MainWindow : Window
{
    private static readonly int[] RefreshOptions = [0, 5, 10, 15, 30, 60];

    private readonly Configuration configuration;
    private readonly ItemCatalogService itemCatalogService;
    private readonly ShoppingListService shoppingListService;
    private readonly PriceRefreshService priceRefreshService;

    private string itemSearchText = string.Empty;
    private IReadOnlyList<ItemSearchResult> searchResults = [];
    private ItemSearchResult? selectedSearchResult;
    private int quantityInput = 1;
    private PurchaseQuality qualityInput = PurchaseQuality.Any;
    private string? routeDataCenter;

    public MainWindow(
        Configuration configuration,
        ItemCatalogService itemCatalogService,
        ShoppingListService shoppingListService,
        PriceRefreshService priceRefreshService)
        : base("MarketRoute CN##MarketRouteCN.Main")
    {
        this.configuration = configuration;
        this.itemCatalogService = itemCatalogService;
        this.shoppingListService = shoppingListService;
        this.priceRefreshService = priceRefreshService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    // 绘制主窗口
    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();

        if (ImGui.BeginTabBar("##MarketRouteCN.Tabs"))
        {
            if (ImGui.BeginTabItem("自定义清单"))
            {
                DrawShoppingListTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("大区报价"))
            {
                DrawComparisonTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("采购路线"))
            {
                DrawRouteTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("设置"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        ImGui.TextUnformatted("FF14 国服全品类批量采购与跨服路线规划");

        if (priceRefreshService.IsRefreshing)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.25f, 1f), "正在查询价格……");
        }

        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is not null)
        {
            ImGui.TextUnformatted($"本次查询时间：{snapshot.QueryTime.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            ImGui.SameLine();
            ImGui.TextDisabled("（市场数据时间请查看各大区与物品详情）");
        }

        if (priceRefreshService.NextRefreshTime is { } nextRefresh)
        {
            var remaining = nextRefresh - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            ImGui.TextUnformatted($"下次自动查询：{remaining:mm\\:ss} 后");
        }

        if (!string.IsNullOrWhiteSpace(priceRefreshService.LastError))
            ImGui.TextColored(new Vector4(0.95f, 0.3f, 0.3f, 1f), priceRefreshService.LastError);
    }

    private void DrawShoppingListTab()
    {
        DrawPurchaseScopeControls();
        ImGui.SeparatorText("添加物品");
        DrawItemSearchEditor();
        ImGui.SeparatorText($"当前清单 · {shoppingListService.Entries.Count} 种物品");
        DrawShoppingListTable();

        ImGui.Spacing();
        var hasItems = shoppingListService.Entries.Count > 0;
        ImGui.BeginDisabled(!hasItems || priceRefreshService.IsRefreshing);
        if (ImGui.Button("查询价格并生成方案", new Vector2(210, 0)))
            priceRefreshService.RequestRefresh();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!hasItems);
        if (ImGui.Button("清空清单"))
            shoppingListService.Clear();
        ImGui.EndDisabled();
    }

    private void DrawPurchaseScopeControls()
    {
        ImGui.SeparatorText("采购范围");

        if (ImGui.RadioButton("单个大区内购齐全部商品", configuration.Scope == PurchaseScope.SingleDataCenter))
        {
            configuration.Scope = PurchaseScope.SingleDataCenter;
            configuration.Save();
            priceRefreshService.ResetAutomaticRefreshSchedule();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("比较四个大区分别购齐整张清单的价格", configuration.Scope == PurchaseScope.CompareAllDataCenters))
        {
            configuration.Scope = PurchaseScope.CompareAllDataCenters;
            configuration.Save();
            priceRefreshService.ResetAutomaticRefreshSchedule();
        }

        if (configuration.Scope == PurchaseScope.SingleDataCenter)
        {
            ImGui.SetNextItemWidth(180);
            DrawDataCenterCombo("目标大区", ref configuration.SelectedDataCenter, saveOnChange: true);
        }
        else
        {
            ImGui.TextDisabled("系统会分别计算陆行鸟、莫古力、猫小胖、豆豆柴在本大区内购齐整张清单的预计总价。玩家最终选择其中一个大区执行路线。");
        }
    }

    // 添加清单物品
    private void DrawItemSearchEditor()
    {
        ImGui.SetNextItemWidth(440);
        var previousText = itemSearchText;
        ImGui.InputTextWithHint(
            "##MarketRouteCN.ItemSearch",
            "输入物品名称中的任意文字……",
            ref itemSearchText,
            128);

        if (!string.Equals(previousText, itemSearchText, StringComparison.Ordinal))
        {
            selectedSearchResult = null;
            searchResults = itemCatalogService.Search(itemSearchText);
        }

        if (searchResults.Count > 0 && selectedSearchResult is null)
        {
            ImGui.BeginChild(
                "##MarketRouteCN.ItemSearchResults",
                new Vector2(440, Math.Min(180, searchResults.Count * 25 + 8)),
                true);

            foreach (var result in searchResults)
            {
                var hqLabel = result.SupportsHighQuality ? " · 支持 HQ" : "";
                if (ImGui.Selectable($"{result.Name}  [#{result.ItemId}]{hqLabel}##{result.ItemId}"))
                {
                    selectedSearchResult = result;
                    itemSearchText = result.Name;
                    qualityInput = PurchaseQuality.Any;
                    searchResults = [];
                }
            }

            ImGui.EndChild();
        }

        if (selectedSearchResult is not null)
        {
            ImGui.TextColored(
                new Vector4(0.35f, 0.85f, 0.45f, 1f),
                $"已选择：{selectedSearchResult.Name}（物品 ID {selectedSearchResult.ItemId}）");
        }
        else if (!string.IsNullOrWhiteSpace(itemSearchText))
        {
            ImGui.TextDisabled("请从检索结果中选择一个有效且可在交易板出售的物品。");
        }

        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("数量", ref quantityInput, 1, 10);
        quantityInput = Math.Clamp(quantityInput, 1, 999_999);

        ImGui.SetNextItemWidth(160);
        DrawQualityCombo(selectedSearchResult?.SupportsHighQuality == true);

        var canAdd = selectedSearchResult is not null && quantityInput > 0;
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button("＋ 加入清单", new Vector2(160, 0)) && selectedSearchResult is not null)
        {
            shoppingListService.Add(
                selectedSearchResult,
                checked((uint)quantityInput),
                qualityInput);

            itemSearchText = string.Empty;
            selectedSearchResult = null;
            searchResults = [];
            quantityInput = 1;
            qualityInput = PurchaseQuality.Any;
        }
        ImGui.EndDisabled();
    }

    private void DrawShoppingListTable()
    {
        if (shoppingListService.Entries.Count == 0)
        {
            ImGui.TextDisabled("清单为空。请先在上方检索并添加物品。");
            return;
        }

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("##MarketRouteCN.ShoppingList", 5, flags))
            return;

        ImGui.TableSetupColumn("物品名称", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGui.TableSetupColumn("物品 ID", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableHeadersRow();

        foreach (var entry in shoppingListService.Entries.ToArray())
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.DisplayName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.ItemId.ToString(CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            var quantity = checked((int)entry.Quantity);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputInt($"##Quantity.{entry.EntryId}", ref quantity, 1, 10))
            {
                quantity = Math.Clamp(quantity, 1, 999_999);
                shoppingListService.UpdateQuantity(entry.EntryId, checked((uint)quantity));
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(GetQualityLabel(entry.Quality, entry.SupportsHighQuality));

            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"删除##{entry.EntryId}"))
                shoppingListService.Remove(entry.EntryId);
        }

        ImGui.EndTable();
    }

    // 显示大区价格对比
    private void DrawComparisonTab()
    {
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null)
        {
            ImGui.TextWrapped("尚未查询价格。请先在“自定义清单”中建立清单并点击“查询价格并生成方案”。");
            return;
        }

        ImGui.TextUnformatted($"本次查询时间：{snapshot.QueryTime.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        if (snapshot.CheapestCompletePlan is { } cheapest && snapshot.Plans.Count > 1)
        {
            ImGui.TextColored(
                new Vector4(0.35f, 0.85f, 0.45f, 1f),
                $"最低完整方案：{cheapest.DataCenterName} · {FormatGil(cheapest.TotalCost)} Gil · {cheapest.ServerCount} 个服务器");
        }

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("##MarketRouteCN.Comparison", 8, flags))
            return;

        ImGui.TableSetupColumn("大区", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("能否购齐", ImGuiTableColumnFlags.WidthFixed, 85);
        ImGui.TableSetupColumn("预计挂单总价", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("服务器数", ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableSetupColumn("目标/购买数量", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableSetupColumn("最新数据", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("最旧数据", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
        {
            if (!snapshot.Plans.TryGetValue(dataCenter, out var plan))
                continue;

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.DataCenterName);

            ImGui.TableNextColumn();
            if (plan.IsComplete)
                ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), "可以购齐");
            else
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), $"{plan.CompletedItems}/{plan.TotalItems}");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(plan.TotalCost));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.ServerCount.ToString(CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{plan.RequiredUnits}/{plan.PurchasedUnits}");

            ImGui.TableNextColumn();
            DrawDataAge(plan.NewestMarketDataTime);

            ImGui.TableNextColumn();
            DrawDataAge(plan.OldestMarketDataTime);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"查看路线##{plan.DataCenterName}"))
                routeDataCenter = plan.DataCenterName;
        }

        ImGui.EndTable();

        ImGui.Spacing();
        ImGui.TextDisabled("价格来自 Universalis 众包数据。本次查询时间与市场数据时间并不相同；市场数据较旧时，游戏内挂单可能已经变化。");
    }

    // 显示服务器采购路线
    private void DrawRouteTab()
    {
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null || snapshot.Plans.Count == 0)
        {
            ImGui.TextDisabled("没有可显示的采购路线。");
            return;
        }

        routeDataCenter ??= snapshot.CheapestCompletePlan?.DataCenterName
                            ?? snapshot.Plans.Keys.FirstOrDefault();

        if (routeDataCenter is null)
            return;

        ImGui.SetNextItemWidth(180);
        DrawAvailablePlanCombo(snapshot, ref routeDataCenter);

        if (!snapshot.Plans.TryGetValue(routeDataCenter, out var plan))
            return;

        ImGui.TextUnformatted($"预计挂单总价：{FormatGil(plan.TotalCost)} Gil");
        ImGui.SameLine();
        ImGui.TextUnformatted($"服务器：{plan.ServerCount}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"完成度：{plan.CompletedItems}/{plan.TotalItems}");

        if (!plan.IsComplete)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), "该大区当前数据无法购齐整张清单，路线只包含可规划的挂单。");
        }

        var stationNumber = 1;
        foreach (var server in plan.ServerPlans)
        {
            if (!ImGui.CollapsingHeader($"第 {stationNumber} 站：{server.WorldName} · {FormatGil(server.TotalCost)} Gil##{server.WorldName}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                stationNumber++;
                continue;
            }

            DrawServerPlan(server);
            stationNumber++;
        }

        ImGui.SeparatorText("缺货或数量不足");
        var incompleteItems = plan.ItemPlans.Where(static item => !item.IsComplete).ToArray();
        if (incompleteItems.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), "该大区当前方案可以购齐全部商品。 ");
        }
        else
        {
            foreach (var item in incompleteItems)
            {
                ImGui.BulletText($"{item.Request.DisplayName}：需要 {item.Request.Quantity}，当前规划 {item.PurchasedQuantity}");
            }
        }
    }

    private static void DrawServerPlan(ServerPurchasePlan server)
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable($"##Server.{server.WorldName}", 6, flags))
            return;

        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 65);
        ImGui.TableSetupColumn("挂单数量", ImGuiTableColumnFlags.WidthFixed, 85);
        ImGui.TableSetupColumn("单价", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("小计", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("市场记录时间", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableHeadersRow();

        foreach (var selected in server.Listings)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(selected.ItemName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(selected.Listing.IsHighQuality ? "HQ" : "NQ");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(selected.Listing.Quantity.ToString(CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(selected.Listing.PricePerUnit));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(selected.Listing.TotalPrice));

            ImGui.TableNextColumn();
            DrawDataAge(selected.Listing.LastReviewTime);
        }

        ImGui.EndTable();
    }

    // 设置价格刷新
    private void DrawSettingsTab()
    {
        ImGui.SeparatorText("价格自动刷新");
        var refreshLabel = configuration.AutoRefreshMinutes == 0
            ? "关闭"
            : $"{configuration.AutoRefreshMinutes} 分钟";

        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("刷新间隔", refreshLabel))
        {
            foreach (var option in RefreshOptions)
            {
                var label = option == 0 ? "关闭" : $"{option} 分钟";
                var selected = configuration.AutoRefreshMinutes == option;
                if (ImGui.Selectable(label, selected))
                {
                    configuration.AutoRefreshMinutes = option;
                    configuration.Save();
                    priceRefreshService.ResetAutomaticRefreshSchedule();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped("自动刷新会重新请求当前模式所需的大区：单大区模式只查询所选大区；四大区对比模式查询四个国服大区。刷新不会保证 Universalis 中的众包市场数据刚刚被玩家上传。");

        ImGui.SeparatorText("数据来源说明");
        ImGui.BulletText("物品名称、可交易状态与 HQ 能力来自当前国服客户端的本地游戏数据。");
        ImGui.BulletText("交易板挂单来自 Universalis API。它是玩家上传的众包数据，不是盛趣或 Square Enix 的实时市场接口。");
        ImGui.BulletText("总价按所选完整挂单计算；由于交易板挂单通常需要整组购买，实际购买数量可能高于目标数量。");
        ImGui.BulletText("V0.1 只规划价格与服务器分组，不自动跨服、不自动打开交易板，也不自动购买。");
    }

    private void DrawQualityCombo(bool supportsHighQuality)
    {
        if (!supportsHighQuality)
        {
            qualityInput = PurchaseQuality.Any;
            ImGui.BeginDisabled();
            if (ImGui.BeginCombo("品质", "不区分品质"))
                ImGui.EndCombo();
            ImGui.EndDisabled();
            return;
        }

        var currentLabel = GetQualityLabel(qualityInput, true);
        if (!ImGui.BeginCombo("品质", currentLabel))
            return;

        foreach (var quality in Enum.GetValues<PurchaseQuality>())
        {
            var selected = qualityInput == quality;
            if (ImGui.Selectable(GetQualityLabel(quality, true), selected))
                qualityInput = quality;

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static string GetQualityLabel(PurchaseQuality quality, bool supportsHighQuality)
    {
        if (!supportsHighQuality)
            return "—";

        return quality switch
        {
            PurchaseQuality.HighQuality => "HQ",
            PurchaseQuality.NormalQuality => "NQ",
            _ => "任意",
        };
    }

    private void DrawDataCenterCombo(string label, ref string selectedDataCenter, bool saveOnChange)
    {
        if (!ImGui.BeginCombo(label, selectedDataCenter))
            return;

        foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
        {
            var selected = string.Equals(selectedDataCenter, dataCenter, StringComparison.Ordinal);
            if (ImGui.Selectable(dataCenter, selected))
            {
                selectedDataCenter = dataCenter;
                if (saveOnChange)
                {
                    configuration.Save();
                    priceRefreshService.ResetAutomaticRefreshSchedule();
                }
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawAvailablePlanCombo(PriceComparisonSnapshot snapshot, ref string selectedDataCenter)
    {
        if (!ImGui.BeginCombo("大区路线", selectedDataCenter))
            return;

        foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
        {
            if (!snapshot.Plans.ContainsKey(dataCenter))
                continue;

            var selected = string.Equals(selectedDataCenter, dataCenter, StringComparison.Ordinal);
            if (ImGui.Selectable(dataCenter, selected))
                selectedDataCenter = dataCenter;

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawDataAge(DateTimeOffset? dataTime)
    {
        if (dataTime is null)
        {
            ImGui.TextDisabled("无数据");
            return;
        }

        var age = DateTimeOffset.UtcNow - dataTime.Value;
        var color = age switch
        {
            { TotalMinutes: <= 15 } => new Vector4(0.35f, 0.85f, 0.45f, 1f),
            { TotalHours: <= 2 } => new Vector4(0.95f, 0.8f, 0.25f, 1f),
            { TotalHours: <= 12 } => new Vector4(0.95f, 0.55f, 0.2f, 1f),
            _ => new Vector4(0.95f, 0.3f, 0.3f, 1f),
        };

        ImGui.TextColored(color, FormatAge(age));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(dataTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (age.TotalMinutes < 1)
            return "刚刚";

        if (age.TotalHours < 1)
            return $"{(int)age.TotalMinutes} 分钟前";

        if (age.TotalDays < 1)
            return $"{(int)age.TotalHours} 小时前";

        return $"{(int)age.TotalDays} 天前";
    }

    private static string FormatGil(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
