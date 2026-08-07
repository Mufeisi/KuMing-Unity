# 路线对比与优化建议（sanduan/Unity vs Crystal-master）

> 对比对象：
> - **sanduan/Unity**（`D:\ChuanQi\Kmyq\sanduan\Unity`）：同一传奇(Mir2)客户端的外部独立 Unity 移植。Unity 2022.3，141 个 C#，全量对象/对话框/特效/UI 已移植。
> - **Crystal-master**（本项目）：Unity 6 LTS，当前 155 个 C#，里程碑式已验证迁移（阶段 1-8，PC/Android 运行 + 确定性探针门禁）。
>
> 结论先行：**本项目路线更优**（可验证、可维护、跨平台统一、无第三方 UI 依赖）；sanduan 是**参考实现**而非替代路线。其最大价值是①补本项目缺口的现成参照（SpellObject/ItemObject/特效效果清单），②旧客户端源码的 Unity 化第二备份。具体可提取项见 `docs/sanduan-extraction.md`。

---

## 1. 两路线核心对比

| 维度 | sanduan/Unity（三端外部移植） | Crystal-master（本项目） |
|---|---|---|
| **理念** | 保旧架构 + 类型垫片：`System.Drawing`→`Shared.Unity.*`，D3D9→CommandBuffer/GL，UI→FairyGUI+IMGUI | 逻辑层逐字移植 + seam 隔离（MirMath/Seams），渲染 CrystalSpriteBatch + RT 直绘，UI 纯 C# 兼容控件 |
| **资源管线** | 运行时逐库解压 `.Lib` → 每图一个 Texture2D（启动慢、内存高） | AssetCompiler 离线编译 `.Lib`→图集 PNG+JSON+golden，运行时加载图集（1955 库全量字节审计 ok=1955 fail=0） |
| **shader** | 11 个（Light/NightBlend/OutLine/GrayScale/RemoveBlack/Gradient/BlackWhiteOverlay/AmbientLightBlend…），**含坏的**（NightBlend 无返回值、RemoveBlack 是错粘贴的 PBR 模板） | 4 个（Sprite/Additive/Multiply/Replace），R1-4 混合语义 + R5 灯光多通道**已验证** |
| **UI** | FairyGUI（`Resources/UI/*_fui.bytes`）+ IMGUI `GUILayout` 文本输入 | 纯 C# 兼容控件 + RT 直绘（阶段5 ADR，10 迭代包验证）；明确否决 uGUI 混合 |
| **网络** | 旧 `IAsyncResult` 原样（BeginConnect/Receive，`_sendList` 空即发 KeepAlive） | 现代化：登录状态机（自适应建号防 24h IP ban）、keepalive 独立心跳（真实 TickCount）、G4 2h soak 全过 |
| **验证** | 无门禁（人工目测可玩） | CoreVerify（0 警告）+ golden 字节审计 + 确定性 batchmode 探针（`net-*.ps1`/`*verify.ps1`）+ 真实服务器 E2E + 模拟器截图断言 |
| **移动端** | 触摸→鼠标事件桥 + `TouchScreenKeyboard`（IMGUI 路径） | TouchInputMapper/Adapter（8 用例）、资源同步、设备分级、keepalive/触摸失帧根因已修复 |
| **Unity 版本** | 2022.3 | 6 LTS |
| **完成度** | 全量移植但**未验证**，质量参差 | 里程碑推进，**已验证**；SpellObject/ItemObject 未移植，特效 shader 不全 |

**本质差异**：sanduan 是"宽而浅"——一次搬完所有文件、可玩性导向、无质量门禁；本项目是"窄而深"——每个里程碑硬验证、可回归、跨平台同路径。对本项目"三端稳定 + 长期可维护 + 工程纪律"的目标，**本项目路线明显更合适**。

---

## 2. 为什么本项目路线更好（论据）

1. **验证门禁是移动端质量的护城河**：sanduan 触摸/网络/渲染全部无断言。本项目阶段7/8 的 keepalive 被踢、触摸失帧、GL 三角形、服务器助跑等根因，**全部靠确定性探针 + E2E 实证**才定位；sanduan 这类问题会直接表现为"真机玩着卡/掉线"，无排查抓手。
2. **资源预编译决定移动端可行性**：sanduan 运行时解压 1955 个 `.Lib`（224 万图）到逐图 Texture2D，低端 Android 内存/启动不可控；本项目离线图集（2656 页 ≤4096²，点过滤验证）天然适配移动端。
3. **无第三方 UI 依赖**：FairyGUI + IMGUI 是额外的版本/平台耦合；本项目纯 C# 控件三端同源。
4. **网络层已验证**：sanduan 网络基本是旧代码原样；本项目已过 G4 2h soak、keepalive 独立心跳。
5. **Unity 6 LTS 长期性**：2022.3 已近维护末期。

**sanduan 不可照搬的硬伤**：①shader 有编译级 bug（见提取文档 §B）；②FairyGUI/IMGUI 与本项目 ADR 冲突；③`Shared.Unity.*` 全垫片与本项目 MirMath 双轨硬塞会破坏 CoreVerify 单一编译源。

---

## 3. 可借鉴/吸收的优化点（按优先级）

### 3.1 立即（P0，补缺口）
- **SpellObject + ItemObject 移植**：本项目对象模型唯一缺的两个，sanduan 有现成 Unity 化参照。落地：逐字移植进 `Client.Core/Ported/` + `tools/CoreVerify` + 真实服务器魔法/拾取 E2E。

### 3.2 短期（P1，特效补齐）
- **特效 shader 效果清单**：用 sanduan 11 个 shader 作"旧客户端 DX9 特效全集"清单，按本项目流程逐项复刻 + 验证：
  - `OutLine`（怪物/NPC 描边高亮）——本项目缺
  - `BlackWhiteOverlay`（近白去底）——本项目缺
  - `Light` 脉冲/闪烁语义——补进 R5 光源阶段
  - `AmbientLightBlend`——对照 R5 环境光方案
  - `GrayScale`/`NightBlend`——本项目已验证，跳过
  - `RemoveBlack`——坏的，不取
  - 每条走 `CrystalSprite*` 风格 + 混合语义探针（R1-4 模板）。

### 3.3 中期（P2，移动端 backlog 消化）
- **Android 软键盘**：`TouchScreenKeyboard` 接 MirTextBox 逻辑层（参考 sanduan CMain.cs OnGUI 的桥思路，但不走 IMGUI；RT 直绘键盘光标/候选）。
- **分辨率缩放统一**：对照 sanduan `SizeRatio`/`ScreenToWorld` 与本项目 `TouchInputMapper` 的坐标换算，收敛为单一适配层。
- **安全区适配**：参考其 `UpdateUISize` 分平台分支，接本项目移动 UI 适配层。

### 3.4 长期（P3，工程资产）
- **MirMath seam 边缘对照**：sanduan `Shared/Unity/` 全垫片作边缘情况字典（`ColorTranslator.FromHtml`、`SystemInformation`、`Drawing2D` 渐变语义），随用随查，不切换类型系统。
- **旧客户端第二备份**：把 sanduan 视为 git 历史（`0bb7e97^`）之外的对照片源，登记路径即可，不复制文件。

---

## 4. 建议动作（落地为任务）

| 动作 | 类型 | 产出 | 验证 |
|---|---|---|---|
| 移植 SpellObject/ItemObject | 阶段内任务 | `Client.Core/Ported/SpellObject.cs`+`ItemObject.cs` | `tools/CoreVerify` 0 错误 + 魔法/拾取 E2E |
| OutLine 描边 shader | 阶段内任务 | `Crystal/SpriteOutline.shader` + 探针 | golden/字节级（R1-4 混合语义流程） |
| 光源脉冲补 R5 | 阶段内任务 | LightRender 脉冲阶段 | 现有 LightRender 探针扩展 |
| Android 软键盘桥 | backlog → 任务 | MirTextBox + TouchScreenKeyboard | Android 模拟器 E2E（登录/中文输入） |
| 分辨率缩放统一 | 阶段8 移动适配 | 单一坐标换算层 | TouchInput 探针回归 |

**不采纳的路线**：切换到 sanduan 的"保旧架构 + 全垫片 + FairyGUI/IMGUI"路线，或把 sanduan 代码整目录合入——两者都破坏本项目已验证的架构与门禁。

---

## 5. 附：快速定位索引

- 提取资产逐条：`docs/sanduan-extraction.md`
- sanduan 源码根：`D:\ChuanQi\Kmyq\sanduan\Unity\Assets\`
- 本项目对象模型：`Unity/Assets/Crystal/Client.Core/Ported/`（SpellObject/ItemObject 缺失处）
- 本项目 shader：`Unity/Assets/Crystal/Client.Rendering/`（`CrystalSprite*.shader`）
- 本项目验证模板：`docs/migration-status.md`（R1-4 混合语义 / R5 灯光 / P4 垂直链路）
