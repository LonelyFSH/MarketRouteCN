using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MarketRouteCN.Services;
using MarketRouteCN.Windows;

namespace MarketRouteCN;

public sealed class Plugin : IDalamudPlugin
{
    private const string PrimaryCommand = "/marketroute";
    private const string ShortCommand = "/mrcn";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly UniversalisClient universalisClient;
    private readonly PriceRefreshService priceRefreshService;
    private readonly MainWindow mainWindow;

    // 初始化插件服务
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IDataManager dataManager,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(pluginInterface);

        var itemCatalogService = new ItemCatalogService(dataManager, log);
        var shoppingListService = new ShoppingListService(Configuration);
        universalisClient = new UniversalisClient(log);
        var optimizer = new PurchaseOptimizer();
        priceRefreshService = new PriceRefreshService(
            Configuration,
            framework,
            universalisClient,
            optimizer,
            log);

        mainWindow = new MainWindow(
            Configuration,
            itemCatalogService,
            shoppingListService,
            priceRefreshService);

        WindowSystem.AddWindow(mainWindow);

        commandManager.AddHandler(PrimaryCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 MarketRoute CN。",
        });
        commandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 MarketRoute CN。",
        });

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;

        log.Information("MarketRoute CN V0.1 initialized.");
    }

    public Configuration Configuration { get; }

    public WindowSystem WindowSystem { get; } = new("MarketRouteCN");

    // 释放事件和资源
    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;

        commandManager.RemoveHandler(PrimaryCommand);
        commandManager.RemoveHandler(ShortCommand);

        WindowSystem.RemoveAllWindows();
        priceRefreshService.Dispose();
        universalisClient.Dispose();
    }

    public void ToggleMainUi()
    {
        mainWindow.Toggle();
    }

    private void OnCommand(string _, string __)
    {
        mainWindow.Toggle();
    }
}
