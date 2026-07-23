using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MarketRouteCN.Services;
using MarketRouteCN.Windows;

namespace MarketRouteCN;

public sealed class Plugin : IDalamudPlugin
{
    private const string ShortCommand = "/mrcn";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly UniversalisClient universalisClient;
    private readonly PriceRefreshService priceRefreshService;
    private readonly PurchaseSessionService purchaseSessionService;
    private readonly MainWindow mainWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IDataManager dataManager,
        IGameInventory gameInventory,
        IMarketBoard marketBoard,
        IPlayerState playerState,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(pluginInterface);

        var itemCatalogService = new ItemCatalogService(dataManager, log);
        var shoppingListService = new ShoppingListService(Configuration);
        var transferService = new ShoppingListTransferService(itemCatalogService);
        var quoteHistoryService = new QuoteHistoryService(Configuration);
        universalisClient = new UniversalisClient(log);
        var optimizer = new PurchaseOptimizer();
        priceRefreshService = new PriceRefreshService(
            Configuration,
            framework,
            universalisClient,
            optimizer,
            quoteHistoryService,
            log);
        purchaseSessionService = new PurchaseSessionService(
            Configuration,
            framework,
            gameInventory,
            marketBoard,
            playerState,
            log);

        mainWindow = new MainWindow(
            Configuration,
            itemCatalogService,
            shoppingListService,
            transferService,
            quoteHistoryService,
            priceRefreshService,
            purchaseSessionService);

        WindowSystem.AddWindow(mainWindow);
        commandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开MarketRouteCN",
        });

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;

        log.Information("MarketRoute CN V0.9.5.0 initialized.");
    }

    public Configuration Configuration { get; }

    public WindowSystem WindowSystem { get; } = new("MarketRouteCN");

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;

        commandManager.RemoveHandler(ShortCommand);

        WindowSystem.RemoveAllWindows();
        purchaseSessionService.Dispose();
        priceRefreshService.Dispose();
        universalisClient.Dispose();
    }

    public void ToggleMainUi()
    {
        mainWindow.Toggle();
    }

    private void OpenSettings()
    {
        mainWindow.HandleCommand("settings");
    }

    private void OnCommand(string _, string arguments)
    {
        mainWindow.HandleCommand(arguments);
    }
}
