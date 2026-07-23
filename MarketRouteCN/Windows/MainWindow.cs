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
    private readonly QuoteHistoryService quoteHistoryService;
    private readonly PriceRefreshService priceRefreshService;
    private readonly PurchaseSessionService purchaseSessionService;

    private string itemSearchText = string.Empty;
    private IReadOnlyList<ItemSearchResult> searchResults = [];
    private ItemSearchResult? selectedSearchResult;
    private int quantityInput = 1;
    private PurchaseQuality qualityInput = PurchaseQuality.Any;
    private string? routeDataCenter;
    private string newListName = "新采购清单";
    private string renameListName = string.Empty;

    public MainWindow(
        Configuration configuration,
        ItemCatalogService itemCatalogService,
        ShoppingListService shoppingListService,
        QuoteHistoryService quoteHistoryService,
        PriceRefreshService priceRefreshService,
        PurchaseSessionService purchaseSessionService)
        : base("MarketRoute CN##MarketRouteCN.Main")
    {
        this.configuration = configuration;
        this.itemCatalogService = itemCatalogService;
        this.shoppingListService = shoppingListService;
        this.quoteHistoryService = quoteHistoryService;
        this.priceRefreshService = priceRefreshService;
        this.purchaseSessionService = purchaseSessionService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(900, 620),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##MarketRouteCN.Tabs"))
            return;

        if (ImGui.BeginTabItem("清单"))
        {
            DrawShoppingListTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("报价"))
        {
            DrawComparisonTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("路线"))
        {
            DrawRouteTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("采购中"))
        {
            DrawPurchaseSessionTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("历史"))
        {
            DrawHistoryTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("设置"))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawHeader()
    {
        ImGui.TextUnformatted("FF14 国服全品类批量采购规划");
        ImGui.SameLine();
        ImGui.TextDisabled($"当前清单：{shoppingListService.ActiveList.Name}");

        if (priceRefreshService.IsRefreshing)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.25f, 1f), "正在查询与优化路线");
            ImGui.SameLine();
            if (ImGui.SmallButton("取消查询"))
                priceRefreshService.CancelRefresh();
        }

        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is not null)
        {
            ImGui.TextUnformatted($"请求时间：{snapshot.RequestedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            ImGui.SameLine();
            ImGui.TextUnformatted($"完成时间：{snapshot.CompletedAt.ToLocalTime():HH:mm:ss}");
        }

        if (priceRefreshService.NextRefreshTime is { } nextRefresh)
        {
            var remaining = nextRefresh - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            ImGui.SameLine();
            ImGui.TextDisabled($"下次自动刷新 {remaining:mm\\:ss}");
        }

        if (!string.IsNullOrWhiteSpace(priceRefreshService.LastError))
            ImGui.TextColored(new Vector4(0.95f, 0.3f, 0.3f, 1f), priceRefreshService.LastError);
    }

    private void DrawShoppingListTab()
    {
        DrawSavedListControls();
        DrawPurchaseScopeControls();
        DrawSectionHeader("添加物品");
        DrawItemSearchEditor();
        DrawSectionHeader($"当前清单 {shoppingListService.Entries.Count} 种物品");
        DrawShoppingListTable();

        ImGui.Spacing();
        var hasItems = shoppingListService.Entries.Count > 0;
        ImGui.BeginDisabled(!hasItems || priceRefreshService.IsRefreshing);
        if (ImGui.Button("查询价格并生成方案", new Vector2(220, 0)))
        {
            routeDataCenter = null;
            priceRefreshService.RequestRefresh();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!hasItems);
        if (ImGui.Button("清空当前清单"))
            shoppingListService.Clear();
        ImGui.EndDisabled();
    }

    private void DrawSavedListControls()
    {
        DrawSectionHeader("采购清单");

        var active = shoppingListService.ActiveList;
        ImGui.SetNextItemWidth(240);
        if (ImGui.BeginCombo("当前清单", active.Name))
        {
            foreach (var list in shoppingListService.Lists)
            {
                var selected = list.ListId == active.ListId;
                if (ImGui.Selectable($"{list.Name}  {list.Entries.Count} 项##{list.ListId}", selected))
                {
                    shoppingListService.SetActive(list.ListId);
                    renameListName = list.Name;
                    itemSearchText = string.Empty;
                    selectedSearchResult = null;
                    searchResults = [];
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(200);
        ImGui.InputText("新清单名称", ref newListName, 64);
        ImGui.SameLine();
        if (ImGui.Button("新建"))
        {
            var created = shoppingListService.Create(newListName);
            renameListName = created.Name;
        }
        ImGui.SameLine();
        if (ImGui.Button("复制当前"))
        {
            var copy = shoppingListService.DuplicateActive();
            renameListName = copy.Name;
        }

        if (string.IsNullOrWhiteSpace(renameListName))
            renameListName = shoppingListService.ActiveList.Name;
        ImGui.SetNextItemWidth(200);
        ImGui.InputText("重命名", ref renameListName, 64);
        ImGui.SameLine();
        if (ImGui.Button("保存名称"))
            shoppingListService.RenameActive(renameListName);
        ImGui.SameLine();
        if (ImGui.Button("删除当前"))
        {
            shoppingListService.DeleteActive();
            renameListName = shoppingListService.ActiveList.Name;
        }
    }

    private void DrawPurchaseScopeControls()
    {
        DrawSectionHeader("报价范围");

        if (ImGui.RadioButton("只在一个大区内购齐", configuration.Scope == PurchaseScope.SingleDataCenter))
        {
            configuration.Scope = PurchaseScope.SingleDataCenter;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("比较四个大区分别购齐的价格", configuration.Scope == PurchaseScope.CompareAllDataCenters))
        {
            configuration.Scope = PurchaseScope.CompareAllDataCenters;
            configuration.Save();
        }

        if (configuration.Scope == PurchaseScope.SingleDataCenter)
        {
            var selectedDataCenter = configuration.SelectedDataCenter;
            ImGui.SetNextItemWidth(180);
            if (DrawDataCenterCombo("目标大区", ref selectedDataCenter))
            {
                configuration.SelectedDataCenter = selectedDataCenter;
                configuration.Save();
            }
        }
        else
        {
            ImGui.TextDisabled("同一张清单会在陆行鸟、莫古力、猫小胖和豆豆柴分别计算完整采购方案。最终只选择一个大区执行。 ");
        }
    }

    private void DrawItemSearchEditor()
    {
        ImGui.SetNextItemWidth(480);
        var oldText = itemSearchText;
        ImGui.InputTextWithHint("##ItemSearch", "输入物品名称中的任意文字", ref itemSearchText, 128);
        if (!string.Equals(oldText, itemSearchText, StringComparison.Ordinal))
        {
            selectedSearchResult = null;
            searchResults = itemCatalogService.Search(itemSearchText);
        }

        if (searchResults.Count > 0 && selectedSearchResult is null)
        {
            ImGui.BeginChild("##ItemResults", new Vector2(480, Math.Min(190, searchResults.Count * 27 + 8)), true);
            foreach (var result in searchResults)
            {
                var hq = result.SupportsHighQuality ? " 支持HQ" : string.Empty;
                if (ImGui.Selectable($"{result.Name}  {result.Category}{hq}##{result.ItemId}"))
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
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), $"已选择 {selectedSearchResult.Name} 物品ID {selectedSearchResult.ItemId}");
        else if (!string.IsNullOrWhiteSpace(itemSearchText))
            ImGui.TextDisabled("请从检索结果中选择物品后再加入清单。 ");

        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("数量", ref quantityInput, 1, 10);
        quantityInput = Math.Clamp(quantityInput, 1, 999_999);

        ImGui.SetNextItemWidth(160);
        DrawNewItemQualityCombo(selectedSearchResult?.SupportsHighQuality == true);

        var canAdd = selectedSearchResult is not null && quantityInput > 0;
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button("加入清单", new Vector2(150, 0)) && selectedSearchResult is not null)
        {
            shoppingListService.Add(selectedSearchResult, checked((uint)quantityInput), qualityInput);
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
            ImGui.TextDisabled("当前清单为空。 ");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##ShoppingList", 6, flags))
            return;

        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGui.TableSetupColumn("物品ID", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, 95);
        ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("排序", ImGuiTableColumnFlags.WidthFixed, 85);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 65);
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
                shoppingListService.UpdateQuantity(entry.EntryId, checked((uint)Math.Clamp(quantity, 1, 999_999)));

            ImGui.TableNextColumn();
            DrawEntryQualityCombo(entry);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"上##{entry.EntryId}"))
                shoppingListService.Move(entry.EntryId, -1);
            ImGui.SameLine();
            if (ImGui.SmallButton($"下##{entry.EntryId}"))
                shoppingListService.Move(entry.EntryId, 1);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"删除##{entry.EntryId}"))
                shoppingListService.Remove(entry.EntryId);
        }

        ImGui.EndTable();
    }

    private void DrawComparisonTab()
    {
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null)
        {
            ImGui.TextDisabled("尚未生成报价。请在清单页面查询价格。 ");
            return;
        }

        ImGui.TextUnformatted($"报价清单 {snapshot.ShoppingListName}");
        ImGui.SameLine();
        ImGui.TextDisabled($"快照 {snapshot.SnapshotId}");

        var cheapest = snapshot.CheapestCompletePlan;
        if (cheapest is not null)
        {
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f),
                $"最低完整方案 {cheapest.DataCenterName} {FormatGil(cheapest.TotalCost)} Gil {cheapest.ServerCount} 个服务器");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), "当前没有能够购齐全部商品的大区方案。 ");
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##Comparison", 9, flags))
            return;

        ImGui.TableSetupColumn("大区", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("完成度", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("预计总价", ImGuiTableColumnFlags.WidthFixed, 125);
        ImGui.TableSetupColumn("服务器", ImGuiTableColumnFlags.WidthFixed, 65);
        ImGui.TableSetupColumn("目标/购买", ImGuiTableColumnFlags.WidthFixed, 95);
        ImGui.TableSetupColumn("超额", ImGuiTableColumnFlags.WidthFixed, 65);
        ImGui.TableSetupColumn("最新数据", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("最旧数据", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableHeadersRow();

        foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
        {
            if (!snapshot.Plans.TryGetValue(dataCenter, out var plan))
                continue;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.DataCenterName);
            ImGui.TableNextColumn();
            DrawCompletion(plan);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(plan.TotalCost));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.ServerCount.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{plan.RequiredUnits}/{plan.PurchasedUnits}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.OverbuyUnits.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            DrawDataAge(plan.NewestMarketDataTime);
            ImGui.TableNextColumn();
            DrawDataAge(plan.OldestMarketDataTime);
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"查看路线##{plan.DataCenterName}"))
                routeDataCenter = plan.DataCenterName;
            ImGui.SameLine();
            ImGui.BeginDisabled(!plan.IsComplete || plan.ServerCount == 0);
            if (ImGui.SmallButton($"开始采购##{plan.DataCenterName}"))
            {
                routeDataCenter = plan.DataCenterName;
                purchaseSessionService.Start(snapshot, plan);
            }
            ImGui.EndDisabled();
        }

        ImGui.EndTable();
        ImGui.Spacing();
        ImGui.TextDisabled("总价按完整挂单计算。查询时间代表插件请求时间，市场数据时间代表该商品最近被上传到数据源的时间。 ");
    }

    private void DrawRouteTab()
    {
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null || snapshot.Plans.Count == 0)
        {
            ImGui.TextDisabled("没有可显示的路线。 ");
            return;
        }

        routeDataCenter ??= snapshot.CheapestCompletePlan?.DataCenterName ?? snapshot.Plans.Keys.FirstOrDefault();
        if (routeDataCenter is null)
            return;

        ImGui.SetNextItemWidth(180);
        DrawAvailablePlanCombo(snapshot, ref routeDataCenter);
        if (!snapshot.Plans.TryGetValue(routeDataCenter, out var plan))
            return;

        ImGui.TextUnformatted($"策略 {GetStrategyLabel(plan.Strategy)}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"总价 {FormatGil(plan.TotalCost)} Gil");
        ImGui.SameLine();
        ImGui.TextUnformatted($"服务器 {plan.ServerCount}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"完成度 {plan.CompletedItems}/{plan.TotalItems}");

        ImGui.BeginDisabled(!plan.IsComplete || plan.ServerCount == 0);
        if (ImGui.Button("采用该方案并开始采购"))
            purchaseSessionService.Start(snapshot, plan);
        ImGui.EndDisabled();

        DrawRouteAlternatives(plan);

        var station = 1;
        foreach (var server in plan.ServerPlans)
        {
            if (ImGui.CollapsingHeader($"第 {station} 站 {server.WorldName} {FormatGil(server.TotalCost)} Gil##Route.{server.WorldName}", ImGuiTreeNodeFlags.DefaultOpen))
                DrawServerPlan(server);
            station++;
        }

        DrawSectionHeader("未完成项目");
        var incomplete = plan.ItemPlans.Where(static item => !item.IsComplete).ToArray();
        if (incomplete.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), "该方案可以购齐全部商品。 ");
        }
        else
        {
            foreach (var item in incomplete)
                ImGui.BulletText($"{item.Request.DisplayName} 需要 {item.Request.Quantity} 当前规划 {item.PurchasedQuantity} 状态 {GetMarketStatusLabel(item.DataStatus)}");
        }
    }

    private static void DrawRouteAlternatives(DataCenterPurchasePlan plan)
    {
        DrawSectionHeader("服务器数量与价格对比");
        if (plan.Alternatives.Count == 0)
        {
            ImGui.TextDisabled("没有完整的备选路线。 ");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##Alternatives", 4, flags))
            return;
        ImGui.TableSetupColumn("服务器数");
        ImGui.TableSetupColumn("最低总价");
        ImGui.TableSetupColumn("超额数量");
        ImGui.TableSetupColumn("服务器");
        ImGui.TableHeadersRow();
        foreach (var alternative in plan.Alternatives)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(alternative.ServerCount.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(alternative.TotalCost));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(alternative.OverbuyQuantity.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(string.Join("、", alternative.Worlds));
        }
        ImGui.EndTable();
    }

    private void DrawPurchaseSessionTab()
    {
        var session = purchaseSessionService.Session;
        if (session is null)
        {
            ImGui.TextDisabled("尚未开始采购会话。请在报价或路线页面采用一个完整方案。 ");
            return;
        }

        ImGui.TextUnformatted($"{session.ShoppingListName}  {session.DataCenterName}");
        ImGui.SameLine();
        ImGui.TextDisabled($"状态 {GetSessionStateLabel(session.State)}");
        ImGui.TextUnformatted($"已确认挂单 {session.PurchasedListings}/{session.TotalListings}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"计划 {FormatGil(session.PlannedCost)} Gil");
        ImGui.SameLine();
        ImGui.TextUnformatted($"已确认 {FormatGil(session.ConfirmedCost)} Gil");
        var progress = session.TotalListings == 0 ? 0f : (float)session.PurchasedListings / session.TotalListings;
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{progress:P0}");

        if (session.State == PurchaseSessionState.Active)
        {
            if (ImGui.Button("暂停"))
                purchaseSessionService.Pause();
        }
        else if (session.State == PurchaseSessionState.Paused)
        {
            if (ImGui.Button("继续"))
                purchaseSessionService.Resume();
        }
        ImGui.SameLine();
        if (ImGui.Button("刷新剩余项目价格"))
            priceRefreshService.RequestRemainingSessionRefresh(session);
        ImGui.SameLine();
        if (ImGui.Button("标记会话完成"))
            purchaseSessionService.Finish();
        ImGui.SameLine();
        if (ImGui.Button("取消会话"))
            purchaseSessionService.Cancel();
        ImGui.SameLine();
        if (ImGui.Button("删除会话记录"))
            purchaseSessionService.RemoveSession();

        var refreshed = priceRefreshService.Snapshot;
        if (refreshed?.SourceSessionId == session.SessionId && refreshed.Plans.TryGetValue(session.DataCenterName, out var refreshedPlan))
        {
            DrawSectionHeader("剩余项目新方案");
            ImGui.TextUnformatted($"最新剩余路线 {FormatGil(refreshedPlan.TotalCost)} Gil {refreshedPlan.ServerCount} 个服务器");
            ImGui.BeginDisabled(!refreshedPlan.IsComplete);
            if (ImGui.Button("应用最新剩余路线"))
                purchaseSessionService.ReplaceRemainingPlan(refreshed, refreshedPlan);
            ImGui.EndDisabled();
        }

        DrawInventorySuggestions();
        DrawSectionHeader("服务器采购步骤");
        var worlds = session.Worlds;
        for (var index = 0; index < worlds.Count; index++)
        {
            var world = worlds[index];
            var listings = session.Listings.Where(item => string.Equals(item.WorldName, world, StringComparison.Ordinal)).ToArray();
            var completed = listings.All(static listing => listing.IsPurchased);
            var currentLabel = index == session.CurrentServerIndex ? " 当前" : string.Empty;
            if (!ImGui.CollapsingHeader($"{index + 1} {world}{currentLabel} {listings.Count(item => item.IsPurchased)}/{listings.Length}##Session.{world}", ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            if (ImGui.SmallButton($"设为当前服务器##{world}"))
                purchaseSessionService.SetCurrentWorld(world);
            ImGui.SameLine();
            var worldPurchased = completed;
            if (ImGui.Checkbox($"整站已购买##{world}", ref worldPurchased))
                purchaseSessionService.SetWorldPurchased(world, worldPurchased);

            DrawSessionListings(listings);
        }
    }

    private void DrawInventorySuggestions()
    {
        if (purchaseSessionService.Suggestions.Count == 0)
            return;

        DrawSectionHeader("检测到背包增加");
        foreach (var suggestion in purchaseSessionService.Suggestions.ToArray())
        {
            ImGui.TextUnformatted($"{suggestion.ItemName} {(suggestion.IsHighQuality ? "HQ" : "NQ")} 增加 {suggestion.Quantity}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"计入采购##{suggestion.SuggestionId}"))
                purchaseSessionService.ApplyInventorySuggestion(suggestion.SuggestionId);
            ImGui.SameLine();
            if (ImGui.SmallButton($"忽略##{suggestion.SuggestionId}"))
                purchaseSessionService.IgnoreInventorySuggestion(suggestion.SuggestionId);
        }
    }

    private void DrawSessionListings(IReadOnlyList<SessionListing> listings)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable($"##SessionListings.{listings[0].WorldName}", 7, flags))
            return;
        ImGui.TableSetupColumn("完成", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("单价", ImGuiTableColumnFlags.WidthFixed, 95);
        ImGui.TableSetupColumn("小计", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableSetupColumn("数据时间", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableHeadersRow();
        foreach (var listing in listings)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var purchased = listing.IsPurchased;
            if (ImGui.Checkbox($"##Bought.{listing.SessionListingId}", ref purchased))
                purchaseSessionService.SetListingPurchased(listing.SessionListingId, purchased);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.ItemName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.IsHighQuality ? "HQ" : "NQ");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.Quantity.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(listing.PricePerUnit));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(listing.TotalPrice));
            ImGui.TableNextColumn();
            DrawDataAge(listing.MarketDataTime);
        }
        ImGui.EndTable();
    }

    private void DrawHistoryTab()
    {
        if (quoteHistoryService.History.Count == 0)
        {
            ImGui.TextDisabled("尚无保存的报价快照。 ");
            return;
        }

        if (ImGui.Button("清空历史"))
            quoteHistoryService.Clear();

        foreach (var snapshot in quoteHistoryService.History)
        {
            if (!ImGui.CollapsingHeader($"{snapshot.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {snapshot.ShoppingListName}##{snapshot.SnapshotId}"))
                continue;

            foreach (var quote in snapshot.DataCenters.OrderBy(static item => item.DataCenterName, StringComparer.Ordinal))
            {
                var complete = quote.IsComplete ? "完整" : $"{quote.CompletedItems}/{quote.TotalItems}";
                ImGui.BulletText($"{quote.DataCenterName} {complete} {FormatGil(quote.TotalCost)} Gil {quote.ServerCount} 个服务器");
                ImGui.SameLine();
                DrawDataAge(quote.OldestMarketDataTime);
            }
        }
    }

    private void DrawSettingsTab()
    {
        DrawSectionHeader("路线策略");
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("采购策略", GetStrategyLabel(configuration.Strategy)))
        {
            foreach (var strategy in Enum.GetValues<PurchaseStrategy>())
            {
                var selected = strategy == configuration.Strategy;
                if (ImGui.Selectable(GetStrategyLabel(strategy), selected))
                {
                    configuration.Strategy = strategy;
                    configuration.Save();
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        var serverThreshold = checked((int)Math.Clamp(configuration.AdditionalServerSavingsThreshold, 0, int.MaxValue));
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputInt("平衡模式每增加一个服务器的成本", ref serverThreshold, 10_000, 50_000))
        {
            configuration.AdditionalServerSavingsThreshold = Math.Max(0, serverThreshold);
            configuration.Save();
        }
        ImGui.TextDisabled("例如设置为 50000，新增一个服务器至少要带来约 50000 Gil 的价格优势才值得。 ");

        var overbuyPenalty = configuration.OverbuyPenaltyPerUnit;
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputInt("每个超额单位惩罚", ref overbuyPenalty, 10, 100))
        {
            configuration.OverbuyPenaltyPerUnit = Math.Max(0, overbuyPenalty);
            configuration.Save();
        }

        var stalePenalty = configuration.StaleDataPenaltyPerHour;
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputInt("每小时旧数据惩罚", ref stalePenalty, 100, 1000))
        {
            configuration.StaleDataPenaltyPerHour = Math.Max(0, stalePenalty);
            configuration.Save();
        }

        DrawSectionHeader("价格刷新");
        var refreshLabel = configuration.AutoRefreshMinutes == 0 ? "关闭" : $"{configuration.AutoRefreshMinutes} 分钟";
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("自动刷新间隔", refreshLabel))
        {
            foreach (var option in RefreshOptions)
            {
                var label = option == 0 ? "关闭" : $"{option} 分钟";
                var selected = option == configuration.AutoRefreshMinutes;
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

        var cacheMinutes = configuration.CacheMinutes;
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputInt("本地价格缓存分钟", ref cacheMinutes, 1, 5))
        {
            configuration.CacheMinutes = Math.Clamp(cacheMinutes, 0, 60);
            configuration.Save();
        }

        var historyLimit = configuration.SnapshotHistoryLimit;
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputInt("保存报价快照数量", ref historyLimit, 1, 5))
        {
            configuration.SnapshotHistoryLimit = Math.Clamp(historyLimit, 1, 50);
            configuration.Save();
        }

        var inventorySuggestions = configuration.EnableInventorySuggestions;
        if (ImGui.Checkbox("检测主背包物品增加并建议计入采购会话", ref inventorySuggestions))
        {
            configuration.EnableInventorySuggestions = inventorySuggestions;
            configuration.Save();
        }

        DrawSectionHeader("数据说明");
        ImGui.BulletText("物品名称和可交易状态来自当前国服客户端的本地游戏数据。 ");
        ImGui.BulletText("挂单来自 Universalis 众包接口，不是游戏运营方提供的实时市场数据。 ");
        ImGui.BulletText("插件按完整挂单计算，实际购买数量可能高于清单目标数量。 ");
        ImGui.BulletText("插件只做查询、优化和采购记录，不自动跨服、不自动操作交易板。 ");
    }

    private static void DrawServerPlan(ServerPurchasePlan server)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable($"##Server.{server.WorldName}", 6, flags))
            return;
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("挂单数量", ImGuiTableColumnFlags.WidthFixed, 85);
        ImGui.TableSetupColumn("单价", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("小计", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("市场记录时间", ImGuiTableColumnFlags.WidthFixed, 125);
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

    private void DrawNewItemQualityCombo(bool supportsHighQuality)
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

        if (!ImGui.BeginCombo("品质", GetQualityLabel(qualityInput, true)))
            return;
        foreach (var quality in Enum.GetValues<PurchaseQuality>())
        {
            var selected = quality == qualityInput;
            if (ImGui.Selectable(GetQualityLabel(quality, true), selected))
                qualityInput = quality;
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private void DrawEntryQualityCombo(ShoppingListEntry entry)
    {
        if (!entry.SupportsHighQuality)
        {
            ImGui.TextUnformatted("—");
            return;
        }

        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo($"##Quality.{entry.EntryId}", GetQualityLabel(entry.Quality, true)))
            return;
        foreach (var quality in Enum.GetValues<PurchaseQuality>())
        {
            var selected = quality == entry.Quality;
            if (ImGui.Selectable(GetQualityLabel(quality, true), selected))
                shoppingListService.UpdateQuality(entry.EntryId, quality);
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private static void DrawCompletion(DataCenterPurchasePlan plan)
    {
        if (plan.IsComplete)
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), "100%");
        else
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), $"{plan.CompletedItems}/{plan.TotalItems}");
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

    private static string GetStrategyLabel(PurchaseStrategy strategy)
    {
        return strategy switch
        {
            PurchaseStrategy.LowestPrice => "极致低价",
            PurchaseStrategy.FewestServers => "最少服务器",
            _ => "平衡模式",
        };
    }

    private static string GetSessionStateLabel(PurchaseSessionState state)
    {
        return state switch
        {
            PurchaseSessionState.Paused => "已暂停",
            PurchaseSessionState.Completed => "已完成",
            PurchaseSessionState.Cancelled => "已取消",
            _ => "进行中",
        };
    }

    private static string GetMarketStatusLabel(MarketDataStatus status)
    {
        return status switch
        {
            MarketDataStatus.NoListings => "无挂单",
            MarketDataStatus.UnresolvedItem => "数据源无法识别",
            MarketDataStatus.RequestFailed => "请求失败",
            _ => "可用",
        };
    }

    private static bool DrawDataCenterCombo(string label, ref string selectedDataCenter)
    {
        var changed = false;
        if (!ImGui.BeginCombo(label, selectedDataCenter))
            return false;
        foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
        {
            var selected = string.Equals(selectedDataCenter, dataCenter, StringComparison.Ordinal);
            if (ImGui.Selectable(dataCenter, selected))
            {
                selectedDataCenter = dataCenter;
                changed = true;
            }
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
        return changed;
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

    private static void DrawSectionHeader(string title)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(title);
        ImGui.Separator();
        ImGui.Spacing();
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
