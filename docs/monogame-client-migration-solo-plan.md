# Crystal 客户端 MonoGame 迁移总体计划（个人全职版）

> 文档版本：1.0
> 编制日期：2026-08-03
> 编制依据：对 `Crystal-master` 仓库 Client 代码的逐文件审查 + 既有 `docs/monogame-client-migration-prd.md`（2026-07-29）的承接
> 适用场景：**1 人全职**开发；**PC 端先行，移动端后行**
> 与旧文档关系：独立文档。旧 PRD 作为团队版参考，本计划以单人执行视角给出可落地的时间表、执行顺序、任务拆解、测试方法与验收标准

---

## 1. 结论先行

审查结论：**这个项目迁移到 MonoGame 属于"换后端"，不是"重写"。** 客户端 7.3 万行业务代码中，绝大部分（场景逻辑、对象动画、对话框、网络处理、地图解析）可以原样保留，真正需要替换的是一条很窄的渲染与平台接缝。

- 全库约 **470 处绘制调用**，全部汇聚到 `MLibrary` 的十几个 `Draw` 方法与 `DXManager` 的两个核心方法。
- `Shared` 项目（21,218 行，纯 C# 网络协议）**一行都不用改**。
- `MapReader`（5 种地图格式解析）、`.Lib` 解码（GZip→BGRA）、寻路算法，全部是纯 C#，可直接迁移。
- 真正的重活集中在：渲染后端替换（SlimDX→MonoGame）、输入事件兼容层、文本渲染、36 个 UI 对话框的逐个验证、以及 `GameScene`（12,517 行单体）的行为验证。

**工期评估（1 人全职，PC 100% 还原）：**

| 情形 | 时间 | 说明 |
| --- | --- | --- |
| 理想状态（全职无中断、方法正确、无大返工） | **9～11 个月** | 代码审查确认渲染接缝很窄，多数代码可平移 |
| 现实估计（含学习、调试、返工、生活中断） | **12～16 个月** | 建议按这个数做预算 |
| 技术验证 Spike（正式开工前） | **4～6 周** | 单独立项，未通过不进入全面迁移 |
| PC G6 发布后追加移动端 80% | **再 +4～7 个月** | 单人同时维护双平台压力很大 |

对比说明：旧 PRD 给单人估算是"PC 100% + 移动端 80% 共 18～30 个月"。本计划聚焦 PC 先行，且代码审查发现 `Shared` 与地图解析完全可复用、渲染接缝比预估更窄，因此 **PC 单独估算（12～16 个月现实）比旧 PRD 的合并估算（18～30 个月）偏乐观是合理的**。如果边做边夹带玩法重构、或反复重写 UI，会回到 18 个月以上。

单人项目的最大敌人不是技术，是**范围蔓延**和**验证缺失**。本计划把"先证明、再铺面、最后优化"作为铁律。

---

## 2. 代码审查结论

### 2.1 仓库事实（2026-08-03 实测）

| 项目 | 实测值 | 迁移影响 |
| --- | --- | --- |
| 客户端 C# 文件数 | 101 个 | 需要逐一过一遍，但大多数只改引用 |
| 客户端代码量 | 约 73,374 行 | 其中 GameScene 12,517、MainDialogs 4,183、NPCDialogs 3,587 |
| 目标框架 | `net8.0-windows7.0` + WinForms | Windows 专用，需替换游戏 Host |
| 渲染 | SlimDX Direct3D9 | 需映射到 MonoGame `SpriteBatch`/`Effect`/`RenderTarget2D` |
| UI 框架 | 自研 `MirControl` 控件树（7,835 行 / 17 文件） | 业务界面复用，输入事件类型需兼容层 |
| 场景/对话框 | 39 个文件 / 约 42,000 行 | 36 个对话框文件，逐个验证 |
| 对象层 | 15 个文件 / 15,379 行 | PlayerObject 5,285、MonsterObject 5,713 |
| 图形抽象 | `DXManager` 590 行、`MLibrary` 1,093 行 | **唯一的渲染接缝** |
| 文本 | GDI+（`TextRenderer.DrawText` 写入纹理） | PC 可保留 GDI+ 栅格化，移动端另做字体图集 |
| 地图 | `MapReader` 支持 5 种地图格式 | 纯 C# 文件解析，直接复用 |
| 资源 | `.Lib`（GZip→BGRA32 原始像素） | 解码逻辑复用，上传路径改为 `Texture2D.SetData` |
| 网络 | `Shared`（21,218 行）+ `Client/MirNetwork/Network.cs`（255 行） | 协议与封包全部复用 |
| 声音 | NAudio（`SoundManager` 647 行） | PC 可保留，移动端需 AudioRuntime |
| 浏览器 | WebView2（设置页/外链） | PC 保留，移动端改系统浏览器 |
| 原型 | `UiPreview`（MonoGame 3.8.5 WindowsDX） | 可保留为实验项目，不计入迁移完成度 |

### 2.2 直接引用旧平台的文件（替换目标清单）

- **引用 SlimDX 的文件 16 个**：`Forms/CMain.cs`、`MirControls/MirControl.cs`、`MirControls/MirGoodsCell.cs`、`MirControls/MirLabel.cs`、`MirControls/MirScene.cs`、`MirControls/MirTextBox.cs`、`MirGraphics/DXManager.cs`、`MirGraphics/MLibrary.cs`、`MirGraphics/ParticleEngine.cs`、`MirGraphics/Particles/*.cs`、`MirObjects/MapObject.cs`、`MirScenes/GameScene.cs`、`MirScenes/Dialogs/BigMapDialog.cs`、`MainDialogs.cs`、`QuestDialogs.cs`
- **引用 System.Drawing 的文件 20 个**：大部分仅用于 `Color/Point/Rectangle/Font/Bitmap` 类型
- **引用 System.Windows.Forms 的文件 3 个（非 Designer）**：`MLibrary.cs`（MessageBox）、`CMain.cs`、`Program.cs`（Application/MessageBox）

### 2.3 难度分布

| 工作项 | 难度 | 工作量占比 | 说明 |
| --- | --- | --- | --- |
| Shared 网络协议复用 | 极低 | ~0% | 直接引用，不修改 |
| MapReader / Lib 解码迁移 | 低 | ~5% | 纯 C#，删掉 WinForms 引用即可 |
| 渲染后端（DXManager 换内核） | 中高 | ~15% | API 少但语义要逐一对齐（混合、灰度、魔法特效） |
| 输入兼容层 | 中 | ~8% | MirControl 事件类型是 WinForms 的，需要一个翻译层 |
| 文本渲染 | 中 | ~5% | PC 用 GDI+ 栅格化保持像素一致 |
| 游戏主循环/Host | 低中 | ~5% | Application.Idle → MonoGame Game |
| GameScene 行为迁移 | 高 | ~15% | 12,517 行单体，渲染调用替换 + 逐块验证 |
| 36 个对话框/系统 | 中 | ~35% | 纯工作量，逐个验证是主 grind |
| 对象层（玩家/怪物/特效） | 中 | ~10% | 替换绘制调用，动画语义验证 |
| 音频 | 低 | ~2% | PC 保留 NAudio |
| 性能与发布 | 中 | ~5% | 批次、加载、安装包 |
| 测试基建（回放/截图对照） | 中 | ~10% | **单人必建**，否则无法证明 100% |

---

## 3. 迁移策略与执行顺序

### 3.1 核心策略：保持 MLibrary 接口，替换 DXManager 内核

`MLibrary.Draw(...)` 与 `DXManager.Draw(Texture, Rectangle?, Vector3, Color)` 是全部 470 处绘制调用的汇聚点。做法：

1. **`MLibrary` 的方法签名一个都不改**，只把内部的 `DXManager.Draw` 调用换成新渲染器。
2. 新建 `Client.Rendering.MgRenderer`（MonoGame 版），实现与 `DXManager` 相同的语义：`Draw / DrawOpaque / SetBlend / SetOpacity / SetGrayscale / SetNormal / SetBlendMagic / SetSurface / DrawText`。
3. 业务代码（场景、对话框、对象）**原样编译**，只是它们调用的底层从 SlimDX 换成 MonoGame。

这样 7.3 万行里至少 70% 的代码在迁移期间**一次编译通过、行为不变**，风险集中在渲染语义对齐。

### 3.2 总体执行顺序（回答"先做什么、后做什么"）

```text
0. 建基线（旧客户端可重复构建 + 截图/封包/性能基线）
   ↓
1. MonoGame Host 骨架（空窗口 + 输入轮询 + TCP 连接 + 调试 HUD）
   ↓
2. 资源 Spike（证明 .Lib 能解码、能上传 Texture2D、Offset/Mask/Shadow 正确）
   ↓
3. 绘制语义（Sprite/混合/Grayscale/Magic/灯光 RenderTarget + 对照页）
   ↓
4. 地图与对象（相机、tile、排序、玩家/怪物动画）—— 第一张可玩画面
   ↓
5. 首条垂直链路（登录→选角→进图→移动→打怪→拾取→背包→NPC→聊天→下线）
   ↓ ===== 4～6 周技术验证，Go/No-Go 节点 =====
6. PC 功能面（主 HUD→背包→NPC/商店→技能→任务→社交→交易→扩展，逐个迭代）
   ↓
7. PC 发布候选（删 SlimDX、稳定性、性能、安装包、灰度）
   ↓
8. 移动端（PC G6 后）
```

**铁律（什么不要先做）：**
- ❌ 不要先"整理干净" GameScene 再迁移 —— 先平移，后按需拆。
- ❌ 不要先做 GUI Studio / 新 UI Runtime —— 运行时稳定后再接入。
- ❌ 不要先批量替换 `Point/Color/Rectangle` 类型 —— 先让旧的编译通过，语义差异最后处理。
- ❌ 不要在旧 DXManager 里直接散落 MonoGame 类型 —— 会产生半新半旧的死代码。
- ❌ 不要在没有任何截图/回放对照的情况下声称"已还原"。

### 3.3 核心技术映射表（SlimDX → MonoGame）

| 当前（SlimDX/DX9） | MonoGame 对应 | 注意事项 |
| --- | --- | --- |
| `Device`（D3D9） | `GraphicsDevice` | 由 `Game` 提供，不再手动创建/重置 |
| `Sprite`（精灵批） | `SpriteBatch` | `Begin/End` 与旧 `Sprite.Flush` 语义对齐 |
| `Line` | 1×1 白色 `Texture2D` 拉伸或 `BasicEffect` | 边框/选中框绘制 |
| `Texture`（A8R8G8B8） | `Texture2D`（`SetData<byte>`） | 旧的 Lock+指针改为 CPU 数组上传 |
| `Surface` / `GetSurfaceLevel` | `RenderTarget2D` | 灯光合成、场景缓存 |
| `PresentParameters` | `GraphicsDeviceManager` | 分辨率/全屏在 Host 层配置 |
| `DeviceLost` / `AttemptReset` | 窗口 Resize 处理 | MonoGame 大幅简化，逻辑删除 |
| `PixelShader`（normal/grayscale/magic.ps） | `Effect`（.fx → MGFX） | 用 MonoGame 内容管线编译，先 Windows 后移动 |
| `SetBlend`（BlendMode） | `BlendState` + `SpriteBatch.Begin` 状态 | 逐模式对照页验证 |
| `SetOpacity` | `SpriteBatch` color 乘法 / 参数 | 注意与预乘 Alpha 的关系 |
| `Format.A8R8G8B8` | `SurfaceFormat.Color`（BGRA）或转 RGBA | **预乘 Alpha 问题必须用测试页确认** |
| `Vector3` 位置 | `Vector2` | Draw 调用点位置参数 |
| `LockRectangle` + `DataPointer` | `SetData` / `GetData` | 消除 unsafe 指针需求（保留也无妨） |
| `TextRenderer.DrawText`（GDI+） | PC：仍用 GDI+ 栅格化→`SetData`；移动：字体图集 | 见 3.4 文本专项 |
| `SlimDX.Windows.RenderForm` | `Game`（`MonoGameGamePlatform`） | WinForms 事件源改为输入轮询 |

**需要重点实验的三个语义差异：**

1. **预乘 Alpha**：旧 `.Lib` 解压出的像素是直通 Alpha（straight）还是预乘（premultiplied）？MonoGame `SpriteBatch` 默认按预乘处理，用错会产生黑边。必须做混合模式对照页，16 组颜色矩阵逐一验证。
2. **灰度/魔法特效**：旧代码通过 `SetGrayscale/SetBlendMagic` 切 PixelShader。MonoGame 需要 `SpriteBatch.Begin(..., effect: grayEffect)` 分组切换，或离屏 RT 后处理。必须在"技能、死亡、冰冻"等真实场景截图对照。
3. **绘制顺序**：旧代码大量 `Sprite.Flush()` 保证图元顺序。MonoGame `SpriteBatch` 同一 Begin/End 内按调用顺序绘制，但跨 Begin 的状态切换会打断批次 —— 需要验证遮挡排序不被破坏（排序一致性优先于 Draw Call 数量）。

### 3.4 专项策略

**文本（PC 100% 的关键）**：PC 阶段继续使用 GDI+（`System.Drawing` 在 `net8.0-windows` 可用），把 `TextRenderer.DrawText` 输出栅格化到 `byte[]`，再 `Texture2D.SetData`。这保证字体、抗锯齿、描边与旧客户端逐像素一致。移动端阶段再换成离线字形图集/SDF。**不要在 PC 阶段提前换字体方案。**

**输入**：MirControl 事件用的是 WinForms 的 `MouseEventArgs/KeyEventArgs`。做法：在 Host 层把 MonoGame 的 `Mouse/Keyboard/TouchPanel` 轮询结果翻译成这些事件类型，派发给 `MirScene.ActiveScene`。这样 7,835 行控件代码与 36 个对话框**不需要改事件处理逻辑**。移动端阶段再引入 `InputAction`（动作级抽象）。

**音频**：PC 保留 NAudio（不依赖 WinForms，可正常与 MonoGame 共存）。同时定义 `AudioRuntime` 接口（PlayOneShot/PlayLoop/Stop/PlayMusic/SetBusVolume），PC 实现是 NAudio，移动实现后续接 MonoGame `SoundEffect`/原生。

**网络**：`Network.cs` 只依赖 `Shared` 和少量静态（`CMain.Time`、`MirScene.ActiveScene`）。迁移时把静态依赖抽成接口，封包逻辑原样保留。每帧处理预算、重连、超时逻辑不变。

**WebView2 / 设置 / 补丁器**：
- 设置界面（`Config`）可以继续作为 WinForms 对话框在 MonoGame 窗口外弹出（PC 阶段最快路径），后续再决定是否做成游戏内 UI。
- 补丁器（`AMain`/AutoPatcher）是独立进程，与游戏渲染无关，**不需要迁移**，保持原样。
- WebView2 用于外链/公告，PC 保留，移动端用系统浏览器。

---

## 4. 分阶段计划（0→8）

> 时间按单人全职估算。括号内为累计周数。
> 每个阶段：目标 → 具体任务 → 测试方法 → 验收标准（Gate）。

### 阶段 0：可重复基线（第 1～2 周，累计 2）

**目标**：把"现在能跑的旧客户端"变成"可复现、可对照、可回放"的参照物。

**具体任务**：
1. 恢复 Git 工作树（当前目录未被 Git 识别为有效工作树，`.git` 存在但 log 为空）；新建迁移分支或独立仓库。
2. 固定 .NET SDK 版本、NuGet 源、MonoGame 版本（3.8.5）、构建命令，写 `build.ps1`。
3. 恢复旧 `Client` Debug/Release 构建，记录构建产物哈希。
4. 固定一台测试服务器 + 数据库 + 资源快照（`Data/`、`Map/`、`Sound/` 等目录打包归档）。
5. 建 3 个测试账号、5 张代表地图（城镇、洞穴、森林、沙巴克、Boss 房）。
6. 录制旧客户端 30 分钟基线流程（登录→进图→移动→战斗→交互→下线）。
7. 采集基线数据：截图集、帧时间、内存、加载时间、网络 trace。

**测试方法**：构建脚本一键出包；双机/双目录对照运行；基线资产 hash 校验。

**验收标准 Gate G0**：
- 新机器按文档能构建出旧客户端并可运行。
- 测试服务器可复现全部验收流程。
- 基线截图/封包/性能数据已归档且可访问。
- 未满足 G0 不进入阶段 1。

### 阶段 1：MonoGame Host 骨架（第 3～5 周，累计 5）

**目标**：MonoGame 窗口跑起来，输入能进业务层，TCP 能连上服务器。

**具体任务**：
1. 新建解决方案结构：`Client.Core`、`Client.Windows`、`Client.Replay`（阶段 1 只建这三个 + 测试）。
2. 建 MonoGame `Game` 派生类：`Initialize/LoadContent/Update/Draw`，固定时间步。
3. `GameClock`：统一时钟与 `CMain.Time` 语义对齐（现有时钟基于 Stopwatch，直接迁移）。
4. 输入轮询层：`Mouse/Keyboard` → 翻译为 WinForms 风格事件 → 派发 `MirScene.ActiveScene`。
5. TCP 接入：连接、登录回包、断线检测（复用 `Shared` 与 `Network.cs` 逻辑）。
6. 纯色场景 + 调试 HUD（FPS、连接状态、封包计数）。
7. 日志与配置（复用 `Settings.cs` 的读写逻辑，抽离 `Application.StartupPath` 依赖）。

**测试方法**：与旧客户端同服务器并行登录（用不同账号）；HUD 显示连接状态；8 小时挂机稳定性。

**验收标准 Gate G1**：
- 固定时间步连续运行 8 小时无崩溃、无内存线性增长。
- 输入事件能正确派发到场景（点击/按键有日志证据）。
- TCP 连接、登录回包、断线重连链路可用。
- Update 与 Draw 不直接修改网络队列。

### 阶段 2：资源兼容 Spike（第 6～8 周，累计 8）

**目标**：证明旧 `.Lib` 资源能在 MonoGame 中无损显示。**这是全项目第一风险，先攻破。**

**具体任务**：
1. 抽离 `.Lib` 头/索引/FrameSet 解析到 `Client.Assets`（纯 C#，去掉 `System.Windows.Forms` 的 MessageBox 依赖）。
2. 实现跨平台 RGBA 解码：`GZip` 解压 → `Rgba32Buffer`（不依赖 `Bitmap`）。
3. 输出 PNG + 像素哈希，与旧客户端（或基准解码结果）对照。
4. 上传 `Texture2D`：`SetData<byte>`，验证 BGRA→RGBA 转换与预乘 Alpha 语义。
5. 实现 Offset、Shadow、Mask、`VisiblePixel` 命中检测。
6. 纹理缓存与释放（对齐 `CleanTime` 语义），无 GPU 泄漏。
7. 抽样至少 5 个代表 `.Lib`：人物帧、怪物、UI、物品图标、带阴影/遮罩/空帧的资源。

**测试方法**：为每个样本库记录"输入哈希 → 帧数/尺寸/偏移 → 像素哈希"对照表；写自动化脚本比对。

**验收标准 Gate G2**：
- 样本帧尺寸、偏移、像素哈希与基线一致。
- 连续加载/释放 1,000 次，显存与内存无持续增长。
- GPU 资源只在图形线程创建/释放。
- **若 G2 失败：修正资源路线，不得跳过旧资源直接铺 UI。**

### 阶段 3：绘制语义与地图（第 9～13 周，累计 13）

**目标**：MonoGame 能画出和旧客户端一致的地图、人物、怪物、特效与灯光。

**具体任务**：
1. `MgRenderer` 完成：Sprite/区域/颜色/透明度（对应 `DXManager.Draw/DrawOpaque`）。
2. 混合模式：Normal、Additive/Blend、Magic、Grayscale、Tint/Opacity —— **先做独立混合模式对照页**（16 色矩阵 + 火焰/技能/光效样本）。
3. 地图：`MapReader` 复用 → tile 渲染、相机与滚动、遮挡排序（Back/Middle/Front 层）。
4. 对象动画：玩家/怪物站立、走、跑、攻击、受击、死亡帧序（`Frames.cs` 语义复用）。
5. 灯光：`RenderTarget2D` 灯光合成（白天/夜晚/火把）。
6. 名称、血条、伤害数字、基础粒子（`ParticleEngine` 迁移）。
7. 文本：GDI+ 栅格化管线接入 `MgRenderer`。

**测试方法**：黄金截图对照（同一地图同一位置）；动画帧序逐帧比对；性能预算采样。

**验收标准 Gate G3**：
- 代表地图静态截图通过像素阈值。
- 站立/走/跑/攻击/受击/死亡帧序通过。
- 1080p 代表场景达 60 FPS（P95 帧时间 ≤ 16.6 ms）。
- 混合模式对照页全部通过。

### 阶段 4：首条端到端垂直链路（第 14～18 周，累计 18）

**目标**：MonoGame 客户端在真实服务器上完成一条完整玩家链路。**项目的 Go/No-Go 节点。**

**必须打通**：

```text
启动 → 登录 → 角色选择 → 进入地图 → 移动/寻路 → 选择怪物
→ 普攻/技能 → 受击/死亡 → 拾取物品 → 打开背包并装备
→ NPC 对话 → 聊天 → 下线/重连
```

**具体任务**：
1. 登录场景、选角场景迁移（`LoginScene.cs` 1,368 行、`SelectScene.cs`）。
2. 主 HUD 迁移（`MainDialogs.cs` 4,183 行的核心部分：背包/装备/技能栏/聊天/小地图）。
3. 游戏场景完整渲染与输入（`GameScene.cs` 渲染路径替换）。
4. 战斗语义验证：普攻、技能、受击、死亡、拾取（`PlayerObject/MonsterObject/SpellObject/ItemObject` 迁移）。
5. 封包回放工具第一版（录制 + 离线回放，用于快速复现与回归）。
6. 服务器无需任何兼容补丁即可跑通链路。

**测试方法**：新客户端与旧客户端连同一服务器，双开对照；封包 trace 逐项比对；回放脚本回归。

**验收标准 Gate G4**：
- 新客户端在真实服务器完成整条链路，服务器零改动。
- 连续游玩 2 小时无阻断级错误。
- 封包差异有逐项说明（允许排序/时序差异，不允许行为差异）。
- **G4 是继续投入的 Go/No-Go：不过，回头修渲染或资源，不硬铺功能面。**

### 阶段 5：PC 功能面迁移（第 19～38 周，累计 38）

**目标**：把 36 个对话框与全部游戏系统逐个迁移到 100%。**这是最长的 grind 阶段，按迭代包推进。**

**迭代节奏（每包 1～2 周）**：冻结验收样本 → 迁移/验证 → 截图对照 → 封包验证 → 更新兼容矩阵。

**推荐迭代包顺序**（从核心到边缘，每个包交付"可完整使用"的窗口，不留 80% 半成品）：

| # | 迭代包 | 涉及文件 | 预估 |
| --- | --- | --- | --- |
| 1 | 主 HUD 补全 + 聊天 + 提示 | MainDialogs 等 | 2 周 |
| 2 | 背包 + 装备 + 物品 Tooltip | InventoryDialog、MirItemCell 等 | 2 周 |
| 3 | NPC 对话 + 商店 + 仓库 | NPCDialogs、TrustMerchantDialog | 2 周 |
| 4 | 技能 + 快捷栏 + Buff | MainDialogs 技能部分、BuffDialog | 1.5 周 |
| 5 | 任务 + 大地图 + 小地图 | QuestDialogs、BigMapDialog | 1.5 周 |
| 6 | 组队 + 好友 + 行会 | GroupDialog、FriendDialog、GuildDialog(2,244) | 2 周 |
| 7 | 交易 + 邮件 + 拍卖 | TradeDialogs、MailDialogs、RollDialog | 2 周 |
| 8 | 英雄 + 宠物 + 坐骑 | HeroDialogs、MountDialog、IntelligentCreatureDialogs | 2 周 |
| 9 | 商城 + 扩展系统 | GameshopDialog、FishingDialog、ItemRent* | 2 周 |
| 10 | 设置 + 帮助 + 边缘窗口 | HelpDialog、CharacterDialog、NoticeDialog 等 | 2 周 |

36 个对话框文件全部覆盖；大文件（NPCDialogs 3,587、GuildDialog 2,244）可拆子任务。

**测试方法**：每包更新兼容矩阵；黄金截图（状态齐全：正常/Hover/Pressed/Disabled/拖拽）；封包流程验证；缺陷按 P0-P3 分级。

**验收标准 Gate G5**：
- PC 兼容矩阵 100%。
- P0/P1 缺陷清零。
- 不再需要 SlimDX 运行路径。

### 阶段 6：PC 发布候选与优化（第 39～44 周，累计 44）

**目标**：达到可灰度发布的质量。

**具体任务**：
1. 删除 SlimDX Adapter 与未用 WinForms 路径。
2. 恢复安装包、补丁、首次启动、异常恢复流程。
3. 8/24/72 小时稳定性测试；多 GPU、多分辨率、窗口/全屏、Alt+Tab 测试。
4. 性能优化：批次、纹理页、加载、GC（按第 7 节指标）。
5. 截图/封包回放全量回归。
6. 回滚包与灰度开关。

**测试方法**：真实玩家灰度（5～20 人）；长稳自动化；性能采样。

**验收标准 Gate G6 / PC RC**：
- 第 7 节全部 PC 指标达标。
- 100% 功能矩阵由本人签字 + 至少一次真实玩家灰度通过。
- 无 P0/P1；回滚方案演练过。

### 阶段 7：移动端骨架（PC G4 后可预研，正式投入在 G6 后）

**具体任务**：
1. Android Host（`Client.Android`）；iOS Host 与签名流水线（需要 Mac）。
2. 屏幕方向、安全区、挂起/恢复。
3. 触控 Input Adapter（动作级 `InputAction`，不是鼠标模拟）与软键盘。
4. 移动资源包（AssetCompiler：`.Lib` → 纹理页 + 元数据）、下载与版本校验。
5. 音频 Runtime 移动实现（替换 NAudio）。
6. 设备性能分级（L/M/H）。

**验收标准 Gate G7**：Android 与 iOS 都能登录、进图、移动、重连；核心代码无平台条件编译扩散。

### 阶段 8：移动 80% 功能与体验（约 4～7 个月）

按旧 PRD 的加权覆盖表执行（战斗 20、移动 14、背包 12、NPC/任务 12 优先）；移动端不是 PC 等比缩小，需要触控 HUD、摇杆、目标锁定、软键盘规则。

**验收标准 Gate G8**：加权覆盖率 ≥ 80，核心四能力域无缺项，L/M/H 设备指标达标。

---

## 5. 单人工作法

### 5.1 每日节奏（建议）

- **上午**：先跑一遍回放/截图回归（20 分钟），确认昨天的修改没有破坏已通过部分。
- **上午中段**：主任务（当前迭代包的功能迁移）。
- **下午**：对照旧客户端逐项验证（双开或截图 diff），修差异。
- **收尾**：更新兼容矩阵 + 提交（小步提交，随时可回滚）。

### 5.2 关键效率工具（阶段 1-3 内必建，价值极高）

1. **双客户端对照**：旧客户端与 MonoGame 客户端连同一服务器、同一账号（或两个测试账号），并排截图。
2. **封包录制/回放**：`Client.Replay` 录服务器回包，离线重放。没有它，每次回归都要登真实服务器，单人会被拖死。
3. **截图 diff 工具**：输出像素差热力图，自动分类"相同/差异/基线错误"。禁止用"肉眼差不多"关闭差异。
4. **兼容矩阵**：一个 Markdown/表格，列出每个窗口/系统/输入路径/封包流程的通过状态。这是单人唯一的进度计分板。

### 5.3 防止范围蔓延

- 兼容与改进分开：迁移期间只修"阻断验收的 bug"，不顺手重构玩法。
- 每个迭代包只做"完整可用的窗口"，不做 10 个"80% 的窗口"。
- 拒绝"先优化性能再继续功能"的诱惑 —— 性能优化必须在 G5 之后按数据做。
- 旧 Bug：默认先兼容（保持行为），影响玩法判定时单独立项，不夹带修复。

### 5.4 进度度量

不看代码行数/文件数。看四个数字：**PC 功能矩阵通过率、自动回放通过率、黄金截图通过率、P0/P1 缺陷数**。

---

## 6. 任务拆解清单（工单级，预估人天）

> 单人预估。`M=里程碑`、`B=阻断项`。

| 编号 | 任务 | 阶段 | 预估人天 | 依赖 |
| --- | --- | --- | --- | --- |
| MG-001 | 恢复旧 Client 可重复构建 + build.ps1 | 0 | 3 | — |
| MG-002 | 固定服务器/数据库/资源快照 + 3 账号 5 地图 | 0 | 3 | MG-001 |
| MG-003 | 建立 PC 功能兼容矩阵 | 0 | 2 | — |
| MG-004 | 录制基线：截图/视频/输入/性能/网络 trace | 0 | 3 | MG-002 |
| MG-005 | Packet trace 格式与脱敏回放设计 | 0 | 2 | — |
| MG-006 | 建解决方案 + MonoGame Windows Host | 1 | 5 | MG-001 |
| MG-007 | GameClock 与 InputFrame（输入轮询→事件翻译） | 1 | 4 | MG-006 |
| MG-008 | 配置/日志/用户目录抽离 | 1 | 3 | MG-006 |
| MG-009 | TCP 会话接入（登录回包/断线/重连） | 1 | 4 | MG-008 |
| MG-010 | 抽离 Lib Header/Index/FrameSet 解析（去 WinForms 依赖） | 2 | 3 | MG-006 |
| MG-011 | 跨平台 RGBA 解码 + PNG/哈希对照 | 2 | 4 | MG-010 |
| MG-012 | Texture2D 上传 + 预乘 Alpha 实验 | 2 | 3 | MG-011 |
| MG-013 | Offset/Mask/Shadow/VisiblePixel + 纹理缓存 | 2 | 5 | MG-012 |
| MG-014 | 混合模式对照页（16 色矩阵 + 特效样本） | 3 | 5 | MG-012 |
| MG-015 | MgRenderer 核心 Draw/DrawOpaque | 3 | 4 | MG-014 |
| MG-016 | 地图 tile + 相机 + 滚动 | 3 | 5 | MG-012 |
| MG-017 | 遮挡排序 + 对象层 | 3 | 4 | MG-016 |
| MG-018 | 玩家/怪物动画帧序 | 3 | 5 | MG-013 |
| MG-019 | 灯光 RenderTarget 合成 | 3 | 4 | MG-015 |
| MG-020 | 名称/血条/伤害数字 + 基础粒子 | 3 | 4 | MG-018 |
| MG-021 | GDI+ 文本栅格化管线 | 3 | 3 | MG-015 |
| MG-022 | 登录场景迁移 | 4 | 4 | MG-015+MG-009 |
| MG-023 | 选角场景迁移 | 4 | 3 | MG-022 |
| MG-024 | 主 HUD 核心（背包/装备/技能栏/聊天/小地图） | 4 | 8 | MG-022 |
| MG-025 | 战斗链路（普攻/技能/受击/死亡/拾取） | 4 | 6 | MG-018+MG-024 |
| MG-026 | 回放工具第一版 + 首条可回放链路 | 4 | 5 | MG-005 |
| MG-027 | GameScene 渲染路径整体替换 + 行为验证 | 4 | 6 | MG-025 |
| MG-028 | 迭代包 1：HUD 补全 + 聊天 + 提示 | 5 | 8 | MG-027 |
| MG-029 | 迭代包 2：背包 + 装备 + Tooltip | 5 | 8 | MG-028 |
| MG-030 | 迭代包 3：NPC + 商店 + 仓库 | 5 | 8 | MG-029 |
| MG-031 | 迭代包 4：技能 + 快捷栏 + Buff | 5 | 6 | MG-030 |
| MG-032 | 迭代包 5：任务 + 大地图 + 小地图 | 5 | 6 | MG-030 |
| MG-033 | 迭代包 6：组队 + 好友 + 行会 | 5 | 8 | MG-031 |
| MG-034 | 迭代包 7：交易 + 邮件 + 拍卖 | 5 | 8 | MG-033 |
| MG-035 | 迭代包 8：英雄 + 宠物 + 坐骑 | 5 | 8 | MG-034 |
| MG-036 | 迭代包 9：商城 + 扩展系统 | 5 | 8 | MG-035 |
| MG-037 | 迭代包 10：设置 + 帮助 + 边缘窗口 | 5 | 8 | MG-036 |
| MG-038 | 删除 SlimDX 路径 + 全量回归 | 6 | 4 | MG-037 |
| MG-039 | 安装/补丁/首启/异常恢复流程 | 6 | 5 | MG-038 |
| MG-040 | 8/24/72h 长稳 + 多 GPU/分辨率矩阵 | 6 | 4 | MG-039 |
| MG-041 | 性能优化（批次/纹理页/加载/GC） | 6 | 8 | MG-040 |
| MG-042 | 灰度发布 + 回滚演练 | 6 | 4 | MG-041 |
| MG-043 | Android Host + 触控/安全区/挂起恢复 | 7 | 15 | G6 |
| MG-044 | 移动资源包 + 下载校验 | 7 | 10 | MG-012 |
| MG-045 | 移动音频/输入/设备分级 | 7 | 8 | MG-043 |
| MG-046 | iOS Host + 签名流水线 | 7 | 10 | MG-043 |
| MG-047 | 移动 80% 功能（按加权表） | 8 | 60+ | MG-043~046 |

**小计（PC，MG-001～042）**：约 215 人天 ≈ 43 周全职 —— 与阶段计划（9～11 个月理想）吻合。

---

## 7. 测试与验收体系

### 7.1 四层验证

1. **数据验证**：资源元数据、封包字节、状态哈希。
2. **行为验证**：输入回放后的角色位置、对象状态、窗口状态。
3. **视觉验证**：黄金截图、动画关键帧、差异热力图。
4. **真人验证**：测试清单、长时间游玩、灰度反馈。

### 7.2 黄金截图覆盖要求

- 分辨率：1024×768、1280×720、1600×900、1920×1080；窗口 + 全屏。
- 语言：中文 + 英文。
- 控件状态：正常 / Hover / Pressed / Disabled / Dragging。
- 时间/灯光：白天、夜晚、火把。
- 世界 Sprite 与 UI 纹理用严格像素阈值；字体允许独立阈值或遮罩区域。

### 7.3 封包与输入回放

- 封包 trace：相对时间、方向、类型、长度、脱敏载荷。
- 输入记录：逻辑坐标、动作、按下/释放、滚轮、文本、时间。
- 回放：固定时钟 + 固定随机种子；每 N 帧输出核心状态哈希。
- 旧客户端无法确定性运行时，以服务器结果和最终状态为准。

### 7.4 缺陷等级

| 等级 | 定义 | 发布规则 |
| --- | --- | --- |
| P0 | 崩溃、丢物、封号风险、无法登录/进图 | 立即停止 |
| P1 | 核心战斗/交易/任务阻断，重大视觉错位 | RC 前清零 |
| P2 | 可绕过的功能或明显视觉差异 | 发布前按清单关闭或接受 |
| P3 | 轻微表现、文案、边缘设备 | 可进后续版本 |

### 7.5 PC 性能指标（阶段 6 目标）

| 指标 | 目标 |
| --- | --- |
| 1080p 代表战斗场景 | 60 FPS，P95 ≤ 16.6 ms |
| Update CPU | P95 ≤ 4 ms |
| Draw 提交 CPU | P95 ≤ 6 ms |
| GPU | P95 ≤ 10 ms |
| 1% Low | ≥ 50 FPS |
| 进图时间 | ≤ 3 秒（不慢于旧客户端基线） |
| 连续运行 | 72 小时无崩溃 |

优化顺序（禁止无数据先优化）：可观测性 → 减少无效工作 → 批次与状态 → 资源与加载 → GC 与数据结构。**不能为合批破坏旧遮挡顺序。**

---

## 8. 风险登记（单人视角）

| 风险 | 概率 | 影响 | 预防/处置 |
| --- | --- | --- | --- |
| 旧资源格式细节未覆盖 | 高 | 极高 | 阶段 2 先做代表样本 + 哈希；G2 不过不前进 |
| 预乘 Alpha / 混合不一致 | 高 | 高 | 独立混合模式对照页 |
| GameScene 迁移引发玩法回归 | 高 | 极高 | 封包回放 + 双开对照；先平移后拆分 |
| 36 个对话框铺不开（士气/精力） | 高 | 高 | 迭代包制、兼容矩阵计分、每天回放回归保节奏 |
| GDI 字体无法像素一致 | 高 | 中高 | PC 保留 GDI+ 栅格化；字体差异独立验收 |
| 单人长期做闷，验证被跳过 | 中高 | 高 | 把回放/截图回归设为每天第一件事 |
| 服务器或资源版本漂移 | 中 | 高 | G0 快照冻结 + hash 校验 |
| 范围蔓延（夹带玩法重构） | 高 | 高 | 兼容与改进分开立项 |
| 移动端触控只是鼠标模拟 | 高 | 高 | 阶段 7 引入 InputAction，不是把 PC 输入平移 |
| iOS 签名/构建环境晚发现 | 中 | 高 | G7 前准备 Mac、证书、流水线 |

---

## 9. 开工前决策清单（逐项确认）

1. "PC 100%"对应哪个旧客户端版本 + 资源包 + 服务器快照？（G0 前必须定死）
2. 目标 Windows 最低版本、CPU/GPU 档位、分辨率范围。
3. PC 字体像素差异的可接受阈值是多少？（决定文本工作量）
4. 允许把旧 `.Lib` 转成发布资源包吗？（影响阶段 2 路线）
5. 哪些旧 Bug 必须兼容、哪些允许修复？（默认先兼容）
6. 是否保留 NAudio（PC 阶段）？—— 建议保留，移动端再换。
7. WebView2 当前承载哪些功能，移动端替代方案是什么？
8. 移动端目标：Android 先行还是双端同时？（建议 Android 先行 4～6 周）
9. 服务器是否允许长期双轨（旧客户端 + 新客户端并行）？—— 建议至少一个发布周期。
10. 每天可投入的净开发时间？周末是否投入？—— 这决定 12～16 个月还是更久。

---

## 10. 官方技术依据

- MonoGame 平台支持（Windows/macOS/Linux/Android/iOS）：<https://docs.monogame.net/articles/getting_started/platforms.html>
- MonoGame 输入（键盘/鼠标/触控）：<https://docs.monogame.net/articles/getting_to_know/whatis/input/>
- 多点触控：<https://docs.monogame.net/articles/getting_to_know/howto/input/HowTo_UseMultiTouchInput.html>
- 内容管线与 MGFX Effect：<https://docs.monogame.net/articles/getting_started/content_pipeline.html>

---

## 11. 最终建议

**项目值得做，但先做 4～6 周技术验证（Spike），用结果决定是否全面投入。** Spike 的五项产出：

1. 恢复旧客户端可重复构建与基线。
2. 证明真实 `.Lib` 可无损解码并上传 `Texture2D`。
3. 证明地图 + 人物 + 关键混合模式在 MonoGame 中可还原（对照截图）。
4. 证明旧协议可直接接入（登录链路）。
5. 产出第一张自动差异图 + 第一条可回放链路。

五项通过 → 按阶段 5-6 铺 PC 功能面（12～16 个月现实预算）；未通过 → 缩小范围或重新评估路线。单人项目，宁可慢而稳，不可快而乱：**每个阶段结束时的 Gate 是唯一的安全网。**
