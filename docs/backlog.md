# Backlog（范围门禁登记）

> 简报外"无关但重要"的问题登记处。格式：`问题 + 文件 + 建议`。
> 主会话维护。

## 阻塞项

- [x] **游戏客户端资源缺失** — `Build/Client/Debug/Data/` 为空（0 个 .lib/.wzl/.wil），`Map/`、`Sound/` 均为空。客户端没有任何可渲染素材。
  - **已解除（2026-08-06）**：用户提供 EliteMir2 数据源 `D:\ChuanQi\3.Server_EN 服务端_客户端 (部分汉化)\EliteMir2\Data`（34 目录 / 1955 个 .Lib / 9.4GB）。R0 已全量解析实证，阶段3 AssetCompiler 编译 1955 库 + 2338 张 `.map` 到 `Build/assetcompile/{all,map}`，`verify-dir`/`AtlasVerify` 全量字节审计通过。
- [x] **服务器内容为空** — `Build/Server/Debug/Maps/` 空；`Envir/NPCs` 仅 11 个占位脚本；`Server.MirDB` 是 240 字节空库；无测试账号/角色/物品。
  - **已解除（2026-08-06）**：用户提供 Server_EN 发布数据（`Server.MirDB` 版本 117 = Crystal `Version`、2338 张 `.map`、24 个 `Configs`、`Envir` 脚本），`Build/Server/publish/` 已验证正常启动；阶段4/5/6 探针已在其上跑通 登录→进图→交互→UI 全链路。
- [x] **旧客户端 30 分钟基线无法录制** — 依赖上面两项。截图/帧时间/内存/网络 trace 都需要真实资源和可进图的服务器。
  - **已解除（2026-08-06）**：①依赖项①②已就位，技术上可录；②实际验收路径已变更——行为/渲染迁移验收改用 **AssetCompiler golden 直解（.Lib 逐字节 SHA-256）** + **真实服务器 + Unity batchmode 探针（数据/像素双断言）**（阶段3 R1-R11、阶段4 P4-M1..M5、阶段5 迭代包 1-10、阶段6），不再依赖旧 SlimDX 客户端运行时截图基线（Unity/Server 侧零 SlimDX 运行路径）。
- [x] **天气系统边缘验证阻塞（阶段6 补验第 11 项）** — 依赖 R7 素材（天气效果动画/贴图），当前资源快照无天气素材，无法录制天气渲染证据。
  - **已解除（2026-08-06）**：`Weather.Lib`（31.7MB，v3 878 图）定位自 `D:\ChuanQi\Baselines\Crystal-G3-Weather-2026-07-31\`（G3 外部天气素材补充快照，sha256 `9A065B7D…`，supplementId `Crystal-G3-Weather-2026-07-31`）。已编译进图集管线（`Build/assetcompile/all/Weather`：compile verify OK + golden 侧车 878 行），Unity 侧 `net-weather.ps1`→`WeatherRender.RunWeather` 全 PASS（阶段6 11/11 完成）。另两个副本：`客户端/Client_VorticeDX11/Data/Weather.Lib`、`客户端/客户端 -外网/Data/Weather.Lib`。

## 低优先级

- [ ] **Android 软键盘（阶段7 第 3 项子项）** — 空场景无输入框消费方（MirTextBox 未在移动端运行），`TouchScreenKeyboard` 桥接推迟至登录/聊天 UI 移动化时一并做。
- [ ] **Android 安全区适配（阶段7 第 2 项子项）** — 当前空场景无移动 UI，`Screen.safeArea`/凹口适配无可验证目标；推迟至触控 HUD/移动资源包阶段一并做。
- [ ] **PatcherWebSite 遗留网站项目**（.NET Framework 4.8）阻塞解决方案级 `dotnet build`（MSB4249）— `Legend of Mir.sln`。
  - 建议：与三端迁移无关；要么从解决方案卸载，要么文档注明仅 VS2022 全 MSBuild 可构建。
- [ ] `docs/build-artifact-hashes.txt` 首字节带 UTF-8 BOM（PowerShell `Set-Content`），不影响使用。
