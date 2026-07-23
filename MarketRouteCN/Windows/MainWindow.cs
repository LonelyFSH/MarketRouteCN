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
    private static readonly int[] InventoryScanOptions = [250, 500, 1000];

    private readonly Configuration configuration;
    private readonly ItemCatalogService itemCatalogService;
    private readonly ShoppingListService shoppingListService;
    private readonly ShoppingListTransferService transferService;
    private readonly QuoteHistoryService quoteHistoryService;
    private readonly PriceRefreshService priceRefreshService;
    private readonly PurchaseSessionService purchaseSessionService;

    private WorkspacePage currentPage;
    private Guid? observedSnapshotId;
    private bool openQuotesAfterRefresh;
    private bool openSessionAfterRefresh;
    private string itemSearchText = string.Empty;
    private IReadOnlyList<ItemSearchResult> searchResults = [];
    private ItemSearchResult? selectedSearchResult;
    private int quantityInput = 1;
    private PurchaseQuality qualityInput = PurchaseQuality.Any;
    private string? routeDataCenter;
    private string newListName = "新采购清单";
    private string renameListName = string.Empty;
    private string importText = string.Empty;
    private string importLineText = string.Empty;
    private string importListName = "导入采购清单";
    private ImportTextFormat importFormat = ImportTextFormat.Auto;
    private ShoppingListImportMode importMode = ShoppingListImportMode.Append;
    private ShoppingListImportResult? importPreview;
    private string transientMessage = string.Empty;

    public MainWindow(
        Configuration configuration,
        ItemCatalogService itemCatalogService,
        ShoppingListService shoppingListService,
        ShoppingListTransferService transferService,
        QuoteHistoryService quoteHistoryService,
        PriceRefreshService priceRefreshService,
        PurchaseSessionService purchaseSessionService)
        : base("MarketRoute CN##MarketRouteCN.Main")
    {
        this.configuration = configuration;
        this.itemCatalogService = itemCatalogService;
        this.shoppingListService = shoppingListService;
        this.transferService = transferService;
        this.quoteHistoryService = quoteHistoryService;
        this.priceRefreshService = priceRefreshService;
        this.purchaseSessionService = purchaseSessionService;
        currentPage = configuration.LastPage;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(980, 650),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = configuration.CompactMode ? new Vector2(800, 540) : new Vector2(920, 610),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        ObserveRefreshResult();
        DrawTopBar();
        ImGui.Separator();

        var sidebarWidth = configuration.CompactMode ? 132f : 152f;
        ImGui.BeginChild("##MarketRouteCN.Sidebar", new Vector2(sidebarWidth, 0), true);
        DrawSidebar();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##MarketRouteCN.Content", Vector2.Zero, false);
        DrawCurrentPage();
        ImGui.EndChild();
    }

    public void HandleCommand(string arguments)
    {
        var command = arguments.Trim().ToLowerInvariant();
        IsOpen = true;
        switch (command)
        {
            case "list":
            case "清单":
                SetPage(WorkspacePage.ShoppingList);
                break;
            case "import":
            case "导入":
                SetPage(WorkspacePage.Import);
                break;
            case "quote":
            case "报价":
                RequestQuoteAndOpen();
                break;
            case "route":
            case "路线":
                SetPage(WorkspacePage.Route);
                break;
            case "session":
            case "采购":
                SetPage(WorkspacePage.Session);
                break;
            case "settings":
            case "设置":
                SetPage(WorkspacePage.Settings);
                break;
            default:
                SetPage(WorkspacePage.Overview);
                break;
        }
    }

    private void ObserveRefreshResult()
    {
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null || snapshot.SnapshotId == observedSnapshotId)
            return;

        observedSnapshotId = snapshot.SnapshotId;
        routeDataCenter = snapshot.CheapestCompletePlan?.DataCenterName ?? snapshot.Plans.Keys.FirstOrDefault();

        if (openSessionAfterRefresh && snapshot.SourceSessionId is not null)
            SetPage(WorkspacePage.Session);
        else if (openQuotesAfterRefresh)
            SetPage(WorkspacePage.Quotes);

        openQuotesAfterRefresh = false;
        openSessionAfterRefresh = false;
    }

    private void DrawTopBar()
    {
        ImGui.TextUnformatted("MarketRoute CN");
        ImGui.SameLine();
        ImGui.TextDisabled("V0.9");
        ImGui.SameLine();
        ImGui.TextDisabled(shoppingListService.ActiveList.Name);

        var session = purchaseSessionService.Session;
        if (session is { State: PurchaseSessionState.Active or PurchaseSessionState.Paused })
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"继续采购 {session.PurchasedListings}/{session.TotalListings}"))
                SetPage(WorkspacePage.Session);
            ImGui.SameLine();
            ImGui.TextDisabled(configuration.AutoMarkMarketPurchases ? "自动记录已开启" : "手动记录");
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(shoppingListService.Entries.Count == 0 || priceRefreshService.IsRefreshing);
        if (ImGui.SmallButton("刷新报价"))
            RequestQuoteAndOpen();
        ImGui.EndDisabled();

        if (priceRefreshService.IsRefreshing)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("正在查询");
            ImGui.SameLine();
            if (ImGui.SmallButton("取消"))
                priceRefreshService.CancelRefresh();
        }
        else if (priceRefreshService.Snapshot is { } snapshot)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"更新于 {snapshot.CompletedAt.ToLocalTime():HH:mm:ss}");
        }

        if (!string.IsNullOrWhiteSpace(priceRefreshService.LastError))
            ImGui.TextColored(new Vector4(0.95f, 0.3f, 0.3f, 1f), priceRefreshService.LastError);
        if (!string.IsNullOrWhiteSpace(transientMessage))
            ImGui.TextColored(new Vector4(0.35f, 0.8f, 0.95f, 1f), transientMessage);
    }

    private void DrawSidebar()
    {
        DrawNavigationButton(WorkspacePage.Overview, "概览");
        DrawNavigationButton(WorkspacePage.ShoppingList, $"清单  {shoppingListService.Entries.Count}");
        DrawNavigationButton(WorkspacePage.Quotes, "报价");
        DrawNavigationButton(WorkspacePage.Route, "路线");

        var session = purchaseSessionService.Session;
        var sessionLabel = session is null ? "采购" : $"采购  {session.PurchasedListings}/{session.TotalListings}";
        DrawNavigationButton(WorkspacePage.Session, sessionLabel);

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("更多##NavigationMore"))
        {
            DrawNavigationButton(WorkspacePage.Import, "导入导出");
            DrawNavigationButton(WorkspacePage.History, "报价历史");
            DrawNavigationButton(WorkspacePage.Settings, "设置");
        }
    }

    private void DrawNavigationButton(WorkspacePage page, string title)
    {
        var label = currentPage == page ? $"{title}  当前" : title;
        if (ImGui.Button($"{label}##Nav.{page}", new Vector2(-1, 32)))
            SetPage(page);
        ImGui.Spacing();
    }



    private void DrawCurrentPage()
    {
        switch (currentPage)
        {
            case WorkspacePage.ShoppingList:
                DrawShoppingListPage();
                break;
            case WorkspacePage.Import:
                DrawImportPage();
                break;
            case WorkspacePage.Quotes:
                DrawQuotesPage();
                break;
            case WorkspacePage.Route:
                DrawRoutePage();
                break;
            case WorkspacePage.Session:
                DrawSessionPage();
                break;
            case WorkspacePage.History:
                DrawHistoryPage();
                break;
            case WorkspacePage.Settings:
                DrawSettingsPage();
                break;
            default:
                DrawOverviewPage();
                break;
        }
    }

    private void DrawOverviewPage()
    {
        DrawPageTitle("概览", "查看当前状态并直接进入下一步。 ");
        DrawOverviewMetrics();
        ImGui.Spacing();

        var nextAction = GetNextAction();
        ImGui.BeginChild("##NextAction", new Vector2(0, 84), true);
        ImGui.TextUnformatted(nextAction.Description);
        ImGui.BeginDisabled(nextAction.Disabled);
        if (ImGui.Button(nextAction.Label, new Vector2(230, 34)))
            nextAction.Action();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("导入清单", new Vector2(120, 34)))
            SetPage(WorkspacePage.Import);
        ImGui.EndChild();

        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null)
            return;

        DrawSectionHeader("最新报价");
        DrawCompactQuoteTable(snapshot);
    }

    private void DrawOverviewMetrics()
    {
        var snapshot = priceRefreshService.Snapshot;
        var cheapest = snapshot?.CheapestCompletePlan;
        var session = purchaseSessionService.Session;

        if (!ImGui.BeginTable("##OverviewMetrics", 3, ImGuiTableFlags.SizingStretchSame))
            return;

        DrawMetricCell("当前清单", $"{shoppingListService.Entries.Count} 种", shoppingListService.ActiveList.Name);
        DrawMetricCell("最低完整报价", cheapest is null ? "暂无" : $"{FormatGil(cheapest.TotalCost)} Gil", cheapest?.DataCenterName ?? "等待查询");
        DrawMetricCell("采购进度", session is null ? "未开始" : $"{session.PurchasedListings}/{session.TotalListings}", session?.DataCenterName ?? "采用路线后开始");
        ImGui.EndTable();
    }

    private static void DrawMetricCell(string title, string value, string detail)
    {
        ImGui.TableNextColumn();
        ImGui.BeginChild($"##Metric.{title}", new Vector2(0, 72), true);
        ImGui.TextDisabled(title);
        ImGui.TextUnformatted(value);
        ImGui.TextDisabled(detail);
        ImGui.EndChild();
    }

    private NextAction GetNextAction()
    {
        if (shoppingListService.Entries.Count == 0)
            return new NextAction("建立采购清单", "当前清单为空。先搜索添加物品，或直接导入 MakePlace 风格文本、CSV 或 JSON。", false, () => SetPage(WorkspacePage.ShoppingList));

        if (!SnapshotMatchesActiveList())
            return new NextAction("查询并打开报价", "当前清单尚未报价，或清单在上次报价后发生了变化。查询完成后会自动打开四区报价。", priceRefreshService.IsRefreshing, RequestQuoteAndOpen);

        if (purchaseSessionService.Session is { State: PurchaseSessionState.Active or PurchaseSessionState.Paused })
            return new NextAction("继续采购", "已有未完成的采购会话。直接回到当前服务器和剩余挂单。", false, () => SetPage(WorkspacePage.Session));

        if (priceRefreshService.Snapshot?.CheapestCompletePlan is not null)
            return new NextAction("查看最低路线", "报价已经完成。直接查看最低完整方案的服务器路线，或从报价卡片立即开始采购。", false, OpenBestRoute);

        return new NextAction("查看缺货详情", "当前没有能购齐整张清单的大区。打开报价查看缺货物品和数据状态。", false, () => SetPage(WorkspacePage.Quotes));
    }

    private void DrawShoppingListPage()
    {
        DrawPageTitle("采购清单", "搜索添加物品，设置数量与品质，然后直接查询并跳到报价。 ");
        DrawSavedListControls();
        DrawPurchaseScopeControls();
        DrawSectionHeader("添加物品");
        DrawItemSearchEditor();
        DrawRecentSearches();
        DrawSectionHeader($"当前清单 {shoppingListService.Entries.Count} 种物品");
        DrawShoppingListTable();

        ImGui.Spacing();
        var hasItems = shoppingListService.Entries.Count > 0;
        ImGui.BeginDisabled(!hasItems || priceRefreshService.IsRefreshing);
        if (ImGui.Button("查询并自动打开报价", new Vector2(230, 38)))
            RequestQuoteAndOpen();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("导入更多物品", new Vector2(150, 38)))
            SetPage(WorkspacePage.Import);
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasItems);
        if (ImGui.Button("复制 CSV", new Vector2(110, 38)))
        {
            ImGui.SetClipboardText(transferService.ExportCsv(shoppingListService.Entries));
            transientMessage = "当前清单 CSV 已复制到剪贴板。";
        }
        ImGui.EndDisabled();
    }

    private void DrawSavedListControls()
    {
        DrawSectionHeader("清单管理");
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
                    ResetItemSearch();
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(190);
        ImGui.InputText("新清单名称", ref newListName, 64);
        ImGui.SameLine();
        if (ImGui.Button("新建并打开"))
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
        ImGui.SetNextItemWidth(190);
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
        DrawSectionHeader("采购范围");
        if (ImGui.RadioButton("单大区购齐", configuration.Scope == PurchaseScope.SingleDataCenter))
        {
            configuration.Scope = PurchaseScope.SingleDataCenter;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("四大区分别比较", configuration.Scope == PurchaseScope.CompareAllDataCenters))
        {
            configuration.Scope = PurchaseScope.CompareAllDataCenters;
            configuration.Save();
        }

        if (configuration.EnableAdvancedOptions && configuration.EnableCrossDataCenterAnalysis)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton("跨大区混合路线", configuration.Scope == PurchaseScope.CrossDataCenterMixed))
            {
                configuration.Scope = PurchaseScope.CrossDataCenterMixed;
                configuration.Save();
            }
        }

        if (configuration.Scope == PurchaseScope.SingleDataCenter)
        {
            var selectedDataCenter = configuration.SelectedDataCenter;
            ImGui.SetNextItemWidth(170);
            if (DrawDataCenterCombo("目标大区", ref selectedDataCenter))
            {
                configuration.SelectedDataCenter = selectedDataCenter;
                configuration.Save();
            }
        }

        ImGui.SetNextItemWidth(170);
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

        if (!configuration.SimpleInterface)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(GetStrategyDescription(configuration.Strategy));
        }
    }

    private void DrawItemSearchEditor()
    {
        ImGui.SetNextItemWidth(configuration.CompactMode ? 390 : 520);
        var oldText = itemSearchText;
        ImGui.InputTextWithHint("##ItemSearch", "输入物品名称中的任意文字", ref itemSearchText, 128);
        if (!string.Equals(oldText, itemSearchText, StringComparison.Ordinal))
        {
            selectedSearchResult = null;
            searchResults = itemCatalogService.Search(itemSearchText);
        }

        if (searchResults.Count > 0 && selectedSearchResult is null)
        {
            ImGui.BeginChild("##ItemResults", new Vector2(configuration.CompactMode ? 520 : 680, Math.Min(190, searchResults.Count * 27 + 8)), true);
            foreach (var result in searchResults)
            {
                var hq = result.SupportsHighQuality ? " 支持 HQ" : string.Empty;
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
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), $"已选择 {selectedSearchResult.Name}  物品 ID {selectedSearchResult.ItemId}");
        else if (!string.IsNullOrWhiteSpace(itemSearchText))
            ImGui.TextDisabled("请从检索结果中选择准确物品。 ");

        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("数量", ref quantityInput, 1, 10);
        quantityInput = Math.Clamp(quantityInput, 1, 999_999);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(140);
        DrawNewItemQualityCombo(selectedSearchResult?.SupportsHighQuality == true);
        ImGui.SameLine();

        var canAdd = selectedSearchResult is not null && quantityInput > 0;
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button("加入清单", new Vector2(130, 0)))
            AddSelectedItem();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!canAdd || priceRefreshService.IsRefreshing);
        if (ImGui.Button("加入后立即报价", new Vector2(160, 0)))
        {
            AddSelectedItem();
            RequestQuoteAndOpen();
        }
        ImGui.EndDisabled();
    }

    private void AddSelectedItem()
    {
        if (selectedSearchResult is null)
            return;

        shoppingListService.Add(selectedSearchResult, checked((uint)quantityInput), qualityInput);
        configuration.RememberSearch(selectedSearchResult.Name);
        transientMessage = $"已加入 {selectedSearchResult.Name}。";
        ResetItemSearch();
    }

    private void DrawRecentSearches()
    {
        if (configuration.RecentSearches.Count == 0)
            return;

        ImGui.TextDisabled("最近添加");
        foreach (var value in configuration.RecentSearches)
        {
            if (ImGui.SmallButton($"{value}##Recent.{value}"))
            {
                itemSearchText = value;
                searchResults = itemCatalogService.Search(value);
                selectedSearchResult = searchResults.FirstOrDefault(item => item.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();
    }

    private void DrawShoppingListTable()
    {
        if (shoppingListService.Entries.Count == 0)
        {
            ImGui.TextDisabled("当前清单为空。可以在上方搜索，也可以直接打开导入页面。 ");
            if (ImGui.Button("打开导入页面"))
                SetPage(WorkspacePage.Import);
            return;
        }

        if (ImGui.SmallButton("按名称排序"))
            shoppingListService.SortByName();
        ImGui.SameLine();
        if (ImGui.SmallButton("按数量排序"))
            shoppingListService.SortByQuantity();
        ImGui.SameLine();
        if (ImGui.SmallButton("清空当前清单"))
            shoppingListService.Clear();

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##ShoppingList", 6, flags))
            return;

        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 2.2f);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, 95);
        ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableSetupColumn("顺序", ImGuiTableColumnFlags.WidthFixed, 85);
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

    private void DrawImportPage()
    {
        DrawPageTitle("导入导出", "支持 MakePlace 风格文本、普通文本、CSV 和 JSON。导入完成后可以直接跳到清单或报价。 ");

        if (ImGui.BeginTable("##ImportControls", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("格式", ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableSetupColumn("方式", ImGuiTableColumnFlags.WidthFixed, 190);
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            DrawImportFormatCombo();
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            DrawImportModeCombo();
            ImGui.TableNextColumn();
            if (importMode == ShoppingListImportMode.CreateNew)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##ImportName", "新清单名称", ref importListName, 64);
            }
            else
            {
                ImGui.TextDisabled(importMode == ShoppingListImportMode.Append ? "追加到当前清单" : "替换当前清单内容");
            }
            ImGui.EndTable();
        }

        DrawImportTextEditor();
        if (ImGui.Button("从剪贴板读取"))
        {
            importText = ImGui.GetClipboardText() ?? string.Empty;
            importPreview = null;
        }
        ImGui.SameLine();
        if (ImGui.Button("预览解析"))
            importPreview = transferService.Parse(importText, importFormat);
        ImGui.SameLine();
        if (ImGui.Button("清空输入"))
        {
            importText = string.Empty;
            importLineText = string.Empty;
            importPreview = null;
        }

        if (importPreview is not null)
            DrawImportPreview(importPreview);

        ImGui.Spacing();
        var canImport = importPreview is { ValidRows: > 0 };
        ImGui.BeginDisabled(!canImport);
        if (ImGui.Button("导入并打开清单", new Vector2(190, 38)))
        {
            ApplyImport();
            SetPage(WorkspacePage.ShoppingList);
        }
        ImGui.SameLine();
        if (ImGui.Button("导入后立即查询报价", new Vector2(210, 38)))
        {
            ApplyImport();
            RequestQuoteAndOpen();
        }
        ImGui.EndDisabled();

        DrawSectionHeader("导出当前清单");
        ImGui.BeginDisabled(shoppingListService.Entries.Count == 0);
        if (ImGui.Button("复制 CSV 到剪贴板"))
        {
            ImGui.SetClipboardText(transferService.ExportCsv(shoppingListService.Entries));
            transientMessage = "CSV 已复制。";
        }
        ImGui.SameLine();
        if (ImGui.Button("复制 JSON 到剪贴板"))
        {
            ImGui.SetClipboardText(transferService.ExportJson(shoppingListService.Entries));
            transientMessage = "JSON 已复制。";
        }
        ImGui.EndDisabled();

        DrawSectionHeader("文本示例");
        ImGui.TextDisabled("东方隔扇 x12");
        ImGui.TextDisabled("黑麻 20 NQ");
        ImGui.TextDisabled("itemId,name,quantity,quality");
    }

    private void DrawImportTextEditor()
    {
        ImGui.SetNextItemWidth(-92);
        if (ImGui.InputTextWithHint("##ImportLine", "输入一行内容，批量清单请使用剪贴板", ref importLineText, 512)
            && ImGui.IsItemDeactivatedAfterEdit()
            && !string.IsNullOrWhiteSpace(importLineText))
        {
            AppendImportLine();
        }

        ImGui.SameLine();
        if (ImGui.Button("添加一行") && !string.IsNullOrWhiteSpace(importLineText))
            AppendImportLine();

        ImGui.BeginChild("##ImportTextPreview", new Vector2(-1, 196), true);
        if (string.IsNullOrWhiteSpace(importText))
            ImGui.TextDisabled("尚未载入清单。可以逐行添加，也可以直接从剪贴板读取多行内容。");
        else
            ImGui.TextUnformatted(importText);
        ImGui.EndChild();
    }

    private void AppendImportLine()
    {
        var line = importLineText.Trim();
        if (line.Length == 0)
            return;

        importText = string.IsNullOrEmpty(importText)
            ? line
            : $"{importText}{Environment.NewLine}{line}";
        importLineText = string.Empty;
        importPreview = null;
    }

    private void DrawImportPreview(ShoppingListImportResult preview)
    {
        DrawSectionHeader("解析预览");
        ImGui.TextUnformatted($"读取 {preview.ParsedRows} 行  有效 {preview.ValidRows} 项  问题 {preview.Errors.Count} 项");

        if (preview.Entries.Count > 0 && ImGui.BeginTable("##ImportPreview", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("物品");
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();
            foreach (var entry in preview.Entries)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.DisplayName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.ItemId.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Quantity.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(GetQualityLabel(entry.Quality, entry.SupportsHighQuality));
            }
            ImGui.EndTable();
        }

        foreach (var error in preview.Errors.Take(12))
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), error);
        if (preview.Errors.Count > 12)
            ImGui.TextDisabled($"另有 {preview.Errors.Count - 12} 条问题未展开。 ");
    }

    private void ApplyImport()
    {
        if (importPreview is not { ValidRows: > 0 } preview)
            return;

        switch (importMode)
        {
            case ShoppingListImportMode.Replace:
                shoppingListService.ReplaceEntries(preview.Entries);
                break;
            case ShoppingListImportMode.CreateNew:
                shoppingListService.CreateWithEntries(importListName, preview.Entries);
                renameListName = shoppingListService.ActiveList.Name;
                break;
            default:
                shoppingListService.AppendEntries(preview.Entries);
                break;
        }

        transientMessage = $"已导入 {preview.ValidRows} 项到 {shoppingListService.ActiveList.Name}。";
        importPreview = null;
    }

    private void DrawQuotesPage()
    {
        var pageDescription = configuration.Scope == PurchaseScope.CrossDataCenterMixed
            ? "比较四个大区，并额外生成跨大区混合方案。"
            : "比较完整清单在各大区的总价。";
        DrawPageTitle("报价", pageDescription);

        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null)
        {
            DrawEmptyQuoteState();
            return;
        }

        if (snapshot.ShoppingListId != shoppingListService.ActiveList.ListId || snapshot.CompletedAt < shoppingListService.ActiveList.UpdatedAt)
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.25f, 1f), "当前结果不是最新清单报价。 ");

        ImGui.TextDisabled($"查询时间 {snapshot.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        ImGui.SameLine();
        ImGui.BeginDisabled(priceRefreshService.IsRefreshing);
        if (ImGui.SmallButton("重新查询"))
            RequestQuoteAndOpen();
        ImGui.EndDisabled();

        var cheapest = snapshot.CheapestCompletePlan;
        if (cheapest is not null)
        {
            var scope = cheapest.IsCrossDataCenter
                ? $"{cheapest.DataCenterCount} 个大区  {cheapest.ServerCount} 个服务器"
                : $"{cheapest.ServerCount} 个服务器";
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f),
                $"最低完整方案  {cheapest.DataCenterName}  {FormatGil(cheapest.TotalCost)} Gil  {scope}");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), "当前没有完整购齐方案。 ");
        }

        if (ImGui.BeginTable("##QuoteCards", 2, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var plan in GetOrderedPlans(snapshot))
            {
                ImGui.TableNextColumn();
                DrawQuoteCard(snapshot, plan, ReferenceEquals(plan, cheapest));
            }
            ImGui.EndTable();
        }
    }

    private void DrawQuoteCard(PriceComparisonSnapshot snapshot, DataCenterPurchasePlan plan, bool cheapest)
    {
        var height = configuration.SimpleInterface ? 178f : 228f;
        ImGui.BeginChild($"##QuoteCard.{plan.DataCenterName}", new Vector2(0, height), true);
        ImGui.TextUnformatted(cheapest ? $"{plan.DataCenterName}  推荐" : plan.DataCenterName);
        ImGui.Separator();

        ImGui.TextUnformatted(plan.IsComplete
            ? $"{FormatGil(plan.TotalCost)} Gil"
            : $"已知价格 {FormatGil(plan.TotalCost)} Gil");
        var routeSize = plan.IsCrossDataCenter
            ? $"{plan.DataCenterCount} 个大区  {plan.ServerCount} 个服务器"
            : $"{plan.ServerCount} 个服务器";
        ImGui.TextDisabled(plan.IsComplete ? routeSize : $"完成 {plan.CompletedItems}/{plan.TotalItems}  {routeSize}");
        ImGui.TextUnformatted("最旧数据");
        ImGui.SameLine();
        DrawDataAge(plan.OldestMarketDataTime);

        if (!configuration.SimpleInterface && ImGui.CollapsingHeader($"详细信息##QuoteDetails.{plan.DataCenterName}"))
        {
            var previous = quoteHistoryService.FindPreviousQuote(snapshot.ShoppingListId, snapshot.SnapshotId, plan.DataCenterName);
            if (previous is not null && previous.TotalCost > 0 && plan.TotalCost > 0)
            {
                var delta = plan.TotalCost - previous.TotalCost;
                ImGui.TextDisabled(delta == 0 ? "与上次相同" : delta < 0 ? $"比上次低 {FormatGil(-delta)}" : $"比上次高 {FormatGil(delta)}");
            }
            ImGui.TextDisabled($"超额 {plan.OverbuyUnits}  风险价 {FormatGil(plan.RiskAdjustedCost)}  可信度 {GetConfidenceLabel(plan)}");
        }

        if (ImGui.Button($"查看路线##OpenRoute.{plan.DataCenterName}", new Vector2(-1, 28)))
        {
            routeDataCenter = plan.DataCenterName;
            SetPage(WorkspacePage.Route);
        }
        ImGui.BeginDisabled(!plan.IsComplete || plan.ServerCount == 0);
        if (ImGui.Button($"直接开始采购##Start.{plan.DataCenterName}", new Vector2(-1, 28)))
        {
            routeDataCenter = plan.DataCenterName;
            purchaseSessionService.Start(snapshot, plan);
            SetPage(WorkspacePage.Session);
        }
        ImGui.EndDisabled();
        ImGui.EndChild();
    }

    private void DrawRoutePage()
    {
        DrawPageTitle("采购路线", "选择方案后可直接开始采购。 ");
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null || snapshot.Plans.Count == 0)
        {
            ImGui.TextDisabled("尚无路线。 ");
            if (ImGui.Button("查询报价"))
                RequestQuoteAndOpen();
            return;
        }

        routeDataCenter ??= snapshot.CheapestCompletePlan?.DataCenterName ?? snapshot.Plans.Keys.FirstOrDefault();
        if (routeDataCenter is null)
            return;

        ImGui.SetNextItemWidth(190);
        DrawAvailablePlanCombo(snapshot, ref routeDataCenter);
        if (!snapshot.Plans.TryGetValue(routeDataCenter, out var plan))
            return;

        var routeSize = plan.IsCrossDataCenter
            ? $"{plan.DataCenterCount} 个大区  {plan.ServerCount} 个服务器"
            : $"{plan.ServerCount} 个服务器";
        ImGui.TextUnformatted($"{FormatGil(plan.TotalCost)} Gil  {routeSize}  {GetStrategyLabel(plan.Strategy)}");
        ImGui.SameLine();
        ImGui.BeginDisabled(!plan.IsComplete || plan.ServerCount == 0);
        if (ImGui.SmallButton("采用并开始"))
        {
            purchaseSessionService.Start(snapshot, plan);
            SetPage(WorkspacePage.Session);
        }
        ImGui.EndDisabled();

        if (plan.Alternatives.Count > 1 && ImGui.CollapsingHeader("路线取舍##RouteAlternatives"))
            DrawRouteAlternatives(plan);

        DrawSectionHeader("采购站");
        var station = 1;
        foreach (var server in plan.ServerPlans)
        {
            var flags = station == 1 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            var label = $"{station}  {server.StopLabel}  {server.Listings.Count} 个挂单  {FormatGil(server.TotalCost)} Gil##Route.{server.DataCenterName}.{server.WorldName}";
            if (ImGui.CollapsingHeader(label, flags))
                DrawServerPlan(server);
            station++;
        }

        var incomplete = plan.ItemPlans.Where(static item => !item.IsComplete).ToArray();
        if (incomplete.Length > 0 && ImGui.CollapsingHeader("未完成项目##IncompleteRoute"))
        {
            foreach (var item in incomplete)
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f),
                    $"{item.Request.DisplayName}  {item.PurchasedQuantity}/{item.Request.Quantity}  {GetMarketStatusLabel(item.DataStatus)}");
        }
    }

    private static void DrawRouteAlternatives(DataCenterPurchasePlan plan)
    {
        if (plan.Alternatives.Count == 0)
            return;

        var columns = plan.IsCrossDataCenter ? 5 : 4;
        if (!ImGui.BeginTable("##Alternatives", columns, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        if (plan.IsCrossDataCenter)
            ImGui.TableSetupColumn("大区", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("服务器", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("总价", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("多一站节省", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("路线");
        ImGui.TableHeadersRow();

        long? previousCost = null;
        foreach (var alternative in plan.Alternatives)
        {
            ImGui.TableNextRow();
            if (plan.IsCrossDataCenter)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(alternative.DataCenterCount.ToString(CultureInfo.InvariantCulture));
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(alternative.ServerCount.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(alternative.TotalCost));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(previousCost is null ? "起始" : FormatGil(Math.Max(0, previousCost.Value - alternative.TotalCost)));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(string.Join("、", alternative.Worlds));
            previousCost = alternative.TotalCost;
        }
        ImGui.EndTable();
    }

    private void DrawSessionPage()
    {
        DrawPageTitle("采购进度", "背包增加后会自动计入当前采购站。 ");
        var session = purchaseSessionService.Session;
        if (session is null)
        {
            ImGui.TextDisabled("尚未开始采购。 ");
            ImGui.BeginDisabled(priceRefreshService.Snapshot?.CheapestCompletePlan is null);
            if (ImGui.Button("采用最低完整方案并开始", new Vector2(250, 36)))
                StartCheapestPlan();
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("查看报价", new Vector2(120, 36)))
                SetPage(WorkspacePage.Quotes);
            return;
        }

        ImGui.TextUnformatted($"{session.ShoppingListName}  {session.DataCenterName}  {GetSessionStateLabel(session.State)}");
        var progress = session.PlannedQuantity == 0
            ? 0f
            : (float)session.AcquiredQuantity / session.PlannedQuantity;
        ImGui.ProgressBar(
            progress,
            new Vector2(-1, 0),
            $"{session.AcquiredQuantity}/{session.PlannedQuantity}  {progress:P0}");

        ImGui.TextDisabled(configuration.AutoRecordInventoryChanges
            ? $"自动记录已开启  每 {configuration.InventoryScanIntervalMilliseconds} 毫秒检测背包"
            : "自动记录已关闭  可使用下方复选框手动完成");
        if (!string.IsNullOrWhiteSpace(purchaseSessionService.LastAutomaticMessage))
        {
            ImGui.TextColored(
                new Vector4(0.35f, 0.85f, 0.45f, 1f),
                purchaseSessionService.LastAutomaticMessage);
            if (purchaseSessionService.CanUndoLastAutomaticRecord)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("撤销最近记录"))
                    purchaseSessionService.UndoLastAutomaticRecord();
            }
        }

        var currentStop = purchaseSessionService.CurrentStop;
        if (currentStop is not null)
        {
            var currentListings = session.Listings.Where(item =>
                string.Equals(item.DataCenterName, currentStop.DataCenterName, StringComparison.Ordinal) &&
                string.Equals(item.WorldName, currentStop.WorldName, StringComparison.Ordinal)).ToArray();
            ImGui.BeginChild("##CurrentStation", new Vector2(0, 82), true);
            ImGui.TextUnformatted($"当前计划  {currentStop.Label}");
            var actualWorld = purchaseSessionService.ActualWorldName;
            if (!string.IsNullOrWhiteSpace(actualWorld))
                ImGui.TextDisabled($"角色所在服务器  {actualWorld}");
            var remainingQuantity = currentListings.Sum(static item => item.RemainingQuantity);
            ImGui.TextDisabled($"剩余 {remainingQuantity} 件  本站 {FormatGil(currentListings.Sum(static item => item.TotalPrice))} Gil");
            ImGui.EndChild();
        }

        if (session.State == PurchaseSessionState.Active)
        {
            if (ImGui.SmallButton("暂停"))
                purchaseSessionService.Pause();
        }
        else if (session.State == PurchaseSessionState.Paused)
        {
            if (ImGui.SmallButton("继续"))
                purchaseSessionService.Resume();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(priceRefreshService.IsRefreshing || session.IsComplete);
        if (ImGui.SmallButton("刷新剩余项目"))
        {
            openSessionAfterRefresh = true;
            priceRefreshService.RequestRemainingSessionRefresh(session);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.SmallButton("手动完成本站并下一站"))
            purchaseSessionService.CompleteCurrentWorldAndAdvance();
        ImGui.SameLine();
        if (ImGui.SmallButton("结束会话"))
            purchaseSessionService.Cancel();

        var refreshed = priceRefreshService.Snapshot;
        if (refreshed?.SourceSessionId == session.SessionId)
        {
            var refreshedPlan = refreshed.Plans.GetValueOrDefault(session.DataCenterName) ?? refreshed.CheapestCompletePlan;
            if (refreshedPlan is not null)
            {
                DrawSectionHeader("剩余项目新方案");
                ImGui.TextUnformatted($"{refreshedPlan.DataCenterName}  {FormatGil(refreshedPlan.TotalCost)} Gil  {refreshedPlan.ServerCount} 个服务器");
                ImGui.BeginDisabled(!refreshedPlan.IsComplete);
                if (ImGui.SmallButton("应用新路线"))
                    purchaseSessionService.ReplaceRemainingPlan(refreshed, refreshedPlan);
                ImGui.EndDisabled();
            }
        }

        DrawInventorySuggestions();
        DrawSectionHeader("采购站");
        var stops = session.Stops;
        for (var index = 0; index < stops.Count; index++)
        {
            var stop = stops[index];
            var listings = session.Listings.Where(item =>
                string.Equals(item.DataCenterName, stop.DataCenterName, StringComparison.Ordinal) &&
                string.Equals(item.WorldName, stop.WorldName, StringComparison.Ordinal)).ToArray();
            var completed = listings.All(static listing => listing.IsPurchased);
            var current = index == session.CurrentServerIndex;
            var flags = current ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            var stopAcquired = listings.Sum(static item => item.AcquiredQuantity);
            var stopPlanned = listings.Sum(static item => item.Quantity);
            var status = completed ? "完成" : $"{stopAcquired}/{stopPlanned}";
            if (!ImGui.CollapsingHeader($"{index + 1}  {stop.Label}  {status}{(current ? "  当前" : string.Empty)}##Session.{stop.DataCenterName}.{stop.WorldName}", flags))
                continue;

            if (ImGui.SmallButton($"设为当前站##{stop.DataCenterName}.{stop.WorldName}"))
                purchaseSessionService.SetCurrentStop(stop.DataCenterName, stop.WorldName);
            ImGui.SameLine();
            var stopPurchased = completed;
            if (ImGui.Checkbox($"整站完成##{stop.DataCenterName}.{stop.WorldName}", ref stopPurchased))
                purchaseSessionService.SetStopPurchased(stop.DataCenterName, stop.WorldName, stopPurchased);
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
        if (listings.Count == 0)
            return;

        var columnCount = configuration.SimpleInterface ? 5 : 7;
        if (!ImGui.BeginTable($"##SessionListings.{listings[0].DataCenterName}.{listings[0].WorldName}", columnCount,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("完成", ImGuiTableColumnFlags.WidthFixed, 58);
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.7f);
        ImGui.TableSetupColumn("进度", ImGuiTableColumnFlags.WidthFixed, 82);
        ImGui.TableSetupColumn("单价", ImGuiTableColumnFlags.WidthFixed, 88);
        ImGui.TableSetupColumn("小计", ImGuiTableColumnFlags.WidthFixed, 95);
        if (!configuration.SimpleInterface)
        {
            ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("数据时间", ImGuiTableColumnFlags.WidthFixed, 105);
        }
        ImGui.TableHeadersRow();

        foreach (var listing in listings)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var purchased = listing.IsPurchased;
            if (ImGui.Checkbox($"##Bought.{listing.SessionListingId}", ref purchased))
                purchaseSessionService.SetListingPurchased(listing.SessionListingId, purchased);
            if (listing.AutoConfirmed)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("自动");
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(listing.ItemName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(
                $"{listing.AcquiredQuantity}/{listing.Quantity}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(listing.PricePerUnit));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(listing.TotalPrice));
            if (!configuration.SimpleInterface)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(listing.IsHighQuality ? "HQ" : "NQ");
                ImGui.TableNextColumn();
                DrawDataAge(listing.MarketDataTime);
            }
        }
        ImGui.EndTable();
    }

    private void DrawHistoryPage()
    {
        DrawPageTitle("报价历史", "同一清单的历史报价用于展示涨跌趋势，不会改变当前路线。 ");
        if (quoteHistoryService.History.Count == 0)
        {
            ImGui.TextDisabled("尚无保存的报价快照。 ");
            if (ImGui.Button("查询当前清单"))
                RequestQuoteAndOpen();
            return;
        }

        if (ImGui.SmallButton("清空历史"))
            quoteHistoryService.Clear();

        foreach (var snapshot in quoteHistoryService.History)
        {
            if (!ImGui.CollapsingHeader($"{snapshot.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {snapshot.ShoppingListName}##{snapshot.SnapshotId}"))
                continue;

            if (!ImGui.BeginTable($"##History.{snapshot.SnapshotId}", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                continue;
            ImGui.TableSetupColumn("大区");
            ImGui.TableSetupColumn("完整度");
            ImGui.TableSetupColumn("总价");
            ImGui.TableSetupColumn("风险价");
            ImGui.TableSetupColumn("服务器");
            ImGui.TableSetupColumn("旧数据");
            ImGui.TableSetupColumn("最旧时间");
            ImGui.TableHeadersRow();
            foreach (var quote in snapshot.DataCenters.OrderBy(static item => item.DataCenterName, StringComparer.Ordinal))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(quote.DataCenterName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(quote.IsComplete ? "100%" : $"{quote.CompletedItems}/{quote.TotalItems}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatGil(quote.TotalCost));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatGil(quote.RiskAdjustedCost));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(quote.ServerCount.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(quote.StaleItems.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                DrawDataAge(quote.OldestMarketDataTime);
            }
            ImGui.EndTable();
        }
    }

    private void DrawSettingsPage()
    {
        DrawPageTitle("设置", "常用选项保持简洁，其余功能收纳在高级选项中。 ");

        DrawSectionHeader("常用");
        var simpleInterface = configuration.SimpleInterface;
        if (ImGui.Checkbox("简洁界面", ref simpleInterface))
        {
            configuration.SimpleInterface = simpleInterface;
            configuration.Save();
        }
        var compact = configuration.CompactMode;
        if (ImGui.Checkbox("紧凑窗口", ref compact))
        {
            configuration.CompactMode = compact;
            configuration.Save();
        }
        var autoRecordInventory = configuration.AutoRecordInventoryChanges;
        if (ImGui.Checkbox("背包增加后自动计入当前采购站", ref autoRecordInventory))
        {
            configuration.AutoRecordInventoryChanges = autoRecordInventory;
            configuration.Save();
        }

        var autoMark = configuration.AutoMarkMarketPurchases;
        if (ImGui.Checkbox("结合交易板购买信号提高匹配可信度", ref autoMark))
        {
            configuration.AutoMarkMarketPurchases = autoMark;
            configuration.Save();
        }
        var autoAdvance = configuration.AutoAdvanceCompletedWorld;
        if (ImGui.Checkbox("本站完成后自动切换到下一站", ref autoAdvance))
        {
            configuration.AutoAdvanceCompletedWorld = autoAdvance;
            configuration.Save();
        }

        var refreshLabel = configuration.AutoRefreshMinutes == 0 ? "关闭" : $"{configuration.AutoRefreshMinutes} 分钟";
        ImGui.SetNextItemWidth(190);
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

        DrawSectionHeader("高级选项");
        var advanced = configuration.EnableAdvancedOptions;
        if (ImGui.Checkbox("启用高级选项", ref advanced))
        {
            configuration.EnableAdvancedOptions = advanced;
            if (!advanced && configuration.Scope == PurchaseScope.CrossDataCenterMixed)
                configuration.Scope = PurchaseScope.CompareAllDataCenters;
            configuration.Save();
        }

        if (configuration.EnableAdvancedOptions)
        {
            var crossDataCenter = configuration.EnableCrossDataCenterAnalysis;
            if (ImGui.Checkbox("启用跨大区混合路线分析", ref crossDataCenter))
            {
                configuration.EnableCrossDataCenterAnalysis = crossDataCenter;
                if (!crossDataCenter && configuration.Scope == PurchaseScope.CrossDataCenterMixed)
                    configuration.Scope = PurchaseScope.CompareAllDataCenters;
                configuration.Save();
            }
            ImGui.TextDisabled("启用后可在清单页选择跨大区混合路线。 ");

            ImGui.SetNextItemWidth(190);
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
            ImGui.SetNextItemWidth(190);
            if (ImGui.InputInt("新增服务器成本 Gil", ref serverThreshold, 10_000, 50_000))
            {
                configuration.AdditionalServerSavingsThreshold = Math.Max(0, serverThreshold);
                configuration.Save();
            }

            var dataCenterThreshold = checked((int)Math.Clamp(configuration.AdditionalDataCenterSavingsThreshold, 0, int.MaxValue));
            ImGui.SetNextItemWidth(190);
            if (ImGui.InputInt("新增大区成本 Gil", ref dataCenterThreshold, 50_000, 100_000))
            {
                configuration.AdditionalDataCenterSavingsThreshold = Math.Max(0, dataCenterThreshold);
                configuration.Save();
            }

            var overbuyPenalty = configuration.OverbuyPenaltyPerUnit;
            ImGui.SetNextItemWidth(190);
            if (ImGui.InputInt("超额单位惩罚", ref overbuyPenalty, 10, 100))
            {
                configuration.OverbuyPenaltyPerUnit = Math.Max(0, overbuyPenalty);
                configuration.Save();
            }

            var stalePenalty = configuration.StaleDataPenaltyPerHour;
            ImGui.SetNextItemWidth(190);
            if (ImGui.InputInt("旧数据每小时惩罚", ref stalePenalty, 100, 1000))
            {
                configuration.StaleDataPenaltyPerHour = Math.Max(0, stalePenalty);
                configuration.Save();
            }

            var enableTarget = configuration.EnableTargetTotalPrice;
            if (ImGui.Checkbox("启用目标总价", ref enableTarget))
            {
                configuration.EnableTargetTotalPrice = enableTarget;
                configuration.Save();
            }
            if (configuration.EnableTargetTotalPrice)
            {
                var targetPrice = checked((int)Math.Clamp(configuration.TargetTotalPriceGil, 0, int.MaxValue));
                ImGui.SetNextItemWidth(190);
                if (ImGui.InputInt("目标总价 Gil", ref targetPrice, 10_000, 100_000))
                {
                    configuration.TargetTotalPriceGil = Math.Max(0, targetPrice);
                    configuration.Save();
                }
            }

            var cacheMinutes = configuration.CacheMinutes;
            ImGui.SetNextItemWidth(190);
            if (ImGui.InputInt("本地缓存分钟", ref cacheMinutes, 1, 5))
            {
                configuration.CacheMinutes = Math.Clamp(cacheMinutes, 0, 60);
                configuration.Save();
            }

            var historyLimit = configuration.SnapshotHistoryLimit;
            ImGui.SetNextItemWidth(190);
            if (ImGui.InputInt("报价历史数量", ref historyLimit, 1, 5))
            {
                configuration.SnapshotHistoryLimit = Math.Clamp(historyLimit, 1, 50);
                configuration.Save();
            }

            ImGui.SetNextItemWidth(190);
            if (ImGui.BeginCombo(
                    "背包检测间隔",
                    $"{configuration.InventoryScanIntervalMilliseconds} 毫秒"))
            {
                foreach (var interval in InventoryScanOptions)
                {
                    var selected = interval == configuration.InventoryScanIntervalMilliseconds;
                    if (ImGui.Selectable($"{interval} 毫秒", selected))
                    {
                        configuration.InventoryScanIntervalMilliseconds = interval;
                        configuration.Save();
                    }
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled("默认 500 毫秒。检测到增加后等待 1 秒稳定再记录。");

            var inventorySuggestions = configuration.EnableInventorySuggestions;
            if (ImGui.Checkbox("非交易板物品增加时给出计入建议", ref inventorySuggestions))
            {
                configuration.EnableInventorySuggestions = inventorySuggestions;
                configuration.Save();
            }
        }

        DrawSectionHeader("数据说明");
        ImGui.TextWrapped("挂单来自 Universalis 众包接口，不是游戏运营方提供的实时市场数据。 ");
    }

    private void DrawCompactQuoteTable(PriceComparisonSnapshot snapshot)
    {
        if (!ImGui.BeginTable("##CompactQuotes", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("方案");
        ImGui.TableSetupColumn("完成");
        ImGui.TableSetupColumn("总价");
        ImGui.TableSetupColumn("路线");
        ImGui.TableSetupColumn("操作");
        ImGui.TableHeadersRow();

        foreach (var plan in GetOrderedPlans(snapshot))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.DataCenterName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.IsComplete ? "100%" : $"{plan.CompletedItems}/{plan.TotalItems}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(plan.TotalCost));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.IsCrossDataCenter
                ? $"{plan.DataCenterCount} 区 {plan.ServerCount} 服"
                : $"{plan.ServerCount} 服");
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"路线##CompactRoute.{plan.DataCenterName}"))
            {
                routeDataCenter = plan.DataCenterName;
                SetPage(WorkspacePage.Route);
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(!plan.IsComplete || plan.ServerCount == 0);
            if (ImGui.SmallButton($"开始##CompactStart.{plan.DataCenterName}"))
            {
                routeDataCenter = plan.DataCenterName;
                purchaseSessionService.Start(snapshot, plan);
                SetPage(WorkspacePage.Session);
            }
            ImGui.EndDisabled();
        }
        ImGui.EndTable();
    }

    private void DrawEmptyQuoteState()
    {
        ImGui.TextDisabled("尚未生成报价。 ");
        ImGui.BeginDisabled(shoppingListService.Entries.Count == 0 || priceRefreshService.IsRefreshing);
        if (ImGui.Button("查询当前清单并自动打开结果", new Vector2(270, 38)))
            RequestQuoteAndOpen();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("返回采购清单", new Vector2(150, 38)))
            SetPage(WorkspacePage.ShoppingList);
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
            ImGui.TextUnformatted("不区分");
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

    private void DrawImportFormatCombo()
    {
        if (!ImGui.BeginCombo("格式", GetImportFormatLabel(importFormat)))
            return;
        foreach (var format in Enum.GetValues<ImportTextFormat>())
        {
            var selected = format == importFormat;
            if (ImGui.Selectable(GetImportFormatLabel(format), selected))
            {
                importFormat = format;
                importPreview = null;
            }
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private void DrawImportModeCombo()
    {
        if (!ImGui.BeginCombo("导入方式", GetImportModeLabel(importMode)))
            return;
        foreach (var mode in Enum.GetValues<ShoppingListImportMode>())
        {
            var selected = mode == importMode;
            if (ImGui.Selectable(GetImportModeLabel(mode), selected))
                importMode = mode;
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private void DrawServerPlan(ServerPurchasePlan server)
    {
        var columns = configuration.SimpleInterface ? 5 : 7;
        if (!ImGui.BeginTable($"##Server.{server.DataCenterName}.{server.WorldName}", columns,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("单价", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("小计", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("市场时间", ImGuiTableColumnFlags.WidthFixed, 105);
        if (!configuration.SimpleInterface)
        {
            ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 55);
            ImGui.TableSetupColumn("类型", ImGuiTableColumnFlags.WidthFixed, 70);
        }
        ImGui.TableHeadersRow();

        foreach (var selected in server.Listings)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(selected.ItemName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(selected.Listing.Quantity.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(selected.Listing.PricePerUnit));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(selected.Listing.TotalPrice));
            ImGui.TableNextColumn();
            DrawDataAge(selected.Listing.LastReviewTime);
            if (!configuration.SimpleInterface)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(selected.Listing.IsHighQuality ? "HQ" : "NQ");
                ImGui.TableNextColumn();
                ImGui.TextDisabled("完整挂单");
            }
        }
        ImGui.EndTable();
    }

    private void RequestQuoteAndOpen()
    {
        if (shoppingListService.Entries.Count == 0)
        {
            SetPage(WorkspacePage.ShoppingList);
            transientMessage = "请先添加至少一种物品。";
            return;
        }
        openQuotesAfterRefresh = true;
        routeDataCenter = null;
        priceRefreshService.RequestRefresh();
        SetPage(WorkspacePage.Quotes);
    }

    private void OpenBestRoute()
    {
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null)
        {
            RequestQuoteAndOpen();
            return;
        }
        routeDataCenter = snapshot.CheapestCompletePlan?.DataCenterName ?? snapshot.Plans.Keys.FirstOrDefault();
        SetPage(WorkspacePage.Route);
    }

    private void StartCheapestPlan()
    {
        var snapshot = priceRefreshService.Snapshot;
        var plan = snapshot?.CheapestCompletePlan;
        if (snapshot is null || plan is null)
        {
            SetPage(WorkspacePage.Quotes);
            return;
        }
        routeDataCenter = plan.DataCenterName;
        purchaseSessionService.Start(snapshot, plan);
        SetPage(WorkspacePage.Session);
    }

    private bool SnapshotMatchesActiveList()
    {
        var snapshot = priceRefreshService.Snapshot;
        return snapshot is not null &&
               snapshot.ShoppingListId == shoppingListService.ActiveList.ListId &&
               snapshot.CompletedAt >= shoppingListService.ActiveList.UpdatedAt;
    }

    private void SetPage(WorkspacePage page)
    {
        currentPage = page;
        configuration.LastPage = page;
        configuration.Save();
    }

    private void ResetItemSearch()
    {
        itemSearchText = string.Empty;
        selectedSearchResult = null;
        searchResults = [];
        quantityInput = 1;
        qualityInput = PurchaseQuality.Any;
    }

    private static void DrawPageTitle(string title, string description)
    {
        ImGui.TextUnformatted(title);
        ImGui.TextDisabled(description);
        ImGui.Spacing();
    }

    private static void DrawSectionHeader(string title)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(title);
        ImGui.Separator();
        ImGui.Spacing();
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
        if (!ImGui.BeginCombo("路线方案", selectedDataCenter))
            return;
        foreach (var plan in GetOrderedPlans(snapshot))
        {
            var selected = string.Equals(selectedDataCenter, plan.DataCenterName, StringComparison.Ordinal);
            if (ImGui.Selectable(plan.DataCenterName, selected))
                selectedDataCenter = plan.DataCenterName;
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private static IReadOnlyList<DataCenterPurchasePlan> GetOrderedPlans(PriceComparisonSnapshot snapshot)
    {
        var ordered = new List<DataCenterPurchasePlan>();
        foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
        {
            if (snapshot.Plans.TryGetValue(dataCenter, out var plan))
                ordered.Add(plan);
        }
        if (snapshot.Plans.TryGetValue(DataCenterCatalog.CrossDataCenterPlanName, out var crossPlan))
            ordered.Add(crossPlan);
        return ordered;
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

    private string GetFreshnessLabel(DataCenterPurchasePlan? plan)
    {
        if (plan is null)
            return "无完整方案";
        return GetConfidenceLabel(plan);
    }

    private string GetConfidenceLabel(DataCenterPurchasePlan plan)
    {
        if (!plan.IsComplete)
            return "不完整";
        var stale = plan.StaleItemCount(TimeSpan.FromHours(Math.Max(1, configuration.StaleDataWarningHours)));
        if (stale == 0 && plan.LowLiquidityItems == 0)
            return "较高";
        if (stale <= Math.Max(1, plan.TotalItems / 4) && plan.LowLiquidityItems <= 1)
            return "中等";
        return "较低";
    }

    private static string GetQualityLabel(PurchaseQuality quality, bool supportsHighQuality)
    {
        if (!supportsHighQuality)
            return "不区分";
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

    private static string GetStrategyDescription(PurchaseStrategy strategy)
    {
        return strategy switch
        {
            PurchaseStrategy.LowestPrice => "优先最低 Gil 成本",
            PurchaseStrategy.FewestServers => "优先减少换服次数",
            _ => "综合价格与服务器数量",
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

    private static string GetImportFormatLabel(ImportTextFormat format)
    {
        return format switch
        {
            ImportTextFormat.PlainText => "普通文本",
            ImportTextFormat.Csv => "CSV 或制表符",
            ImportTextFormat.Json => "JSON",
            _ => "自动识别",
        };
    }

    private static string GetImportModeLabel(ShoppingListImportMode mode)
    {
        return mode switch
        {
            ShoppingListImportMode.Replace => "替换当前清单",
            ShoppingListImportMode.CreateNew => "创建新清单",
            _ => "追加到当前清单",
        };
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

    private sealed record NextAction(string Label, string Description, bool Disabled, Action Action);
}
