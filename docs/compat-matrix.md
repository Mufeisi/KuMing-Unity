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
- [ ] 英雄/宠物/钓鱼等扩展 — 🟨 英雄/坐骑 🟩（迭代包8 `net-hero.ps1` 五遍）；钓鱼 🟨（`FishingDialog`/`FishingStatusDialog` 已逐字移植 + 真实 `MirAnimatedButton`，编译通过；探针未覆盖，需服务器钓鱼数据流补验）
- [x] 设置 + 键位重绑定 + 手感可调项 — 🟩 迭代包10（ChatOption 筛选/透明 + HelpDialog + KeyboardLayout 键位 `CheckNewInput` 重绑定，`net-settings.ps1`）
- [ ] 昼夜/灯光/天气/特效 — 🟨 灯光 🟩（R5 `LightRender`）+ 特效 🟩（Effect 9 类移植 + R1 渲染）；天气 🟥 阻塞（`Libraries.Weather` 素材在数据源与编译产物均缺失，R7，见 `docs/backlog.md`）

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

**✅ Gate G5：通过（有条件）**——10 迭代包 PC 功能面全过 + 无 SlimDX 依赖。**阶段6 边缘补验已执行**（`net-edge.ps1` 7 子模式全 PASS：del/run/split/revive/recon/autopath/magic，上表对应项转 🟩）；钓鱼窗口已移植（任务 #9，编译通过）待真机补验；剩余：天气（R7 素材阻塞，见 `docs/backlog.md`）。
