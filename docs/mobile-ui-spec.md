# 移动 UI 交互规范（mobile-ui-spec）

> 阶段8 0 项产物。移动端统一交互契约：**后续所有对话框触控任务只调 `MobileUiAdapter`，不各自实现 hit-test/坐标翻转/手感**（UI 单一体系禁令，见计划 §0）。权威计划：`docs/three-platform-migration-plan.md` 8-0。历史依据：X-1 touchdiag 坐标实证（2026-08-08）+ y-mirror 根因修复。
>
> 状态：✅ 8-0 已交付（2026-08-08，mobileuiverify 探针 PASS）。

## 0. 坐标空间（一切的前提）

| 空间 | 原点 | y 方向 | 载体 | 消费者 |
|------|------|--------|------|--------|
| 设备空间 raw | 左下 | 上 | Unity `Input.touch.position`（backbuffer 像素系，1280×720） | `TouchJoystick`（方向量化以 y 上为正） |
| UI 空间 ui | 左上 | 下 | `MirControl.DisplayRectangle` / `MobileBag` / `MobileHud` / 渲染布局 | 所有按钮命中、对话框 hit-test |

**唯一翻转点**：`MobileUiAdapter.ToUi(raw) = (raw.x, ScreenH - raw.y)`；`ToUiPoint(raw)` 返回 `MirMath.Point`。除摇杆外一切消费者收 ui。禁止在别处再次翻转。

- y-mirror 历史根因：渲染（CrystalSpriteBatch）左上原点、触摸左下原点，未翻转时背包按钮可见右上 (2163,250) 而命中区右下 (2164,830)（X-1 touchdiag 实证三处互证）。8-0 以适配层统一翻转修正，现有按钮 tap 可见位置即命中。

## 1. 触控尺寸

- **统一最小触控尺寸 `MobileUiAdapter.MinTouchSize = 44px`**（Apple/Google 触控指南下限）。新窗口按钮必须经 `MobileUiAdapter.TouchRect(center, size)` 归一（短边不足 44 以中心外扩），命中区 ≥44×44。
- 现有按钮合规性：背包按钮 72×54 ✓、攻击按钮 r60（直径 120）✓。硬编码几何（`MobileBag.ButtonW/H`、`MobileHud.AttackRadius`）保留，但所有**新**按钮走 TouchRect。

## 2. 命中

- 按钮/控件命中一律收 ui 空间坐标（`ToUi` 后）。`MobileUiAdapter.UiHitTest(MPoint p)` 递归判定可见对话框树（注入根 `DialogRoot`，运行时默认 `GameScene.Scene`）。
- 与摇杆互斥（消费序，`RouteTouch`）：① 命中类按钮（背包）先消费；② 面板打开期间其余触摸不喂摇杆/HUD；③ Down 落在可见对话框区域不激活摇杆；④ 放行后摇杆收 raw、HUD 收 ui。
- 探针覆盖：mobileuiverify case3/case4。

## 3. 返回键

- `MobileUiAdapter.PollBackKey()` 每帧轮询；检测 `IsBackPressed`（默认 `Input.GetKeyDown(KeyCode.Escape)`，Android 上即系统返回），处理注入 `BackHandler`（返回 true=已消费不冒泡）。
- 运行时钩子（MobileBootstrap）：关顶层对话框——当前最小形态=关背包面板（`InventoryDialog.Visible=false`）；无对话框未消费。
- 探针覆盖：mobileuiverify case5。

## 4. 层级

- 对话框**单一体系**：全部为 MirControl 控制树（UI 单一体系禁令），渲染统一走 `CrystalSpriteBatch`，禁第二 UI 框架/原生组件混搭。
- 顶层对话框 = 最后一个 Show 的可见对话框；返回键关顶层（见 §3）。面板打开期间（`PanelOpen=true`）场景移动暂停、摇杆 Cancel、其余触摸不穿透（消费序②）。
- 后续多对话框任务（NPC/任务/聊天）以 `InventoryDialog` 的 Show/Hide + Visible 门控为模板，复用 `RouteTouch` 的 `PanelOpen`/`DialogHit` 语义。

## 5. 滚动

- **规则**：对话框内滚动 vs 摇杆移动互斥——Down 落在可滚动区（对话框内）即被对话框消费（走 `DialogHit`），不激活摇杆。
- 逐字移植的 `MirControl` 无通用 `Scrollable` 成员（滚动为各控件自带），故 seam 为可注入谓词 `MobileUiAdapter.IsScrollable(ctrl)`（缺省 false）。阶段8 滚动窗口（聊天/列表）落地时接线：有滚动区的控件经此注册，其 `DisplayRectangle` 内的拖动归滚动、不归移动。
- 探针覆盖：mobileuiverify case6（注入谓词 + stub 树判定）。

## 6. 输入契约（全窗口统一手感，禁止各自为政）

| 手势 | 阈值 | 实现 |
|------|------|------|
| 单击 | Up 在按下后 ≤`TapMaxMs=200ms` | `MobileInput` |
| 长按 | 按住 ≥`LongPressMs=500ms` | `MobileInput` |
| 拖动 | 位移 >`DragThresholdPx=10px` | `MobileInput` |
| 双击 | 两次 Tap 的 Up-Up 间隔 ≤`DoubleTapIntervalMs=300ms` 且落点偏差 ≤32px | `MobileInput` |
| 摇杆死区 | 位移 <`TouchJoystick.DeadZonePx=12px` 不移动 | `TouchJoystick`（DRY 不复定义） |
| 奔跑阈值 | 位移 ≥`TouchJoystick.RunThresholdPx=64px` 切跑 | `TouchJoystick`（DRY 不复定义） |

- 手势分类器 `MobileInput`（`GestureKind: Tap/LongPress/Drag/DoubleTap`，优先级 拖动 > 长按 > 双击/单击），后续对话框 tap/长按/双击统一经它识别。判定优先级与边界由 mobileuiverify case7 钉死。

## 7. 安全区

- `Screen.safeArea`（真机刘海/挖孔/系统栏）驱动 HUD/按钮/背包布局，替换硬编码边距。当前基准：背包按钮 `ButtonMargin=(90,140)`（模拟器状态栏约顶部 126px 触摸被系统消费不进 Unity，X-1 实证），攻击按钮右缘 90/底缘 160 拇指可达区。
- 8-5-1 软键盘桥接 + 安全区适配时统一接入适配层，不改按钮几何。

## 8. 软键盘接口

- 单点桥接 seam：`TouchScreenKeyboard` → 文本控件（`MirTextBox`），8-5-1 落地。接口形态（预留）：软键盘弹出时 `safeArea` 收缩、对话框上移，输入焦点控件接入 `MobileInput` tap 定位。禁止各对话框自实现键盘/输入法。

## 验收对照（计划 8-0）

- ✅ mobileuiverify 探针 PASS（翻转/最小触控/命中/互斥/返回/滚动/契约时序 7 case 组）
- ✅ CoreVerify 0 错误；joystick/hud/bag 已有探针回归（提取不改行为；摇杆原生 raw 空间不变）
- ✅ 后续对话框触控只调适配层；本规范 8 项齐
