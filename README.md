# MarketRoute CN

面向 **FF14 国服**交易板的全品类批量采购、四大区报价比较和大区内跨服采购路线规划插件。

## V0.1 功能

- 通过国服客户端本地物品表检索可交易物品；
- 玩家从候选结果中选择物品，不保存未经确认的自由文本；
- 支持数量以及“任意 / HQ / NQ”品质条件；
- 不支持 HQ 的物品会自动锁定为“不区分品质”；
- 支持两种范围：
  - 在玩家指定的一个大区内购齐整张清单；
  - 分别计算陆行鸟、莫古力、猫小胖、豆豆柴购齐同一张清单的预计总价；
- 按完整挂单选择采购组合，考虑交易板不能拆分购买的情况；
- 按服务器分组显示采购路线；
- 显示插件查询时间、市场数据年龄和具体挂单记录时间；
- 支持关闭、5、10、15、30、60 分钟自动刷新。

## 重要说明

市场挂单来自 [Universalis](https://universalis.app/) 的众包 API，而不是盛趣或 Square Enix 的实时市场接口。插件刚刚刷新，不代表相关商品的市场数据刚刚被玩家上传。请始终核对界面中的“市场数据时间”。

V0.1 只读取、比较和规划，不自动切换服务器、不自动操作交易板，也不自动购买。

## 命令

```text
/marketroute
/mrcn
```

## 开发环境

- Dalamud API 15
- .NET 10
- x64
- `Dalamud.NET.Sdk/15.0.0`

Dalamud v15 使用 API Level 15 和 .NET 10，项目采用官方推荐的 `Dalamud.NET.Sdk` 项目结构。

## 本地编译

```powershell
dotnet restore .\MarketRouteCN.sln
dotnet build .\MarketRouteCN.sln -c Debug -p:Platform=x64
```

开发 DLL 通常位于：

```text
MarketRouteCN\bin\x64\Debug\MarketRouteCN.dll
```

## GitHub 自定义仓库

首次发布 `v0.1.0.0` 后，自定义插件仓库地址为：

```text
https://raw.githubusercontent.com/LonelyFSH/MarketRouteCN/main/repo.json
```

发布前请在 GitHub 仓库中开启：

```text
Settings → Actions → General → Workflow permissions → Read and write permissions
```

然后创建与 `.csproj` 中 `<Version>` 一致的四段式标签，例如：

```text
v0.1.0.0
```

GitHub Actions 会构建 `MarketRouteCN.zip`、创建 Release，并自动更新根目录 `repo.json`。

## 当前已知限制

- 本环境未安装 .NET 10，因此生成源码时未在本地执行实际编译；请以 GitHub Actions 的 Build 结果为准。
- 物品检索目前是中文名称“包含”搜索，拼音和首字母搜索计划后续加入。
- V0.1 优化目标是最低挂单总价；“少跑服务器”和“价格/服务器数量平衡模式”计划后续加入。
- 当清单或挂单规模很大时，优化器会从精确整组算法切换为按单价排序的保守方案，以控制计算开销。

## 数据与隐私

- 采购清单保存在本地 Dalamud 插件配置中；
- 插件只向 Universalis 请求物品 ID 和指定国服大区的公开市场数据；
- 不上传角色名、账号标识、聊天内容或个人库存。

## License

MIT
