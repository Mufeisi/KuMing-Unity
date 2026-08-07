# sanduan/Unity 可提取资产清单（参考）

> 定位：`D:\ChuanQi\Kmyq\sanduan\Unity` 是同一传奇(Mir2)客户端的独立 Unity 移植（Unity 2022.3 + FairyGUI + `Shared.Unity.*` 类型垫片 + CommandBuffer 渲染）。本清单列出**值得借鉴/提取的具体资产**，每条含：位置、内容、本项目缺口、可迁移性、验证方式。
>
> 原则：**选择性提取 + 按本项目验证方法论（golden/字节审计/确定性探针）落地**，不整目录复制。sanduan 无任何验证门禁，部分代码（含 shader）有 bug，直接照搬会引入未经验证的缺陷。
>
> 参考性质：本项目旧 SlimDX 客户端源码已在 commit `0bb7e97` 删除（git 历史保留）。sanduan 相当于同一批 `Client/MirObjects/*` 的 **Unity 化第二备份**，是 git 历史之外的对照片源。

---

## A. 对象模型缺口（最高优先，直接可参考）

### A1. `Assets/Client/MirObjects/SpellObject.cs`（378 行）
- **内容**：SpellObject 完整实现（魔法投射物对象：`Process`/`Draw`/`DrawEffects`，基于 Shared.Unity 类型）。
- **本项目缺口**：`Unity/Assets/Crystal/Client.Core/Ported/` 缺此文件（对象模型中唯一缺失的投射物类）。
- **可迁移性**：**高**。项目对齐方式为"从旧源码逐字移植 + MirMath/seam 适配"；sanduan 版提供 Unity 化后的语义参照（尤其 Draw 层叠、混合语义）。
- **验证方式**：移植后用 `tools/CoreVerify`（0 警告 0 错误）+ 真实服务器魔法 E2E 探针（P4 链路已有魔法交互基础）。

### A2. `Assets/Client/MirObjects/ItemObject.cs`（153 行）
- **内容**：ItemObject 完整实现（地面拾取物对象）。
- **本项目缺口**：同 A1，`Ported/` 缺此文件。
- **可迁移性**：**高**（小文件，逻辑简单）。
- **验证方式**：同上。

> 备注：`MonsterObject`（5720 行）/`PlayerObject`（5567 行）/`UserObject`（834 行）本项目**已移植**（阶段2 里程碑 1-5c，CoreVerify 0 错误），sanduan 版本仅作移植语义交叉参考，不再构成缺口。

---

## B. 特效 shader 效果清单（高价值，按"效果→复刻→验证"提取）

本项目现有 4 个 shader（`CrystalSprite`/`CrystalSpriteAdditive`/`CrystalSpriteMultiply`/`CrystalSpriteReplace`）。sanduan 的 11 个 shader 映射了旧客户端 DX9 全套特效语义，可作**效果清单**，逐项复刻进本项目 shader 管线并验证。

| sanduan shader | 效果 | 本项目现状 | 提取价值 |
|---|---|---|---|
| `Light.shader` | 光源纹理：`SrcAlpha/OneMinusSrcAlpha` + 时间脉冲（0.975±0.05·sin(9t) 亮度/alpha） | R5 灯光已多通道实现（additive + multiply），无脉冲 | 🟡 脉冲/闪烁语义可补进 R5 光源阶段 |
| `GrayScale.shader` | 变灰 `dot(rgb, 0.299/0.587/0.114)` + `saturate(c+_Color)` 遮罩 | CrystalSpriteBatch 已有 `SetGrayscale`（R1-4 已验证） | ⚪ 已覆盖，仅对照 |
| `OutLine.shader` | 4 邻域 alpha/rgb 采样描边 + 阴影色(16/8/8)例外 | **无**（怪物/NPC 高亮描边） | 🔴 需要，首个提取项 |
| `BlackWhiteOverlay.shader` | 近白色像素置透明 | **无** | 🟡 需要（部分 UI/特效去白底） |
| `AmbientLightBlend.shader` | 双纹理叠加（overlay alpha>0 覆盖） | **无**（R5 灯光为多通道） | 🟡 对照 R5 环境光方案 |
| `Effect.shader` | 特效染色 `saturate(c+_Color)` | Sprite shader 可覆盖 | ⚪ 低 |
| `Gradient.shader` | 顶点色上下渐变（屏空间） | 无 | 🟢 低优先（GDI+ 渐变基线留待阶段4 golden） |
| `NightBlend.shader` | 乘混合 `DstColor Zero` | = CrystalSpriteMultiply（已验证） | ⚪ 已覆盖 |
| `RemoveBlack.shader` | **坏的**：默认 PBR surface 模板，与 2D 精灵无关 | — | ❌ 不取 |

**⚠️ 关键教训**：`NightBlend.shader` 的 frag 函数**无 return 语句**（编译即坏）、`RemoveBlack.shader` 是错误粘贴的 PBR 模板——证明 sanduan 的 shader **不能直接复制**，必须按本项目"效果语义 → Crystal/Sprite 风格 shader → golden/字节验证"流程重做。`docs/migration-status.md` R1-4 的混合语义验证流程即为现成模板。

---

## C. 移动端适配参考（backlog 待办，参考实现）

### C1. `Assets/Client/Forms/CMain.cs` OnGUI 文本输入 + `TouchScreenKeyboard`
- **内容**：Android/iOS 软键盘桥——`GUILayout.TextField/PasswordField` + `TouchScreenKeyboard.Open` 绑定 MirTextBox；`useTextFieId` 聚焦路由。
- **本项目缺口**：backlog「Android 软键盘（阶段7 第3 项子项）」待办（MirTextBox 未移动化）。
- **可迁移性**：**方法可参考，代码不可照搬**——本项目 UI 是"纯 C# 控件 + RT 直绘"（阶段5 ADR），无 IMGUI；需用 `TouchScreenKeyboard` 接 MirTextBox 逻辑层，RT 直绘键盘光标/候选，不经 IMGUI。
- **验证**：Android 模拟器 E2E（login 输入框 + 密码框 + 中文输入）。

### C2. `CMain.ScreenToWorld` / `ToUnityLocal` / `Settings.SizeRatio`
- **内容**：逻辑分辨率(旧客户端 1024×768 逻辑坐标)与物理屏坐标互转，带黑边缩放（`SizeRatio`）。
- **本项目缺口**：移动 UI 适配层（计划 X-1/8-0 涉及）。
- **可迁移性**：**算法参考**。本项目已有 `TouchInputMapper`（物理/逻辑坐标缩放，阶段7），可对照统一。

### C3. `CMain.HandleTouchInput()`（触摸→鼠标事件桥）
- **内容**：`Input.touches` → `MouseEventArgs` 桥（Began=MouseClick、Moved=滚轮、Ended=Down+Click+Up）。
- **本项目现状**：`TouchInputMapper/Adapter` 更严谨（8 用例探针 + 模拟器 tap 验证，阶段7），已覆盖并改进。
- **可迁移性**：**低**，本项目已超集。唯一可借鉴：Ended 位置兜底（本项目增量1 已实证并修复，同源）。

### C4. 分辨率/全屏处理（`UpdateUISize`/`Screen.SetResolution` 分平台）
- **内容**：Windows 窗口化/移动端全屏分支 + `Screen.sleepTimeout`/`runInBackground`。
- **本项目现状**：阶段7 已有横屏 + AppLifecycle。
- **可迁移性**：🟢 低，已覆盖。

---

## D. 类型垫片交叉参考（低优先）

### D1. `Assets/Shared/Unity/*`（21 文件：Color/Point/Rectangle/Size/Vector2/3/Font/Text/Imaging/Drawing2D/Matrix/Pen/Keyboard/Mouse/SystemInformation…）
- **内容**：System.Drawing → Unity 的**完整类型替换层**（如 `Color.cs` 内嵌 System.Drawing 全部 KnownColor 常量，`Text.cs` 枚举 TextRenderingHint…）。
- **本项目现状**：MirMath 别名 + Seams（`Shared/Functions.cs` 适配层等），CoreVerify 0 错误已稳定。
- **可迁移性**：**不切换**（两套类型系统硬塞会破坏单一编译源）。仅作 **MirMath seam 边缘情况对照**（如 `ColorTranslator.FromHtml`、`SystemInformation`、`Drawing2D` 渐变等语义）。

---

## E. 渲染/资源管线对照（不提取，仅语义参照）

### E1. `Assets/Client/MirGraphics/MLibrary.cs`（1131 行）
- **内容**：运行时逐库解压 `.Lib` v3 GZip → `MImage`（含 `unsafe byte* Data`）→ 每图一个 `Texture2D`。
- **本项目**：AssetCompiler 离线编译 `.Lib` → 图集 PNG + JSON + golden 字节审计（1955 库全过）。**资源管线已完全替代**（启动快、移动端内存可控、可验证）。
- **结论**：不提取。若要对照 `.Lib` 解析边缘情况，本项目 `Ported/MapCode.cs` 已含解析，git 历史有原版。

### E2. `Assets/Client/MirGraphics/DXManager.cs`（512 行）`CreateLights()`
- **内容**：光源纹理 CPU 生成——椭圆射线 `(normX, normY)` 判内 + `Color.Lerp` 6 色标 + `alpha=1-distance`，11 档尺寸，`radiusY=0.4*width`。
- **本项目现状**：R5 `LightRender` 已用 CPU 椭圆射线径向渐变实现（同色标，`t=sqrt((ry·dx)²+(rx·dy)²)/(rx·ry)`），GDI+ 逐像素复刻留待 golden baseline。
- **可迁移性**：**参数对照**（sanduan `radiusY=0.4·width`、6 色标与 R5 一致，可核对椭圆公式差异），不重构。

---

## F. 旧客户端源码第二备份

- **内容**：`Assets/Client/{MirObjects,MirScenes,MirControls,MirGraphics,MirNetwork}` 全量文件（141 个 C#，含已删除旧客户端的全部对话框/对象/特效）。
- **用途**：本项目 git 历史（`0bb7e97^`）之外的对照片源。当 `git show` 某旧文件不便、或需要"旧逻辑在 Unity 语境下的表现"时，直接读 sanduan 版本。
- **注意**：saduan 版本是**被改过的 Unity 化版本**（类型垫片/FairyGUI），语义对照时以 git 历史原版为准，sanduan 只作 Unity 化副作用参照。

---

## 提取优先级建议

| 优先级 | 事项 | 对应项 | 预估 |
|---|---|---|---|
| P0 | 移植 SpellObject + ItemObject（补全对象模型） | A1/A2 | 1~2d |
| P1 | OutLine 描边 shader 复刻 + 验证 | B | 0.5~1d |
| P1 | 光源脉冲/AmbientLightBlend 对照补 R5 | B | 0.5d |
| P2 | Android 软键盘桥（MirTextBox + TouchScreenKeyboard） | C1 | 1d |
| P2 | 分辨率缩放统一（TouchInputMapper 对照 SizeRatio） | C2 | 0.5d |
| P3 | MirMath seam 边缘对照（ColorTranslator/SystemInformation） | D1 | 随用随查 |
