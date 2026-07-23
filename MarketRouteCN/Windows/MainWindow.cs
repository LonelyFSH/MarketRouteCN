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
            MinimumSize = configuration.CompactMode ? new Vector2(840, 570) : new Vector2(980, 650),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        ObserveRefreshResult();
        DrawTopBar();
        ImGui.Separator();

        var sidebarWidth = configuration.CompactMode ? 150f : 190f;
        ImGui.BeginChild("##MarketRouteCN.Sidebar", new Vector2(sidebarWidth, 0), true);
        DrawSidebar();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##MarketRouteCN.Content", Vector2.Zero, false);
        DrawWorkflowStrip();
        ImGui.Separator();
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
        ImGui.TextDisabled("V0.8");
        ImGui.SameLine();
        ImGui.TextDisabled($"当前清单 {shoppingListService.ActiveList.Name}");

        if (purchaseSessionService.Session is { State: PurchaseSessionState.Active or PurchaseSessionState.Paused } session)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"继续采购 {session.PurchasedListings}/{session.TotalListings}"))
                SetPage(WorkspacePage.Session);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(shoppingListService.Entries.Count == 0 || priceRefreshService.IsRefreshing);
        if (ImGui.SmallButton("立即刷新并看报价"))
            RequestQuoteAndOpen();
        ImGui.EndDisabled();

        if (priceRefreshService.IsRefreshing)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.25f, 1f), "正在查询与优化");
            ImGui.SameLine();
            if (ImGui.SmallButton("取消"))
                priceRefreshService.CancelRefresh();
        }

        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"最近查询 {snapshot.CompletedAt.ToLocalTime():HH:mm:ss}");
        }

        if (priceRefreshService.NextRefreshTime is { } nextRefresh)
        {
            var remaining = nextRefresh - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            ImGui.SameLine();
            ImGui.TextDisabled($"自动刷新 {remaining:mm\\:ss}");
        }

        if (!string.IsNullOrWhiteSpace(priceRefreshService.LastError))
            ImGui.TextColored(new Vector4(0.95f, 0.3f, 0.3f, 1f), priceRefreshService.LastError);
        if (!string.IsNullOrWhiteSpace(transientMessage))
            ImGui.TextColored(new Vector4(0.35f, 0.8f, 0.95f, 1f), transientMessage);
    }

    private void DrawSidebar()
    {
        ImGui.TextUnformatted("工作区");
        ImGui.Spacing();
        DrawNavigationButton(WorkspacePage.Overview, "概览", "下一步与状态");
        DrawNavigationButton(WorkspacePage.ShoppingList, "采购清单", $"{shoppingListService.Entries.Count} 种物品");
        DrawNavigationButton(WorkspacePage.Import, "导入导出", "文本 CSV JSON");
        DrawNavigationButton(WorkspacePage.Quotes, "四区报价", priceRefreshService.Snapshot is null ? "尚未查询" : "已有结果");
        DrawNavigationButton(WorkspacePage.Route, "采购路线", routeDataCenter ?? "等待方案");

        var session = purchaseSessionService.Session;
        var sessionStatus = session is null ? "未开始" : $"{session.PurchasedListings}/{session.TotalListings}";
        DrawNavigationButton(WorkspacePage.Session, "采购进度", sessionStatus);
        DrawNavigationButton(WorkspacePage.History, "报价历史", $"{quoteHistoryService.History.Count} 条");
        DrawNavigationButton(WorkspacePage.Settings, "设置", GetStrategyLabel(configuration.Strategy));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("快捷命令");
        ImGui.TextDisabled("/mrcn quote");
        ImGui.TextDisabled("/mrcn route");
        ImGui.TextDisabled("/mrcn session");
    }

    private void DrawNavigationButton(WorkspacePage page, string title, string detail)
    {
        var marker = currentPage == page ? "当前 " : string.Empty;
        if (ImGui.Button($"{marker}{title}##Nav.{page}", new Vector2(-1, 30)))
            SetPage(page);
        ImGui.TextDisabled(detail);
        ImGui.Spacing();
    }

    private void DrawWorkflowStrip()
    {
        if (!ImGui.BeginTable("##WorkflowStrip", 4, ImGuiTableFlags.SizingStretchSame))
            return;

        ImGui.TableNextColumn();
        if (ImGui.Button($"1 清单 {shoppingListService.Entries.Count} 项##Flow.List", new Vector2(-1, 34)))
            SetPage(WorkspacePage.ShoppingList);

        ImGui.TableNextColumn();
        var quoteReady = SnapshotMatchesActiveList();
        var quoteLabel = quoteReady ? "2 报价 已完成" : "2 报价 待查询";
        if (ImGui.Button($"{quoteLabel}##Flow.Quote", new Vector2(-1, 34)))
        {
            if (quoteReady)
                SetPage(WorkspacePage.Quotes);
            else
                RequestQuoteAndOpen();
        }

        ImGui.TableNextColumn();
        ImGui.BeginDisabled(priceRefreshService.Snapshot is null);
        if (ImGui.Button("3 路线 直接查看##Flow.Route", new Vector2(-1, 34)))
            OpenBestRoute();
        ImGui.EndDisabled();

        ImGui.TableNextColumn();
        var session = purchaseSessionService.Session;
        var sessionLabel = session is null ? "4 采购 采用方案" : "4 采购 继续进度";
        ImGui.BeginDisabled(session is null && priceRefreshService.Snapshot?.CheapestCompletePlan is null);
        if (ImGui.Button($"{sessionLabel}##Flow.Session", new Vector2(-1, 34)))
        {
            if (session is not null)
                SetPage(WorkspacePage.Session);
            else
                StartCheapestPlan();
        }
        ImGui.EndDisabled();

        ImGui.EndTable();
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
        DrawPageTitle("概览", "在一个页面完成下一步，也可以直接跳到报价、路线或采购进度。 ");

        if (configuration.ShowOnboarding)
        {
            ImGui.BeginChild("##Onboarding", new Vector2(0, 118), true);
            ImGui.TextUnformatted("推荐流程");
            ImGui.TextWrapped("建立或导入清单后，点击查询并打开报价。选择任意大区即可直接跳到路线，也可以从报价卡片直接开始采购。自动刷新只更新报价，不会静默替换正在执行的采购会话。 ");
            if (ImGui.SmallButton("已了解并隐藏说明"))
            {
                configuration.ShowOnboarding = false;
                configuration.Save();
            }
            ImGui.EndChild();
            ImGui.Spacing();
        }

        DrawOverviewMetrics();
        ImGui.Spacing();

        var nextAction = GetNextAction();
        ImGui.BeginChild("##NextAction", new Vector2(0, 110), true);
        ImGui.TextUnformatted("建议的下一步");
        ImGui.TextWrapped(nextAction.Description);
        ImGui.BeginDisabled(nextAction.Disabled);
        if (ImGui.Button(nextAction.Label, new Vector2(260, 38)))
            nextAction.Action();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("导入现有清单", new Vector2(170, 38)))
            SetPage(WorkspacePage.Import);
        ImGui.EndChild();

        DrawSectionHeader("快捷入口");
        if (ImGui.BeginTable("##OverviewActions", 3, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            if (ImGui.Button("添加物品", new Vector2(-1, 42)))
                SetPage(WorkspacePage.ShoppingList);
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(shoppingListService.Entries.Count == 0 || priceRefreshService.IsRefreshing);
            if (ImGui.Button("查询并打开四区报价", new Vector2(-1, 42)))
                RequestQuoteAndOpen();
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(priceRefreshService.Snapshot?.CheapestCompletePlan is null);
            if (ImGui.Button("采用最低完整方案并开始", new Vector2(-1, 42)))
                StartCheapestPlan();
            ImGui.EndDisabled();
            ImGui.EndTable();
        }

        DrawSectionHeader("最近报价");
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null)
        {
            ImGui.TextDisabled("尚未生成报价。 ");
            return;
        }

        ImGui.TextUnformatted($"请求时间 {snapshot.RequestedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        ImGui.SameLine();
        ImGui.TextDisabled($"完成时间 {snapshot.CompletedAt.ToLocalTime():HH:mm:ss}");
        DrawCompactQuoteTable(snapshot);
    }

    private void DrawOverviewMetrics()
    {
        var snapshot = priceRefreshService.Snapshot;
        var cheapest = snapshot?.CheapestCompletePlan;
        var session = purchaseSessionService.Session;

        if (!ImGui.BeginTable("##OverviewMetrics", 4, ImGuiTableFlags.SizingStretchSame))
            return;

        DrawMetricCell("当前清单", $"{shoppingListService.Entries.Count} 种", shoppingListService.ActiveList.Name);
        DrawMetricCell("最低完整报价", cheapest is null ? "暂无" : $"{FormatGil(cheapest.TotalCost)} Gil", cheapest?.DataCenterName ?? "等待查询");
        DrawMetricCell("报价数据", snapshot is null ? "暂无" : GetFreshnessLabel(cheapest), snapshot is null ? "等待查询" : snapshot.CompletedAt.ToLocalTime().ToString("HH:mm:ss"));
        DrawMetricCell("采购进度", session is null ? "未开始" : $"{session.PurchasedListings}/{session.TotalListings}", session?.DataCenterName ?? "采用路线后开始");

        ImGui.EndTable();
    }

    private static void DrawMetricCell(string title, string value, string detail)
    {
        ImGui.TableNextColumn();
        ImGui.BeginChild($"##Metric.{title}", new Vector2(0, 92), true);
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
        DrawSectionHeader("报价与路线设置");
        if (ImGui.RadioButton("一个大区内购齐", configuration.Scope == PurchaseScope.SingleDataCenter))
        {
            configuration.Scope = PurchaseScope.SingleDataCenter;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("比较四个大区分别购齐", configuration.Scope == PurchaseScope.CompareAllDataCenters))
        {
            configuration.Scope = PurchaseScope.CompareAllDataCenters;
            configuration.Save();
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
        ImGui.SameLine();
        ImGui.TextDisabled(GetStrategyDescription(configuration.Strategy));
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

        ImGui.InputTextMultiline("##ImportText", ref importText, 30_000u, new Vector2(-1, 230));
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
        DrawPageTitle("四区报价", "同一张完整清单分别在四个大区计算。点击卡片即可直接打开路线或开始采购。 ");
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null)
        {
            DrawEmptyQuoteState();
            return;
        }

        if (snapshot.ShoppingListId != shoppingListService.ActiveList.ListId)
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.25f, 1f), "当前显示的是其他清单的报价。重新查询可切换到当前清单。 ");
        else if (snapshot.CompletedAt < shoppingListService.ActiveList.UpdatedAt)
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.25f, 1f), "当前清单在报价后已修改，价格结果可能不再对应最新数量。 ");

        ImGui.TextUnformatted($"{snapshot.ShoppingListName}  请求 {snapshot.RequestedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        ImGui.SameLine();
        ImGui.BeginDisabled(priceRefreshService.IsRefreshing);
        if (ImGui.SmallButton("重新查询当前清单"))
            RequestQuoteAndOpen();
        ImGui.EndDisabled();

        var cheapest = snapshot.CheapestCompletePlan;
        if (cheapest is not null)
        {
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 0.45f, 1f), $"最低完整方案 {cheapest.DataCenterName}  {FormatGil(cheapest.TotalCost)} Gil  {cheapest.ServerCount} 个服务器");
            if (configuration.EnableTargetTotalPrice)
            {
                var reached = cheapest.TotalCost <= configuration.TargetTotalPriceGil;
                var color = reached ? new Vector4(0.35f, 0.85f, 0.45f, 1f) : new Vector4(0.95f, 0.75f, 0.25f, 1f);
                var difference = Math.Abs(cheapest.TotalCost - configuration.TargetTotalPriceGil);
                ImGui.TextColored(color, reached
                    ? $"已达到目标总价  低于目标 {FormatGil(difference)} Gil"
                    : $"尚未达到目标总价  高于目标 {FormatGil(difference)} Gil");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), "当前没有能够购齐全部商品的大区方案。 ");
        }

        if (ImGui.BeginTable("##QuoteCards", 2, ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
            {
                if (!snapshot.Plans.TryGetValue(dataCenter, out var plan))
                    continue;
                ImGui.TableNextColumn();
                DrawQuoteCard(snapshot, plan, ReferenceEquals(plan, cheapest));
            }
            ImGui.EndTable();
        }

        ImGui.TextDisabled("当前价按完整挂单计算。风险价表示最脆弱的已选挂单失效后，能够重新购齐时的估算成本。 ");
    }

    private void DrawQuoteCard(PriceComparisonSnapshot snapshot, DataCenterPurchasePlan plan, bool cheapest)
    {
        var height = configuration.CompactMode ? 235f : 265f;
        ImGui.BeginChild($"##QuoteCard.{plan.DataCenterName}", new Vector2(0, height), true);
        ImGui.TextUnformatted(cheapest ? $"{plan.DataCenterName}  最低完整方案" : plan.DataCenterName);
        ImGui.Separator();

        ImGui.TextUnformatted(plan.IsComplete ? $"{FormatGil(plan.TotalCost)} Gil" : $"已知价格 {FormatGil(plan.TotalCost)} Gil");
        ImGui.TextDisabled(plan.IsComplete ? "完整度 100%" : $"完整度 {plan.CompletedItems}/{plan.TotalItems}");
        ImGui.TextUnformatted($"服务器 {plan.ServerCount}  超额 {plan.OverbuyUnits}");

        var previous = quoteHistoryService.FindPreviousQuote(snapshot.ShoppingListId, snapshot.SnapshotId, plan.DataCenterName);
        if (previous is not null && previous.TotalCost > 0 && plan.TotalCost > 0)
        {
            var delta = plan.TotalCost - previous.TotalCost;
            var trend = delta == 0 ? "与上次相同" : delta < 0 ? $"比上次低 {FormatGil(-delta)}" : $"比上次高 {FormatGil(delta)}";
            ImGui.TextDisabled(trend);
        }
        else
        {
            ImGui.TextDisabled("暂无同清单历史对比");
        }

        if (plan.RiskCostIncrease > 0)
            ImGui.TextUnformatted($"风险价 {FormatGil(plan.RiskAdjustedCost)}  波动 {FormatGil(plan.RiskCostIncrease)}");
        else
            ImGui.TextDisabled("当前未计算出额外价格风险");

        ImGui.TextUnformatted($"数据可信度 {GetConfidenceLabel(plan)}");
        ImGui.TextDisabled($"旧数据 {plan.StaleItemCount(TimeSpan.FromHours(Math.Max(1, configuration.StaleDataWarningHours)))}/{plan.TotalItems}  低流动 {plan.LowLiquidityItems}");
        ImGui.TextUnformatted("最旧市场数据");
        ImGui.SameLine();
        DrawDataAge(plan.OldestMarketDataTime);

        if (ImGui.Button($"查看 {plan.DataCenterName} 路线##OpenRoute.{plan.DataCenterName}", new Vector2(-1, 30)))
        {
            routeDataCenter = plan.DataCenterName;
            SetPage(WorkspacePage.Route);
        }
        ImGui.BeginDisabled(!plan.IsComplete || plan.ServerCount == 0);
        if (ImGui.Button($"采用并立即开始采购##Start.{plan.DataCenterName}", new Vector2(-1, 30)))
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
        DrawPageTitle("采购路线", "先选择服务器组合，再按站查看完整挂单。可以从这里一步进入采购进度。 ");
        var snapshot = priceRefreshService.Snapshot;
        if (snapshot is null || snapshot.Plans.Count == 0)
        {
            ImGui.TextDisabled("尚无可显示路线。 ");
            if (ImGui.Button("查询并打开报价"))
                RequestQuoteAndOpen();
            return;
        }

        routeDataCenter ??= snapshot.CheapestCompletePlan?.DataCenterName ?? snapshot.Plans.Keys.FirstOrDefault();
        if (routeDataCenter is null)
            return;

        ImGui.SetNextItemWidth(180);
        DrawAvailablePlanCombo(snapshot, ref routeDataCenter);
        if (!snapshot.Plans.TryGetValue(routeDataCenter, out var plan))
            return;

        ImGui.TextUnformatted($"{plan.DataCenterName}  {GetStrategyLabel(plan.Strategy)}  {FormatGil(plan.TotalCost)} Gil  {plan.ServerCount} 个服务器");
        ImGui.SameLine();
        if (ImGui.SmallButton("返回四区报价"))
            SetPage(WorkspacePage.Quotes);

        ImGui.BeginDisabled(!plan.IsComplete || plan.ServerCount == 0);
        if (ImGui.Button("采用路线并直接进入采购进度", new Vector2(280, 38)))
        {
            purchaseSessionService.Start(snapshot, plan);
            SetPage(WorkspacePage.Session);
        }
        ImGui.EndDisabled();

        DrawRouteAlternatives(plan);
        DrawSectionHeader("服务器采购顺序");
        var station = 1;
        foreach (var server in plan.ServerPlans)
        {
            var label = $"第 {station} 站  {server.WorldName}  {server.Listings.Count} 个挂单  {FormatGil(server.TotalCost)} Gil##Route.{server.WorldName}";
            if (ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen))
                DrawServerPlan(server);
            station++;
        }

        var incomplete = plan.ItemPlans.Where(static item => !item.IsComplete).ToArray();
        if (incomplete.Length > 0)
        {
            DrawSectionHeader("未完成项目");
            foreach (var item in incomplete)
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.3f, 1f), $"{item.Request.DisplayName} 需要 {item.Request.Quantity}  当前规划 {item.PurchasedQuantity}  {GetMarketStatusLabel(item.DataStatus)}");
        }
    }

    private static void DrawRouteAlternatives(DataCenterPurchasePlan plan)
    {
        DrawSectionHeader("服务器数量与价格取舍");
        if (plan.Alternatives.Count == 0)
        {
            ImGui.TextDisabled("没有完整的备选路线。 ");
            return;
        }

        if (!ImGui.BeginTable("##Alternatives", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("服务器数", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("最低总价", ImGuiTableColumnFlags.WidthFixed, 125);
        ImGui.TableSetupColumn("增加服务器后的节省", ImGuiTableColumnFlags.WidthFixed, 145);
        ImGui.TableSetupColumn("超额", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("服务器");
        ImGui.TableHeadersRow();

        long? previousCost = null;
        foreach (var alternative in plan.Alternatives)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(alternative.ServerCount.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(alternative.TotalCost));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(previousCost is null ? "起始方案" : FormatGil(Math.Max(0, previousCost.Value - alternative.TotalCost)));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(alternative.OverbuyQuantity.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(string.Join("、", alternative.Worlds));
            previousCost = alternative.TotalCost;
        }
        ImGui.EndTable();
    }

    private void DrawSessionPage()
    {
        DrawPageTitle("采购进度", "完成当前站后可直接进入下一站。刷新剩余项目不会自动覆盖正在执行的路线。 ");
        var session = purchaseSessionService.Session;
        if (session is null)
        {
            ImGui.TextDisabled("尚未开始采购会话。 ");
            ImGui.BeginDisabled(priceRefreshService.Snapshot?.CheapestCompletePlan is null);
            if (ImGui.Button("采用最低完整方案并开始采购", new Vector2(280, 38)))
                StartCheapestPlan();
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("打开报价选择其他大区", new Vector2(210, 38)))
                SetPage(WorkspacePage.Quotes);
            return;
        }

        ImGui.TextUnformatted($"{session.ShoppingListName}  {session.DataCenterName}  {GetSessionStateLabel(session.State)}");
        ImGui.TextUnformatted($"计划 {FormatGil(session.PlannedCost)} Gil  已确认 {FormatGil(session.ConfirmedCost)} Gil");
        var progress = session.TotalListings == 0 ? 0f : (float)session.PurchasedListings / session.TotalListings;
        ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{session.PurchasedListings}/{session.TotalListings}  {progress:P0}");

        var currentWorld = purchaseSessionService.CurrentWorld;
        if (currentWorld is not null)
        {
            ImGui.BeginChild("##CurrentStation", new Vector2(0, 92), true);
            ImGui.TextUnformatted($"当前站  {currentWorld}");
            var currentListings = session.Listings.Where(item => string.Equals(item.WorldName, currentWorld, StringComparison.Ordinal)).ToArray();
            ImGui.TextDisabled($"剩余 {currentListings.Count(static item => !item.IsPurchased)} 个挂单  本站 {FormatGil(currentListings.Sum(static item => item.TotalPrice))} Gil");
            ImGui.BeginDisabled(currentListings.All(static item => item.IsPurchased));
            if (ImGui.Button("完成当前站并进入下一站", new Vector2(230, 32)))
                purchaseSessionService.CompleteCurrentWorldAndAdvance();
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("只进入下一站", new Vector2(150, 32)))
                purchaseSessionService.AdvanceToNextWorld();
            ImGui.EndChild();
        }

        if (session.State == PurchaseSessionState.Active)
        {
            if (ImGui.SmallButton("暂停会话"))
                purchaseSessionService.Pause();
        }
        else if (session.State == PurchaseSessionState.Paused)
        {
            if (ImGui.SmallButton("继续会话"))
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
        if (ImGui.SmallButton("标记全部完成"))
            purchaseSessionService.Finish();
        ImGui.SameLine();
        if (ImGui.SmallButton("取消会话"))
            purchaseSessionService.Cancel();
        ImGui.SameLine();
        if (ImGui.SmallButton("删除会话记录"))
            purchaseSessionService.RemoveSession();

        var refreshed = priceRefreshService.Snapshot;
        if (refreshed?.SourceSessionId == session.SessionId && refreshed.Plans.TryGetValue(session.DataCenterName, out var refreshedPlan))
        {
            DrawSectionHeader("剩余项目新方案");
            ImGui.TextUnformatted($"新路线 {FormatGil(refreshedPlan.TotalCost)} Gil  {refreshedPlan.ServerCount} 个服务器");
            ImGui.BeginDisabled(!refreshedPlan.IsComplete);
            if (ImGui.Button("应用新路线并继续采购"))
                purchaseSessionService.ReplaceRemainingPlan(refreshed, refreshedPlan);
            ImGui.EndDisabled();
        }

        DrawInventorySuggestions();
        DrawSectionHeader("所有采购站");
        var worlds = session.Worlds;
        for (var index = 0; index < worlds.Count; index++)
        {
            var world = worlds[index];
            var listings = session.Listings.Where(item => string.Equals(item.WorldName, world, StringComparison.Ordinal)).ToArray();
            var completed = listings.All(static listing => listing.IsPurchased);
            var current = index == session.CurrentServerIndex;
            var flags = current ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.CollapsingHeader($"{index + 1}  {world}  {listings.Count(item => item.IsPurchased)}/{listings.Length}{(current ? "  当前" : string.Empty)}##Session.{world}", flags))
                continue;

            if (ImGui.SmallButton($"设为当前站##{world}"))
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
        if (listings.Count == 0)
            return;
        if (!ImGui.BeginTable($"##SessionListings.{listings[0].WorldName}", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
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
        DrawPageTitle("设置", "调整路线权重、刷新频率、目标总价和界面密度。 ");
        DrawSectionHeader("界面");
        var compact = configuration.CompactMode;
        if (ImGui.Checkbox("紧凑模式", ref compact))
        {
            configuration.CompactMode = compact;
            configuration.Save();
        }
        var onboarding = configuration.ShowOnboarding;
        if (ImGui.Checkbox("在概览显示使用说明", ref onboarding))
        {
            configuration.ShowOnboarding = onboarding;
            configuration.Save();
        }

        DrawSectionHeader("目标价格");
        var enableTarget = configuration.EnableTargetTotalPrice;
        if (ImGui.Checkbox("启用清单总价目标", ref enableTarget))
        {
            configuration.EnableTargetTotalPrice = enableTarget;
            configuration.Save();
        }
        var targetPrice = checked((int)Math.Clamp(configuration.TargetTotalPriceGil, 0, int.MaxValue));
        ImGui.SetNextItemWidth(190);
        if (ImGui.InputInt("目标总价 Gil", ref targetPrice, 10_000, 100_000))
        {
            configuration.TargetTotalPriceGil = Math.Max(0, targetPrice);
            configuration.Save();
        }

        DrawSectionHeader("路线策略");
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
        if (ImGui.InputInt("每增加一个服务器的成本", ref serverThreshold, 10_000, 50_000))
        {
            configuration.AdditionalServerSavingsThreshold = Math.Max(0, serverThreshold);
            configuration.Save();
        }
        ImGui.TextDisabled("平衡模式中，新增服务器只有带来足够价格优势时才会被选择。 ");

        var overbuyPenalty = configuration.OverbuyPenaltyPerUnit;
        ImGui.SetNextItemWidth(190);
        if (ImGui.InputInt("每个超额单位惩罚", ref overbuyPenalty, 10, 100))
        {
            configuration.OverbuyPenaltyPerUnit = Math.Max(0, overbuyPenalty);
            configuration.Save();
        }

        var stalePenalty = configuration.StaleDataPenaltyPerHour;
        ImGui.SetNextItemWidth(190);
        if (ImGui.InputInt("每小时旧数据惩罚", ref stalePenalty, 100, 1000))
        {
            configuration.StaleDataPenaltyPerHour = Math.Max(0, stalePenalty);
            configuration.Save();
        }

        var staleWarning = configuration.StaleDataWarningHours;
        ImGui.SetNextItemWidth(190);
        if (ImGui.InputInt("旧数据警告小时", ref staleWarning, 1, 2))
        {
            configuration.StaleDataWarningHours = Math.Clamp(staleWarning, 1, 48);
            configuration.Save();
        }

        DrawSectionHeader("价格刷新");
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

        var cacheMinutes = configuration.CacheMinutes;
        ImGui.SetNextItemWidth(190);
        if (ImGui.InputInt("本地价格缓存分钟", ref cacheMinutes, 1, 5))
        {
            configuration.CacheMinutes = Math.Clamp(cacheMinutes, 0, 60);
            configuration.Save();
        }

        var historyLimit = configuration.SnapshotHistoryLimit;
        ImGui.SetNextItemWidth(190);
        if (ImGui.InputInt("保存报价快照数量", ref historyLimit, 1, 5))
        {
            configuration.SnapshotHistoryLimit = Math.Clamp(historyLimit, 1, 50);
            configuration.Save();
        }

        var inventorySuggestions = configuration.EnableInventorySuggestions;
        if (ImGui.Checkbox("检测主背包增加并建议计入采购", ref inventorySuggestions))
        {
            configuration.EnableInventorySuggestions = inventorySuggestions;
            configuration.Save();
        }

        DrawSectionHeader("数据说明");
        ImGui.TextWrapped("物品名称和可交易状态来自本地游戏数据。挂单来自 Universalis 众包接口，不保证与游戏内当前交易板完全一致。插件按完整挂单计算，只做查询、规划和进度记录，不会自动跨服或自动购买。 ");
    }

    private void DrawCompactQuoteTable(PriceComparisonSnapshot snapshot)
    {
        if (!ImGui.BeginTable("##CompactQuotes", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("大区");
        ImGui.TableSetupColumn("完成度");
        ImGui.TableSetupColumn("总价");
        ImGui.TableSetupColumn("服务器");
        ImGui.TableSetupColumn("操作");
        ImGui.TableHeadersRow();
        foreach (var dataCenter in DataCenterCatalog.ChinaDataCenters)
        {
            if (!snapshot.Plans.TryGetValue(dataCenter, out var plan))
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.DataCenterName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.IsComplete ? "100%" : $"{plan.CompletedItems}/{plan.TotalItems}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatGil(plan.TotalCost));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(plan.ServerCount.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"打开路线##Compact.{plan.DataCenterName}"))
            {
                routeDataCenter = plan.DataCenterName;
                SetPage(WorkspacePage.Route);
            }
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

    private static void DrawServerPlan(ServerPurchasePlan server)
    {
        if (!ImGui.BeginTable($"##Server.{server.WorldName}", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("物品", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupColumn("品质", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("数量", ImGuiTableColumnFlags.WidthFixed, 75);
        ImGui.TableSetupColumn("单价", ImGuiTableColumnFlags.WidthFixed, 95);
        ImGui.TableSetupColumn("小计", ImGuiTableColumnFlags.WidthFixed, 105);
        ImGui.TableSetupColumn("市场时间", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("挂单风险", ImGuiTableColumnFlags.WidthFixed, 90);
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
            ImGui.TableNextColumn();
            ImGui.TextDisabled("完整挂单");
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
