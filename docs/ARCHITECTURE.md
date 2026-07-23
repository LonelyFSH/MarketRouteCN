# Architecture

MarketRoute CN V0.5 分为五层。

ItemCatalogService 负责读取本地可交易物品。

UniversalisClient 负责分批请求、缓存、重试和解析市场挂单。

PurchaseOptimizer 负责完整挂单组合和服务器子集优化。

PriceRefreshService 负责报价快照、自动刷新和历史保存。

PurchaseSessionService 负责采购过程、恢复和背包变化建议。
