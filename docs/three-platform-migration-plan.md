# Crystal 三端迁移计划（Unity · 权威文档）

> 文档版本：2.3（2026-08-07，终审通过并冻结，进入 8-0 开发）
> 状态：🔲 待启动 / 🔶 进行中 / ✅ 已完成
> 本文档是**三端迁移的唯一权威计划**。取代 `monogame-client-migration-prd.md` 与 `monogame-client-migration-solo-plan.md`
> （均基于 MonoGame，已被 Unity 方案取代，已标为历史，保留作指标/风险参考）。
> 已完成工作的流水账与踩坑记录见 `docs/migration-status.md`；PC 功能验收清单见 `docs/compat-matrix.md`；
> 文档索引见 `docs/README.md`。

---

## 0. 战略与决策（2026-08-07 定稿）

| 决策项 | 结论 |
| --- | --- |
| 引擎 | **Unity 6 LTS**（6000.5.6f1）+ 内置渲染管线 |
| UI 技术路线 | **纯 C# 兼容控件 + RT 直绘**（全 UI 统一走此路径，阶段5 ADR 已定并验证 10 迭代包）。**明确否决 RT+uGUI 混合**（理由见下） |
| 目标 | 三端：PC / Android / iOS |
| 主线顺序 | **Android 移动端优先 → PC 收尾与发布 → iOS 落地**（iOS 依赖 macOS 环境） |
| 移动端节奏 | 阶段8 按「适配层 → 战斗 → 背包 → NPC/任务 → 聊天/社交 → 经济 → 扩展 → 性能」推进 |
| 验收方法论 | **确定性 batchmode 探针（`net-*.ps1` / `*verify.ps1`）是硬 gate**；模拟器/真机 E2E 只作冒烟，不作硬 gate；**性能按里程碑采样（8-P），不做一次性大返工** |
| 任务粒度 | **一个任务 = 一个功能 = 一个验证 = 一个 Commit**（适合 AI Agent 执行，粒度偏大先拆分） |
| 协议 | **Shared 协议冻结**（2026-08-07 起）：包结构/字段/枚举冻结，仅允许 Bug 修复与性能优化；任何修改须：① 提交 ADR ② 更新 compat-matrix ③ 更新全部 verify 脚本，且由主会话裁决（与"服务器零修改"不变量一致） |
| 阶段冻结 | 每阶段/大项完成：Feature Freeze → 完整验证 → **Git Tag**（如 `stage8-bag-v1`）→ 进入下一项 |
| 崩溃分析 | **最小形态**：崩溃日志落盘钩子（`Application.logMessageReceived` + 托管异常 → 本地文件，三端通吃）；暂不引第三方崩溃平台（私服规模不需要云端基建）。评审 P2，作发布工程化前置 |
| 渠道包体系 | **暂缓**：私服分发走官网/补丁器直下，无应用商店渠道需求；未来若需多包体，用 Unity 变体机制届时再立项。评审 P2 |
| UI 单一体系 | **禁止新增第二 UI 框架**：未来所有新 UI 一律优先复用 MirControl 树 + RT 直绘；任何引入 uGUI/其他 UI 框架的提议须主会话立项评审（防双体系回潮） |
| 移动输入契约 | 全窗口统一手感（8-0 落 `docs/mobile-ui-spec.md`）：单击 ≤200ms、长按 500ms、拖动超阈值（DragThresholdPx=10 / DeadZonePx=12 / RunThresholdPx=64）、双击间隔限制；禁止各窗口自定手感 |
| 铁律 | `AGENTS.md`：范围门禁 / 时间预算超 50% 停止汇报 / 完成=工件 / 每任务一 commit + 主会话验收 |

**两个前提性决策（从 2026-08-07 卡点提炼）：**
1. **移动端验证分层**：纯逻辑组件（摇杆/战斗/HUD/背包）一律先写确定性探针；对话框渲染复用 PC 的 batchmode 探针；
   模拟器 E2E（androidverify）只做端到端冒烟（宽松阈值、不设硬 gate）。禁止用模拟器 E2E 验证单个组件。
2. **模拟器触摸注入需先单独诊断**：SwiftShader 低帧率下 `adb input tap` 的 down-up 间隙落在单帧内会被 Unity
   整体跳过。任何注入类 E2E 前，先跑 2 分钟诊断脚本确认 `touch>0`，再谈断言。

**关于 RT 直绘 vs Unity UI（评审建议 3 的否决理由）：**
- 10 个迭代包对话框已全部以 MirControl 树 + RT 直绘完成，并经 `net-*.ps1` 逐像素验证；换 uGUI 需推倒重写已验证成果。
- 滚动/分页/弹窗层级/点击链在 RT 控件树已解决（NPC 分页滚动、MainDialog 点击链、迭代包 1-10）。
- 双 UI 系统并存 = 双渲染路径、Z 序/叠加问题、双维护成本（违反规则 1 双轨 / 规则 2 最简）。
- 移动端特有的文本输入/安全区/触控规范，作为**共享适配层（任务 8-0）**和单点桥接（TouchScreenKeyboard）解决，不换框架。

---

## 1. 阶段总览（状态重标）

| 阶段 | 名称 | 状态 | Gate | 说明 |
| --- | --- | --- | --- | --- |
| 0 | 可重复基线 | ✅ | G0 | 构建固定、运行时数据就位、验收路径改 golden+探针双断言 |
| S | 服务端现代化 | ✅ | G1/B2 | SAEA 收发 + 500 连接压测 + 账号存库后台化 + 8h soak 实证 |
| 2 | Shared 多目标 + Unity 工程 | ✅ | — | `netstandard2.1;net8.0`；Core 里程碑 1-5c（真实对象逐字移植） |
| 3 | 资源管线 + 渲染 Spike | ✅ | G2/G3 | AssetCompiler 全量 1955 库；R1-R11（渲染/灯光/粒子/文本/对象）；1080p 性能门禁 |
| 4 | 端到端垂直链路 | ✅ | G4 GO | 登录→…→下线，服务器零修改，2h soak PASS；**协议冻结自此生效** |
| 5 | PC UI 功能面 | ✅ | G5 有条件 | 迭代包 1-10（全部对话框控制树）+ 文本管线 |
| 6 | PC Player + 边缘补验 | ✅ | — | C1-C5 运行时 + 真实屏幕渲染；11/11 边缘补验；**C4 决策与 G6 见阶段9** |
| 7 | 移动骨架 | ✅ | G7 部分 | Android Host/生命周期/触控适配/资源同步/设备分级 + iOS 配置；**G7 判定拆到阶段8/10** |
| 8 | Android 移动功能 | 🔶 | G8 | **8-0 适配层（新增）** → 第1项 ✅ → 第2项 ✅ → 第3项 ✅ → 第4项 ✅ → 第5项 待启动 |
| 9 | PC 收尾与发布 | 🔲 | G6 | 原阶段6 剩余：UI 决策/安装补丁/长稳/性能/灰度 |
| 10 | iOS 落地 | 🔲 | G7 iOS | 阻塞：macOS + Xcode + 证书 |

---

## 2. 已完成工作摘要（按阶段，详见 migration-status.md）

- **阶段0/服务端**：G0 基线；B1 网络现代化（SAEA/包预算/合并发送/状态端口，trace 23 包 diff 空）；B2 存库后台化（90s 压测 + 周期存库实证）；G1 500 连接 P95=1ms。
- **阶段2**：Shared 双目标化；Unity 工程 + `Crystal.Client.Core` 里程碑 1-5c（MapCode/Damage/MapObject/Effect/**MonsterObject/PlayerObject/UserObject** 逐字移植，CoreVerify 0 错误）。
- **阶段3**：AssetCompiler（.Lib→图集+元数据+golden）全量 1955 库字节审计 ok=1955 fail=0；R1-R11 渲染探针（图集/地图/动画/场景 y-sort/灯光/粒子/文本/玩家/真实状态机驱动）；G2 1080p 120 帧 P50=0.53ms。
- **阶段4**：P4-M1..M5 垂直链路全通（登录/选角/进图/对象/战斗/拾取/背包/NPC/聊天/下线/HUD/双开/2h soak），Gate G4 GO，服务器零源码修改。
- **阶段5**：迭代包 1-10 全部对话框控制树（HUD/聊天/背包/装备/商店/仓库/技能/任务/地图/组队/好友/行会/交易/邮件/拍卖/英雄/坐骑/商城/设置）+ 文本字形管线（TextGlyphBuilder）；Gate G5 通过（有条件）。
- **阶段6**：边缘补验 11/11（del/run/split/revive/recon/autopath/magic/钓鱼/天气）；C1-C3 运行时（GameRenderer/GameSession/GameRuntime）；C5 PC Player 构建 + 真实屏幕渲染（`[pcplayer] shot` PASS）。
- **阶段7**：移动骨架 6 项全完成（Android Host APK 16MB、横屏/生命周期、TouchInput 触控适配 8 用例、ResourceSync 资源同步 2 场景、DeviceCapability 分级 2 场景、iOS 配置 3 断言）。
- **阶段8 已完**：前置 Android E2E（login→enter→render→move PASS）；第1项 战斗触控 HUD 三增量（摇杆 11 用例 / 自动战斗 6 用例+真实击杀 E2E / HUD 渲染 7 用例）+ 稳定性修复（keepalive 独立心跳、GL 三角形规范拆分、模拟器帧率适配）；8-0 适配层（X-1/X-2/8-0 三探针 26 用例）；第2项 增量1 背包按钮（bagverify 8/8 + 冒烟截图含背包按钮，tag `stage8-bag-v1`）；第2项 增量2 背包面板交互（baginteractverify 7/7，见任务 `8-2-2`）；第2项 增量3 装备穿戴（equipverify 5/5，见任务 `8-2-3`）；第2项 增量4 药品使用（useitemverify 6/6，见任务 `8-2-4`）；第2项 增量5 地面拾取（pickupverify 6/6，见任务 `8-2-5`）；第3项 增量1 NPC 对话（npcverify 6/6 + net-npc 回归 PASS，见任务 `8-3-1`）；第3项 增量2 商店买卖（shopverify 9/9 + 相关回归 PASS，见任务 `8-3-2`）；第3项 增量3 仓库存取（storageverify 9/9，见任务 `8-3-3`）；第4项 增量1 任务四窗（questverify 9/9，见任务 `8-4-1`）；第4项 增量2 大地图触控（mapverify 8/8 + gamesession/quest 回归 PASS，见任务 `8-4-2`）；第4项 增量3 小地图触控（minimapverify 6/6 + gamesession/hud/ui/pickup 回归 PASS，见任务 `8-4-3`）；第5项 增量1 软键盘+安全区（safeareaverify 7/7 + hud/bag/ui/softkeyboard 回归 PASS，见任务 `8-5-1`）；第5项 增量2 聊天触控（chatverify 11/11 + safearea/softkeyboard/minimap/ui 回归 PASS，见任务 `8-5-2`）。
- **sanduan 提取（交叉工作流）**：P0 对象模型补全（SpellObject + ItemObject 逐字移植 + GameSession 派发 + FloorItems 槽位，`ObjectModelVerify` 24/24，见任务 `P0`）；P1 OutLine 描边复刻（CrystalSpriteOutline shader + DrawOutline 光环，图集兼容，`OutlineVerify` 3/3，见任务 `P1-outline`）；P1 光源脉冲补 R5 + AmbientLightBlend 对照（LightPulse + `CRYSTAL_TIME` 脉冲模式，`lightpulseverify` 3 时刻 PASS，维持 R5 方案，见任务 `P1-lightpulse`）；P2 Android 软键盘桥（SoftKeyboardBridge 纯逻辑核心 + ISoftKeyboard seam + UnitySoftKeyboard 包装，`SoftKeyboardVerify` 8/8，见任务 `P2-softkeyboard`）；P2 分辨率缩放统一（ScreenMetrics 单一扇出 + ToUi 纯镜像对照决策，`ResolutionVerify` 14/14，见任务 `P2-resolution`）。

**当前在途**：阶段8 第6项 增量1 组队流程触控化（8-6-1 ✅）已完成。下一增量 8-6-2 好友流程触控化。阶段8 第4/5项 已全收口（8-4-1 任务四窗 ✅ + 8-4-2 大地图 ✅ + 8-4-3 小地图 ✅ + 8-5-1 软键盘+安全区 ✅ + 8-5-2 聊天 ✅）。sanduan 提取 P0-P2 已收口（2026-08-08，优先级表全 ✅），第2项 背包与物品已全收口（8-2-1..8-2-5 ✅），第3项 NPC/商店/仓库 已全收口（8-3-1..8-3-3 ✅）。

---

## 3. 剩余任务分解（排序后）

### 3.1 顺序逻辑

1. **X 组（横切·立即）**：先修复验证方法论（X-1 触摸诊断 → X-2 androidverify 冒烟化）；
2. **8-0 移动 UI 适配层**（**X-1 → X-2 → 8-0**，冒烟规则确定后再建 UI 规范）：把散落在 MobileBootstrap 的 UiHitTest/坐标翻转/命中规则提取为共享层，建立移动交互规范（先跑通再抽象：摇杆/HUD/背包已跑通，此刻抽象是收口不是预防）；
3. **阶段8 第2项 收尾**：在途背包工作按"探针验收 + 提交"收尾，再从增量2 做到增量5；
4. **阶段8 第3-8项**：按 PRD 加权表逐功能铺完（每功能独立任务/验证/提交）；
5. **8-P 性能采样**：随每个大项完成做真机采样，对照 PRD 11.2；
6. **阶段9**：PC 收尾与发布候选；**阶段10**：iOS 阻塞 macOS 环境；
7. **X 组（收尾）**：中文语言包 + 文档体系收尾。

### 3.2 任务清单总表

| ID | 任务 | 阶段 | 状态 | 依赖 | 预估 |
| --- | --- | --- | --- | --- | --- |
| X-1 | 模拟器触摸注入诊断脚本 | 横切 | ✅ | — | 0.5d |
| X-2 | androidverify.ps1 冒烟化改造 | 横切 | ✅ | X-1 | 0.5d |
| 8-0 | 移动 UI 适配层（交互规范 + 移动输入契约） | 8·基础 | ✅ | X-1, X-2 | 1d |
| 8-2-1 | 移动背包按钮 + InventoryDialog 接入（在途收尾） | 8·第2项·增1 | ✅ | 8-0 | 0.5~1d |
| 8-2-2 | 背包面板交互：选中/详情/Tooltip/切页 | 8·第2项·增2 | ✅ | 8-2-1 | 1~1.5d |
| 8-2-3 | 装备穿戴（点格→EquipItem→角色外观） | 8·第2项·增3 | ✅ | 8-2-2 | 1d |
| 8-2-4 | 药品使用（UseItem 触控，HP/MP 药） | 8·第2项·增4 | ✅ | 8-2-2 | 0.5d |
| 8-2-5 | 拾取（点地面物品→PickUp） | 8·第2项·增5 | ✅ | 8-2-2 | 1d |
| 8-3-1 | NPC 对话树触控化 | 8·第3项 | ✅ | 8-2-2 | 1d |
| 8-3-2 | 商店买卖触控化 | 8·第3项 | ✅ | 8-3-1 | 1d |
| 8-3-3 | 仓库存取触控化 | 8·第3项 | ✅ | 8-3-1 | 0.5~1d |
| 8-4-1 | 任务四窗触控化 | 8·第4项 | ✅ | 8-3-1 | 1d |
| 8-4-2 | 大地图触控化 | 8·第4项 | ✅ | 8-4-1 | 0.5~1d |
| 8-4-3 | 小地图触控化 | 8·第4项 | ✅ | 8-4-1 | 0.5~1d |
| 8-5-1 | 软键盘桥接 + 安全区适配（基础） | 8·第5项 | ✅ | 8-0 | 1d |
| 8-5-2 | 聊天触控化（频道/发送） | 8·第5项 | ✅ | 8-5-1 | 1d |
| 8-6-1 | 组队流程触控化 | 8·第6项 | ✅ | 8-3-1 | 1d |
| 8-6-2 | 好友流程触控化 | 8·第6项 | 🔲 | 8-6-1 | 1d |
| 8-6-3 | 行会面板触控化 | 8·第6项 | 🔲 | 8-6-1 | 1d |
| 8-7-1 | 交易窗口触控化 | 8·第7项 | 🔲 | 8-6-1 | 1d |
| 8-7-2 | 邮件系统触控化 | 8·第7项 | 🔲 | 8-7-1 | 1d |
| 8-7-3 | 拍卖行触控化 | 8·第7项 | 🔲 | 8-7-1 | 1d |
| 8-7-4 | 商城触控化 | 8·第7项 | 🔲 | 8-7-1 | 1d |
| 8-8-1 | 英雄面板触控化 | 8·第8项 | 🔲 | 8-7-1 | 1d |
| 8-8-2 | 坐骑/宠物触控化 | 8·第8项 | 🔲 | 8-8-1 | 0.5~1d |
| 8-8-3 | 钓鱼窗口触控化 | 8·第8项 | 🔲 | 8-8-1 | 0.5~1d |
| 8-8-4 | 设置三件套触控化 | 8·第8项 | 🔲 | 8-8-1 | 1d |
| 8-9-1 | OTA：Manifest 版本系统 | 8·OTA | 🔲 | 阶段7 项4 已备 | 1d |
| 8-9-2 | OTA：资源下载系统 | 8·OTA | 🔲 | 8-9-1 | 1d |
| 8-9-3 | OTA：增量更新 | 8·OTA | 🔲 | 8-9-2 | 1d |
| 8-9-4 | OTA：断点续传/失败恢复 | 8·OTA | 🔲 | 8-9-2 | 1d |
| 8-10 | 性能分级动态降级落地（DeviceCapability 消费） | 8·第9项 | 🔲 | 8-9-2 | 2~3d |
| 8-P | 移动性能采样（贯穿各里程碑） | 8·专项 | 🔲 | 随大项 | 0.5d/次 |
| 8-11 | G8 收官 + Android 侧 G7 判定 | 8·收官 | 🔲 | 8-2~8-10 | 1d |
| 9-1 | PC Player UI 收尾决策（RT 直绘定案，关 C4） | 9 | 🔲 | — | 0.5d |
| 9-2 | 安装/补丁/首启/异常恢复流程 | 9 | 🔲 | 9-1 | 2~3d |
| 9-3 | 8/24/72h 长稳 + 多 GPU/分辨率矩阵 | 9 | 🔲 | 9-1 | 2~3d |
| 9-4 | 性能优化（按数据，禁止无数据先优化） | 9 | 🔲 | 9-3 | 3~5d |
| 9-5 | 灰度发布准备 + G6 判定 | 9 | 🔲 | 9-2~9-4 | 2~3d |
| 10-1 | macOS 环境 + Xcode + 证书 | 10 | 🔲 阻塞 | 外部 | — |
| 10-2 | iOS Player 构建跑通（BuildIOS 已就位） | 10 | 🔲 | 10-1 | 1~2d |
| 10-3 | iOS 真机登录/进图/移动/重连（G7 iOS） | 10 | 🔲 | 10-2 | 2~3d |
| 10-4 | TestFlight + 双端发布收尾 | 10 | 🔲 | 10-3 | 2d |
| X-3 | 中文语言包（翻译阶段） | 横切 | 🔲 | 阶段5 迭代包11 已备 | 2~3d |
| X-4 | 三端兼容矩阵扩展 + 文档收尾 | 横切 | 🔲 | 阶段8 中期 | 1~2d |
| X-5 | 崩溃日志钩子（最小形态，三端） | 横切·P2 | 🔲 | 发布工程化前置 | 0.5~1d |

**阶段8 合计预估：30~40 天**（含约 20% 缓冲——移动端问题通常集中在输入/键盘/分辨率/低端设备；按各子任务预估求和后上浮）。

### 3.3 任务详单

每个任务含：目标（一个可交付工件）→ 开发 → 测试 → 验证 → 提交 → 验收。按 AGENTS.md 模板执行。
**每完成一个大项（背包/NPC/社交/经济等），打 Git Tag 并做一次 8-P 性能采样。**

---

#### X-1 模拟器触摸注入诊断脚本
状态：✅（2026-08-08 完成）｜预估：0.5d｜依赖：—

- 目标：产出一个 2 分钟诊断脚本，独立于 30 分钟 E2E，定位「adb 触摸注入是否到达 Unity」。
- 开发：`Build/touchdiag.ps1`——起 Server + 装 APK + 启动 + 注入一条 300ms swipe → 立即读 logcat `[mobile] touch-diag`，断言 `touch>0`；顺带打印坐标换算（注入逻辑坐标 vs 物理坐标）命中按钮的 rect。
- 测试：真实模拟器跑一遍；正例（逻辑坐标 swipe 命中摇杆区）touch>0；对照负例（物理坐标 >1280 注入）touch=0 复现已知根因。
- 验证：`Build/touchdiag.ps1` exit 0，输出 `touch=N>0` + 命中 rect。
- 提交：`feat(横切): 模拟器触摸注入诊断脚本`（仅该脚本 + 说明）。
- 验收：诊断脚本 2 分钟内定位注入问题，后续所有注入类调试不再进 30 分钟 E2E。
- ✅ 实证结论（2026-08-08，v2 重写）：**v1 的「物理 x>1280 注入被 Unity 丢弃（touch=0）」假设被证伪**——负例物理 x=2200 仍 touch=1 到达 Unity。9 点注入实验钉死真实映射：显示系（adb input/截图）2400×1080 y 向下 ↔ backbuffer 系（Unity touch.position）1280×720 y 向上，变换 `raw=(dx×1280/2400, 720−dy×720/1080)`，仅 raw_x≥1280 或 raw_y=0（显示 dx=2400 或 dy=1080 精确边缘）被丢。**真根因：渲染（CrystalSpriteBatch）用左上原点、Unity 触摸用左下原点，y 镜像；MobileBootstrap.UiHitTest 翻了 y（MirControl 左上 rect 正确），但 MobileBag/MobileHud 的 hit test 未翻 → 背包/攻击按钮可见位置与命中区上下颠倒**（截图扫描 + 注入实验 + 源码三处互证：背包渲染右上 (2163,250)、编码命中区右下 (2164,830)）。该 bug 由 8-0 适配层统一翻转修正。

#### X-2 androidverify.ps1 冒烟化改造
状态：✅（2026-08-08 完成）｜预估：0.5d｜依赖：X-1

- 目标：把 androidverify 从「硬 gate 断言脚本」降为「冒烟 + 诊断产物脚本」。
- 开发：保留 登录/进图/截图/坐标日志链路；去掉 `moved`/`bag` 等时序敏感硬断言，改为 WARN 输出 + 产物归档；新增 `-Smoke` 开关（跳过二次裁剪/重推/重启的重复步骤，只做单次进图+截图）；`$stepTotal` 与实际步骤对齐。
- 测试：完整跑一遍（冒烟模式）exit 0，产物 `androidverify-shot.png` 色数>阈值。
- 验证：`Build/androidverify.ps1 -Smoke` PASS；对比冒烟 vs 硬 gate 耗时。
- 提交：`refactor(阶段8): androidverify 降级为冒烟`。
- 验收：30-40 分钟一轮的硬 gate 不再存在；移动验收走确定性探针，androidverify 只留诊断价值。
- ✅ 实证（2026-08-08）：`-Smoke` 冒烟 PASS——单次进图（缓存出生坐标 / 无缓存回落地图中心 350,350 跳过 discovery）+ 全链路五断言 `chain: connect=True enter=True login=True select=True user=True coords=294,615` + hud-scan（atk/hp）正常；bag/moved 时序敏感断言按时序降级为 WARN-only（产物归档不挡 PASS）。冒烟耗时约 2 分钟（复用 warm 模拟器），硬 gate 退出舞台，移动验收全走确定性探针。

#### 8-0 移动 UI 适配层（评审建议「阶段7.5」的落地形态）
状态：✅（2026-08-08 完成）｜预估：1d｜依赖：X-1, X-2

- 目标：交付一个共享的「触摸→MirControl」适配层 + 移动交互规范（含**移动输入契约**），供后续所有对话框触控任务复用。
- 开发：
  1. 从 `MobileBootstrap` 提取 `UiHitTest`/坐标翻转/触摸互斥 → `MobileUiAdapter`（`Client.Rendering` 共享组件，静态注入钩子可单测）；
  2. 统一按钮命中规则（最小触控尺寸 ≥ 44×44px，现有 MobileBag/MobileHud 硬编码对齐）；
  3. 返回键（Android Back → 关顶层对话框）与弹窗层级钩子；
  4. 滚动冲突规则（对话框内滚动 vs 摇杆移动的互斥，基于 MirControl.Scrollable）；
  5. **移动输入契约**（全窗口统一手感，禁止各自为政）：单击 ≤200ms、长按 500ms、拖动超阈值（DragThresholdPx=10、DeadZonePx=12、RunThresholdPx=64）、双击间隔限制；
  6. 输出 `docs/mobile-ui-spec.md`：触控尺寸/命中/返回/层级/滚动/输入契约/安全区/软键盘接口 8 项规范。
- 测试：`mobileuiverify` 探针（命中/翻转/互斥/返回键/滚动冲突/输入契约时序 用例）。
- 验证：探针 PASS + CoreVerify 0 错误 + 已有 joystick/hud/bag 探针回归（提取不改行为）。
- 提交：`feat(阶段8): 移动 UI 适配层 + 交互规范`。
- 验收：后续所有对话框触控任务只调适配层，不各自实现 hit-test/翻转/手感；规范文档 8 项齐。
- ✅ 实证（2026-08-08）：三探针回归 PASS——`mobileuiverify` 7/7（翻转/最小触控/对话框命中/互斥路由/返回键/滚动冲突/输入契约时序）+ `joystickverify` 12/12 + `mobilehudverify` 7/7（提取不改行为，摇杆收 raw / HUD收 ui 的原生空间契约保持）+ CoreVerify 0 错误；同时修复 `TouchInputAdapter.SetMPoint` 未翻转 y 的镜像 bug（对话框鼠标事件路径同源缺陷，现统一走 `MobileUiAdapter.ToUiPoint`）。

#### 8-2-1 移动背包按钮 + InventoryDialog 接入（在途收尾）
状态：✅（2026-08-08 完成）｜预估：0.5~1d｜依赖：8-0

- 目标：交付已完成的背包按钮增量，验收改为确定性探针，产出 commit。
- 开发（已完成，复核即可）：
  1. `MobileBag.cs`——右上角按钮纯逻辑（命中/按下态/toggle/容错），开=亮黄 关=橙黄；
  2. `UiText`/`TextGlyphBuilder` 由 Editor 移入 `Client.Rendering`（运行时文本桥）；
  3. `GameSession.InitInGameDialogs`——进图实例化 MainDialog + InventoryDialog；
  4. `MobileBootstrap`——RenderHud 画面板/按钮、PollJoystick UI hit-test 互斥、ToggleBag 开/关日志；
  5. 收口到 8-0 适配层（按钮命中改走 `MobileUiAdapter`）。
  6. **待补**：`MobileBagVerify` 探针 + `Build/bagverify.ps1`（照 joystickverify 模板，纯逻辑无需服务器）。
- 测试：bagverify 用例（命中 toggle/边缘外不触发/面板开时摇杆停用/UI hit-test 互斥/Cancel 不 toggle/连点翻转/屏幕重设重布局）。
- 验证：`Build/bagverify.ps1` PASS exit 0 + `tools/CoreVerify` 0 错误 + PC 回归 `net-bag.ps1` + 模拟器冒烟 `androidverify.ps1 -Smoke`（不设 gate）。
- 提交：`feat(阶段8): 移动背包按钮 + InventoryDialog 触控接入` + 回归矩阵记录。
- 验收：bagverify PASS + PC 回归 PASS + 冒烟截图含背包按钮 + 主会话确认无越界。完成后打 tag `stage8-bag-v1`。
- ✅ 实证（2026-08-08）：探针 `bagverify` 8/8 PASS（命中 toggle/按钮外不触发/消费语义/Cancel 不 toggle/连点翻转/松手容错/屏幕重设重布局/开态跨重设保留）+ `mobilehudverify` 7/7（攻击按钮补 post-cancel 抑制断言）+ CoreVerify 0 错误 + PC 回归 `net-bag` PASS（bag ok exit 0）+ 模拟器冒烟 `androidverify -Smoke` PASS（chain 全 true coords=294,615，hud-scan atk=33136 hp=7124，shot1 含背包按钮：物理 (2096,210)-(2230,290) 中心 (2163,250) 与预期完全一致，10901 像素填充 99.7%）。修复真实 bug：Cancel 后残留 Up 会走松手容错误 toggle → `MobileBag`/`MobileHud` 加 `_canceled` 抑制位（设计对齐）。打 tag `stage8-bag-v1`。

#### 8-2-2 背包面板交互：选中/详情/Tooltip/切页
状态：✅｜预估：1~1.5d｜依赖：8-2-1｜完成：2026-08-08

- 目标：背包面板内可点选格子显示详情/Tooltip，可切 Bag/Equip/Storage 页。
- 开发：`MirItemCell.OnMouseClick` 点格选中（有物品→`SelectedCell=this` 边框高亮+图标置灰；空格→清本网格选中，跨网格守卫）；`InventoryDialog.ClearSelection` 选中生命周期（Reset 切页/Hide 关闭 清选中+Tooltip+HoverItem）；`MobileBootstrap` 返回键/关背包走 `Hide()`。切页触控走既有 Mir 鼠标链（TouchInputAdapter 同链路：Move 更新 hover → Down 置 ActiveControl → Up+Click）。
- 测试：`baginteractverify` 探针 7/7（点格命中+Tooltip/空格取消/越界忽略/切页状态/切页清选中/关闭清选中+Tooltip 释放/任务页）。
- 提交：`feat(阶段8): 背包面板触控交互（选中/详情/切页）`。
- 验收：真机/模拟器点开背包→点格子→出详情→切页→关闭，全程无摇杆误触发。

- ✅ 实证（2026-08-08）：探针 `baginteractverify` 7/7 PASS（点格选中+Tooltip/空格取消/越界忽略/切页状态/切页清选中/关闭清选中/任务页，exit 0）+ 回归 `bagverify` 8/8 PASS + CoreVerify 0 错误 + PC 回归 `net-bag` PASS（bag ok exit 0）。
- 🔧 探针关键修复（batchmode 无库数据陷阱）：`MirImageControl.Size` getter 在 `AutoSize&&Library!=null&&Index>=0` 时返回 `Library.GetTrueSize(Index)=(0,0)`，吞掉 ctor 显式尺寸 → 面板/按钮 `Size=0` → `IsMouseOver` 永不命中。探针统一关 `AutoSize` 回落 base.Size（面板 340x240 覆盖 CloseButton 全宽 289..329）。数组替换陷阱：`_user.Inventory = new UserItem[56]` 会丢原数组物品，case5/6 重选前需保格 `Inventory[6]=_sword`。

#### 8-2-3 装备穿戴
状态：✅｜预估：1d｜依赖：8-2-2｜验收：真机穿/脱武器，角色外观（Body/Hair/Weapon 层）随之变化

- 目标：背包格点击「装备/卸下」→ `C.EquipItem` → 角色外观更新。
- 开发：装备位判定（ItemInfo.Type 映射 EquipmentSlot）；双击或按钮确认（避免误触）；`S.EquipItem` 成功 → 装备槽 + 外观重算（SetLibraries/RefreshStats 已移植）。
- 测试：探针扩展用例（可装备/不可装备/卸下/状态回流）。
- 验证：探针 PASS + CoreVerify 0 错误 + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 背包装备穿戴触控`。
- 验收：真机穿/脱武器，角色外观（Body/Hair/Weapon 层）随之变化。

#### 8-2-4 药品使用
状态：✅｜预估：0.5d｜依赖：8-2-2｜验收：真机喝药回血，数量正确

- 目标：背包点药水 → `C.UseItem` → HP/MP 恢复 + 数量减少。
- 开发：药水类型判定（UseItem 语义，参考 PC net-interact）；触控确认；`S.UseItem` 回包更新库存。
- 落地（2026-08-08）：`MirItemCell.UseItem` 药水/卷轴/书/食物分支 → `C.UseItem`（锁格防重复双击）；`GameSession` 新增 `S.UseItem` 分发 + 回流 handler（成功→数量-1/清格 + RefreshStats，失败→仅解锁）。HP/MP 恢复走独立 `S.HealthChanged`（服务器权威封顶，客户端不本地补血 → 满血不溢出天然满足）。对照决策：旧客户端药水 Shape==4 确认框（MirMessageBox）与腰带自动移动（C.MoveItem）**不移植**——无移动 MirMessageBox，双击即触控确认（KISS/YAGNI）。
- 测试：探针用例（用后数量-1/满血不溢出/非药水拒绝）。
- 验证：`Build/useitemverify.ps1` → `[useitemverify] PASS cases=6`（双击发包+锁格 / 数量-1 / 不本地补血 HP 恒不变 / 失败回流不扣数 / 最后一瓶清格 / 非药水走装备链）；回归 baginteract 7/7 + equip 5/5 + mobileui 7/7 + softkeyboard 8/8 + resolution 14/14 + bag 8/8。
- 提交：`feat(阶段8): 药品使用触控`。
- 验收：真机喝药回血，数量正确。

#### 8-2-5 拾取
状态：✅｜预估：1d｜依赖：8-2-2｜完成：2026-08-08｜验收：真机走到掉落物旁点击拾取，背包物品+1

- 目标：点地面物品 → `C.PickUp` → 进背包。
- 落地（2026-08-08）：`MobilePickup` 纯逻辑控制器（仿 `MobileCombat`）——地图 tap（ui 空间）→ 屏转格（`ItemObject.Process` 世界→屏幕逆变换：`tileX = ui.X/CellWidth - OffSetX + user.Movement.X`）→ 最近 `ItemObject`（tap 距 ≤TapRadius=1 且距玩家 ≤PickupRadius=3）设目标；目标格==玩家格 → `C.PickUp`（节流 PickupCooldownMs=200，对齐旧客户端 `PickUpTime+200` 同源）；否则 `PathFinder` 逐格 `C.Walk`（物品格非 Blocking 可直达）到格即拾取；目标被拾取移除（`S.ObjectRemove` → `MapObject.Remove`）→ 自动清目标。接线：`MobileBootstrap` 摇杆 Up 且无拖拽位移（`!ReleasedWithIntent`）且非 HUD 按钮区（`MobileHud.Hit`）→ tap；摇杆 Down/拖拽/面板打开 → `Cancel`（移动优先）；拾取目标激活时战斗自动索敌让位。对照决策：服务端 `PickUp()` 仅拾取玩家**当前所在格**（逐格走位到格是必须，非优化）；拾取反馈（选中框/拾取飘字）不移植——物品移除即反馈（KISS/YAGNI）。注：`FloorItems` 图集不在仓库，运行时地面物品尚未生成（`GameSession.ObjectItem` 早退），拾取控制器作用于对象模型，待数据落地即可用。
- 测试：探针 6/6（命中+邻格命中/无物品/距离外拒绝/相邻拾取+节流+冷却重发/目标移除清目标/两格寻路走位到格后拾取）。
- 验证：`Build/pickupverify.ps1` → `[pickupverify] PASS cases=6`；回归 useitem 6/6 + equip 5/5 + baginteract 7/7 + mobileui 7/7；PC 回归 `net-interact.ps1`（PickUp 路径）。
- 提交：`feat(阶段8): 地面拾取触控`。
- 验收：真机走到掉落物旁点击拾取，背包物品+1。

#### 8-3-1 NPC 对话树触控化
状态：✅（2026-08-08，npcverify 6/6）｜预估：1d｜依赖：8-2-2

- 目标：点 NPC → 对话树选项可点、翻页、关闭。
- 落地：`MobileNpc` 纯逻辑控制器（仿 MobilePickup）——地图 tap 屏转格 → 最近 NPCObject（≤TapRadius=1）命中 → 置 `GameScene.NPCID` + 发 `C.CallNPC{ObjectID, Key="[@Main]"}`；无 NPC/对话框已开拒绝（落回拾取）。`GameSession.NpcResponse`（S.NPCResponse 分支）→ `NPCDialog.NewText` 渲染选项 + Show；选项点击走 TouchInputAdapter → GameScene.OnMouseClick → `NPCDialog.ButtonClicked` → `C.CallNPC[动作]`（复用 PC 控制树，节流走 `GameScene.NPCTime`）；`@Exit` 关闭。对话框在 InitInGameDialogs 预建（Visible=false，防 MapObject NPC 移除 NRE）。
- 测试：`NpcVerify` 探针 6 用例（命中发包+NPCID / 无 NPC 拒绝 / 超半径拒绝 / 独立节流 5000ms / 对话框已开不重弹 / NPCResponse 渲染+选项点击发包+选项节流+@Exit 关闭）。
- 验证：npcverify 6/6 PASS + 全移动回归（pickup/baginteract/equip/useitem/mobileui/mobilehud）PASS + PC 真服 `net-npc` PASS + gamesessionverify PASS。
- 对照决策：点击节流用**独立** 5000ms（`MobileNpc._lastCallAt`），不共享 `GameScene.NPCTime`——旧客户端共享计时会在开框后吞掉首个选项点击（quirk），触控版让选项即点即响。
- 提交：`feat(阶段8): NPC 对话触控化`。
- 验收：真机点 NPC 对话完整流程。

#### 8-3-2 商店买卖触控化
状态：✅（2026-08-08）｜预估：1d｜依赖：8-3-1

- 目标：商店 8 格列表买卖、数量、购买按钮触控。
- 开发：
  - `GameSession`：补 `S.NewItemInfo` 派发（填 `ItemInfoList`，此前运行时为空）+ `S.NPCGoods` 派发 → `NpcGoods` handler（逐商品 `GetItemInfo` 解析 `Info`，未收录跳过；设 `NPCRate`；`NPCGoodsDialog.NewGoods` + `Show` 连带开背包）；`InitInGameDialogs` 常驻创建 `NPCGoodsDialog`（默认隐藏，同 NPCDialog 模式）。
  - `MobileBootstrap` 移动守卫：`BackHandler` 商店关闭插入在 NPC 对话之前（顶层先关）；`uiOpen`/`PollJoystick bagOpen` 追加 `NPCGoodsDialog.Visible`（面板开时暂停摇杆/战斗/拾取）。
  - `NPCGoodsDialog`/`MirGoodsCell` 复刻复用（PC 迭代包3）：点格选中→`BuyButton`→`C.BuyItem{ItemIndex=UniqueID, Count=maxQuantity, Type}`，`Count` 按 StackSize/金币/listing Count 三封顶（对齐旧客户端 BuyItem）；`CloseButton` 关闭。
- 测试：`ShopVerify` 探针 9 用例（NPCGoods 分发+Info 解析+NPCRate+背包连带 / 未收录商品跳过不崩 / 点格选中+切换 / 单件 Count=1 / 叠放整组 Count=listing / 金币封顶 / StackSize 封顶 / 未选不发包 / 关闭隐藏）。
- 验证：shopverify 9/9 PASS + 相关回归（npcverify 6/6、baginteract 7/7、equip 5/5）PASS。
- 对照决策：
  - **只做购买侧**。出售（`NPCDropDialog`）未移植 → 不在范围内（已知限制，后续迭代）。
  - **数量选择沿用旧客户端最大可购**（`BuyItem` 三封顶），`MirAmountBox` 输入框不接入（WinForms 键盘 UX，裁剪注释已记录；SoftKeyboard 接线推迟）。
  - 双点击判定用静态 `_lastClickTime` 跨 case 会误伤 → 探针 `CMain.Time` 全程单调递增（`+=10000`）规避。
- 提交：`feat(阶段8): 商店买卖触控化`。
- 验收：真机买药成功、金币扣除。

#### 8-3-3 仓库存取触控化
状态：✅（2026-08-08，storageverify 9/9）｜预估：0.5~1d｜依赖：8-3-1

- 目标：仓库 10×16 网格存取、分页触控。
- 落地：
  - `GameSession`：补 `S.UserStorage` 派发（填 `GameScene.Storage` + `StorageDialog.Show` 连带开背包）+ `S.NPCStorage` 派发（关 NPC 对话 + 弹仓库框）+ `S.StoreItem`/`S.TakeBackItem` 回声（`ApplyStorageSwap`：Success → 本地交换 + `RefreshStats`，失败仅解锁）；`InitInGameDialogs` 常驻创建 `StorageDialog`（默认隐藏，同 NPCDialog/NPCGoodsDialog 模式）。
  - `StorageDialog`：网格 `Click` 接 `OnGridClick`——存：选中背包格（`GameScene.SelectedCell` 为 Inventory 且有物品）→ 点空仓库格 → `C.StoreItem{From=背包格,To=仓库格}`（目标被占静默，服务端权威）；取：点有物品仓库格 → 扫背包首空格（`BeltIdx` 起）→ `C.TakeBackItem{From=仓库格,To=背包格}`。双格 `Locked` 防重复双击，回声解锁（成功失败都解锁）。
  - `MobileBootstrap` 移动守卫：`BackHandler` 仓库关闭（NPC 之后、商店之前）；`uiOpen`/`bagOpen` 追加 `StorageDialog.Visible`（面板开时暂停摇杆/战斗/拾取）。
- 测试：`StorageVerify` 探针 9 用例（UserStorage 填格+弹框+背包连带 / NPCStorage 关NPC+弹框 / 选背包格点空仓库格存+双格锁 / 目标被占静默不发包 / 点有物品格取+找首空格 / StoreItem 回声交换+解锁 / TakeBackItem 回声 / 分页切换可见性+按钮态 / 关闭隐藏）。
- 验证：storageverify 9/9 PASS + 相关回归（shopverify 9/9、npcverify 6/6、baginteract 7/7、equip 5/5、useitem 6/6、pickup 6/6）PASS。
- 对照决策：
  - **存取交互放 Core `StorageDialog`**（对齐 `NPCGoodsDialog.BuyItem` 模式），地图 tap 只负责开框。
  - **`MouseDown` 快照选中态**：`MirItemCell.OnMouseClick` 会先把有物品格自身设为 `SelectedCell` 再触发 Click，若用 Click 时读选中态，"选背包格→点已占用仓库格"会被误判成取出 → 在 `MouseDown` 事件快照按下前选中态（`_downSelection`），占用格时静默。
  - **回声解锁按槽位扫**：`InventoryDialog.Grid` 下标≠物品槽位（`Grid[0].ItemSlot=6`），解锁须按 `ItemSlot` 扫描定位真实格；仓库 `StorageDialog.Grid[to]` 下标即槽位（存=To、取=From 双向映射）。
  - 仓库密码全套（MirInputBox/MirMessageBox/C.SetStoragePassword/C.UnlockStorage/S.*Result）、RentButton 租赁、升级仓库 `GetCell` 不移植（依赖对话框/未用，裁剪注释已记录）。
- 提交：`feat(阶段8): 仓库存取触控化`。
- 验收：真机存取物品成功。

#### 8-4-1 任务四窗触控化
状态：✅（2026-08-08，questverify 9/9）｜预估：1d｜依赖：8-3-1

- 目标：任务列表/日记/详情/追踪 四窗触控可操作。
- 落地：
  - `GameSession`：补 `S.NewQuestInfo` 派发（填 `GameScene.QuestInfoList`，NPCObject.Load 按 `NPCIndex` 关联）；`S.ChangeQuest` 派发（Add/Update/Remove → 双 `User` 引用同步 `CurrentQuests`（`GameScene.User` + `MapObject.User`，供日记/追踪各自读取）；`TrackQuest` → `QuestTrackingDialog.AddQuest`；Remove → 摘追踪，避免追踪 ID 残留 `Settings.TrackedQuests`）；`CompleteQuest` → 移除（对齐旧客户端 ChangeQuest Remove 语义）；`ShareQuest` 空体（不移植）。
  - `NpcResponse` 门控：`MapControl.GetObject(GameScene.NPCID) is NPCObject npc && npc.Quests.Count > 0` 才连带弹 `QuestListDialog`（无任务 NPC 只保留对话，列表不弹）。
  - `InitInGameDialogs` 常驻创建四窗（`NPCDialog` 之后，顺序契约：QuestTracking → QuestDiary → QuestList → QuestDetail；QuestListDialog ctor 读 `NPCDialog.Size.Width` 定 Location）；`MobileBootstrap` quest 入口按钮 + 四窗守卫（面板开时暂停摇杆/战斗/拾取，BackHandler 逐层关闭）。
  - `QuestSingleQuestItem` 触控接线：原 `e as MouseEventArgs` cast（Unity Click 为 `EventHandler`，恒 null）致交互全死 → 点行=左键（开详情 + 选中），追踪切换拆到独立 `_trackButton`（原右键行为）。
- 测试：`QuestVerify` 探针 9 用例（NewQuestInfo 落库+NPC 关联 / NpcResponse 门控弹列表+对话保留 / 无任务不弹 / ChangeQuest Add 双引用+TrackQuest+Settings 落盘 / Add 无追踪只入册 / Update/Remove 双引用+Remove 摘追踪 / 日记分组+点行开详情+行选中态 / 追踪按钮 toggle（开+关）+Settings 更新 / 追踪 5 条上限第 6 条不生效）。
- 验证：questverify 9/9 PASS + 相关回归（npcverify 6/6、shopverify 9/9、storageverify 9/9、bagverify/baginteract/equip/useitem/pickup/gamesessionverify）PASS。
- 对照决策：
  - **双引用同步**：任务被 `GameScene.User`（日记）与 `MapObject.User`（追踪）分别读取，ChangeQuest 必须同步写两处，否则单窗可见性漂移。
  - **Remove 摘追踪**：`ChangeQuest Remove` 时若任务在追踪列表，`RemoveQuest` 同步清 `Settings.TrackedQuests` 槽位（旧客户端 ChangeQuest 同语义，否则 ID 残留致下次进图幽灵追踪）。
  - **触控分离点行与追踪钮**：原右键开追踪不适用于触控，拆独立按钮（Index 917/918 切换）；探针夹具强制显式尺寸复现真机命中区（batchmode 空库 AutoSize→`GetTrueSize` 0×0、状态文本测量撑宽盖住按钮）。
  - `QuestMessage` 滚轮不移植（`ScrollUpButton`/`ScrollDownButton` 已有）。
- 提交：`feat(阶段8): 任务四窗触控化`。
- 验收：真机打开任务追踪、追踪切换生效。

#### 8-4-2 大地图触控化
状态：✅｜预估：0.5~1d｜依赖：8-4-1

- 目标：大地图视口拖动/缩放、NPC 行、移动按钮触控。
- 开发：`BigMapDialog` 触控（视口拖拽、NPC 行点击、地图按钮）；复用 PC 迭代包5。
- 测试：探针（视口拖动/行点击）。
- 验证：探针 PASS + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 大地图触控化`。
- 验收：真机大地图可拖动。
- 实现：`GameSession` 派发 `S.NewMapInfo`/`S.WorldMapSetup`（`MapInfoList` 记录 + 移动按钮/NPC 行构建）；`BigMapDialog` 常驻创建（`InitInGameDialogs`）；`MobileAutoPath` 自动寻路逐格 `C.Walk` 驱动（视口点击设 `AutoPath` → 走位 → 取消）；`MobileBootstrap` 地图按钮栈（紫）、打开即定位当前图、寻路上升沿自动关窗。`MapVerify` 8/8（记录构建/世界地图/请求回填/视口寻路/移动按钮/NPC 行/传送金门控/逐格走位）。

#### 8-4-3 小地图触控化
状态：✅｜预估：0.5~1d｜依赖：8-4-1

- 目标：小地图缩放档切换、坐标、追踪图标触控。
- 开发：`MiniMapDialog` 触控（大小档切换/地图切换按钮）；复用 PC 迭代包5。
- 测试：探针（档位切换）。
- 验证：探针 PASS + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 小地图触控化`。
- 验收：真机小地图切换正常。
- 实现：`InitInGameDialogs` 常驻创建 `MiniMapDialog`（HUD 右上角，旧客户端 GameScene ctor 直接建，Visible 默认 true）+ `DuraStatusPanel` seam 占位（旧客户端 DuraStatusDialog 未移植，Toggle/档位自适应读其 Location → 空控件防 NRE）。档位切换/大地图按钮已内置（ToggleButton Index 2102-2104 → `Toggle` 2090↔2091、BigMapButton 2096-2098 → `BigMapDialog.Toggle`），TouchInputAdapter 鼠标链触控自动生效。`MobileBootstrap`：RenderHud 每帧 `mini.Process()`（刷地图名/坐标）+ `UiText.WarmTree` + `Draw`（背包面板打开仍显示，旧客户端 HUD 常驻语义）；`MobileHud.Hit` 扩展小地图 DisplayRectangle 区（点小地图按钮走 MirButton.Click 链，不触发世界 tap）。`MiniMapVerify` 6/6（常驻+初始大档/档位双向切换/Process 文本/大地图开合/BeforeDraw 无图强切小档+有图校正大档/DuraStatusPanel 契约）。

#### 8-5-1 软键盘桥接 + 安全区适配（基础）
状态：✅｜预估：1d｜依赖：8-0

- 目标：`TouchScreenKeyboard` 桥接 + `Screen.safeArea` 布局钩子，移动 UI 全局生效。
- 开发：`MirTextBox` → `TouchScreenKeyboard` 桥（阶段7 推迟项就位）；`Screen.safeArea` 驱动 HUD/按钮/背包布局（替换硬编码 margin，接入 8-0 适配层）。
- 测试：探针（safeArea 注入布局断言）+ 模拟器软键盘弹收。
- 验证：探针 PASS + PC 回归 `net-ui.ps1` + 冒烟。
- 提交：`feat(阶段8): 软键盘桥接 + 安全区适配`。
- 验收：真机输入中文/英文；刘海机按钮不被遮挡。
- 实现：`SafeArea` 单一来源（`Screen.safeArea` 左下原点 → 四边 inset `(left,top,right,bottom)`，Provider seam 注入，消费方一律读它禁各自硬编码）——`MobileHud` 血条（顶/左 inset 下移内缩）+ 攻击按钮（底/右 inset 上抬内缩）、`MobileBag` 右上按钮列 + 派生按钮（装备/任务/地图 SetMargin 堆叠）继承；inset=0（非刘海全屏）布局与旧基准逐像素一致。软键盘触控接线（桥 `SoftKeyboardBridge` 于 sanduan P2 交付，本项补接线）：`MobileUiAdapter.RouteTouch` Down 命中可见启用 `MirTextBox` → `TryFocusTextBox`（递归子树，`InputTextBox.Enabled` 读启用态——`MirControl.Enabled` getter internal 跨程序集不可读）→ `SoftKeyboardBridge.Focus`（Open TouchScreenKeyboard，初始文本/密码/最大长度走框属性），Poll 文本回流 + Enter 提交（`KeyPress(Enter)` 进控件树，ChatDialog/登录同链）。`SafeAreaVerify` 7/7（inset 注入读值/HUD 偏移/背包+派生列继承/inset=0 回归不漂移/聚焦 Open+文本回流+Enter 提交/框外·不可见·禁用不聚焦/RouteTouch Down 聚焦+对话框消费不喂摇杆）。

#### 8-5-2 聊天触控化
状态：✅｜预估：1d｜依赖：8-5-1

- 目标：聊天框输入（走软键盘）、频道选择、发送触控。
- 开发：`ChatDialog` 触控（输入焦点/频道按钮/发送）；复用 PC 迭代包1。
- 测试：探针（焦点/频道/发送）。
- 验证：探针 PASS + PC 回归 `net-ui.ps1` + 冒烟。
- 提交：`feat(阶段8): 聊天触控化`。
- 验收：真机发一条聊天。
- 实现：`InitInGameDialogs` 常驻创建 `ChatDialog`（旧客户端 MainDialog ctor 直接建底部聊天窗，Unity 端此前裁剪从未实例化；ChatDialog ctor 读 `MainDialog.Location`，须在 main 之后，NetProbe 顺序契约同款）。`MobileChat` 触控控制器（底部左缘两个程序化按钮）：聊天按钮 → `OpenInput`（首次开注入当前频道前缀 + `SetChatText("")` 聚焦显示 + `SoftKeyboardBridge.Focus` 弹软键盘）；频道按钮 → 循环 0 附近/1 全员 `!`/2 行会 `@`（服务器按文本前缀分频道，`ApplyChannel` 开着输入框则去旧前缀补新前缀并重开软键盘使初始文本生效——Poll SyncText 以键盘文本覆盖框文本）。发送走软键盘 Enter（`SoftKeyboardBridge` Submitted → `ChatTextBox_KeyPress` → `C.Chat`），不另设发送按钮（YAGNI）。`MobileBootstrap`：`_chat` 按钮 + UiConsumer 链（`_map` 后）+ RenderHud 每帧 `chat.Draw()`（常驻底部）+ `UiText.WarmTree`（ChatLines 批前合帧）+ Back 优先关输入（`CloseInput` 对齐 PC Escape 隐藏清空语义）。接线逻辑集中在 `MobileChat` 静态助手（OpenInput/ApplyChannel/CloseInput，探针与运行时共用 DRY）。`ChatVerify` 11/11（常驻+输入框默认隐藏/聊天 tap 开输入+聚焦+键盘/频道循环 0→1→2→0/文本回流+Enter 提交 C.Chat/前缀注入发送/开着切频道重写前缀+重开键盘/未开切频道不触碰/按钮区外不消费/Cancel 抑制/CloseInput 幂等/RouteTouch 集成消费不喂摇杆）。

#### 8-6-1 组队流程触控化
状态：✅｜预估：1d｜依赖：8-3-1

- 目标：组队邀请/离队 触控流程。
- 开发：复用 PC 迭代包6；网络动作按钮接回（组队 C.* 封包，PC 版为探针留空，移动端补全）。
- 测试：探针（组队动作发包断言）。
- 验证：探针 PASS + PC 回归 `net-team.ps1` + 冒烟。
- 提交：`feat(阶段8): 组队流程触控化`。
- 验收：真机组队二人流程。
- 实现：`MirInputBox` 移植（Modal 挂 `GameScene.Scene`，Prguse 660 + 单行 `MirTextBox` + OK/Cancel；Enter→OK / Esc→Cancel 键盘路由；移动端经 `TouchInputAdapter` 鼠标链点击 + 软键盘桥输入/提交，无原生 WinForms 表单裁剪 `Program.Form.Controls`）。`GroupDialog` 网络交互接回（PC 迭代包6 探针留空处补全）：SwitchButton→`C.SwitchGroup{!AllowGroup}`（允许态由回声 `S.SwitchGroup` 更新，开队清列表）；Add/Del 弹 `MirInputBox` 输入成员名 → `C.AddMember`/`C.DelMember`（客户端侧队长/满队守卫走 `ChatDialog.ReceiveChat` 提示，`GroupHasMaxMembers` 15 人/`YouAreNotGroupLeader`）；`public AddMember(string)` 直发供键盘。`GameSession` 补 7 派发（`internal static` + `InternalsVisibleTo` 供探针）：`S.SwitchGroup`（AllowGroup 同步+关队清列表）/`S.DeleteGroup`（清列表/字典/雷达 `BigMapViewPort.PlayerLocations`）/`S.DeleteMember`（三处移除+Group 频道提示）/`S.AddMember`（入列去重+提示）/`S.GroupMembersMap`（成员地图 upsert）/`S.SendMemberLocation`（封包 `System.Drawing.Point`→`MPoint` 雷达 upsert）/`S.GroupInvite`（弹 `MirMessageBox` YesNo：Yes→`C.GroupInvite{true}`+开 GroupDialog，No→`{false}` 拒绝，Esc→No 语义对齐移动端 Back）。`InitInGameDialogs` 常驻创建 `GroupDialog`（默认隐藏）。`MobileBootstrap` 组队入口（`_group` 按钮栈，红 tint，背包列下移 4 格）+ UiConsumer 链 + `BackHandler`（先关模态框再关组队面板）+ `uiOpen`/`bagOpen` 守卫 + RenderHud 每帧 `group.Draw()`+`UiText.WarmTree`（含瞬态模态框）。`GroupVerify` 16/16（常驻创建/切换按钮开-回声-关两拍/Add 弹窗+输入 OK 发包/输入框收起/Del 弹窗发包/直发+满队+非队长守卫/S.SwitchGroup 回声/DeleteGroup 三清/DeleteMember 三处移除/AddMember 去重/GroupMembersMap upsert/雷达 upsert/GroupInvite Yes 开窗/Esc 拒绝/输入框 Esc 取消不发包/Enter 提交/RouteTouch 组队按钮消费不喂摇杆）。

#### 8-6-2 好友流程触控化
状态：🔲｜预估：1d｜依赖：8-6-1

- 目标：好友增删/备注 触控流程。
- 开发：复用 PC 迭代包6；动作接回；列表滚动触控。
- 测试：探针（好友动作）。
- 验证：探针 PASS + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 好友流程触控化`。
- 验收：真机加/删好友。

#### 8-6-3 行会面板触控化
状态：🔲｜预估：1d｜依赖：8-6-1

- 目标：行会公告/状态页查看 触控。
- 开发：复用 PC 迭代包6；滚动条触控（MirControl.Scrollable）。
- 测试：探针（面板渲染/滚动）。
- 验证：探针 PASS + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 行会面板触控化`。
- 验收：真机看行会面板、滚动公告。

#### 8-7-1 交易窗口触控化
状态：🔲｜预估：1d｜依赖：8-6-1

- 目标：交易 2×5 格、金币、锁定/确认 触控流程。
- 开发：复用 PC 迭代包7；交易确认时序；数量输入走 8-5-1 软键盘。
- 测试：探针（交易锁定/确认发包）。
- 验证：探针 PASS + PC 回归 `net-market.ps1` + 冒烟。
- 提交：`feat(阶段8): 交易窗口触控化`。
- 验收：真机两人交易完成。

#### 8-7-2 邮件系统触控化
状态：🔲｜预估：1d｜依赖：8-7-1

- 目标：邮件读写/包裹 触控。
- 开发：复用 PC 迭代包7；列表滚动；动作接回。
- 测试：探针（邮件列表/读写）。
- 验证：探针 PASS + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 邮件系统触控化`。
- 验收：真机读信/取附件。

#### 8-7-3 拍卖行触控化
状态：🔲｜预估：1d｜依赖：8-7-1

- 目标：拍卖搜索/竞拍/寄售 触控。
- 开发：复用 PC 迭代包7；筛选树触控；出价走软键盘。
- 测试：探针（筛选/竞拍/寄售发包）。
- 验证：探针 PASS + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 拍卖行触控化`。
- 验收：真机搜索+寄售。

#### 8-7-4 商城触控化
状态：🔲｜预估：1d｜依赖：8-7-1

- 目标：商城分类/购买/支付类型勾选 触控。
- 开发：复用 PC 迭代包9；分类 tab/购买确认。
- 测试：探针（分类/勾选/购买）。
- 验证：探针 PASS + PC 回归 `net-shop.ps1` + 冒烟。
- 提交：`feat(阶段8): 商城触控化`。
- 验收：真机购买流程。

#### 8-8-1 英雄面板触控化
状态：🔲｜预估：1d｜依赖：8-7-1

- 目标：英雄背包/状态/管理 触控。
- 开发：复用 PC 迭代包8；动作接回。
- 测试：探针（英雄面板触控）。
- 验证：探针 PASS + PC 回归 `net-hero.ps1` + 冒烟。
- 提交：`feat(阶段8): 英雄面板触控化`。
- 验收：真机打开英雄面板。

#### 8-8-2 坐骑/宠物触控化
状态：🔲｜预估：0.5~1d｜依赖：8-8-1

- 目标：坐骑装备/骑乘 触控。
- 开发：复用 PC 迭代包8；动画控件触控。
- 测试：探针。
- 验证：探针 PASS + PC 回归 + 冒烟。
- 提交：`feat(阶段8): 坐骑触控化`。
- 验收：真机骑乘。

#### 8-8-3 钓鱼窗口触控化
状态：🔲｜预估：0.5~1d｜依赖：8-8-1

- 目标：钓鱼施放/状态 触控。
- 开发：复用 PC 阶段6 钓鱼；施放按钮触控。
- 测试：探针。
- 验证：探针 PASS + PC 回归 `net-fishing.ps1` + 冒烟。
- 提交：`feat(阶段8): 钓鱼触控化`。
- 验收：真机钓鱼。

#### 8-8-4 设置三件套触控化
状态：🔲｜预估：1d｜依赖：8-8-1

- 目标：筛选/透明/帮助/键位 触控（移动端键位改虚拟键）。
- 开发：复用 PC 迭代包10。
- 测试：探针。
- 验证：探针 PASS + PC 回归 `net-settings.ps1` + 冒烟。
- 提交：`feat(阶段8): 设置触控化`。
- 验收：真机改设置生效。

#### 8-9-1 OTA：Manifest 版本系统
状态：🔲｜预估：1d｜依赖：阶段7 项4 已备

- 目标：全量资源包 + 版本 manifest 生成与校验。
- 开发：AssetCompiler 全量 → manifest（含版本号）；`ResourceSync` 远端清单 + 本地版本比对。
- 测试：manifest 确定性 + 版本校验用例。
- 验证：探针 PASS。
- 提交：`feat(阶段8): OTA manifest 版本系统`。
- 验收：版本不匹配即触发下载。

#### 8-9-2 OTA：资源下载系统
状态：🔲｜预估：1d｜依赖：8-9-1

- 目标：首启下载 + 进度 + 校验落盘（替代 adb push）。
- 开发：`ResourceSync.DownloadFile` 接入 MobileBootstrap 首启；进度/失败重试。
- 测试：resourcesyncverify 扩展真机场景。
- 验证：探针 PASS + 真机冷启动下载进图。
- 提交：`feat(阶段8): OTA 下载系统`。
- 验收：真机卸载重装后自动下载进图。

#### 8-9-3 OTA：增量更新
状态：🔲｜预估：1d｜依赖：8-9-2

- 目标：增量清单（仅下载变化/缺失文件）。
- 开发：PlanDiff 增量（已有）；打包侧增量发布流程。
- 测试：diff 检出用例。
- 验证：探针 PASS。
- 提交：`feat(阶段8): OTA 增量更新`。
- 验收：小版本只下增量。

#### 8-9-4 OTA：断点续传/失败恢复
状态：🔲｜预估：1d｜依赖：8-9-2

- 目标：弱网断点续传、坏包拒绝、无脏文件残留。
- 开发：断点（临时文件续传）、校验失败删除重下（已有语义扩展）。
- 测试：断网/坏包/续传用例。
- 验证：探针 PASS + 真机弱网。
- 提交：`feat(阶段8): OTA 断点与恢复`。
- 验收：弱网可续传，坏包不留残留。

#### 8-10 性能分级动态降级落地
状态：🔲｜预估：2~3d｜依赖：8-9-2

- 目标：L/M/H 三档实际生效（渲染分辨率/粒子/阴影/远处更新/纹理等级）。
- 开发：`DeviceCapability.SampleUnity()` 启动采样 → `TierQuality` 应用到 GameRenderer/MobileHud/MobileCombat；档位切换热重载。
- 测试：`devicetierverify` 已有 5 组注入断言；扩展档位→渲染参数映射用例。
- 验证：探针 PASS + 低端真机帧率/内存采样（对照 PRD 11.2）。
- 提交：`feat(阶段8): 设备分级动态降级`。
- 验收：低端设备稳定 30fps、内存 ≤700MB（L 档）。

#### 8-P 移动性能采样（贯穿）
状态：🔲｜预估：0.5d/次｜依赖：随大项

- 目标：每个大项（背包/NPC/社交/经济/扩展）完成后采样一次，防"功能全完成但低端跑不动"。
- 开发：`MobilePerfVerify`（Editor/真机采样：FPS/CPU/GPU/GC/内存/DrawCall，输出 JSON）；真机要求（**模拟器 SwiftShader 不代表真机性能**，采样必须真机）。
- 验证：对照 PRD 11.2（L：30FPS/≤700MB；M/H：60FPS/≤1GB）+ **真机设备矩阵**（L：4GB RAM/低端 SoC；M：8GB/主流；H：12GB/高端，每档至少一台实机；RAM 为起始规格，可按实际机型调整）；结果写回 migration-status。
- 提交：`perf(阶段8): 背包里程碑采样`（每次随大项）。
- 验收：各里程碑性能达标，偏差立项 8-10。

#### 8-11 G8 收官 + Android 侧 G7 判定
状态：🔲｜预估：1d｜依赖：8-2~8-10

- 目标：移动加权覆盖率 ≥80 判定 + Android 真机 G7（登录/进图/移动/重连）。
- 工作：三端 compat-matrix 移动列建立并勾选；加权覆盖表计算；Android 真机长稳（30 分钟压力）；无平台条件编译扩散检查（grep `#if UNITY_` 在 Client.Core）。
- 验证：加权覆盖率表 + 真机压力报告 + G8/G7-Android 判定写回 migration-status。
- 提交：`docs(阶段8): G8 收官 + G7-Android 判定`。
- 验收：G8 GO 或列明缺口清单。完成后打 tag `stage8-mobile-v1`。

---

#### 9-1 PC Player UI 收尾决策
状态：🔲｜预估：0.5d｜依赖：—

- 目标：确认 PC UI 最终路径为 RT 直绘，正式关闭 backlog 的 C4 uGUI 项。
- 工作：RT 直绘已在 PC Player 实证可渲染 HUD（pcverify 截图色数 776）；uGUI 无消费者。**决策：关闭 C4**。
- 验证：决策记录写回 migration-status ADR。
- 提交：`docs(阶段9): PC UI 路径决策（关 C4）`。
- 验收：C4 项正式关闭。

#### 9-2 安装/补丁/首启/异常恢复
状态：🔲｜预估：2~3d｜依赖：9-1

- 目标：PC 可安装、可补丁、首启引导、崩溃恢复。
- 开发：安装包（AutoPatcher 关联）、首启设置（分辨率/全屏）、异常恢复（崩溃日志 + 安全模式启动）、回滚包。
- 验证：全新机器安装→启动→进图；模拟崩溃恢复。
- 提交：`feat(阶段9): PC 安装与恢复流程`。
- 验收：一键安装出包可玩。

#### 9-3 长稳 + 多 GPU/分辨率矩阵
状态：🔲｜预估：2~3d｜依赖：9-1

- 目标：8/24/72h 长稳 + 多 GPU/分辨率回归。
- 工作：长稳脚本（起服+挂机+内存采样）；多 GPU/分辨率矩阵（对照 G3 门禁）。
- 验证：72h 无崩溃、无内存增长；矩阵 PASS。
- 提交：`feat(阶段9): 长稳与设备矩阵`。
- 验收：指标达标（PRD 11.1）。

#### 9-4 性能优化（按数据）
状态：🔲｜预估：3~5d｜依赖：9-3

- 目标：达到 PRD 11.1 PC 指标（1080p 60fps/P95≤16.6ms/进图≤3s）。
- 工作：按「可观测性→无效工作→批次/状态→资源/加载→GC」顺序，每步先采样再改（禁止无数据优化）。
- 验证：g2-perf 门禁 + 进图计时。
- 提交：按优化项拆分提交。
- 验收：指标达标。

#### 9-5 灰度发布 + G6 判定
状态：🔲｜预估：2~3d｜依赖：9-2~9-4

- 目标：真实玩家灰度 + G6 判定。
- 工作：灰度开关/回滚、5-20 人灰度、P0/P1 清零、G6 判定写回。
- 提交：`docs(阶段9): G6 判定`。
- 验收：G6 GO 或缺口清单。完成后打 tag `stage9-pc-rc-v1`。

---

#### 10-1 macOS 环境 + Xcode + 证书
状态：🔲 阻塞（外部）｜预估：—｜依赖：外部环境

- 目标：具备 iOS 构建/签名环境。
- 工作：Mac + Xcode + Apple Developer 证书 + `CRYSTAL_IOS_TEAMID` 就位；**固定 Unity 6000.5.6f1 与对应支持的 Xcode 主版本**（IL2CPP/签名/构建差异随版本漂移，改动须主会话裁决）；签名凭据流程入文档（密钥不入库）。
- 验收：`xcodebuild` 可在 Mac 上打出 IPA。

#### 10-2 iOS Player 构建跑通
状态：🔲｜预估：1~2d｜依赖：10-1

- 目标：BuildIOS 配置在 Mac 上产出可安装 IPA。
- 开发：BuildIOS.cs 已是骨架（bundle/横屏/minOS/签名入口）；补 Xcode 工程生成（Unity 导出）→ xcodebuild → 签名。
- 验证：模拟器/真机安装启动，`[mobile]` 日志链路。
- 提交：`feat(阶段10): iOS 构建流水线`。
- 验收：IPA 真机可装可启。

#### 10-3 iOS 真机登录/进图/移动/重连（G7 iOS）
状态：🔲｜预估：2~3d｜依赖：10-2

- 目标：G7 双端门禁的 iOS 侧达成。
- 工作：真机走通 登录→进图→移动→重连（复用 MobileBootstrap + TouchInput + ResourceSync）；刘海安全区真机校准（8-5-1 在 iOS 验证）。
- 验证：真机手动 + 日志断言（连接/进图/坐标变化/断线重连）。
- 提交：`feat(阶段10): iOS 真机链路`。
- 验收：G7 iOS 部分通过。完成后打 tag `stage10-ios-v1`。

#### 10-4 TestFlight + 双端发布收尾
状态：🔲｜预估：2d｜依赖：10-3

- 目标：iOS TestFlight 分发 + 双端发布文档。
- 工作：TestFlight 上传、审核材料（隐私/权限）、双端发布检查清单。
- 提交：`docs(阶段10): 双端发布清单`。
- 验收：TestFlight 外测链接可安装。

---

#### X-3 中文语言包（翻译阶段）
状态：🔲｜预估：2~3d｜依赖：阶段5 迭代包11 已备

- 目标：中文文案在 UI 全链路生效。
- 工作：接 UiText 中文路径（Arial fallback 已实证可行，或中文字体包）；取消 NetProbe 11 处 `ProbeLang.Ensure()` 注释；净图对比/区域字形核验门禁增强（当前 `bright` 谓词有弱项）。
- 验证：中文 UI 截图净图对比 + 字形区域核验。
- 提交：`feat(翻译): 中文语言包生效`。
- 验收：中文界面无缺字/乱码，关键窗口逐像素可读。

#### X-4 三端兼容矩阵扩展 + 文档收尾
状态：🔲｜预估：1~2d｜依赖：阶段8 中期

- 目标：compat-matrix 增加移动列，docs 体系与 AGENTS.md 对齐。
- 工作：compat-matrix.md 加「移动端」分节（按加权表勾选）；AGENTS.md 指向新计划文档；migration-status 流水继续。
- 提交：`docs: 三端兼容矩阵扩展`。
- 验收：矩阵移动列与实际一致。

#### X-5 崩溃日志钩子（最小形态）
状态：🔲｜预估：0.5~1d｜依赖：发布工程化前置（阶段9/10）

- 目标：三端统一崩溃/异常日志落盘，可事后取回定位。
- 开发：`Application.logMessageReceived` 钩子 + 托管异常捕获（AppDomain 回调）→ 追加写本地 `crash.log`（Android 应用目录 / PC 用户目录）；轮转（保留最近 N 份）；不做上传（后续按需再加，接入点预留）。
- 测试：探针（注入异常 → 断言日志落盘/轮转）。
- 验证：探针 PASS + 真机人为触发异常取回日志。
- 提交：`feat(横切): 崩溃日志钩子`。
- 验收：PC/Android 崩溃后均有可读日志文件；不阻塞主循环。
- 注：渠道包体系（评审 P2）**暂缓**——私服无应用商店渠道需求；未来需要时按 Unity 变体机制单独立项。

---

## 4. 常用验证命令速查（回归矩阵）

| 用途 | 命令 | 状态 |
| --- | --- | --- |
| 编译基线 | `dotnet build "Legend of Mir.sln"`（或 `build.ps1`） | 常设不变量 |
| Core 移植验证 | `tools/CoreVerify` | 每次 Core 改动 |
| 登录/选角/进图 | `net-login / net-select / net-game` | 回归 |
| 交互/战斗 | `net-interact / net-interact -Combat 1` | 回归 |
| 下线/双开/soak | `net-logout / net-dualopen` | 回归 |
| UI 迭代包 1-10 | `net-ui / net-bag / net-input / net-npc / net-skill / net-quest / net-team / net-market / net-hero / net-shop / net-settings` | 回归（串行！Unity/Library 共享锁） |
| 边缘补验 | `net-edge -Edge del/run/split/revive/recon/autopath/magic` + `net-fishing` + `net-weather` | 回归 |
| PC Player | `pcverify.ps1` | PC 回归 |
| 移动逻辑探针 | `joystickverify / mobilecombatverify / mobilehudverify / bagverify / mobileuiverify` | 移动硬 gate（bagverify/mobileuiverify 计划新建） |
| 移动真实链路 | `net-combatauto.ps1`（真实服务器击杀） | 移动集成 |
| 移动性能 | `MobilePerfVerify`（真机采样） | 里程碑采样（8-P） |
| 模拟器冒烟 | `androidverify.ps1 -Smoke` | 冒烟（非 gate） |
| 触摸诊断 | `touchdiag.ps1` | 注入调试（计划新建，任务 X-1） |

**注意**：所有 `net-*.ps1` 与 Unity 进程共享 `Unity/Library`，必须**串行**执行；同一阶段文件不相交才可并行。

---

## 5. 变更记录

| 日期 | 变更 |
| --- | --- |
| 2026-08-07 | v2.0 重制：切 Unity 三端权威计划；Android 优先主线；移动验收方法论改为确定性探针；标注旧 MonoGame 文档废弃；任务按 8-2 起编号细化 |
| 2026-08-07 | v2.1 吸收 Codex 评审：新增 8-0 移动 UI 适配层（取代"阶段7.5"概念）；任务粒度全拆（一个功能=一个验证=一个 Commit）；新增协议冻结与阶段冻结（git tag）规则；8-P 移动性能采样贯穿；否决 RT+uGUI 混合（理由见 §0）；OTA 拆 8-9-1..4 |
| 2026-08-07 | v2.2 补全评审 P2 项评估：崩溃分析采纳最小形态（X-5 崩溃日志钩子）；渠道包体系暂缓（私服无渠道需求，决策记 §0） |
| 2026-08-07 | v2.3 终审小修并冻结：8-0 依赖收紧为 X-1→X-2→8-0；新增**移动输入契约**（单击/长按/拖动/双击时序，统一手感）与 **UI 单一体系禁令**（禁止第二 UI 框架）；阶段8 合计预估 30~40 天（含 20% 缓冲）；8-P 增**真机设备矩阵**（L/M/H 4/8/12GB）；10-1 固定 Unity 6000.5.6f1 与 Xcode 主版本；协议修改流程补 ADR/compat-matrix/verify 三步。文档冻结，进入 8-0 |
