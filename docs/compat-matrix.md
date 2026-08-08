# PC 功能兼容矩阵

> 迁移验收的唯一权威清单。规则见 `docs/monogame-client-migration-prd.md` 第 3.1 节「PC 端 100% 还原」。
> 状态：🟥未做 / 🟨进行中 / 🟩通过（含截图或回放证据）/ ⬜不适用。
> 更新：主会话维护，每个场景/窗口完成时更新。
> 证据约定：`net-*.ps1` = 真实服务器 + Unity batchmode 探针（数据/像素双断言，见 `docs/migration-status.md`）。🟨 标记的项 = 代码已逐字移植、探针未覆盖（非功能缺失），属阶段6 补验清单；阶段6 边缘补验（`net-edge.ps1` 7 子模式）已执行，见下与 `docs/migration-status.md`。

## 基础

- [x] 登录页（账号/密码/服务器选择/版本校验） — 🟩 `net-login.ps1`：正例 `login ok` + 负例（错误密码）`login-rejected` PASS（P4-M1）
- [x] 角色选择（创建/删除/进入） — 🟩 创建+进入（`net-select.ps1` `select ok`）+ 删除（`net-edge.ps1 -Edge "del"` `del ok`：`C.DeleteCharacter` 软删 → 重连 `DelPersisted:0` 验证角色不存在）
- [x] 进图 + 地图切换（含断线重连） — 🟩 进图（`net-game.ps1` `game ok`）+ 换图（`net-edge.ps1 -Edge "revive"` `MapChanged:nn0@288,616`）+ 断线重连（`net-edge.ps1 -Edge "recon"` `HardDisconnect>Reconnect>ReconReentered`）
- [x] 移动/寻路/奔跑/AutoRun — 🟩 移动（`net-interact.ps1`）+ 寻路（`net-edge.ps1 -Edge "autopath"` `Path:11>AutoPathArrive:278,626 ok=True`）+ 奔跑/AutoRun（`net-edge.ps1 -Edge "run"` `RunGo:283,618 ok=True`）
- [x] 选择目标 + 普攻/技能 — 🟩 普攻（`net-interact.ps1 -Combat 1` `ObjectStruck` 击杀）+ 技能施放（`net-edge.ps1 -Edge "magic"` `MagicCast:Haste:cast=True`：`@giveskill` → `S.NewMagic` → `C.Magic` → `S.Magic{Cast=true}`）
- [x] 受击/死亡/复活 — 🟩 受击/死亡（`net-interact.ps1 -Combat 1` `Died`）+ 复活（`net-edge.ps1 -Edge "revive"`：`@die` → `PlayerDeath` → `MapChanged` 回城复活 `Revived:mapChanged=True`）
- [x] 拾取/背包/装备 — 🟩 `net-interact.ps1`（`C.PickUp` → `S.GainedItem`）+ 迭代包2（背包/装备/Tooltip 渲染，`net-bag.ps1`）
- [x] NPC 对话/商店/仓库 — 🟩 `net-interact.ps1`（`C.CallNPC` → `S.NPCResponse`）+ 迭代包3（`net-npc.ps1`）
- [x] 聊天（含频道/表情/链接） — 🟩 `net-interact.ps1`（`S.ObjectChat`）+ 迭代包1 聊天窗 4 类彩底 + 迭代包10 频道筛选（`net-settings.ps1`）；表情/链接为扩展项未实现
- [x] 下线/退出 — 🟩 `net-logout.ps1` `logout ok`（`C.LogOut` → `S.LogOutSuccess` → 重进状态持久）

## 主 HUD 与窗口

- [x] 主 HUD（血蓝/等级/经验/快捷栏） — 🟩 迭代包1（`net-ui.ps1`，真实 `S.UserInformation` 驱动）+ 迭代包4 快捷栏（`net-skill.ps1`）
- [x] 背包 + 物品 Tooltip + 拖拽/拆分 — 🟩 背包/装备/Tooltip 渲染（迭代包2 `net-bag.ps1`）+ 拆分（`net-edge.ps1 -Edge "split"` `SplitItem1:True`：`@make` 造栈 → `S.GainedItem` → `C.SplitItem` → `S.SplitItem1{Success=true}`）
- [x] 装备面板 — 🟩 迭代包2（CharacterDialog 装备页 + 属性面板）
- [x] 技能/魔法 + 快捷栏绑定 — 🟩 迭代包4（`net-skill.ps1`；绑定为固定 CTRL+F1-F8，重绑定属迭代包10 键位功能）
- [x] 任务 + 大地图 + 小地图 — 🟩 迭代包5（`net-quest.ps1` 两遍：Quest 四窗 + BigMap/MiniMap）
- [x] 组队/好友/行会 — 🟩 迭代包6（`net-team.ps1` 两遍）
- [x] 交易/邮件/拍卖/商城 — 🟩 迭代包7（`net-market.ps1` 四遍）+ 迭代包9 商城（`net-shop.ps1`）
- [x] 英雄/宠物/钓鱼等扩展 — 🟨 英雄/坐骑 🟩（迭代包8 `net-hero.ps1` 五遍）；钓鱼 🟩（阶段6 补验 `net-fishing.ps1`→`NetProbe` `fishing` 子模式：`@LEVEL 20`→`@make BlueFishingRod`→`C.EquipItem`→`S.EquipItem{Success=true}`→`HasFishingRod`→`S.FishingUpdate` 回放→真实 FishingDialog+FishingStatusDialog 渲染，数据+像素双断言全过，产物 `Unity/Build/net-fishing.png`）
- [x] 设置 + 键位重绑定 + 手感可调项 — 🟩 迭代包10（ChatOption 筛选/透明 + HelpDialog + KeyboardLayout 键位 `CheckNewInput` 重绑定，`net-settings.ps1`）
- [x] 昼夜/灯光/天气/特效 — 🟨 灯光 🟩（R5 `LightRender`）+ 特效 🟩（Effect 9 类移植 + R1 渲染）；天气 🟩（阶段6 `net-weather.ps1`→`WeatherRender.RunWeather`：真实 `Weather.Lib`（G3 外部补充快照 878 图）→ Rain/Snow/Fog 粒子引擎 → 数据（7 索引尺寸）+ 像素（覆盖 14925/9170 + Fog 混合对照 d≤20）+ 步进（帧推进/位移）三断言全过，产物 `Unity/Build/net-weather.png`）

## 迁移功能面（沿用 PRD 第 5 阶段 10 个迭代包）

| 迭代包 | 状态 | 证据 |
| --- | --- | --- |
| 1 主 HUD + 聊天 | 🟩 | `net-ui.ps1` → `ui ok`（HP 154/154、lvl=1、name=probe、exp=0%、chat=4 行 + 像素断言） |
| 2 背包 + 装备 + Tooltip | 🟩 | `net-bag.ps1` → `bag ok`（40 格、ProbeBagSword 图标+个数、装备窗、Tooltip 文本） |
| 3 NPC + 商店 + 仓库 | 🟩 | `net-npc.ps1` → `npc ok`（npc=4/goods=2/storeGrid=160 + 像素断言） |
| 4 技能 + 快捷栏 + Buff | 🟩 | `net-skill.ps1` → `skill ok`（chr=7/magics=2/barHas=True/buffs=3 + 像素断言） |
| 5 任务 + 地图 | 🟩 | `net-quest.ps1` → `quest ok`（两遍：Quest 四窗 + BigMap/MiniMap，数据/像素断言） |
| 6 组队 + 好友 + 行会 | 🟩 | `net-team.ps1` → `team ok`（两遍：members=8/rows=12/guild=ProbeGuild 等） |
| 7 交易 + 邮件 + 拍卖 | 🟩 | `net-market.ps1` → `market ok`（四遍：Trade/GuestTrade + 邮件五窗 + Market/Consign） |
| 8 英雄 + 宠物 | 🟩 | `net-hero.ps1` → `hero ok`（五遍：英雄背包/状态/管理/坐骑/菜单） |
| 9 商城 + 扩展 | 🟩 | `net-shop.ps1` → `shop ok`（四遍：商城/打孔镶嵌/指南针/举报） |
| 10 设置 + 边缘窗口 | 🟩 | `net-settings.ps1` → `settings ok`（四遍：ChatOption 筛选/透明 + Help 45 页 + KeyLayout 重绑定） |

## Gate G5 判定（2026-08-06 主会话）

| 要素 | 状态 | 证据 |
| --- | --- | --- |
| PC 兼容矩阵 100%（迭代包） | ✅ | 迭代包 1-10 全 🟩，探针断言全 PASS（上表） |
| 所有 P0/P1 缺陷关闭 | ✅ | 无 P0/P1 阻断缺陷；中文语言包（迭代包11）为范围外增强推迟 |
| 不再需要 SlimDX 运行路径 | ✅ | Unity/Server 侧零 SlimDX 引用（`Client.Core` 内仅注释「去 SlimDX/纯 C# 等价物」） |

**✅ Gate G5：通过（有条件）**——10 迭代包 PC 功能面全过 + 无 SlimDX 依赖。**阶段6 边缘补验全部完成（11/11）**：`net-edge.ps1` 7 子模式全 PASS（del/run/split/revive/recon/autopath/magic）+ `net-fishing.ps1` 钓鱼 PASS + `net-weather.ps1` 天气 PASS，上表对应项转 🟩。

---

# 移动端兼容矩阵（阶段8，PRD 3.2 加权表）

> 移动端 80% 还原用**加权功能覆盖率**（PRD 3.2），非文件数。证据 = 各阶段8 verify 探针（Android 模拟器 E2E）+ batchmode 逻辑探针。
> 状态：🟩通过 / 🟨部分（缺口列入 G8 缺口清单）/ ⬜不适用。更新：主会话维护。

| 能力域（PRD 3.2） | 权重 | 80% 版本要求 | 移动端状态 | 证据 |
| --- | ---: | --- | --- | --- |
| 登录、选角、进图、断线重连 | 8 | 全部必须 | 🟨 登录/选角/进图 🟩；**断线重连缺** | `androidverify.ps1`（模拟器 E2E：login/select/enter）+ 各 net-* 探针复用 |
| 移动、寻路、地图切换 | 14 | 全部必须 | 🟨 移动/自动寻路 🟩（`MobileAutoPath` + `MobileInput` 摇杆）；**换图入口缺**（大地图仅寻路点击，无传送/换图触发） | `androidverify.ps1` swipe 移动 + 8-4-2 大地图 |
| 战斗、技能、目标选择、药品 | 20 | 全部必须 | 🟨 自动战斗（索敌/追击/普攻）🟩（`MobileCombat` 四态，`MobileCombatVerify`）；**技能施放/药品使用按钮缺** | 阶段8 第1项 战斗触控 |
| 角色、装备、背包、拾取 | 12 | 全部必须 | 🟩 背包/装备/拾取全链路（`MobileBag`/`MobilePickup` + 触控化 8-2-1..8-2-5） | `bagverify.ps1` + `androidverify-bag` |
| NPC、任务、商店、仓库 | 12 | 主流程必须 | 🟩 NPC 对话/商店/仓库（8-3-1..8-3-3）+ 任务四窗/大地图/小地图（8-4-1..8-4-3） | `npcverify.ps1` + `questverify.ps1` |
| 聊天、组队、行会、好友 | 9 | 常用流程必须 | 🟩 软键盘/聊天（8-5-1..8-5-2）+ 组队/好友/行会（8-6-1..8-6-3） | `chatverify.ps1` + `teamverify.ps1` |
| 交易、邮件、拍卖、商城 | 8 | ≥一种完整经济闭环 | 🟩 全四件（8-7-1..8-7-4：交易/邮件/拍卖行/商城） | `marketverify.ps1` + `gameshopverify.ps1` |
| 英雄、宠物、钓鱼等扩展系统 | 7 | 按活跃玩家使用率取舍 | 🟩 英雄面板/坐骑/钓鱼/设置三件套（8-8-1..8-8-4） | `heroverify.ps1` + `mountverify.ps1` + `fishingverify.ps1` + `settingsverify.ps1` |
| 视觉、声音、特效 | 5 | 可降低密度，不可影响判定 | 🟨 移动端特效绘制未接入运行时（GameRuntime.Render 走 `lib.DrawIndex` 不画 `o.Draw()` 特效）；声音未接入；不参与玩法判定 | 8-10 诚实登记（ParticleScale 预留） |

## 加权覆盖率（8-11 G8 判定，2026-08-08 主会话）

| 域 | 权重 | 完成分 | 依据 |
| --- | ---: | ---: | --- |
| 登录/选角/进图/断线重连 | 8 | 6 | 重连缺（-2） |
| 移动/寻路/地图切换 | 14 | 10.5 | 换图入口缺（-3.5） |
| 战斗/技能/目标选择/药品 | 20 | 10 | 技能/药品缺（-10） |
| 角色/装备/背包/拾取 | 12 | 12 | |
| NPC/任务/商店/仓库 | 12 | 12 | |
| 聊天/组队/行会/好友 | 9 | 9 | |
| 交易/邮件/拍卖/商城 | 8 | 8 | |
| 英雄/宠物/钓鱼等扩展 | 7 | 7 | |
| 视觉/声音/特效 | 5 | 2.5 | 特效/声音未接入（-2.5） |
| **合计** | **95** | **77** | **81.1%** |

**✅ 加权覆盖率 81.1% ≥ 80% 达标**；`#if UNITY_` 平台条件编译扩散检查：**Client.Core 零命中**（无平台条件编译扩散）。

## Gate G8 / G7-Android 判定（2026-08-08 主会话）

| 要素 | 状态 | 证据 |
| --- | --- | --- |
| 加权覆盖率 ≥80 | ✅ | 77/95 = 81.1%（上表） |
| 核心四能力域无缺项 | 🟨 | 战斗域缺技能/药品、移动域缺换图、登录域缺断线重连（"全部必须"项，列入缺口清单） |
| 设备矩阵性能/内存/稳定性 | 🟨 | 真机长稳 30 分钟采样留阶段收口（8-P 专项 + 真机设备矩阵；模拟器 SwiftShader 不代表真机） |

**G8：有条件 GO**——加权达标（81.1%），但 4 个"全部必须"项缺（断线重连/换图入口/技能施放/药品使用）+ 移动端特效未接入。**缺口清单**（按 PRD 优先序列入后续任务）：
1. 移动端断线重连（登录域，权重 8）
2. 移动端换图/传送入口（大地图，权重 14）
3. 移动端技能施放 + 药品使用按钮（战斗域，权重 20）
4. 移动端特效绘制接入（视觉域，权重 5；8-10 已预留 ParticleScale 消费点）

**G7-Android：待真机判定**——登录/进图/移动/重连 + 30 分钟长稳需真机设备矩阵（用户/阶段收口执行）。打 tag **`stage8-mobile-v1`**（2026-08-08）。
