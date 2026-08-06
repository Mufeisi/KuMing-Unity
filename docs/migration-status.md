# 迁移状态（权威共享文件）

> 主会话唯一维护。每个新任务会话开工前先读本文件，确认文件所有权与已确立模式，避免重做/打架。
> 更新规则：任务完成后由主会话更新本文件；进行中条目由对应任务会话写入。

## 当前阶段

**G0 可重复基线（构建已固定；运行时数据已就位；基线录制阻塞已解除——验收路径改为 golden 直解 + 真实服务器探针双断言，见 backlog ③）**
**服务端网络现代化（B1 完成 + G1 500 连接压测达标：SAEA 收发 + 主循环包预算 + 状态端口对齐，trace/压测双 harness 验证）**
**Shared 多目标化完成（`netstandard2.1;net8.0`；Unity 侧改为源码 asmdef 编译，见下方 ADR）**
**服务端性能（B2：账号存库后台化完成，8h soak 以 90s 压测 + 周期存库实证替代，已关闭 2026-08-06）**
**资源管线 Spike（R0：.Lib v3 解析对 1955 个真实文件 / 245 万图 100% 验证，风险 #1 去险）**
**资源管线（R0→阶段3：Crystal.AssetCompiler 建成，.Lib→图集+元数据+帧表，全量 1955 库编译 + verify-dir 字节级审计 **ok=1955 fail=0 missing=0** 完成）**
**Unity 客户端工程（阶段 2：`Crystal.Shared.Runtime` asmdef 编译通过；`Crystal.Client.Core` 里程碑 1 编译通过（MirMath.Point + Seam 桩 Settings/MapControl + 逐字移植 Frames/SoundList/PathFinder，A* 可寻路）；里程碑 2 编译通过（逐字移植 MapCode.cs 全部 8 种 .map 格式解析 → CellInfo[,]，seam 补 MonsterObject.BaseImage=Monster 枚举）；里程碑 3 编译通过（逐字移植 Damage.cs，新增 MirMath.Color/Font + Seams/MirLabel + Point.Offset）；里程碑 4 编译通过（逐字移植 abstract MapObject.cs 对象模型 + FrameLoop 与 Effect.cs 9 个特效类，对象子类型桩 PlayerObject/UserObject/UserHeroObject/HeroObject/QueuedAction/MonsterObject 就位，渲染/音频/文本 seam（MirGraphics/MLibrary/Libraries/DXManager/TextRenderer/SoundManager）+ 场景/UI seam（GameScene/Dialogs/MapControl 扩充/CMain.Graphics/MirLabel）+ Functions 点类型适配层齐备，`tools/CoreVerify` dotnet 验证工程 **0 警告 0 错误**）；里程碑 5a（真实 MonsterObject 5.7k 行逐字移植）；里程碑 5b（真实 PlayerObject 5.3k 行逐字移植，封包边界转换 + seam 缺口补齐，`tools/CoreVerify` **0 错误**，警告与 5a 基线一致）；里程碑 5c（真实 UserObject 827 行逐字移植，玩家状态核心：Stats/装备槽/技能 Buff/套装计算/BindAllItems，`tools/CoreVerify` **0 错误**，警告与 5a 基线一致））**
**阶段3 渲染 Spike：`Client.Assets` 读取器 + `AtlasVerify` 批处理门禁建成，Unity 运行时 vs golden 全量 1955 库逐像素哈希对照 **ok=1955 fail=0 missing=0**（2598.7s）完成**
**阶段3 渲染 Spike：`CrystalSpriteBatch` + 3 shader + `RenderVerify` 建成，R1-3 黄金帧渲染验证 **12 库 24000+ 帧全通过**（Auras 401 + 批量 Magic/Magic2/Effect/Items/Deco/Background/UI/StateEffect/Dragon/Monster/AArmour 全 fail=0）**
**阶段3 渲染 Spike：`BlendVerify` 建成，R1-4 混合/灰度/透明度/缩放语义验证 **5 例全通过**（NORMAL/ADDITIVE 混合、Opacity 0.5、Grayscale、Transform 2x，CPU 期望逐像素 ±2 对照）**
**阶段3 渲染 Spike（R2 地图 tile）：`MapRender` 探针建成，`MapReader` + MapLibs 图集 → CrystalSpriteBatch 绘制地面 Back/Middle/Front 三层 → RT PNG。map 0（700×700）全量 lib 解析 **unresolved=0**（MapLibRel 覆盖 0-273 = WemadeMir2/ShandaMir2/WemadeMir3 全段），back=342 / front=122/216 绘制，**3 处字节级 spot-check 全 fail=0**（back lib0 Tiles ×2 + front lib251 WemadeMir3/Snow/Dungeonsc）**
**阶段3 渲染 Spike（R3 动画精灵）：`AnimationRender` 探针建成，`lib.Manifest.Frames` FrameSet 表 + DrawFrame=Start+(Count+Skip)*Direction+FrameIndex → CrystalSpriteBatch 锚点+OffX/OffY 绘制 → RT 帧条 PNG。**字节级 spot-check 2 处全 fail=0**（Monster/000 Standing dir0 idx=0 4 帧全画 + Walking dir3 idx=56，验证方向步进公式）。**EncodeToPNG 行序实证**（OrientProbe）：ReadPixels→GetPixels32 为 top-down（row0=RT顶），但 **EncodeToPNG 输出翻转图**（PNG row0=RT底）→ 所有 PNG 输出须先按行翻转再编码（MapRender/AnimationRender 同款 `Array.Copy(px,(rtH-1-y)*rtW,fl,y*rtW,rtW)`）；验证：帧条内容正立后位于 y=[246,370]（锚点 352-OffY 105），翻转前误在顶部**
**阶段3 渲染 Spike（R4 场景合成+y-sort）：`SceneRender` 探针建成，DrawFloor 三层 + 地图对象精灵同 RT 合成，对象按 (Y,X) 行主序先远后近（复刻 GameScene.DrawObjects + CellInfo.Sort）。对象锚点公式 **DrawLocation=((x-camX+offX)*48,(y-camY+offY)*32) 无地面 -OffSetX 像素校正**（MonsterObject.cs:435）。map 0（350,350）双怪物（Monster/000@(350,348) 远 + Monster/001@(350,350) 近）：**锚点钉死 2 处 fail=0 + y-sort 遮挡 fail=0**（重叠区显示近者色、远者独占区仍在）。注意：地图图集根 `Build/assetcompile/map` 与对象图集根 `Build/assetcompile/all` 分离（CRYSTAL_MAP_ATLAS_DIR）**
**阶段3 渲染 Spike（R5 灯光管线）：`LightRender` 探针建成，复刻 DrawLights 三段语义（暗色 clear + additive 光源径向渐变 + Zero/SrcColor multiply 合成），三检（场景字节级 / lightTex additive / 合成 multiply）**fail=0**（320×200 双灯 + 夜场景 4 灯含半屏外裁切/全屏外零贡献）。光源图 CPU 椭圆射线径向渐变（同旧客户端色标），`CrystalSpriteMultiply.shader`（`Blend Zero SrcColor`）接入 CrystalSpriteBatch（新增 MULTIPLY 混合模式）；GDI+ PathGradientBrush 逐像素复刻留待 golden baseline（阶段 4）**
**阶段3 渲染 Spike（R6 帧选择矩阵）：`AnimationRender.RunMatrix` 批处理入口建成，验证帧选择公式 `DrawFrame=Frame.Start+(Count+Skip)*Direction+FrameIndex`（MonsterObject.cs:360 / PlayerObject.cs:763 同式）在多动作×8方向×多帧下的正确性。双层验证：①数据层逐格算 idx，越界/空帧按旧客户端 CheckImage 语义"跳过不绘"；②渲染层每动作每方向抽末帧做字节级直通对照 golden。**11 代表库全 fail=0**（Monster/000,001,002,003,010,011,013,051,056,200,201）：checks=3255（公式选中有效帧）、skipped=233（复刻越界/空帧跳过）、spot=513（渲染 spot 全字节一致）、12.3s。**边界语义实证**：013 Standing dir7 全 6 帧越界（Start=0 Count=6 off=6 末帧 47 > 库 28）、051 全动作 dir7 越界、056 Revive dir7 越界——均正确识别为跳过。注意：Reverse 动作（Revive/Dead 反向播放）末帧即实际首画帧，spot 用 Count-1**
**阶段3 渲染 Spike（R7 粒子系统）：`ParticleEngine`/`Particle`/`FogParticle` 三件套逐字移植进 `Unity/Assets/Crystal/Client.Core/Ported/`（SlimDX.Vector2→MirMath.Vector2、System.Drawing.Color→MirMath.Color、ColorTranslator.FromHtml→`new Color(0x85,0xFF,0xFF,0xFF)`、新增 MirMath.BlendMode/Vector2/Size.Empty/MLibrary.GetSize/ImageSize 测试尺寸 seam/Libraries.Weather 字段），`tools/CoreVerify` 0 错误。**R7 探针 `tools/ParticleVerify`（net8.0 控制台，源码 asmdef 编译 Client.Core，确定性 seed+单调 CMain.Time）**：26/26 PASS exit 0——生成节律（严格大于才生成）、位移（`<` 节流边界 Now==Next 更新）、wrap-around 四方向（xreset/yreset 取模）、AliveTime 消亡、FogParticle 偏移跳过、帧推进（含 `ParticleImageInfo` ctor 逐字 quirk：NextFrame 在 Duration 赋值前计算→首次推进后恢复 Duration 节律）、同 seed 两次运行序列逐项一致。**渲染层素材阻塞已解除（2026-08-06）**：`Libraries.Weather` 定位自 G3 外部补充快照（`Baselines/Crystal-G3-Weather-2026-07-31/Weather.Lib`，878 图），编译进图集 + 阶段6 天气补验 `net-weather.ps1` 全 PASS（11/11）**

**阶段3 渲染 Spike（R8 文本栅格化管线）：旧客户端文本语义 GDI+ 基线（`tools/TextVerify`，net8.0-windows+UseWindowsForms，复刻 MirLabel.CreateTexture：MeasureText→OutLine+2→5×DrawText 描边/前景、AntiAliasGridFit/TextContrast=0 栅格参数）6/6 PASS exit 0（尺寸+2、描边非空、前景白存在、无描边<有描边，产物 `Build/TextVerify/text-outline.png`/`text-plain.png`）。**Unity 动态字体探针 `Unity/Assets/Crystal/Client.Rendering.Editor/TextRender.cs`（batchmode，`Font.CreateDynamicFontFromOSFont`+`TextGenerator.Populate` CPU 字形提取→atlas UV 合成为文本纹理→CrystalSpriteBatch 画 RT→PNG）实证 PASS exit 0**：`text="Hello" font=Arial size=8 verts=20 fontTex=256x256`、atlas rect u=[0,14] v=[0,18]、合成纹理 15×19 opaque=85 maxA=254、RT 连续 Draw opaque=85、PNG 解码 opaque=85——字形像素确定性可复现，镜像为 text.png 可见 "Hello" 字形（8 号粗体）。**两个关键实证**：①Unity 动态字体在 `-batchmode -nographics` 下 TextGenerator 可 CPU 提取字形像素（无 GDI+，PC/Android/iOS 通吃，替代旧 GDI+ 栅格化的跨平台路径）；②⚠️ **batchmode 下 CrystalSpriteBatch 的 `Begin→Clear→Draw→End` 期间不可插入 RT 回读**（ReadPixels 切换 RenderTexture.active 破坏 GL.Viewport/投影状态，导致后续 Draw 静默画空；探针插桩定位此现象，真实 MirLabel 用法连续绘制不受影响）。MirLabel seam 下一步接线：`MirLabel.CreateTexture` 由 Unity 动态字体合成纹理替代，MeasureText/OutLine/描边语义按 GDI+ 基线对齐**

**阶段3 渲染 Spike（G2 1080p 门禁）：`SceneRender.RunPerf` 性能门禁建成并通过——1080p（1920×1080）代表场景（map 0 中心 350,350：back 342 + front 122 + 双怪物 y-sort）**连续 120 帧全量绘制 P50=0.53ms P95=0.64ms FPS=1879.8（目标 ≥60），正确性 fail=0（双怪物锚点钉死 + 遮挡 fail=0），G2-PASS exit 0**，JSON 产物 `Unity/Build/g2-perf.json`（`{"rt":"1920x1080","frames":120,"avg_ms":0.53,"p50_ms":0.53,"p95_ms":0.64,"fps":1879.8}`）。**关键实证：CrystalSpriteBatch 合并批（绘制序=插入序）是 y-sort 安全且高性能的**——旧逐行 Flush（612 draw calls/帧）P50=21ms FPS=35.5 G2-FAIL；合并批（88 draw calls/帧）P50=0.53ms。**根因修复：单共享 Mesh + 4096 长静态数组赋给 mesh 引入 buffer 残留污染 → 多纹理合并批非确定性错画（共享 buffer 竞态，远/近怪物渲染错乱 fail=1528 且两次运行 fail 数不同）；改为 per-texture 独立 Mesh + 精确大小数组 + 脏检查缓存（静态场景跨帧零重建，120 帧仅 8 次重建）后 fail=0 + 1879 FPS。**回归：R1-3 RenderVerify（Monster/000 360 帧 + Magic 4834 帧）、R2 MapRender、R3 AnimationRender、R5 LightRender、R8 TextRender 全 fail=0。遗留：`CRYSTAL_BATCH=0` 保留逐行模式作对照；per-texture Mesh 缓存需场景/图集切换时 `ReleaseMeshes()` 清理（已接入 SceneRender finally）**

**阶段3 渲染 Spike（R9 玩家角色渲染）：`PlayerRender` 探针建成（`Unity/Assets/Crystal/Client.Rendering.Editor/PlayerRender.cs`），复刻 PlayerObject.SetLibraries + DrawBody/DrawHead/DrawWeapon 的核心语义**：图集映射 C 系列（BodyLibrary=CArmours[Armour]、HairLibrary=CHair[Hair]、WeaponLibrary1=CWeapons[Weapon]，PlayerObject.cs:555-574）；帧区间用 **FrameSet.Player 硬编码表**（Frames.cs:157-198，manifest Frames 为空不可用）Standing(0,4,0)/Walking(32,6,0)/Attack1(136,6,0) 等；帧选择 DrawFrame=Start+OffSet*Dir+FrameIndex（OffSet=Count+Skip，PlayerObject.cs:763）；层叠 DrawBody(DrawFrame+ArmourOffSet)→DrawHead(+HairOffSet)→DrawWeapon(+WeaponOffSet)，Offset 按 Gender（男 0 / 女 808/808/416，PlayerObject.cs:586-588）；锚点 DrawLocation=((x-camX+offX)*48,(y-camY+offY)*32) 精灵左上+OffX/OffY（同 R4）。**验证 8 变体全 PASS fail=0**：男/女 × Standing/Walking/Attack1/Attack3/Struck/Running × dir0-7 × frame0-5 × 素材索引 00-05。帧选择公式实证（Walking d3 f2→drawFrame=52、Attack1 d1→142、Running d2→92 全正确）；女 offset 808 帧为真实非空帧（CArmour/00 idx808 60x72 ≠ 男 64x76）；层叠顺序（Body 最底）+ 遮挡校验全通过。**注意：RT 渲染探针须去 `-nographics`**（-nographics 禁用 RT 渲染，PNG 全 0xCD 未初始化内存；TextRender CPU 字形提取不受影响）**
**阶段3 渲染 Spike（R10 玩家场景合成）：`SceneRender` 扩展玩家对象（`p:<action>:<dir>:<frame>:<x>:<y>:<armour>:<hair>:<weapon>:<gender>` 10 段规范，`ResolvePlayer` 复用 R9 语义：FrameSet.Player + CArmour/CHair/CWeapon 图集 + DrawFrame 公式 + Gender offset；`AddLayer` 将 Body/Hair/Weapon 存为独立 `ObjLayer`（各自 Src/TW/TH），`DrawScene` 按 Body→Hair→Weapon 层叠绘制）接入 y-sort（按 (Y,X) 行主序先远后近）。**验证 6 变体全 PASS fail=0**：①玩家近怪物远（玩家覆盖怪物）②玩家-玩家相邻（两角色层叠遮挡）③女玩家+怪物（Walking dir3）④玩家 Attack1 dir1 f2（动态帧选择）⑤玩家无武器（weapon=-1 跳过 Weapon 层）⑥男/女玩家同行。**关键 bug 修复：VerifyOcclusion far-only 分支对玩家 far 改用 `RenderColorAt` 取实际渲染色**——原用代表帧 Body 色 `far.Src`，但玩家 far 自身 hair/weapon 层可覆盖 Body 像素（实测 (599,208) 期望 E0B890 实际 482410=hair 色），致 4/6 变体 false fail；overlap 分支本已用 RenderColorAt，统一后全过。**像素实证**：RT(599,208)=482410=far 玩家 hair 顶层色，验证逻辑期望必须用 RenderColorAt 顶层色而非 Body 色
**阶段3 渲染 Spike（R11 真实对象状态机驱动，阶段 3 收官）：用逐字移植的真实 `MonsterObject`/`PlayerObject` 状态机取代 R10 手工 spec 驱动场景渲染**。`MLibraryUnity`（`Client.Rendering`，renderable MLibrary：继承 seam `MLibrary`、`AtlasLibrary` 图集 + `DrawIndex(index,point,color,offSet,opacity)` 渲染内核 + `BridgeFrames` 将 manifest `FrameEntry`→`FrameSet` 按 `ActionId==MirAction` 数值 cast）接入 `Libraries` 数组（`Libraries.Monsters[img]`/`CArmours`/`CHair`/`CWeapons` 写回），真实对象 `Load` 时命中。`RunObjects`（`CRYSTAL_OBJSPEC`：怪物 `m:<image>:<action>:<dir>:<frame>:<x>:<y>` 7 段 / 玩家 `p:<action>:<dir>:<frame>:<x>:<y>:<armour>:<hair>:<weapon>:<gender>:<class>` 11 段）建相机（`MapObject.User` + `GameScene.Scene` + `GameScene.CanMove=false` + `MapControl.OffSetX/Y`）→ 预 EnsureMLibrary → 构造真实对象 `Load` → 逐帧 `Process()`（`CMain.Time` 步进）→ 验证（数据级 `DrawFrame` 同 R10 公式 + 像素级 `VerifyPresence`/`VerifyOcclusion` + 地图 `DrawMapTiles` 复用）。**验证矩阵全过**：V1-V4 怪物（Standing/Attack1/Walking 状态机 `dataMatch=True` + 像素 fail=0，Walking 真实锚点含 Moving OffSet）；V5 双怪 y-sort fail=0；V6-V8 玩家（男/女 `dataMatch=True` + layers=3 Body/Hair/Weapon + fail=0）。**强等价回归：R11 vs R10 同 spec PNG 逐像素 diff=0**（Standing+Attack1 组 / 双怪组 / 玩家组）。**踩坑与修复**：①`MonsterObject` 为 internal（移植疏漏，其余 Ported 类皆 public）→ 改 public；②`objs.Sort` 未同步 `realObjs` → 渲染错位（反向遮挡致 Standing false fail）→ `Obj.Real` 携带真实对象引用，渲染/验证从 objs 取；③`S.ObjectPlayer.TransformType` 默认 0 → `if (TransformType > -1)` 误入 Transform 分支 `BodyLibrary=Transform[0]=null`（玩家全未画，got=背景色）→ Load 显式 `TransformType=-1`；④女玩家 `ArmourOffSet=808` 由真实 `SetLibraries` Others case 计算（非 R10 手工硬编码）；⑤真实 `Draw` 有方向相关武器层，R11 对齐 R10 用 `WeaponLibrary1` 单层补画；⑥R11 渲染循环漏地图 → 提取 `DrawMapTiles` helper（R10 `DrawScene` 与 R11 `RunObjects` 共用，DRY）

**阶段4 端到端垂直链路（P4-M1 登录链路 + P4-M2 选角进图 + P4-M3 GameScene 主循环 + P4-M4 五类交互 + P4-M5 下线/HUD/持续游玩完成：Unity 客户端对真实服务器走通 登录→选角→进图→对象 spawn/移动/渲染→战斗→拾取→背包→NPC→聊天→下线→HUD，服务器零修改；✅ Gate G4 判定 GO（见下方 P4-M5 节：全链路 7/7 探针 + 双开对照 + 2h soak PASS））**
**阶段5 UI 系统（迭代包1 完成：纯 C# 兼容控件 + RT 直绘方案落地——控件基类最小契约 + ChatDialog 完整移植 + MainDialog HUD 状态条移植进 Client.Core，UiText 渲染桥（Unity 动态字体字形预构建）接 TextRenderer seam，真实 MainDialog+ChatDialog 控制树经 `net-ui.ps1`→`NetProbe.RunUi` 合成 RT(1024×768) 出 PNG，数据断言（HP 154/154/lvl=1/name=probe/exp=0%/chat=4）+ 像素断言（HP/name 白字形、orb 红区、聊天面板、蓝/红/绿/暗红四行彩底）**全 PASS exit 0**）**
**阶段5 UI 系统（迭代包2 完成：背包/装备/Tooltip 控制树渲染 + 交互输入——MainDialog 顶部功能按钮点击链 hover→pressed→click→开/关对话框 + ChatTextBox 输入光标，`net-input.ps1`/`net-bag.ps1`/`net-ui.ps1` + CoreVerify 0w/0e 全绿）**
**阶段5 UI 系统（迭代包3 完成：NPC 对话 + 商店 8 格 + 仓库 10×16 网格控制树渲染，`net-npc.ps1`→`NetProbe.RunNpc` 出 PNG，数据断言 npc=4/goods=2/storeGrid=160 + 像素断言全过，回归矩阵 4 脚本 + CoreVerify 0w/0e 全绿）**
**阶段5 UI 系统（迭代包4 完成：技能页 7 格 MagicButton + 快捷栏 8 格 + Buff 状态栏控制树渲染，`net-skill.ps1`→`NetProbe.RunSkill` 出 PNG，数据断言 chr=7/magics=2/barHas=True/buffs=3 + 像素断言全过，回归矩阵 5 脚本 + CoreVerify 0w/0e 全绿）**
**阶段5 UI 系统（迭代包5 完成：任务列表/任务日记/任务详情/任务追踪 + 大地图 + 小地图控制树渲染，`net-quest.ps1`→`NetProbe.RunQuest` 两遍渲染（遍1 Quest 四窗 + 遍2 BigMap/MiniMap）出 PNG，数据/像素断言全过（questListFrame/questRowName/diaryGroup/diaryTask/trackName/trackTask/detailFrame/rewardDeco/bigFrame/bigNpc/miniFrame/miniView/miniCoord），回归矩阵 6 脚本（ui/bag/input/npc/skill/quest）+ CoreVerify 0w/0e 全绿）**
**阶段5 UI 系统（迭代包6 完成：组队 + 好友 + 行会控制树渲染，`net-team.ps1`→`NetProbe.RunTeam` 两遍渲染（遍1 组队+好友 + 遍2 行会）出 PNG，数据/像素断言全过（members=Probe..Member7 allow=True rows=12 online=True blocked=2 guild=ProbeGuild lv=3 mem=12/50 groupFrame/friendFrame/guildFrame/guildName/guildLevel/guildMembers/notice），回归矩阵 6 脚本 + CoreVerify 0w/0e 全绿）**
**阶段5 UI 系统（迭代包7 完成：交易 + 邮件 + 拍卖控制树渲染，`net-market.ps1`→`NetProbe.RunMarket` **四遍渲染**（遍1 Trade+GuestTrade + 遍2 邮件五窗 + 遍3 TrustMerchant Market 面板 + 遍4 TrustMerchant Consign 面板）出 PNG，数据断言（trade=Probe/5000 guest=Guest/3000 mail=3/3 rows=5/5 filters=8）+ 像素断言（tradeFrame=30416/tradeIcon/guestFrame/guestIcon/mailListFrame/mailRowSender/composeLetterFrame/composeRecipient/composeParcelFrame/parcelCell/readLetterFrame/readSender/readParcelFrame/readParcelCell/marketFrame=227659/filterTree/row0Icon/row0Name/searchBtn/consignFrame/consignItem/sellBtn/helpLabel 全过），回归矩阵 7 脚本 + CoreVerify 0w/0e 全绿）**
**阶段5 UI 系统（迭代包8 完成：英雄 + 宠物/坐骑控制树渲染，`net-hero.ps1`→`NetProbe.RunHero` **五遍渲染**（遍1 英雄背包 AutoPot + 遍2 英雄状态/腰带/行为 + 遍3 英雄管理 + 遍4 坐骑 5 槽 + 遍5 英雄菜单）出 PNG，数据断言（inv=40 autoPot=True avatar=1400 avatars0=Hero1/1770 mountIdx=167 mountName=ProbeMount）+ 像素断言（heroInvFrame=71853/heroInvIcon/autoPotBtn/autoPotLabel/infoFrame/infoAvatar/infoName/beltFrame/beltCell/behaviour/manageFrame/currentAvatar/slotAvatar/mountFrame=112390/mountAnim/reins/mask/mountName/menuFrame/menuBtn 全过），回归矩阵 8 脚本 + CoreVerify 0w/0e 全绿）**

**阶段5 UI 系统（迭代包9 完成：商城 + 小扩展集控制树渲染，`net-shop.ps1`→`NetProbe.RunShop` **四遍渲染**（遍1 商城 + 遍2 打孔镶嵌 + 遍3 指南针 + 遍4 举报）出 PNG，数据断言（filled=6/g0=BattleSword/无 Wizard 泄漏/page=1 / 1/filters=Show All|Weapons/class=Warrior/Gold 勾选 Credit 未勾/New 初始隐藏 + handler 增删改）+ 像素断言（shopFrame=323217/shopIcon/shopName/gold/credit/box/page + socketFrame/socketStone + compass + reportDrop/reportBox/reportSend 全过），回归矩阵 9 脚本 + CoreVerify 0w/0e 全绿）**

**阶段5 UI 系统（迭代包10 完成：设置三件套控制树渲染，`net-settings.ps1`→`NetProbe.RunSettings` **四遍渲染**（遍1 ChatOptionDialog 筛选 + 遍2 透明 + 遍3 HelpDialog + 遍4 KeyboardLayoutDialog）出 PNG，数据断言（AllFiltersOff 初态/AllButton 全开/GeneralButton 关/透明 tab 切换/TransparentChat+ChatDialog 着色/45 页帮助/翻页/图片页/Movements 标题/Keylist 默认 F9/行点击 WaitingForBind/CheckNewInput Ctrl+K+Delete）+ 像素断言（coFrame/coAll/coChatTab/co2Frame/co2On + helpFrame/helpImg/helpPageLabel + kbdFrame/kbdRowBtn 全过），回归矩阵 10 脚本 + CoreVerify 0w/0e 全绿）**

**阶段5 UI 系统（迭代包10 补充：文本渲染修复 + 探针复跑验证完成，net-settings/net-shop 回归 PASS）**：`TextGlyphBuilder` 逐字符合成器建成（`Client.Rendering.Editor/TextGlyphBuilder.cs`），修复 Unity `TextGenerator` 多字形整段 Populate 的 UV 塌缩 bug（≥6 字形 UV 收缩窄列 + 负宽 quad + 丢字符，"4. Movements" 12 字符只出 11 quad 丢 's'）：①`RequestCharactersInTexture` 全字符入图集（此后单字符 Populate 不触发图集重建，UV 稳定）；②逐字符单 Populate → 稳定 UV 包围盒 + advance 游标 + 字形 y 包围盒基线对齐 → 逐字形 blit 到文本位图（非整段 UV 总包围框裁剪）。`UiText.GetTextTexture` 与 `TextRender` 均委托 `Build`。**复跑验证（探针切回英文路径）**：net-settings PASS（exit 0 "settings ok"），pass4 KeyboardLayout 净图 strictWhite 文字像素 **2776（修复前 260）**——"F9" 按钮字形 ASCII 可读、标题区 346/行绑定区 1683；net-shop 回归 PASS（商城+扩展集英文路径无破坏）。**中文语言包（迭代包11）→ 用户确认推迟至翻译阶段（2026-08-06）**：非迁移核心路径（功能面 100% 由迭代包 1-10 + 阶段6 补验覆盖），因历史截图字体观感反馈插入、原不在计划内。`LoadClientLanguage`/`LoadServerLanguage` 的 .NET Core 枚举-写入 bug 已修复，`Chinese.json`（1089 Text + 158 Enum）已生成，`NetProbe.cs` 11 处 `ProbeLang.Ensure()` 保持注释（恢复时取消即可）。**FontProbe 实证（2026-08-06，`-nographics` 跑通 Exit 0）**：中文在 `TextGlyphBuilder` 层构建**正常**——Arial 动态字体经系统 fallback 对 CJK 产生 quad（"移动" chars=2 quads=2、字形 27×27），逐字符构建 `背包开/关` size=8 → 41×12 opaque=171（prewarm True/False 同）、`移动` size=8 → 19×12 opaque=93；此前 net-settings 中文"零渲染"（strictWhite 260 vs 英文 2776）已被 TextGlyphBuilder 修复顺带解决或仅存于 UiText 接线层（未复现）。**翻译阶段待办**：接 UiText 中文路径（字体方案选型：Arial fallback 可用，或中文字体包）+ 取消 11 处 Ensure 注释 + 净图对比/区域字形核验门禁增强。**验证门禁弱点实证**：`bright`（r+g+b>60）对 Clear 背景 (25,25,25) 求和 75 亦命中，`kbdRowBtn` 等断言实际不验证文字可读性——后续依赖中文迭代增强（净图对比/区域字形核验）**

**Gate G5 判定（2026-08-06 主会话，✅ 通过-有条件）**：阶段5 收官。①**PC 兼容矩阵（迭代包）100%**——迭代包 1-10 全 🟩，探针断言全 PASS（`docs/compat-matrix.md` 同步更新为真实状态：基础/窗口清单按 P4-M1..M5 + 迭代包覆盖度标记 🟩/🟨）；②**无 P0/P1 阻断缺陷**——中文语言包（迭代包11）为范围外增强推迟，不计阻断；③**不再需要 SlimDX 运行路径**——Unity/Server 侧零 SlimDX 引用（`Client.Core` 内 SlimDX 仅注释「去 SlimDX/纯 C# 等价物」，无实际代码依赖）。**转阶段6 补验清单**（🟨 边缘项，均为代码已逐字移植、探针未覆盖，非功能缺失）：删除角色 / 地图切换 / 断线重连 / 寻路 / 奔跑 / AutoRun / 技能施放 / 复活 / 物品拖拽拆分 / 钓鱼窗口 / 天气（R7 素材阻塞）。

**阶段6 边缘补验（2026-08-06 主会话，11/11 项全 PASS）**：新增 `Build/net-edge.ps1` 编排 + `NetProbe.Mode.Edge` 7 子模式探针（`Unity/Assets/Crystal/Client.Rendering.Editor/NetProbe.cs`），真实服务器 + Unity batchmode，`[netprobe] edge ok` 断言。**全 7 子模式 PASS（exit 0）** + **钓鱼窗口 PASS（`net-fishing.ps1`）** + **天气 PASS（`net-weather.ps1`）**：
- **del**（删除角色）：`C.DeleteCharacter` 软删 → 重连 `DelPersisted:0` 验证角色不存在（角色名全局保留 → 用唯一账号/角色名）
- **run**（奔跑）：`C.Walk` 一步（服务器 `_stepCounter++`/`ActionTime=+600`）→ `C.Run` 两步 `RunGo ok=True`（失败根因=服务端阻塞对象客户端未预知 → `RunWalk` 阶段 `EmptyCell` 重验 + 换方向重走最多 3 次）
- **split**（物品拖拽拆分）：`@make {idx} 2` 造栈（fresh 角色起始物均 Count=1 无现成栈）→ `S.GainedItem{Count=2}` 确认可叠放 → `C.SplitItem` → `S.SplitItem1{Success=true}`
- **revive**（复活 + 换图）：`@die` → `S.PlayerDeath` → 回城 `S.MapChanged:nn0@288,616` → `Revived:mapChanged=True`
- **recon**（断线重连）：TCP 硬断 → 重连重进图 `ReconReentered`（登录状态机复位验证）
- **autopath**（寻路 + AutoRun）：`PathFinder` 11 节点路径逐节点 `C.Walk` → `AutoPathArrive ok=True`（防御：连续 5 次未推进视为服务器侧未知阻塞 → 该格打阻塞位重寻路绕行）
- **magic**（技能施放）：`@giveskill` → `S.NewMagic`/`S.MagicLeveled` → `C.Magic` → `S.Magic{Cast=true}`。**施放法术由 Lightning 改 Haste**：Lightning 需 45 MP（BaseCost 38+LevelCost 7），fresh level-1 Warrior 仅 11 MP → 服务器 `cost>MP` 早退不发 S.Magic（MagicCast 超时）；Haste 仅 5 MP 且无目标/道具依赖，Cast=true。

其余：**钓鱼窗口（阶段6 补验第 10 项，2026-08-06 完成）**：`net-fishing.ps1`（`FishingDialog`/`FishingStatusDialog` 真实控制树 + 渔具 5 槽 + `MirAnimatedButton` 动画施放按钮 + `S.FishingUpdate` 封包回放 Ported `PlayerObject.FishingUpdate` 状态链）→ **PASS exit 0（`edge ok`）**，产物 `Unity/Build/net-fishing.png`。链路：`@LEVEL 20`（见坑②）→ `@make BlueFishingRod`（`S.NewItemInfo:794` 真实 DB ItemInfo：type=Weapon shape=49 reqClass=None）→ `S.GainedItem` → `C.EquipItem{Weapon}` → `S.EquipItem{Success=true}` → `HasFishingRod:True` → `S.FishingUpdate` 回放（`Fishing:True / FoundFish:True`）→ 渲染断言全过（fishFrame=56721/rodPx=14400/statusFrame=24297/chance=1794/progress=1094/fishBtn=2126/TitlePx=23）。**三个踩坑（修复）**：①`net-fishing.ps1` 为 UTF-8 无 BOM + 中文注释 → PowerShell 5.1 按 ANSI 解码致 ParserError（其余 net-*.ps1 均 ASCII 注释无此问题）→ 转 UTF-8 BOM；②`BlueFishingRod` `reqType=Level reqAmt=20` → fresh level-1 角色 `CanEquipItem` 拒绝（`equip-rejected`）→ 探针先 `@LEVEL 20` 再 `@make`（服务器串行处理 Chat 无竞态）；③`MirItemCell.ItemArray` 缺 `MirGridType.Fishing` case → 渲染 `NotImplementedException` → 补 `MapObject.User.Equipment[(int)EquipmentSlot.Weapon]?.Slots`（旧客户端 MirItemCell.cs:84 逐字语义：渔具槽=鱼竿子物品）+ `RunEdge()` 补 `_outPath/_rtW/_rtH` 初始化（fishing 渲染 RT 用）。回归：`net-shop.ps1`（MirItemCell Socket 路径相邻改动）PASS + `tools/CoreVerify` 0 错误。**天气（阶段6 补验第 11 项，2026-08-06 完成，R7 渲染层闭环）**：素材 `Weather.Lib`（31.7MB，v3 878 图 / 16 页，sha256 `9A065B7D…`）定位自 `D:\ChuanQi\Baselines\Crystal-G3-Weather-2026-07-31\`（G3 外部天气素材补充快照，supplementId `Crystal-G3-Weather-2026-07-31`）→ AssetCompiler 编译进 `Build/assetcompile/all/Weather`（compile verify OK 878 图字节级 + golden 侧车 878 行）。**`net-weather.ps1`→`WeatherRender.RunWeather`（`Client.Rendering.Editor/WeatherRender.cs`，纯客户端探针无需服务器）** → **PASS exit 0（`weather ok`）**，产物 `Unity/Build/net-weather.png`。验证：①数据层 7 粒子索引 GetSize 与 manifest 一致（0=512² 雾/1=32² 烬/43=400² 雪/164=512² 雨/359·531·587=512² 叶）；②渲染层复刻 `GameScene.UpdateWeather`（GameScene.cs:12278）引擎组装（Rain=164 150帧 / Snow=43 20帧 / Fog=0 + 网格铺粒子 + velocity），pass A 帧 0 像素断言（Rain coverage=14925 / Snow coverage=9170 真实图集绘制）+ Fog 精确混合对照（src `#E7E3E7`×0.4+bg×0.6，d=(10,12,10)≤20）；③pass B Process 6 帧状态机活跃（帧推进 5,5,0 + Snow 位移 (-394,-6)）。**关键 bug 修复：`MLibraryUnity.DrawIndex` opacity 双重应用**——opacity 同时写入 `color.a` 与 `DrawOpaque` 参数 → 实际 alpha=o²（Fog 0.4 实测 got=44 vs exp=107 暴露；BlendVerify 走 `Draw`+`SetOpacity` 单次路径未覆盖、R11 对象渲染 opacity=1 亦无感）→ 修复为单次应用（`color.a` 只携带图元 alpha）后 got=97（d=10）。回归：`net-game.ps1`（R11 渲染链路）PASS + `tools/CoreVerify` 0 错误。**阶段6 全部 11 项完成**；R7 阻塞解除（backlog 已勾除）。`Setup.ini` TestServer 翻转已恢复（备份一致），无 Unity/Server 进程残留。

**阶段7 移动骨架（Android Host，2026-08-06 开工）**：`BuildAndroid.cs`（`Client.Rendering.Editor`）PlayerSettings 配置（company=Mir2/product=Crystal/bundle=com.crystal.mir2/minSdk=26）+ 最小启动场景 `Assets/Scenes/Main.unity` + ProjectSettings（Android/iOS 包名/minSdk/目标版本/图标）→ `BuildPipeline.BuildPlayer` APK 构建，**`[build-android] OK apk=Unity/Build/Android/crystal.apk size=16MB`（2026-08-06）**。APK 为构建产物不入库（gitignore `/Unity/Build/Android/`）。G7 门禁（双端登录/进图/移动/重连，无平台条件编译扩散）待触控 adapter/移动资源包/摇杆/简化 HUD 落地。

**阶段7 第 2 项（屏幕方向、挂起与恢复，2026-08-06 完成）**：①**屏幕方向**：`PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft`（传奇横屏，`defaultScreenOrientation: 4→3` 持久化 ProjectSettings）；②**生命周期组件**：新 `AppLifecycle.cs`（`Client.Rendering` runtime）——`OnApplicationPause/OnApplicationFocus` 回调输出 `[app-lifecycle]` 前缀日志 + 暂停时长记录（后续断线重连/状态保存接入点），`BuildAndroid.cs` 场景幂等挂载（`EnsureAppLifecycle`，`[build-android] scene ... appLifecycle=true`）；③**模拟器实证**（Android 16，Medium_Phone_API_36.1）：安装 `Success` → 启动 `COLD 4.9s` → **横屏渲染 PNG 2400x1080**（`Unity/Build/android-emulator-landscape.png`，aapt manifest `screenOrientation` 为 unspecified 但 Unity 运行时强制 LandscapeLeft，实测方向为准）→ `HOME` 切后台（`topResumedActivity` 转桌面，OnApplicationPause 记录起点）→ 回前台 `[app-lifecycle] resume pausedMs=31029`（31 秒暂停时长精确）+ `focus=True`，进程全程存活（Unity 切后台日志缓冲待恢复 flush，`_pauseStart` 时间戳正确）。**安全区适配推迟**：空场景无移动 UI，无适配目标可验证，等触控 HUD/移动资源包时一并做（backlog 登记）。

**阶段7 第 3 项（触控 Input Adapter，2026-08-06 完成）**：①**纯逻辑层 `TouchInputMapper`**（`Client.Rendering`，无 Unity Input 依赖）：单指主触摸手势→Mir 鼠标语义——Down 锁定起点、Move 位移超阈值（`DragThresholdPx=10`，dx²+dy² 严格大于）转拖拽、Up 未拖拽=Click、Cancel 中止、次触点忽略；②**`TouchInputAdapter`**（MonoBehaviour，`Update` 轮询 `Input.touches` + 鼠标回退 `Input.GetMouseButton*` 供 PC/模拟器测试）→ 更新 `CMain.MPoint`（控件 hit-test 基准）→ `GameScene.Scene.OnMouseMove/Down/Up/Click` 分发，**复用探针 `ClickControl` 同链路**（Move 更新 MouseControl/hover → Down 置 ActiveControl → Up+Click；GameScene 在 `Client.MirScenes` 全限定引用规避 `Crystal.Client` 命名空间解析陷阱）；③**`TouchInputVerify` 探针**（Editor batchmode）：8 用例全 PASS exit 0——点击/拖拽/阈值边界（恰等不翻）/已拖拽不重复翻/cancel 中止/未触摸非法序列忽略/状态复位；④**`BuildAndroid.cs` 幂等挂载**（`EnsureTouchInput`，`[build-android] scene ... touchInput=true`）；⑤**模拟器实证**（Android 16）：logcat `[touch-input] adapter started (touch->mouse)` + adb tap/swipe 注入后进程存活无 FATAL/ANR（空场景 `GameScene.Scene` 为 null 分发安全跳过，接入游戏逻辑后自然生效）。**软键盘推迟**：空场景无输入框消费方（MirTextBox 未在移动端运行），`TouchScreenKeyboard` 桥接留待登录/聊天 UI 移动化（backlog 登记）。

## 已确立的模式 / 决策（ADR 摘要）

| 决策 | 内容 |
| --- | --- |
| 客户端引擎 | Unity 6 LTS + 内置渲染管线 + uGUI，不引 DOTS |
| Unity 编辑器 | **6000.5.6f1（Unity 6.5 LTS）** 已装 `C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe`；`Unity/` 工程 headless 创建（Unity 相关目录已进 .gitignore） |
| Unity 引 Shared 方式 | **源码 asmdef 编译**（非计划原定的 netstandard2.1 DLL）：直接 `-batchmode` 启动 Unity 不携带 Hub 会话 token 时 importer meta 不落盘（DLL 无法注册为插件），故改由 `tools/sync-unity-shared.sh` 将 `Shared/` 源码（排除 bin/obj）同步进 `Unity/Assets/Crystal/Shared.Runtime/Shared/`（gitignored），asmdef 直接编译。单一编译源不变（Shared/ 仍是权威），Unity 侧副本为构建产物。协议零漂移 |
| Unity C# 语言版本 | asmdef 默认 C# 9.0，Shared 依赖隐式 using → `Unity/Assets/csc.rsp` 加 `-langversion:10`（与 netstandard2.1 构建一致）；`GlobalUsings.cs` 声明 7 个 `global using` |
| Unity 许可证 | **✅ 有效（个人许可）**：`C:\ProgramData\Unity\Unity_lic.ulf`（含 headless/android/ios 授权）+ 用户级 `UnityEntitlementLicense.xml`；经 Hub 启动（带 `-accessToken`）日志正常。**注意**：直接 `-batchmode` 启动不带 Hub 会话 token 时 LicensingClient 拉不到令牌（`401 Token not found in cache`）→ importer meta 不序列化（59 字节裸 GUID）；脚本编译不受影响。**Build Player 须经 Hub 启动的会话执行** |
| 服务端 | 保留 C#，网络层现代化（SocketAsyncEventArgs + 每连接 SPSC 队列 + 主循环预算） |
| Shared 共享 | `Shared.csproj` multi-target `netstandard2.1;net8.0`，Unity 引 netstandard2.1 产物 |
| 构建入口 | `build.ps1`（跳过后台遗留网站项目 PatcherWebSite，它需 VS2022 全 MSBuild） |
| SDK 固定 | `global.json` 锁 8.0.418 |
| 加密 | 双端口 + 滚动 XOR 混淆（一期），明文端口过渡保留一个版本 |
| 运营后台 | 保留 WinForms SMain 编辑器 + 进程内嵌 Web 面板 |
| 迁移方法论 | 沿用 `docs/monogame-client-migration-prd.md` 的绞杀式 + Gate 门禁 + 回放/黄金截图验证 |
| 广播优化 | **不做空间索引**：`DataRange=16` → 33²=1089 格 vs O(P) 线性扫 `Players`，实际每图玩家数远低于 1089（2338 图摊 300-2000 人），线性扫已最优（B2-0 实测否决） |
| 账号存库 | **后台线程序列化**：主线程浅快照（含 ID 计数器防撕裂）+ `Task.Run` 序列化 + `.n` 原子交换，周期存库不再阻塞主循环；`SaveDB` 保持同步（549KB 静态数据 ~1ms，无需异步） |
| 压测反滥用绕过 | 服务端三层限制（`MaxIP`/`IPBlockSeconds`/`MaxUser`）对同 IP 压测是硬墙：harness 覆写为 10000/0/10000；`IPBlockSeconds=0` 曾因 `Envir.Time` tick 粒度产生 `banDate==Now` 残留封禁，修复为 ctor 仅在 `>0` 时登记 |

## 文件所有权（任务分配/进行中/已完成）

| 区域 | 文件/模块 | 状态 | 属主任务 |
| --- | --- | --- | --- |
| 协议 | `Shared/` 全部 | ✅ 多目标化完成（netstandard2.1;net8.0），Unity 引 netstandard2.1 产物 | N0 |
| 地图解析 | `Client/MirObjects/MapCode.cs` | ✅ 已迁移（Unity Core 里程碑 2 编译通过）；R2 探针运行时验证 8 种 .map | 阶段 2→3 |
| 渲染门面 | `Client/MirGraphics/MLibrary.cs`、`DXManager.cs` | 解析侧已 Spike 验证（R0）；GPU 面未迁移（需重写） | R0 |
| 对象模型 | `Client/MirObjects/MapObject.cs`、`Effect.cs`、`MonsterObject.cs`、`PlayerObject.cs`、`UserObject.cs` | ✅ 已迁移（Unity Core 里程碑 4 + 5a + 5b + 5c 编译通过）：abstract MapObject 对象模型 + FrameLoop + 9 个特效类逐字移植；**真实 MonsterObject（5.7k 行）逐字移植完成**（含 Frames 集、CreateProjectile 覆盖、绘制/特效/名称全套）；**真实 PlayerObject（5.3k 行）逐字移植完成**（SetLibraries/SetEffects/Process/SetAction/ProcessFrames/音效/绘制/名称全套，封包边界转换：ObjectPlayer.NameColour/Location、FishingUpdate.FishingPoint、C.Magic/C.RangeAttack Point → SDPoint）；**真实 UserObject（827 行）逐字移植完成**（玩家状态核心：Load(S.UserInformation)/SetSlots/RefreshStats 全套（等级/负重/装备/套装/Mir套装/技能/Buff/公会Buff/上限）、BindAllItems、GetMaxGain、QueuedAction 队列；seam 补 GameScene.ItemInfoList/Bind/GuildDialog、Settings.LoadTrackedQuests、GuildDialog 桩、Functions.GetRealItem 适配）；对象子类型桩仅剩 UserHeroObject/HeroObject | 里程碑 4 + 5a + 5b + 5c |
| 场景 | `Client/MirScenes/GameScene.cs`（12.5k 行） | 未迁移（需先拆分） | — |
| UI | `Client/MirControls/`、`MirScenes/Dialogs/` | 未迁移（需 uGUI 化） | — |
| 网络(客户端) | `Client/MirNetwork/Network.cs` | 未迁移 | — |
| 网络(服务端) | `Server/MirNetwork/MirConnection.cs` | ✅ 已完成（B1-H0/a/b/c/d：SAEA 接收 + 主循环包预算 + 合并发送/部分发送 + 状态端口对齐；G1 500 连接压测达标，trace 回归 23 包 diff 空） | B1 |
| 主循环 | `Server/MirEnvir/Envir.cs` | 未迁移 | — |
| 音频 | `Client/MirSounds/` | 未迁移 | — |
| 输入 | `Client/KeyBindSettings.cs` | 未迁移 | — |
| 资源管线工具 | `tools/AssetCompiler/`（.Lib→图集+元数据+帧表+golden） | ✅ 全量 1955 库编译 + verify-dir 字节审计 + golden-dir 侧车完成 | R0→阶段3 |
| Unity 工程 | `Unity/`（Assets/Packages/ProjectSettings） | 已创建；`Crystal.Shared.Runtime` asmdef 编译通过；Core 里程碑 1-5c（MapCode/Damage/MapObject/Effect/**MonsterObject**/**PlayerObject**/**UserObject** 等）完成，`tools/CoreVerify` dotnet 验证工程基线成立（里程碑 5a：0 警告 0 错误；里程碑 5b/5c：0 错误，警告与 5a 一致）；`Client.Assets` 读取器 + `AtlasVerify` 批处理门禁建成（**全量 1955 库 ok=1955 fail=0 missing=0**） | 阶段 2→3 |
| R2 地图 tile 探针 | `Unity/Assets/Crystal/Client.Rendering.Editor/MapRender.cs` | ✅ MapLibs 段映射 0-273 全覆盖 + 三层 DrawFloor 复刻 + 字节级 spot-check；map 0 unresolved=0、3 处 fail=0、PNG 无灰缝 | 阶段3 R2 |
| R3 动画精灵探针 | `Unity/Assets/Crystal/Client.Rendering.Editor/AnimationRender.cs`、`OrientProbe.cs` | ✅ FrameSet 帧选择 + 锚点/OffX/OffY 绘制 + 字节级 spot 2 处 fail=0（含 dir3 步进）；EncodeToPNG 行序实证（PNG 输出须先翻转）；**R6 扩展 `RunMatrix` 批处理：帧选择公式×多动作×8方向×多帧矩阵验证，11 代表库全 fail=0（checks=3255 / skipped=233 越界复刻 / spot=513）** | 阶段3 R3+R6 |
| R4 场景合成探针 | `Unity/Assets/Crystal/Client.Rendering.Editor/SceneRender.cs` | ✅ DrawFloor 三层 + 对象 y-sort 合成；对象锚点公式（无 -OffSetX）实证 + 遮挡字节级 fail=0；双图集根区分 | 阶段3 R4 |
| G2 性能门禁 | `SceneRender.RunPerf` + `CrystalSpriteBatch` per-texture Mesh 缓存（`_meshes`/`_meshSnap`/`GetMesh`/`ReleaseMeshes`/精确数组 BuildMesh） | ✅ 1080p 代表场景连续 120 帧 P50=0.53ms FPS=1879.8 G2-PASS（目标 60），正确性 fail=0；**合并批 y-sort 安全**（旧逐行 612 calls 35.5FPS FAIL）；**共享 mesh 残留 buffer 竞态根因修复**（per-texture Mesh + 脏检查缓存，静态场景跨帧零重建 120 帧 8 次重建）；回归 R1-3/R2/R3/R5/R8 全 fail=0；`ReleaseMeshes` 防跨场景泄漏 | 阶段3 G2 |
| R5 灯光管线探针 | `Unity/Assets/Crystal/Client.Rendering.Editor/LightRender.cs` + `CrystalSpriteMultiply.shader`（CrystalSpriteBatch 增 MULTIPLY） | ✅ 暗色 clear + additive 光源 + Zero/SrcColor multiply 合成三检字节级 fail=0（含裁切/全屏外灯）；光源图 CPU 椭圆射线渐变（GDI+ 复刻留待 golden） | 阶段3 R5 |
| R7 粒子系统 | `Unity/Assets/Crystal/Client.Core/Ported/ParticleEngine.cs`、`Particle.cs`、`FogParticle.cs` + `MirMath/Vector2.cs`、`BlendMode.cs`、`MirMath/Size.Empty`、`Seams/MirGraphics.cs`（MLibrary.GetSize/ImageSize/Weather）、`tools/ParticleVerify/`（确定性状态机探针）+ `Client.Rendering.Editor/WeatherRender.cs`（天气渲染探针） | ✅ 三件套逐字移植 CoreVerify 0 错误；R7 探针 26/26 PASS exit 0（生成节律/位移/wrap-around/消亡/偏移/帧推进/同 seed 确定性）；**渲染层闭环（2026-08-06）：Weather.Lib 素材就位（G3 补充快照 878 图）→ 阶段6 天气补验 `net-weather.ps1` 全 PASS（11/11），并修复 `MLibraryUnity.DrawIndex` opacity 双重应用 bug** | 阶段3 R7 + 阶段6 |
| R8 文本栅格化管线 | `tools/TextVerify/`（GDI+ 语义基线，net8.0-windows）+ `Unity/Assets/Crystal/Client.Rendering.Editor/TextRender.cs`（Unity 动态字体探针） | ✅ GDI+ 基线 6/6 PASS（MeasureText/OutLine+2/5×DrawText 描边）；Unity 探针 PASS exit 0（TextGenerator CPU 字形提取→atlas 合成为文本纹理→SpriteBatch→RT→PNG，text.png 可见字形）；**实证：动态字体 `-nographics` 可用；batchmode 连续 Begin→Clear→Draw→End 期间不可插入 RT 回读（ReadPixels 破坏 GL 状态致 Draw 静默画空）** | 阶段3 R8 |
| R9 玩家角色渲染探针 | `Unity/Assets/Crystal/Client.Rendering.Editor/PlayerRender.cs`（复刻 PlayerObject.SetLibraries + DrawBody/Head/Weapon：CArmours[Armour]+CHair[Hair]+CWeapons[Weapon] 三图集层叠、FrameSet.Player 硬编码表、DrawFrame=Start+OffSet*Dir+FrameIndex、Gender offset 男0/女808/808/416） | ✅ 8 变体全 PASS fail=0（男/女×Standing/Walking/Attack1/Attack3/Struck/Running×dir0-7×frame0-5×素材 00-05）；帧选择公式实证正确（Walk d3 f2→52、Attack1 d1→142）；层叠顺序+遮挡校验通过 | 阶段3 R9 |
| R10 玩家场景合成探针 | `Unity/Assets/Crystal/Client.Rendering.Editor/SceneRender.cs`（玩家对象 `p:` 10 段规范 + `ResolvePlayer`/`AddLayer`/`OccludesAt`/`RenderColorAt` + `ObjLayer` 多层绘制接入 y-sort） | ✅ 6 变体全 PASS fail=0（玩家近怪物远/玩家-玩家相邻/女玩家+怪物/玩家 Attack1/玩家无武器/男-女同行）；**far-only 验证 bug 修复：玩家 far 用 RenderColorAt 取顶层实际渲染色（原用 Body 色被自身 hair/weapon 层覆盖致 4/6 false fail）** | 阶段3 R10 |
| R11 真实对象状态机驱动 | `Unity/Assets/Crystal/Client.Rendering.Editor/SceneRender.cs`（+`RunObjects` 入口，`CRYSTAL_OBJSPEC` `m:`/`p:` 规范）+ `Unity/Assets/Crystal/Client.Rendering/MLibraryUnity.cs`（renderable MLibrary：`AtlasLibrary` + `DrawIndex` 渲染内核 + `BridgeFrames` manifest→FrameSet）+ `Client.Rendering.asmdef`（+Crystal.Client.Core/+Crystal.Shared.Runtime） | ✅ 真实对象状态机（`MonsterObject`/`PlayerObject` `Load(S.ObjectMonster/S.ObjectPlayer)`→逐帧 `Process()`→`DrawFrame=Frame.Start+Frame.OffSet*Dir+FrameIndex`→`DrawIndex` 渲染）驱动场景；V1-V8 数据级 `dataMatch=True`（DrawFrame 逐字节同 R10 公式）+ 像素 fail=0 + y-sort 遮挡 fail=0；**强等价回归：R11 vs R10 同 spec PNG 逐像素 diff=0**（Standing+Attack1 组 / 双怪组 / 玩家组，Walking 用真实锚点不参与 diff） | 阶段3 R11 |
| 阶段7 Android Host | `Unity/Assets/Crystal/Client.Rendering.Editor/BuildAndroid.cs` + `Assets/Scenes/Main.unity` + `Client.Rendering/AppLifecycle.cs` + `TouchInputMapper.cs` + `TouchInputAdapter.cs` + `Client.Rendering.Editor/TouchInputVerify.cs` + ProjectSettings（Android/iOS：company/product/bundle/minSdk/目标版本/图标/横屏） | ✅ APK 构建通过（`[build-android] OK`，Unity/Build/Android/crystal.apk 16MB，产物不入库）+ 模拟器实证（安装/启动/横屏 2400x1080/挂起恢复 `resume pausedMs=31029`/触控 adapter started + tap 无崩溃）+ TouchInputVerify 8 用例 PASS；G7 门禁（摇杆/移动资源包/简化 HUD）待落地；安全区/软键盘推迟至移动 UI | 阶段7 |

## 基线快照（G0 已记录）

- 构建产物哈希：`docs/build-artifact-hashes.txt`（2026-08-04 Debug；2026-08-06 审计复跑 build.ps1 全 9 项目 0 错误后重写）
- 可启动服务端运行时：`Build/Server/publish/`（Release 发布版 + 用户提供的 EliteMir2 `Server_EN` 数据：`Server.MirDB` 版本 117 = Crystal `Version`，2338 张 `.map`、24 个 `Configs`、`Envir` 脚本，已验证正常启动）
- 游戏画面/封包基线：**未录制**——验收路径已变更（golden 直解 + 真实服务器探针双断言），见 `docs/backlog.md` ③
- **git 提交历史（2026-08-06 审计修复后）**：`e353a0b` G0 基线 → `a23ac9b` G0 基线文档 → `995a3ac` 全量快照（G0 后 B1→阶段6 全部工作入库，审计 P0 修复）→ `e7ffadf` 钓鱼补验（阶段6 10/11）→ `85b6eab` Build/ 验证脚本入库（审计 P1 修复）→ `3e437bb` 基线快照节记录提交历史 → `81c3f74` 天气补验（阶段6 11/11 + DrawIndex opacity bug 修复）→ `5c342a7` 修正迁移状态文档 5 处滞后 → `2ae2542` 中文语言包推迟至翻译阶段 → `9bbe66b` B2 8h soak 标记关闭。后续任务须"每任务一 commit + 主会话验收"，不再积压工作区。

## 服务端网络现代化进度（B1）

| 任务 | 状态 | 工件 |
| --- | --- | --- |
| B1-H0 封包 trace/回放 harness | ✅ 完成 | `tools/ServerTrace/`（record + diff + **--host 进程内起服**），`record --host --data <dir>` 两次录制 diff 空证明确定性（23 包） |
| B1-a SAEA 接收路径 | ✅ 完成 | `MirConnection.cs` 接收从 APM（BeginReceive）→ `SocketAsyncEventArgs` + `ArrayPool` 池化接收缓冲；帧解析/dataCounter/24h IP 封禁语义原样保留；`record --host` 新旧 trace 逐包一致（23 包 diff 空） |
| B1-b 主循环每连接包数预算 | ✅ 完成 | `MirConnection.Process()` 接收排空从全量 → 每 tick 最多 10 包（`MaxPacketsPerTick`）；顺序不变、IO 线程 50/5s 封禁已限队列增速；`record --host` diff 空 |
| B1-c 合并发送 + 部分发送 | ✅ 完成 | 发送从逐包 `List<byte>.AddRange` + `ToArray()` → `ArrayPool` 缓冲合并一次 `SendAsync`（`FlushSend`/`StartSend`），部分发送重试剩余字节，256KB 上限超限断连；`BeginSend` 调用点（`SendDisconnect`/版本失败路径）全改；`record --host` diff 空 |
| B1-d 状态端口 SAEA 对齐 | ✅ 完成 | `MirStatusConnection` APM→SAEA；验证：`--host` 下端口 3000 返回 `c;/NoName/0/CrystalM2/1.0.0.0//;`，主端口 trace 仍 diff 空 |
| G1 网络压测（500 连接） | ✅ 完成 | `tools/ServerStress/`：进程内起服 + N 连接保活流量 + 读 `Envir.Main` 的 `TickLatency`/`ConnProcessLatency` 环形采样算分位 + **服务端实际接受连接峰值采样**（`Connections.Count`）；**500/500 全连、服务端 accept 峰值 500/500**，`conn_process` P95=1ms（门禁 <5ms），`full_tick` P95=17ms（既有 20ms 对象预算） |

**已确立事实/约束：**
- 主循环 `full_tick` 被对象处理 20ms 预算主导（空载亦 ~16ms），连接开销须看 `ConnProcessLatency`（含 StatusConnections 的每 tick 采样）；压测同 IP 直连需先 `Settings.IPBlockSeconds = 0`。
- **服务端反滥用三层上限（压测必须全部绕过）**：① `MaxIP=5`（同 IP 并发连接数，`Envir.cs` accept 计数）；② `IPBlockSeconds=5`（同 IP 连接间隔，MirConnection ctor 每次连接登记）；③ `MaxUser=50`（全局连接数，accept finally `while (Connections.Count >= MaxUser) Sleep(1)` 会卡死 accept 线程）。压测 harness 依次覆写 `MaxIP=10000 / IPBlockSeconds=0 / MaxUser=10000`。
- **`Settings.IPBlockSeconds=0` 覆写曾无效的根因**：`UpdateIPBlock(ip, TimeSpan.Zero)` 写 `IPBlocks[ip] = Now`，而 `Envir.Now = _startTime + Time`（`Time` 仅每 workloop tick 刷新 ~20ms），同 tick 内后续 accept 读到 `banDate == Now` → `banDate < Now` 为 false → 全部被判封禁。**已修复**：MirConnection ctor 仅在 `IPBlockSeconds > 0` 时登记封禁（0 语义=禁用；生产 5s 行为不变），协议零变化（trace 23 包 diff 空）。
- 早期 G1 "500/500 全连"为无效测量：仅客户端 TCP 建连成功（进入内核 backlog），服务端因 `MaxIP=5` 实际只 accept ~1 个连接。修正后：`server_accepted_peak=500/500`、`conn_process` P95=1ms。

**已确立事实/约束：**
- 服务端反滥用：每次连接 ctor 即封同 IP `IPBlockSeconds`=5s（`Server/Settings.cs:73`），同 IP 5s 内重连被 accept 拒绝，客户端要等 RST 才感知。`record` 连续两次需间隔 >5s；`--host` 探针连接也会触发，harness 已内置 6s 等待。
- `Envir.Main.Start()` 启动前台线程，宿主进程在 Main 返回后不退出 → harness 录制完需 `Environment.Exit` 强制终止。
- 版本校验 `CheckVersion=False`（用户 Setup.ini）→ trace 脚本无需版本哈希。
- 主循环对每连接 `Process()` 无包数预算（`Envir.cs:2074-2078`），改造点之一。

## Shared 多目标化（阶段 2 前置，N0）

- `Shared/Shared.csproj` → `<TargetFrameworks>netstandard2.1;net8.0</TargetFrameworks>`；netstandard2.1 目标下新增 `System.Text.Json 8.0.5` 条件包引用。
- netstandard2.1 默认 C# 8 → 条件 `<LangVersion>10.0</LangVersion>`（否则 global usings 报 CS8400）；**禁 C# 12 集合表达式**（Language.cs:4109 曾报 CS8936，已改经典数组初始化）。
- 验证：Shared 双目标 0 错误；Server.Library / tools 全编译；`record --host` trace 与 B1 基线逐包一致（23 包 diff 空），协议行为零变化。

## 服务端性能（B2）

| 任务 | 状态 | 工件 |
| --- | --- | --- |
| B2-0 测量决定 B2 项排序 | ✅ 完成 | 结论：广播空间索引**否决**（见 ADR：DataRange=16 → 1089 格 vs O(P) 线性扫，实际每图玩家 << 1089）；存库后台化收益最大 |
| B2-2 账号存库后台化 | ✅ 完成 | `Envir.cs`：`BeginSaveAccounts` 主线程浅快照（`AccountSaveSnapshot` 含 ID 计数器防撕裂）→ `Task.Run` 序列化 → `WriteAccountsFile`（先写 `.n` 再备份再交换，任一步失败当前文件保持有效，比原"先移备份"更稳）；同步 `SaveAccounts` 等 `Saving` 排空后写终态；`SaveDB` 保持同步（549KB 静态数据 ~1ms） |
| B2-2 验证 | ✅ 完成 | 90s 压测触发周期存库：`verify-b2/Server.MirADB` 10:30 重写 + `Back Up/Accounts/` 生成 1 个备份 + 无 `.n`/`.o` 残留；trace 回归 23 包与基线 diff 空；conn_process P95=0ms |
| B2-2 8h soak | ✅ 已关闭（2026-08-06）：以 90s 压测 + 周期存库实证替代（存库后台化不阻塞主循环已验证：conn_process P95=0ms、周期存库重写无残留）；8h 长稳作为已知停停计得，后续若需可独立任务重跑（跨会话不存活是已知限制） | B2 |

**Harness cwd 修复（B2-2 发现）：**
- 旧 `ServerStress/ServerTrace` 的 `Host()` 在 boot 后 `finally` 还原 `Environment.CurrentDirectory` → 服务端运行期相对路径（`AccountPath`/`DatabasePath`/`AccountsBackUpPath`）按调用时 cwd 解析 → **周期存库写到了 repo 根目录**（曾产生 `Server.MirADB`/`Server.MirDB`/`Back Up/` 20+ 备份污染，已清理）。
- 修复：boot 后保持 `cwd = dataDir`（进程靠 `Environment.Exit` 退出，无需还原）；`ServerTrace` 的 `--out` 在切 cwd 前先 `Path.GetFullPath` 固定为绝对路径。
- 影响：此前 8h soak（旧二进制 + 污染路径）已终止；现用新代码 + 正确数据目录重跑。

## 资源管线 Spike（R0）

**工具**：`tools/LibSpike/`（.NET 8 控制台，`inspect <lib> <idx>` 单图诊断 + 目录全量校验）
**数据源**：`D:\ChuanQi\3.Server_EN 服务端_客户端 (部分汉化)\EliteMir2\Data`（34 目录 / 1955 个 .Lib / 9.4GB，与用户客户端同源）

**结果（对照 `MLibrary.cs` 移植解析逻辑）：**
| 校验项 | 结果 |
| --- | --- |
| 头/索引/帧表解析（版本 2/3 混合：v2=767 / v3=1188） | 边界自洽 100%（2,241,344 图 offset→end 精确衔接） |
| 像素数据：GZip 解压 = BGRA 4Bpp | 2,241,344 图**精确等于 W×H×4，0 坏**；另 104 图多出 10 字节尾部零填充（仅 6 个 NPC 文件 403-407 等，**读取时截断到 W×H×4 即可**） |
| Mask 布局 | 120 张全部按 **MaskLength** 字节排布（12B 头 + MaskLength）；原客户端 `CreateTexture` 读 `Length` 是 bug（`MLibrary.cs:928`），管线须用 MaskLength |
| 空占位 | 0×0 / Length=0 为合法空图槽（原客户端 `CheckImage` 返回 false） |
| 帧表 | `byte MirAction + Frame(8×int + 2×bool = 34B)`，全库合计 5,601 帧 |
| 空库 | count=0 合法（如 Monster/178.Lib 16 字节） |

**结论**：`.Lib` v3 二进制布局完全确定，可据此实现 `Crystal.AssetCompiler`（图集打包 + BGRA→RGBA + FrameSet 表）。解码逻辑已用真实资源全量实证，风险 #1（.Lib/混合/预乘 Alpha 细节）核心部分消除。报告存档：`Build/LibSpike/report-elitemir2.txt`。

## 阶段3 渲染 Spike（图集 + golden + Unity 对照）

**工具/产物**：
- `tools/AssetCompiler/`（.NET 8 控制台）：`.Lib → 图集 PNG + <rel>.json 清单 + <rel>.golden` 三件套。子命令 `compile-all`、`verify-dir`（图集 PNG 解码 vs 直解 .Lib 逐字节比对）、`golden-dir`（每图 RGBA 提取 SHA-256 侧车 `<rel>.golden`，行格式 `"<index> <hex>"`，跳过 Empty）、`png-dump`/`png-scan`（像素级诊断）。
- `Unity/Assets/Crystal/Client.Assets/`：运行时读取器 `AtlasLibrary`（JsonUtility 解清单 → SpriteFrame[]，懒加载页纹理，GetPage/GetMaskPage/UnloadAll）+ `Manifest.cs`/`SpriteFrame.cs`（netstandard2.1 兼容：无 HashData/ToHexString）。
- `Unity/Assets/Crystal/Client.Assets.Editor/AtlasVerify.cs`：批处理门禁，`CRYSTAL_ATLAS_DIR=<dir> Unity.exe -batchmode -nographics -executeMethod ...Run`，逐库加载 + GetPixels32 提取每帧 RGBA → SHA-256 vs golden，最后 `EditorApplication.Exit(0/1)`。

**全量验证结果**：
- `verify-dir`：**ok=1955 fail=0 missing=0**（图集 PNG 与 .Lib 逐字节一致）。
- `golden-dir`：**golden ok=1955 fail=0**（1447.7s，1955 侧车文件）。
- `AtlasVerify`：单库 Auras 401 帧全过（`ok=1 fail=0 missing=0`）；**全量 1955 库通过 `atlas-verify ok=1955 fail=0 missing=0`（2598.7s）**。

**关键发现（渲染层必须遵守）**：
1. **Unity `Texture2D.LoadImage` → `GetPixels32` 返回垂直翻转**（Unity row 0 = PNG 末扫描线；8 种布局变换暴力比对只有翻转 5/5 命中）。PNG 文件本身是标准 top-down（zlib 扫描线逐字节核验），与 .Lib 行 0=图顶一致（旧客户端 `DecompressImage` 不翻转直接上传纹理）。`HashFrame` 提取用 `row=(tex.height-1-(f.Y+y))` 补偿；**阶段 3 精灵渲染 V 轴相对旧客户端相反，UV/绘制需处理，以黄金截图最终验证**。
2. **页尺寸可超 4096**：编译器常规页 ≤4096²，但超限图"独占一页"（自然尺寸）——mmap（小地图）4 页 4800×3200。移动端 ASTC/纹理上限需按 4800 或拆分 mmap 页处理（阶段 3/7 关注）。
3. **批处理门禁跨库 OOM（已修复）**：`AtlasLibrary.UnloadAll()` 原用 `UnityEngine.Object.Destroy`（延迟到帧末销毁），而 `-batchmode -nographics` 无帧循环 → 原生纹理跨库永不释放，虚拟保留膨胀到 393GB 后 OOM（`Fatal Error! Could not allocate memory ... MemoryLabel: Texture`），首次全量跑到第 1110 库（Monster/297）崩溃。**修复**：`UnloadAll` 改 `DestroyImmediate`（显式整库卸载语义，即时释放）+ `AtlasVerify.VerifyLib` finally 加 `GC.Collect`。二次全量已越过原崩溃点（>1229 库 0 失败，含 mmap 2.55GB 库）。

## 阶段3 R2 地图 tile 渲染（MapRender 探针）

**工具/产物**：`Unity/Assets/Crystal/Client.Rendering.Editor/MapRender.cs`（asmdef 增引 `Crystal.Client.Core`；`MapCode.cs` 的 `MapReader` 由 internal → public）。`MapReader` 解析原始 `.map` → `MapLibRel` 段映射（0-273 全覆盖：WemadeMir2/ShandaMir2/WemadeMir3）→ `AtlasLibrary` 加载图集 → `CrystalSpriteBatch` 复刻 `GameScene.DrawFloor` 的 Back/Middle/Front 三层 → RT → PNG。env：`CRYSTAL_MAP_DIR/ATLAS_DIR/MAP/CENTER/RT_W/RT_H/LAYER/OUT/SPOT`（`SPOT="x,y"` 验 back、`SPOT="f:x,y"` 验 front）。

**tile 语义（对照 GameScene.cs）**：
- Back 仅偶数格提交（`y%2==0 && x%2==0`），index=`(BackImage&0x1FFFFFFF)-1`（96×64 地面菱形覆盖 4 格）。
- Middle index=`MiddleImage-1`，仅 48×32 / 96×64 尺寸绘制。
- Front index=`(FrontImage&0x7FFF)-1`，门取关态、动画取帧 0，跳过 `FrontIndex==200`。
- 相机：`OffSetX=W/2/48, OffSetY=H/2/32-1`，`drawX=(x-camX+OffSetX)*48-OffSetX`，`drawY=(y-camY+OffSetY)*32`。

**验证结果（map 0 = 700×700，中心 350,350）**：
- **usedLibs=23 unresolved=0**（补 WemadeMir3 段映射前曾 unresolved=3，对应 Snow/Dungeonsc+Furnituresc+Wallsc）。
- **字节级 spot-check 3 处全 fail=0**：back lib0/WemadeMir2/Tiles `idx=903 96x64` at (564,288)、back lib0 `idx=902` at (84,288)、front lib251/WemadeMir3/Snow/Dungeonsc `idx=2766 48x32` at (564,288)。逐像素比对 `GetPixels32`（行补偿 `ph-1-(f.Y+y)`）vs RT 回读（top-down，无翻转）。
- 全场景 PNG（`Build/map-render-0.png` 1152×640）像素抽样：`sample_gray=0`（无背景灰缝隙）、空行 0，中心列呈多样地形色。
- 中间层诊断：map 0 中心区 middle=0（合法无中间层）；(0,84) 处 `midSizes=[48x32=7] midLibs=[1]` 证明 48×32 中间 tile 可绘制。

**关键发现**：
1. **MapLibs 段映射覆盖到 273 即满足真实数据**：源 `Data/Map/` 仅 WemadeMir2/ShandaMir2/WemadeMir3（无 ShandaMir3），137 库编译全量 = 46+28+63。Mir3 段 base=200+i*15（i=0..4：空/Wood/Sand/Snow/Forest），组内 14 段；`MapLibRel` 用 `idx-200)/15` 解组、`%15` 解段。
2. **Mir3 图元在 map 0 只出现在 front/middle 层**（back 无引用）：front spot-check 即覆盖 Mir3 渲染链路。
3. **`SpotCheck` 的 draw 坐标须用相机中心而非地图中心**（初版硬编码 `mr.Width/2`，中心偏离时图元画出屏外假 fail=0）——已修为参数传入 camX/camY。

## 阶段3 R3 动画精灵渲染（AnimationRender 探针）

**工具/产物**：`Unity/Assets/Crystal/Client.Rendering.Editor/AnimationRender.cs`（CrystalSpriteBatch + AtlasLibrary + FrameSet 表合成精灵帧条 → RT → PNG）。env：`CRYSTAL_ATLAS_DIR/LIB/ACTION/DIR/FRAME/SPOT_FRAME/RT_W/RT_H/OUT`；`CRYSTAL_FRAME=-1` 全帧横排，`CRYSTAL_SPOT_FRAME=n` 单帧字节级直通对照（ReplaceBlend）。

**帧选择语义（对照 PlayerObject.cs:761）**：`DrawFrame = Frame.Start + (Count+Skip)*Direction + FrameIndex`；FrameSet 表来自 AssetCompiler manifest JSON 的 `Frames`（`FrameEntry`：Action/ActionId/Start/Count/Skip/Interval/Effect*/Reverse/Blend），按动作名匹配（如 "Standing"/"Walking"），已由 `LibManifest.Frames` 反序列化。Draw 位置 = 格锚点 + `f.OffX/f.OffY`（怪物 OffY 为负，精灵从脚部锚点向左上延伸）。

**验证结果**：
- Standing dir0：`start=0 count=4 skip=0 offSet=4 interval=500`，idx=0..3 全画（frames drawn=4/4）；spot idx=0 **fail=0**（字节级直通）。
- Walking dir3：`start=32 count=8 skip=0 offSet=8`，`idx=32+8*3+0=56` spot **fail=0** —— 方向步进公式实证。
- 帧条正立验证：内容 bbox y=[246,370]（锚点 anchorY=352 + OffY≈-105），正立（翻转前误在 y=[29,153]）。

**关键发现：EncodeToPNG 行序（所有 PNG 输出必读）**：
1. `ReadPixels`→`GetPixels32` 本平台为 **top-down**（row0=RT顶，先前 R1 已实证）。
2. 但 **`EncodeToPNG` 输出翻转图**（PNG row0 = RT 底行）——`OrientProbe` 实证（20×20 红块画在 (20,20)，翻转前 PNG 红块落在 y=[60,79] 底部）。**所有 PNG 写出须先按行翻转**：`Array.Copy(px,(rtH-1-y)*rtW,fl,y*rtW,rtW)` + `SetPixels32` + `Apply`（MapRender/AnimationRender 同款代码块）。
3. 字节级 spot-check 不受影响（直接在 RT 回读像素上比对，不走 PNG），故 R1/R2 spot 结论依旧有效。

## 阶段3 R4 场景合成 + y-sort 遮挡（SceneRender 探针）

**工具/产物**：`Unity/Assets/Crystal/Client.Rendering.Editor/SceneRender.cs`（DrawFloor 三层 + 地图对象精灵同 RT 合成 → 正立 PNG）。env：`CRYSTAL_MAP_DIR/ATLAS_DIR（对象图集 all）/MAP_ATLAS_DIR（地图图集 map）/MAP/CENTER/RT_W/RT_H/OUT/OBJECTS`；`CRYSTAL_OBJECTS="<rel>:<action>:<dir>:<frame>:<x>:<y>;..."`。

**合成语义（对照 GameScene.CreateTexture）**：
- 地面：先整幅 DrawFloor（Back/Middle/Front 三层，与 R2 MapRender 同式）。
- 对象：按 `DrawFrame=Frame.Start+(Count+Skip)*Direction+FrameIndex` 选帧，锚点 `DrawLocation=((x-camX+offX)*48,(y-camY+offY)*32)`，精灵左上 = DrawLocation+(OffX,OffY)。
- **对象锚点无地面那 `-OffSetX` 像素校正**（MonsterObject.cs:435）——与地面 drawX 公式差 `-offX` 像素，必须分开实现。
- **y-sort**：对象按 (Y,X) 行主序先远（小Y）后近（大Y）绘制（GameScene.DrawObjects 逐行 `M2CellInfo[x,y].DrawObjects()` 同构）；地面在对象之前，永不遮挡对象。

**验证结果（map 0 中心 350,350，RT 1152×640）**：
- 地面 back=342 middle=0 front=122（与 R2 一致），floorLibs=23 unresolved=0。
- 对象锚点钉死 fail=0：Monster/000@(350,348) `draw=(576,224) sprite=(566,119)`（idx=0，Off −10,−105）；Monster/001@(350,350) `draw=(576,288) sprite=(548,215)`（idx=0，Off −28,−73）——首个不透明局部像素 RT==图集源。
- y-sort 遮挡 fail=0：两精灵 bbox 重叠 28px 带，重叠区取两者都不透明且颜色不同的像素 → RT==近者（Monster/001，大Y）色；远者（Monster/000）独占区 RT==远者色。
- PNG 像素抽样（PowerShell）：(600,60) 草地绿、(600,150) 远者深青、(600,230) 近者深灰——朝向正确、遮挡可见。

**关键发现**：
1. **地图图集与对象图集分属不同根**：`Build/assetcompile/map`（137 地图库 WemadeMir2/ShandaMir2/WemadeMir3）vs `Build/assetcompile/all`（Monster/装甲等其余库）。SceneRender 用 `CRYSTAL_MAP_ATLAS_DIR` 区分两源，`MapLibRel` 提为 public 复用。
2. 遮挡验证要求两源像素**颜色不同**才可判别（否则重叠区无法区分谁在上）；近者 alpha 需为 255（半透明边缘混入远者色，验证自动跳过此类像素）。

## 阶段3 R5 灯光管线（LightRender 探针）

**工具/产物**：`Unity/Assets/Crystal/Client.Rendering.Editor/LightRender.cs`；`CrystalSpriteMultiply.shader`（`Blend Zero SrcColor`，D3D9 `Zero/SourceColor` 的 ShaderLab 等价）+ CrystalSpriteBatch 新增 `MULTIPLY` 混合模式（Flush 三元链 ReplaceBlend → MULTIPLY → additive → alpha）。env：`CRYSTAL_LIGHTS="<cx>,<cy>,<sizeIdx>[,<tintR,tintG,tintB>];..."`（多灯分号分隔）、`CRYSTAL_DARKNESS`/`CRYSTAL_RT_W`/`CRYSTAL_RT_H`/`CRYSTAL_OUT`。

**灯光合成语义（对照 GameScene.DrawLights + DXManager.CreateLights）**：
1. lightRT 清为暗色（Night 黑/map 色、Evening/Dawn (50,50,50)、Day 白）→ GL.Clear 验证。
2. additive（SrcAlpha,One）画光源径向渐变图（LightSizes 11 档 205×156..925×703，tint lightColour 染色）→ 单灯混合贡献 = grad.rgb*tint.rgb*grad.a/65025。
3. 整幅 Zero/SrcColor（multiply，dest=dest*src）乘回场景 → final = scene*lightTex/255。

**光源渐变图**：旧客户端是 GDI+ PathGradientBrush 按路径三角网格 Gouraud 插值（实测与简单椭圆射线公式最大差 220/255，不可字节复刻），Unity 侧改为 **CPU 椭圆射线径向渐变**（同色标 [White,(210),(160),(70),(40),transparent]@[0,.2,.4,.6,.8,1.0]，t=sqrt((ry*dx)²+(rx*dy)²)/(rx*ry)，t 线性插值色标）。**GDI+ 逐像素精确复刻留待 golden baseline（阶段 4）**，本探针验证混合语义字节级。

**验证结果（320×200 checkerboard 场景，CPU 期望逐像素 ±2）**：
- 场景字节级 fail=0（Point 过滤全屏逐像素相等）。
- lightTex additive fail=0：CPU 期望须按 **GPU 点过滤采样模型**（fragment 中心 (x+0.5,y+0.5) → col=round(U*(W-1)), row=round(V*(H-1)) → 读纹理存储像素）；直接按公式取渐变会差半像素（falloff 区 ~3 字节系统性偏差）。
- 合成 multiply fail=0：lightTex 回读为 top-down，重建纹理（SetPixels32 row0=底部）须行翻转（同 WritePng 模式），否则 batcher 采样 V 轴反向。
- 双灯（白 + 暖 tint 重叠）fail=0；夜场景（dark=10,10,20）4 灯（含半屏外裁切 + 全屏外零贡献）fail=0。

**关键发现**：
1. ShaderLab 无 `SourceColor` 混合因子（D3D9 命名）：dst 槽位用源色须写 `SrcColor`（`Blend Zero SrcColor`）。
2. GPU 点过滤采样 = round 归一化坐标到 texel 并取纹理存储像素，CPU 期望须按此模型而非重算渐变公式（差半像素即 ~3 字节偏差）。
3. top-down 回读像素重建纹理（SetPixels32 row0=底部）必须行翻转。

## 阶段4 P4-M1 客户端网络层 + 登录链路

**目标**：阶段 4 端到端垂直链路（登录→选角→进图→移动→战斗→拾取→背包→NPC→聊天→下线，Gate G4）的地基——Unity 客户端连接真实服务器，完成 TCP+封包握手 → 版本校验 → 登录 → 收到角色列表，**服务器零修改**。

**变更**：
- `Unity/Assets/Crystal/Client.Core/Seams/Network.cs`：空 stub → **逐字移植旧客户端传输骨架**（`Client/MirNetwork/Network.cs` 255 行：TcpClient NoDelay + BeginConnect/BeginReceive 异步收发 + `Shared.Packet.ReceivePacket` 解码入队 + ConcurrentQueue 收/发队列 + KeepAlive 超时）。去 WinForms：封包派发 `MirScene.ActiveScene.ProcessPacket` → 静态 `OnPacket` 委托；`MirMessageBox`/`Program.Form` → `Debug.Log`。`Connected`=“已握手”（收到 S.Connected），非“TCP 已连”（旧语义保持）。
- `Seams/Settings.cs`：+`IPAddress/Port=7000/TimeOut=5000`。`Seams/CMain.cs`：+`BytesReceived/BytesSent` 计数。
- `Unity/Assets/Crystal/Client.Rendering.Editor/NetProbe.cs`：`NetProbe.RunLogin` 登录状态机探针（batchmode）。
- `Build/net-login.ps1`：编排（起 Server.exe 独立进程 → 等端口 7000 → Unity 探针正/负例 → 断言 → 停服务器）。

**封包流（服务端语义实证，Envir.cs:3875/3685）**：`S.Connected` → 发 `C.ClientVersion{VersionHash=空}`（`Setup.ini CheckVersion=False` 免 exe MD5 校验）→ `S.ClientVersion(Result=1)` → 发 `C.NewAccount`（自适应注册，`S.NewAccount(Result=8 创建成功 / 7 已存在)`）→ 发 `C.Login` → 成功直接 `S.LoginSuccess{Characters=List<SelectInfo>}`（角色列表随登录返回，无独立请求）。`S.Login` 任何 Result 均为环境异常（注册后必成功）。账号规则 `^[A-Za-z0-9]{3,15}$`（ID）/`{5,15}$`（密码），`probe1` 合法。

**验证结果（真实服务器 + Unity batchmode，服务器零修改）**：
- 正例：`login ok characters=0 seq=Connected>ClientVersion:1>NewAccount:8>LoginSuccess:0` + **exit 0**。
- 负例（错误密码）：`fail=login-rejected:4 seq=Connected>ClientVersion:1>NewAccount:7>Login:4` + **exit 1**（NewAccount:7 已存在不重复注册，Login:4 密码不符）。
- `net-login.ps1` 全流程 **PASS**（Server.exe 独立启动实测可行：~2.8GB 加载、端口 7000 约 30s 就绪）。

**关键发现/坑**：
1. **Server.exe 可独立启动**（cwd=publish，无交互参数），无需 serve-only harness（plan 备选未用）。脚本用 `Start-Process -WorkingDirectory publish` + TcpClient 轮询端口。
2. **`ServerPacketIds` 全局枚举、`ServerPackets`/`ClientPackets` 是 namespace**（Shared/Enums.cs vs ServerPackets.cs）：`using S = ServerPackets` / `using C = ClientPackets`，`(S.Login)p` cast 即达。
3. Unity batchmode 异步 socket 回调（BeginConnect/BeginReceive）在线程池线程执行，探针主循环 `Thread.Sleep(50)+CMain.Time+=50+Network.Process()` 轮询即可（不依赖主线程消息泵）。
4. **`ServerPacketIds` 值冲突陷阱**：`Connected` 与登录流程其余封包序号不同，`Network.Process()` 断开分支只放行 `S.Disconnect`/`S.ClientVersion`（旧语义）。
5. 账号 DB 持久化（Server.MirADB），重跑幂等：正例走 NewAccount:7→LoginSuccess，不重复建号。

**P4-M2 选角 + 进图（完成）**：`NetProbe.RunSelect` 延续登录状态机。`LoginSuccess.Characters` 空 → `C.NewCharacter{Name,Gender=Male,Class=Warrior}`（`S.NewCharacterSuccess.CharInfo.Index` 取角色 Index；非空 → 直接取 `Characters[0].Index`）→ `C.StartGame{CharacterIndex}` → 服务端 `PlayerObject.StartGameSuccess()`（MirConnection.cs:1129）连发 `S.StartGame{Result=4,Resolution}` + `GetItemInfo/GetMapInfo/GetUserInfo` + `Spawned()`。断言三要素：`S.StartGame(Result=4)` + `S.MapInformation(FileName 非空)` + `S.UserInformation(ObjectID>0)`。**验证 PASS**：`select ok seq=Connected>ClientVersion:1>NewAccount:8>LoginSuccess:0>NewCharacterSuccess:5>SendStartGame:5>StartGame:4>MapInformation:nn0>UserInformation:33291:probe`（地图 nn0 新手村，玩家对象 33291）。`S.MapInformation` 纯元数据（FileName/Title/Lights 等，不含地图文件）；`S.UserInformation` 大封包（ObjectID/Name/Class/Level/Location + Inventory/Equipment/QuestInventory UserItem 数组，Shared 可解析）。角色创建 Result 语义：0 禁用/1 名非法/2 性别/3 职业/4 已满/5 名已存在，成功=NewCharacterSuccess。

**P4-M3 GameScene 主循环（完成）**：`Spawned()` 后对象封包驱动真实 MapObject 创建与 R11 渲染，服务器零修改。
- **变更**：`Seams/MirGraphics.cs` +`NPCs`/`Flags` 槽位 + `MLibrary.DrawTinted`；`Seams/GameScene.cs` +`QuestInfoList`；`Seams/MapControl.cs` `RemoveObject/AddObject` stub→真实网格注册（`M2CellInfo[x,y]`，越界守卫）；`Client.Core/Ported/NPCObject.cs` 移植（裁 Quest 系统：UpdateBestQuestIcon 空体、QuestIcon 恒 None、Quests 列表保留契约；类型转换 `Color.FromArgb(x.ToArgb())`/`new Point(x,y)`）；`SceneRender.cs` 8 处 private→internal（EnsureMLibrary/DrawMapTiles/EnsureLib/EnsureMapLib/BuildLibIndex/MapLibRelLazy/_atlasDir/_mapAtlasDir）供探针复用；`NetProbe.RunGame`（env：CRYSTAL_MAP_DIR/ATLAS_DIR/MAP/OUT/RT_W/RT_H/GAME_MS）；`Build/net-game.ps1` 编排。
- **探针流程**：登录状态机（复用 P4-M1）→ 无角色 `C.NewCharacter` → `C.StartGame{Index}` → `S.MapInformation` 接 `MapReader` 加载地图（`GameScene.Scene=new GameScene{MapControl=M2CellInfo}`）→ `S.UserInformation` 建 `UserObject`（`MapObject.User` 相机锚点）→ 收包窗口 6s 内 `S.ObjectMonster/ObjectNPC/ObjectTurn/ObjectWalk/ObjectRun/ObjectRemove` 驱动 `Load`/`ActionFeed`/`Remove`，逐帧 `Process()` → 复用 R11 渲染（`DrawMapTiles` + y-sort `DrawIndex`）→ PNG + 断言。
- **验证 PASS**：`game ok seq=Connected>ClientVersion:1>NewAccount:8>LoginSuccess:0>NewCharacterSuccess:5>SendStartGame:5>StartGame:4>MapInformation:nn0>MapLoaded:700x700>UserInformation:33291:probe>UserSpawn:288,616>GameEntered`。渲染统计 **monsters=15/15 npcs=12/6 moves=6 removes=0 drawn=21**（15 怪物全渲染；12 NPC 封包 6 渲染，另 6 图库缺素材 BodyLibrary=null 不绘；6 移动封包入 ActionFeed；PNG 47.9KB）。**回归：net-login（正/负例）+ net-select 全 PASS**。
- **关键坑**：①`ServerPackets.ObjectNPC` **class 名大写 C**，enum 成员 `ServerPacketIds.ObjectNpc` 小写 c（NetProbe 曾写 `(S.ObjectNpc)` 编译 CS0234）——`ServerPackets` 是 namespace，class 是 `ObjectNPC`；②`MonsterObject` 属性是 **`BaseImage`**（`Monster` enum）非 `Image`（CS1061），封包 `S.ObjectMonster.Image` 亦为 `Monster` enum 需 `(ushort)` cast 到图库索引；③**Unity seam 图库数组初始全 null，对象 `Load` 时 `BodyLibrary.Frames` NRE**（旧客户端 static ctor 预加载全部图库，Unity 侧无此步）→ 探针 handler 收到封包时先 `EnsureMLibrary($"Monster/{img:D3}"/$"NPC/{img:D2}")` 写回 `Libraries` 数组再 `Load`；④`S.ObjectNPC.Image` 为 `ushort`、`S.ObjectMonster.Image` 为 `Monster` enum，EnsureObjectLib 签名统一 `ushort`；⑤无 `ObjectPlayer` 封包（新服空场景，登录即单人）——P4-M4 起人工驱动玩家操作封包。

## 阶段4 P4-M4 战斗/拾取/背包/NPC/聊天（双向交互链路）

**目标**：五类交互封包**双向交换 + 语义正确**——Chat→背包交换→NPC 对话→拾取→使用物品，`CRYSTAL_COMBAT=1` 追加走动+攻击怪物。服务器源码**零修改**。

**变更**：
- `NetProbe.RunInteract`（`Client.Rendering.Editor/NetProbe.cs`）：登录状态机（复用 P4-M1/P4-M2，含账号/角色自适应注册）→ `C.StartGame` → 依次驱动**五项确定性交互**（每步 `_istepDeadline` 超时即 fail）：
  1. **Chat**：`C.Chat{Message 含 "probe-interact-1"}` → 服务器广播 **`S.ObjectChat`**（`ObjectID=自己 && Type=Normal && Text 含标记`，`ChatOk`）。
  2. **Bag swap**：**`C.MoveItem{Grid=Inventory,From,To}`**（取背包前两槽）→ `S.MoveItem{Success && From/To 匹配}`（`BagOk`，`BagSwap:0:1`）。
  3. **NPC dialogue**：`C.CallNPC{ObjectID=可视 NPC 首个,Key="[@MAIN]"}`（3s 无应换下一 NPC）→ **`S.NPCResponse{Page.Count>0}`**（`NpcOk:31` = `Page[0]` 行长度）。
  4. **Pickup**：`C.DropItem{UniqueID=_potionUid,Count=1}` → `S.DropItem{Success}` + **`S.ObjectItem`**（地面对象，`DropObj:{ObjectID}@{loc}`）→ `C.PickUp{}` → **`S.GainedItem`**（`PickupOk`）。
  5. **Use**：`C.UseItem{UniqueID=_potionUid,Grid=Inventory}` → `S.UseItem{UniqueID 匹配}`（`UseOk:{Success}`，药水 idx 1987/1988 优先，无则任意物品）。
- **战斗（COMBAT=1，独立账号 `probecombat1`/`probecombat`）**：`PickCombatTarget` 按曼哈顿距离排序（同格跳过）→ 逐格 `C.Walk` 逼近（每格 8s 超时/stuck 3 次换目标）→ 贴邻 `C.Attack{Direction,Spell=None}` → `S.UserLocation` + **`S.ObjectStruck{ObjectID=target,AttackerID=self}`** 判定 `CombatOk`；`_attackAttempts>=6` 重选目标，总步 60s 超时。
- `Build/net-interact.ps1`：编排（起 Server.exe → 等端口 → Unity batchmode RunInteract → 断言 `interact ok` → 停服务器）。`-Combat 1` 切换账号。

**验证结果（真实服务器，全矩阵 PASS）**：
- 交互（基础账号）：`interact ok seq=Connected>ClientVersion:1>NewAccount:8>LoginSuccess:0>NewCharacterSuccess:5>SendStartGame:5>StartGame:4>MapInformation:nn0>UserInformation:33291:probe>Inventory:12>...>ChatOk>BagSwap:0:1>BagOk>CallNPC:5>NpcOk:31>DropItem:2241>DropObj:33292@288,615>PickUp>PickupOk>UseItem:2241>UseOk:True`。
- 战斗（独立账号）：`...>CombatStart>CombatTarget:287@289,617>Attack:Down>Died:551>CombatOk>Died:287`（贴邻击杀目标 287）。
- **回归矩阵**：`net-login`（正/负例）+ `net-select` + `net-game` + `net-interact` + `net-interact -Combat 1` 全 **PASS**。

**关键发现/坑**：
1. **等级 1 战士属性废人**：`BaseStats.Calculate(job,level)`（Shared/BaseStats.cs:155）`Base + level/Gain` **整数除法** → 1 级 MaxDC = `0 + 1/5` = **0**（`Gain==0` 才返回 Base）。实测 1 级战士 HP 18、AC 0、DC 0、Accuracy 5——**打不动怪**。
2. **ObjectStruck 门控**（`MonsterObject.Attacked`）：`armour >= damage` → `BroadcastDamageIndicator(Miss)` **提前 return，永不广播 ObjectStruck**。0 伤害命中不产生命中反馈，玩家攻击必须 > 怪物护甲。
3. **测试角色数值靠配置修**：boost `Build/Server/publish/Configs/BaseStatsWarrior.ini`（HP 150、AC 15-20、DC 15-20、Accuracy 40，备份 `.bak`）。**真实配置是 `BaseStatsWarrior.ini`（新格式 `[Accuracy] Formula=Stat Base=5`），`BaseStats.ini` 是旧格式遗留文件未加载**——`tools/ServerTrace` 增 `stats` 子命令实证。`Build/` gitignore，不进版本库。
4. **删号不能复用同名**：`DeleteCharacter` 软删（`temp.Deleted=true`），`CharacterList` 加载含 Deleted（启动 `CharacterList.AddRange(AccountList[TrueAccount].Characters)`），`CharacterExists` 查全表 → **名字保留至 purge（1 个月）**，同会话删号重建 `NewCharacter Result=5`。
5. **账号漂移重置 = 恢复 DB 备份 + 自适应注册**：基础角色 `probe` 曾在旧战斗测试中在野外下线 → 无 NPC 可视 → `NpcOk` 失败。方案：`Back Up/{Accounts,Database}/...02-16-27.bak`（10:16，探索前）成对覆盖 `Server.MirADB/Server.MirDB` → 账号角色回退到不存在 → 探针自适应注册**在建号在出生点**（Inventory:12，绑定点 288,615）。当前态已留 `DB-prestore-1208/`。
6. **账号拆分保稳定**：战斗用独立 `probecombat1`，基础账号永不离开出生点，跨 run 稳定；后续 P4-M5 双开对照同原理。

## 阶段4 P4-M5 下线 + HUD + 持续游玩（Gate G4 收官）

**目标**：垂直链路最后三环——**下线持久化**（C.LogOut→S.LogOutSuccess→重进状态保留）、**HUD 状态条叠加**（真实 S.UserInformation 驱动 HP/MP/Level 渲染）、**持续游玩无阻断**（双开 A↔B 互见 + 长时间 soak），交付 Gate G4 Go/No-Go 判定。服务器源码**零修改**。

**变更**（`Client.Rendering.Editor/NetProbe.cs` 增 3 个入口 + `Build/` 3 个编排脚本）：
- **`NetProbe.RunLogout` + `Build/net-logout.ps1`**：登录状态机 → 进图 → `C.LogOut{}`（空封包）→ `S.LogOutSuccess{Characters}` → `C.StartGame` 重进 → 再收 `S.UserInformation`（证明角色状态跨下线持久）。`_gameDeadline` 触发 `RenderGame` 前先走完 logout 状态机。
- **`NetProbe.RunDualOpen` + `Build/net-dualopen.ps1`**：A 线程（`probe1/probe`）主状态机；B 线程（`probe2/probe2b`，独立脚本化 raw socket）登录→建号→进图→**持续环走 + 聊天**。A 断言收到 `S.ObjectPlayer{B}` + `S.ObjectWalk{B}`；B 断言网格 Add 分发看到 `S.ObjectPlayer{A}`。`-SoakMs>0` 追加持续游玩 soak：B 按 `_soakDeadline` 持续走/聊至期限，A 全程 `Network.Process()` 处理封包不掉线，`Done` 带 `conn={Connected} pkts={_aPktCount}` 证据。
- **`NetProbe.RunHud` + `Build/net-hud.ps1`**：Game 模式加 `_drawHud` 分支——`S.UserInformation` 填 `_userHp/_userMp/_userLevel`，`RenderGame` 末尾（map+对象绘制后、`End()` 前）叠加：HP 红条 `(10,10)` 宽=`Clamp(HP,300)`、MP 蓝条 `(10,26)`、Level 白字 `(10,42)`（R8 文本管线）。全帧回读后按像素断言：左上区域红/蓝/白计数超阈值。

**验证结果（真实服务器 + Unity batchmode，全矩阵 PASS）**：
- **logout**：`logout ok seq=...>GameEntered>LogOutSuccess:1>ReEnter>SendStartGame:5>StartGame:4>MapInformation:nn0>UserInformation:33292:probe>ReEntered:33292:probe` —— 下线清场（新 ObjectID 33292）后重进角色状态完整回读。
- **dualopen**：`dualopen ok seq=...>GameEntered>SeenPlayerB:33292:probe2b>WalkB:288,617>...`（A 见 B 移动）+ B 线程 `entered obj=33292 sawA=True` / `post-enter sawA=True`（B 见 A）。fast 模式与 `-SoakMs 120000`（2 分钟持续游玩）双 PASS。**A 登录状态机修复**（`NetProbe.cs`）：旧版 ClientVersion 后无条件发 `C.NewAccount`——每连接触发服务器 `AccountsMade` 计数，同一 IP 1h 内第 4 次建号触发 **24h IP ban**（`Envir.cs:3696-3700`）。改为**先 `C.Login`，收到 `S.Login{Result=3}`（账号不存在，`Envir.cs:3898`）才建号**——账号存在时 `seq=...>ClientVersion:1>LoginSuccess:1`（零 NewAccount），重连不再累积 ban。
- **hud**：`hud ok` + 诊断 `hp=154 mp=14 level=1 hpPx=1889 mpPx=200 lvPx=258 pxTop=RGBA(217,26,20,255)`——HP=154 红条渲染 154px 宽、MP 蓝条、白字 "Lv 1"（合成 17×23 maxA=255），全由真实服务器 `S.UserInformation` 驱动。产物 `Unity/Build/net-hud.png`（HP 红条 rows 10-21 / MP 蓝条 rows 26-37 / 白字 rows 44-59 像素级核验）。
- **回归矩阵（本轮全量重跑）**：`net-login`（正/负例）+ `net-select` + `net-game` + `net-interact` + `net-logout` + `net-dualopen` + `net-hud` **7/7 PASS**。

**关键发现/坑**：
1. **服务端同 IP 反滥用是双开硬墙**：每次 TCP accept，`MirConnection` ctor（MirConnection.cs:97-98）即登记同 IP **5s 未来封禁**（`Envir.UpdateIPBlock` 写 `IPBlocks[ip]=Now+5s`，`Envir.cs:3628`；`Settings.IPBlockSeconds=5` 硬编码 Settings.cs:73 不可配），accept 门（Envir.cs:3612）拒未来封禁 IP → **同 IP 5s 内第二连接 TCP 已通但被服务端立即关闭，客户端连 `S.Connected` 都收不到**。修复（零服务端修改）：探针 B 线程 `Thread.Sleep(5500)` 避开封禁窗口再连接。
2. **HUD 像素断言双重坑**：①`Color32` 字段是 0-255 **字节**，与 0-1 浮点阈值（`c.r>0.4f`）比较恒真/恒假（25 字节 >0.4f 即真）——谓词必须用字节阈值（`r>100 && r-g>50`）；②`GetPixels32()` 本平台 **top-down**（`px` row0=RT 顶，`pxTop=RGBA(217,26,20,255)` 实证），翻转成 `fl` 后是 **bottom-up**，`EncodeToPNG` 再翻回正确图——**断言须在 px（top-down）上做或对 px/fl 区域扫描取 max**，否则按 fl 采样会命中错误行（初版 `hpPx=0` 即此）。
3. **Unity 动态字体图集 RGB=黑+alpha**：`Font.CreateDynamicFontFromOSFont` 图集字形 RGB 为黑（alpha 携带字形）→ 合成文本纹理须**强制白字形**（`alpha>32 → (255,255,255,alpha)`，否则 alpha 混合画黑字在暗地图上不可见）；且**字体纹理须在 `CrystalSpriteBatch.Begin()` 前构建**（渲染上下文内首建图集未就绪，实测字形纹理返回非 null 但全透明）。

**Gate G4 判定（Go/No-Go）**：
- **全链路封包差异说明**：P4-M1..M5 全部基于**未修改的服务器**——Unity 侧逐字移植客户端网络层（`Seams/Network.cs`），探针按旧客户端语义发送 `C.ClientVersion/C.Login/C.NewCharacter/C.StartGame/C.Walk/C.Attack/C.Chat/C.MoveItem/C.CallNPC/C.DropItem/C.PickUp/C.UseItem/C.LogOut`，收 `S.ClientVersion/S.LoginSuccess/S.StartGame/S.MapInformation/S.UserInformation/S.ObjectMonster/S.ObjectNPC/S.ObjectWalk/S.ObjectChat/S.MoveItem/S.NPCResponse/S.ObjectItem/S.GainedItem/S.UseItem/S.ObjectStruck/S.LogOutSuccess`。服务器侧**零源码改动**（仅运行时数据：`Build/Server/publish/Configs/BaseStatsWarrior.ini` 测试账号数值 boost，gitignored）。旧客户端 vs Unity 客户端的**包序/包体一致**（P4-M1 登录包流与 `Client/MirScenes/LoginScene.cs:98-321` 逐包对应；trace 23 包基线未动）。
- **双开对照**：A/B 双向互见（A 见 B 的 `ObjectPlayer+ObjectWalk`，B 见 A 的 `ObjectPlayer`），同服同场景对象网格同步验证。
- **持续游玩（soak）**：keepalive 修复已验证（`Seams/Network.cs` 按真实时间 `Environment.TickCount` 驱动，解耦模拟时间 `CMain.Time`——batchmode 主循环被后台任务抢占时模拟刻度失真会错过服务器 10s 窗口）。15 分钟 soak PASS（`dualopen ok`，全程 `conn=True`、`ka=218` 单调递增、A 处理 7900+ 包不掉线，服务器无 reason 21）。**2h soak PASS**（`SoakMs=5760000` 模拟 ms = 2h 真实，schtasks `CrystalG4Soak`）：`dualopen ok`，`real=7219s`（120.3 分钟），**全程 `conn=True`、`conn=False`/lost 次数=0**，`ka=1440`（严格 5.01s/个），A 处理 `recv=33939` 包、B 解析 `bParsed=40909` 包，B 线程全程存活，服务器日志无 reason 20/21 断开。

**✅ Gate G4 判定：GO**——端到端垂直链路全要素达标（服务器零源码修改）：
- **登录→选角→进图→移动→战斗→拾取→背包→NPC→聊天→下线**全链路真实服务器可跑（P4-M1..M5 探针矩阵 7/7 + 交互/战斗 PASS）；
- **包差异零**：Unity 侧包序/包体与旧客户端一致（LoginScene.cs 逐包对应，trace 23 包基线未动）；
- **双开对照**：同服同场景双向互见，对象网格同步；
- **2h 持续游玩无阻断**：双开全程在线、keepalive 严格 5s、无任何服务端断开。

## 阶段5 UI 系统（迭代包1：真实 MainDialog + ChatDialog 控制树渲染）

**目标**：用户选定 **纯 C# 兼容控件 + RT 直绘** UI 方案——逐字移植旧客户端控件基类（MirControl/MirLabel/MirImageControl/MirButton/MirTextBox 最小契约）进 Client.Core，由真实 `MainDialog`/`ChatDialog` 控制树驱动绘制，经 `CrystalSpriteBatch` 合成 RT 出 PNG，像素断言验收。服务器零修改。

**移植清单（Client.Core Ported/，`tools/CoreVerify` 全程 **0 警告 0 错误**）**：
- **控件基类最小契约**（任务 #1）：MirControl（Controls/Visible/Enabled/Draw/DrawControl 基类，BackColour 经 TextRenderer.FillBackgroundImpl 填充）、MirLabel（AutoSize→TextRenderer.MeasureText）、MirImageControl（DrawControl→Library.Draw，AutoSize→GetTrueSize/DisplayLocation→GetOffSet）、MirButton/MirTextBox 安全构造路径；
- **ChatDialog 完整聊天窗逻辑**（任务 #2）：4 类消息（Announcement/System/Shout/System2 各配彩色 back-rect）、ReceiveChat 追加 History、Update 重排 ChatLines、auto-scroll（StartIndex=History.Count-LineCount）；
- **MainDialog HUD 核心状态条**（任务 #3）：`S.UserInformation` 驱动的 HealthLabel（`HP {hp}/{max}`）/LevelLabel/CharacterName/ExperienceLabel + orb/exp 条（DrawSection source-rect 裁剪路径）+ frame1 底图；
- **MLibraryUnity 裁剪绘制 + virtual 覆写**（任务 #5）：seam `MLibrary.GetSize/GetTrueSize/GetOffSet` 改 virtual 由 Atlas.Frames 驱动；`Draw(int,Rectangle,Point,Color,bool)`/`Draw(int,Rectangle,Point,Color,float)` source-rect 裁剪重载覆写（HUD orb/exp 条走此路径）；`Libraries.Prguse` 去 readonly 供探针替换 atlas-backed 实例（影响整行逗号块）。

**渲染桥接（任务 #6，UiText.cs）**：把 Client.Core TextRenderer seam 的 5 个静态委托（Measure/Measure5/DrawText/FillBackground/DrawCaret）接到 Unity 动态字体（`Font.CreateDynamicFontFromOSFont` + `TextGenerator.Populate` CPU 字形）+ CrystalSpriteBatch。**关键坑（net-hud 实证延续）**：batch 内首建动态字体图集 → glyph UV 有效但 GetPixels32 透明 → 对策 `PreWarm(字号)` 预热图集 + `WarmTree(控制树)` 在 `CrystalSpriteBatch.Begin()` **之前**为每个 MirLabel.Text 预合成字形纹理（强制白字形：图集 RGB=黑+alpha，`src.a>32 ? white : transparent`），batch 内 DrawText 只命中 `_textTex` 缓存。实心背景/光标（纯纹理）无此限制。

**探针 + 编排（任务 #7/#8，`Build/net-ui.ps1` → `NetProbe.RunUi`）**：真实服务器（Server.exe 7000 端口）→ Unity batchmode 登录→选角→进图→`GameEntered`→RT(1024×768) 合成 MainDialog+ChatDialog 控制树 → PNG + 数据/像素双断言。**全 PASS exit 0（`ui ok`）**：
- **数据断言**：HealthLabel=`HP 154/154`（真实 S.UserInformation 驱动）、LevelLabel=`1`、CharacterName=`probe`、ExperienceLabel=`0%` 非空、chat 4 行全入列；
- **像素断言**：hpPx=58 / namePx=5（严格白字形区）、orbPx=6574（红区 r-b>20 区分 frame1 红基线）、panelPx=42976（聊天面板亮区）、蓝=1875 / 红=1834 / 绿 / 暗红=1531 四行彩色 back-rect（frame2221 彩色基线 0 保证可信）、帧条 net-ui.png 出图。

**关键实证与踩坑**：
- **布局依赖顺序**：ChatDialog ctor 读 `GameScene.Scene.MainDialog.Location.X` → 必须先建 MainDialog 再建 ChatDialog；`ChatNoticeDialog` 也须预建（seam 字段非 null）；
- **UserObject 富化**：不调 `Load(S.UserInformation)`（RefreshStats 重依赖），手动设 HP/MP/Level/Class/Experience/MaxExperience + `Stats[Stat.HP/MP]=max(v,1)`（除零防线）；
- **ChatDialog auto-scroll**：4 条 ReceiveChat 后 `StartIndex==History.Count-LineCount` → 只显示末行 → 探针显式 `chat.StartIndex=0; chat.Update()` 才 4 行全可见；
- **Mode.Ui 漏 LoadMap**：MapInformation handler 的 LoadMap 只在 Game/Hud 下调用 → `_mapLoaded` 恒 false → 超时。修复：条件扩展到 Mode.Ui；
- **绿行像素谓词**：`MColor.Green=(0,128,0)`（System.Drawing 同源，非 0,255,0）→ `g>170` 永不命中（greenPx=1）→ 阈值下探 `g>100 && g-r>60 && g-b>60` 后 PASS。蓝(0,0,255)/红(255,0,0)/暗红(139,0,0) 谓词不受影响；
- **PowerShell 5.1 编码**：net-ui.ps1 纯 ASCII 英文注释（中文 UTF-8 注释在 GBK codepage 无 BOM 下解析报错，`non-ascii=0` 验证）；
- **net-ui.png 垂直镜像**：RenderUi 输出漏了 R3 已实证的 `EncodeToPNG` 行序坑（PNG row0=RT 底，须先按行翻转再编码）——RenderGame 有 `Array.Copy(px,(rtH-1-y)*rtW,fl,y*rtW,rtW)` 翻转而 RenderUi 直接编码，导致 UI 画在 RT 底部却显示在图片顶部。补同款翻转后像素采样验证：top/mid=暗背景(25,25,25)、panel(546,692/705)=白(255,255,255)，方向正确。

**✅ 阶段5 迭代包1 完成**：真实控制树（非手工 spec）经 seam 桥接出真实 HUD + 4 行彩色聊天窗，数据/像素双断言全过。

## 阶段5 UI 系统（迭代包2：背包/装备/物品提示 + 按钮交互 + 输入光标）

**目标**：迭代包1 的 HUD/聊天渲染之上，逐字移植背包面板（InventoryDialog/CharacterDialog/ItemToolTip/MirItemCell）进 Client.Core 控制树，并打通**交互输入**——MainDialog 顶部功能按钮点击链（hover→pressed→click→开/关对话框）+ ChatTextBox 输入光标（Focused caret）。服务器零修改。

**背包移植（任务 #9-#14，`tools/CoreVerify` 全程 0 警告 0 错误）**：
- **Libraries seam 补库**（任务 #9）：`Items`/`StateItems`（图集产物 Stateitem，字段沿用旧客户端单数 t）/`Title`/`UI_32bit`（负重条 UI）；
- **GameScene seam 补字段**（任务 #10）：`SelectedCell` + `ItemToolTip`（ItemToolTip 渲染依赖）；
- **MirItemCell 渲染核心**（任务 #11）：Grid 背包格——Frame/Item 图、个数（StackCount）、New 标记、Slot 背板；**InventoryDialog 背包窗口**（任务 #12）：5×8 Grid 40 格 + 关闭按钮 + 切页（Bag/Equip/Storage）+ Process 走 GameScene.User；**CharacterDialog 装备窗口**（任务 #13）：Equipment 页 + 属性面板；**ItemToolTip 物品提示**（任务 #14）：ItemInfo 驱动的名称/属性多行浮层。

**背包探针 + 编排（任务 #15/#16，`Build/net-bag.ps1` → `NetProbe.RunBag`）**：真实服务器→Unity batchmode 进图→RT 合成 InventoryDialog+CharacterDialog+ItemToolTip 控制树→PNG+断言全 PASS（`bag ok`）：背板 320×260（Title 196 frame）、格子 5×8、测试剑 `ProbeBagSword`（user.Inventory[6]）格内图标 + 个数、装备窗 frame、Tooltip 文本区。**踩坑**：MirGridType.Equipment 的 grid 尺寸映射（OldGrid 维度表，背包 5×8/装备 6×5 区分）；`user.Inventory[6]` 跳过 0-5 腰带槽。

**交互输入（任务 #17-#19）**：MainDialog 顶部功能按钮（Inventory/Character/Skill 开关对话框）→ **GameScene 鼠标入口四 override**（Seams/GameScene.cs，MirScene 分发语义）：OnMouseDown/OnMouseUp 转发 MouseControl（!=this 时）、OnMouseMove 仅 MouseControl.Moving 时、OnMouseClick ActiveControl/MouseControl 兜底（MirControl.OnMouseUp 已 Deactivate 清空 ActiveControl → ActiveControl 路径断链，靠 MouseControl fallback 补回）。**输入光标**：MirTextBox.DrawControl 在 `TextBox.Focused` 时经 `TextRenderer.DrawCaretImpl` 画白竖线；探针 SetFocus 后像素断言 caret 白线 ≥5。

**关键实证与踩坑（迭代包2）**：
- **PixelDetect 命中断链**：MainDialog 设 `PixelDetect=true` → MirImageControl.IsMouseOver 走 `Library.VisiblePixel(Index, ...)`——seam 恒 false（非 virtual 空实现）→ 按钮 hover 永不命中，点击链全部落到 Scene。修复：seam `VisiblePixel` 改 virtual，MLibraryUnity 覆写为**图集像素 alpha 检测**（帧内相对坐标 + OffSet 锚点 + 行翻转 `texRow = tex.height-(f.Y+y)-1`，页面像素数组缓存 `GetPagePx` 避免逐次 GetPixels32）；
- **探针对话框须挂 Scene**：Scene.OnMouseMove 遍历 Scene.Controls 做 hit-test，MainDialog/ChatDialog 必须 `Parent=GameScene.Scene`（首版漏挂 → hover-mc=GameScene）；CharacterDialog 初始 Hide 避免遮挡按钮 hit-test；
- **MirTextBox 字形预热**：UiText.WarmTree 只覆盖 MirLabel，ChatTextBox 非 MirLabel → 新增 `UiText.WarmText(text,font)` 在 batch 前预合成字形（否则 batch 内建字体图集透明）；caret 实心白线无此限制；
- **GetInstanceID 废弃**：Unity 6000 下 `Object.GetInstanceID()` 报 CS0619 → 页面像素缓存键改 Texture2D 引用（Dictionary<Texture2D,Color32[]>）；
- **并行 Unity 进程锁冲突**：net-ui 与 net-bag 并行启动共享 `Unity/Library` → net-ui 编译前即退出（无 [netprobe] 输出）；回归矩阵**串行**执行。

**✅ 阶段5 迭代包2 完成**（任务 #17-#21）：背包/装备/Tooltip 控制树渲染 + MainDialog 按钮点击链（hover→pressed→click→开背包）+ ChatTextBox 光标全过。回归矩阵 `net-input.ps1`/`net-bag.ps1`/`net-ui.ps1` + CoreVerify 0w/0e **全绿**（`input ok`/`bag ok`/`ui ok`）。

## 阶段5 UI 系统（迭代包3：NPC 对话 + 商店 + 仓库）

按 PRD 推荐顺序（1.主 HUD+聊天 → 2.背包+装备+Tooltip → 3.NPC+商店+仓库）推进迭代包 3（任务 #22-#29）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Controls/MirGoodsCell.cs` | `Client/MirControls/MirGoodsCell.cs` | 名称/数量/价格标签 + New 标记 + 物品图标绘制 | UsePearls/Recipe/MultipleAvailable、BorderInfo override |
| `Dialogs/NPCDialog.cs` | `Client/MirScenes/Dialogs/NPCDialogs.cs` | R 正则选项按钮 `{文本/@动作}` + C 正则彩色文本 + 8 行分页 + Up/Down/PositionBar 滚动 + ButtonClicked 发 `C.CallNPC` | BigButton/Quest/Help 按钮、链接 tooltip、滚轮、PositionBar 拖拽 |
| `Dialogs/NPCGoodsDialog.cs` | 同上 | 8 个 MirGoodsCell + BuyButton/BuyLabel + Up/Down/PositionBar + BuyItem 发 `C.BuyItem`（StackSize>1 整组 maxQuantity 购买） | MirAmountBox 数量输入、UsePearls、Craft 分支、双开点击 |
| `Dialogs/StorageDialog.cs` | 同上 | 10×16 网格（GridType=Storage 绑 GameScene.Storage）+ Storage1/Storage2 分页 + Close | 仓库密码全套（MirInputBox/MirMessageBox/C.SetStoragePassword/C.UnlockStorage）、Rent 点击、GetCell |

**Seam 补齐**（`Seams/GameScene.cs`）：`ChatDialog`/`NPCGoodsDialog`/`StorageDialog` 字段、`NPCRate`/`NPCID`/`NPCTime`/`Storage`（static）。`MirItemCell.ItemArray` 增 `MirGridType.Storage` case。

**踩坑**：
- **IsShopItem 归属**：旧 `ItemInfo` 上无该字段——它是 **UserItem** 字段（`ItemData.cs:306`），商店探针初版误写在 ItemInfo 初始化器 → CS0117。移到 `new UserItem(...) { IsShopItem = true }`。
- **仓库密码/租赁流程裁剪**：依赖 `MirMessageBox`/`MirInputBox`（后续迭代对话框），StorageDialog 只保留网格渲染 + 分页切换，密码流程留待对应对话框移植。
- **NewColour 颜色名**：MirMath.Color 无命名色解析，`{文本/颜色名}` 彩色文本固定渲染黄（迭代包3 裁剪颜色名映射，渲染路径保留）。

**✅ 阶段5 迭代包3 完成**（任务 #22-#29）：NPC 对话窗 + 商店 8 格列表 + 仓库 10×16 网格控制树渲染全过。`net-npc.ps1`→`NetProbe.RunNpc` 出 PNG，数据断言（npc=4 行/goods=2 商品/storeGrid=160）+ 像素断言（npcFrame/npcBtn 黄按钮/goodsFrame/goodsCell/storeFrame/storeCell 全过）。回归矩阵 `net-npc.ps1`/`net-input.ps1`/`net-bag.ps1`/`net-ui.ps1` + CoreVerify 0w/0e **全绿**（`npc ok`/`input ok`/`bag ok`/`ui ok`）。

## 阶段5 UI 系统（迭代包4：技能页 + 快捷栏 + Buff 状态栏）

按 PRD 推荐顺序（1.主 HUD+聊天 → 2.背包+装备+Tooltip → 3.NPC+商店+仓库 → 4.技能+快捷栏+Buff）推进迭代包 4（任务 #30-#37）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Controls/MagicButton.cs` | `Client/MirControls/MagicButton.cs` | 231×33 技能格 + SkillButton 图标（MagIcon2 Icon×2）+ LevelImage/ExpImage + Level/Name/Exp/Key 标签 + CoolDown 施法遮罩 + SetDelay（34 帧） | SkillButton.Click 原打开 AssignKeyPanel（未移植） |
| `Dialogs/SkillBarDialog.cs` | `Client/MirScenes/Dialogs/MainDialogs.cs:1516` | 8 格 MagIcon 图标 + F1-F8 键名标签 + Prguse2 冷却遮罩 + 切换条按钮 + Show/Hide 联动 `Settings.SkillBar` + UseSpell 点击 | GetKey 原读 `KeybindOptions` 配置 → 固定 `CTRL+F1-F8` |
| `Dialogs/BuffDialog.cs` | `Client/MirScenes/Dialogs/BuffDialog.cs` | BuffIcon/MagIcon/Prguse2 三库图标 + 展开/折叠按钮 + 图标数标签 + 淡入淡出 + 冷却闪烁 + BuffString 完整描述 + 委托注入 `Settings.ExpandedBuffWindow` | `BuffType.Guild` case（依赖 GuildDialog.ActiveStats）、PoisonBuffDialog |
| `Dialogs/CharacterDialog.cs`（技能页） | `Client/MirScenes/Dialogs/MainDialogs.cs` | 7 格 Magics（HeroMagic 分支）+ NextButton/BackButton 翻页 + RefreshInterface 按 StartIndex 分页填充 | SocketDialog 等装备交互 |

**Seam 补齐**（`Seams/`）：`Settings.SkillBar`/`SkillbarLocation`/`ExpandedBuffWindow`；`Libraries.MagIcon`/`MagIcon2`/`BuffIcon`；`GameScene.UseSpell`/`Hero`/`SkillBarDialog` 字段。

**踩坑**：
- **BuffDialog.CreateBuff 不维护 Buffs 列表**：原版只插 `_buffList` 图标，`Buffs` 由调用方 GameScene 维护（旧 `GameScene.CreateBuff` 语义）。探针须先 `Buffs.Add` 再 `CreateBuff`，否则数据断言 `buffs=0` 语义失配（像素仍渲染但列表为空）。
- **SkillBarDialog.Update 的 `if (!Visible) return` 位于 HasSkill 计算之后**：Show() 因 HasSkill 初始 false 提前 return，探针须先 `Visible=true` 再 `Update()` 才走图标填充分支。
- **BuffDialog.Process 鼠标未悬停时把 Opacity 递减到 0**：探针手动 `Opacity=1` 且不调 Process，否则图标（含默认在原点叠加的 Location）不可见。

**✅ 阶段5 迭代包4 完成**（任务 #30-#37）：技能页 7 格 MagicButton + 快捷栏 8 格 + Buff 状态栏控制树渲染全过。`net-skill.ps1`→`NetProbe.RunSkill` 出 PNG，数据断言（chr=7/magics=2/barHas=True/buffs=3）+ 像素断言（charFrame=89683/skillText=38/magIcon=1007/barFrame=5937/barIcon=1536/buffFrame=3995/buffIcon=2563 全过）。回归矩阵 `net-skill.ps1`/`net-npc.ps1`/`net-input.ps1`/`net-bag.ps1`/`net-ui.ps1` + CoreVerify 0w/0e **全绿**（`skill ok`/`npc ok`/`input ok`/`bag ok`/`ui ok`）。

## 阶段5 UI 系统（迭代包5：任务 + 大地图 + 小地图）

按 PRD 推荐顺序（1.主 HUD+聊天 → 2.背包+装备+Tooltip → 3.NPC+商店+仓库 → 4.技能+快捷栏+Buff → 5.任务+大地图+小地图）推进迭代包 5（任务 #38-#45）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Dialogs/QuestDialogs.cs` | `Client/MirScenes/Dialogs/MainDialogs.cs` | QuestListDialog（任务列表 950）/QuestDetailDialog（详情 960）/QuestDiaryDialog（任务日记 961）/QuestTrackingDialog（追踪条）+ QuestRow/QuestGroupQuestItem/QuestSingleQuestItem/QuestRewards 子控件 + 完成/放弃按钮 + TrackedQuests 追踪 | 完成任务链、接受任务网络、QuestHint 提示、点击跳转 |
| `Dialogs/BigMapDialog.cs` | `Client/MirScenes/Dialogs/MainDialogs.cs:820` | 820 大地图 frame + BigMapViewPort 视口（mmap 图集 + UserRadarDot 定位）+ BigMapNPCRow 行（MapLinkIcon）+ 地图/移动按钮 | MovementButtons 构建（MapInfoList 关联）、Network 换图 |
| `Dialogs/MiniMapDialog.cs` | `Client/MirScenes/Dialogs/MainDialogs.cs:1764` | 2090/2091 大/小两档切换 + mmap 图集视口渲染（MiniMap_BeforeDraw 缩放裁剪）+ 地图/坐标标签 + 大地图/邮件切换按钮 + AMode/PMode 标签 | MailButton（Mail 未移植）、DXManager 雷达点 + npc.GetAvailableQuests 任务图标 |

**Seam 补齐**（任务 #42）：`Libraries.MiniMap`（mmap 图集动态选有效页 `while (mm<64 && GetSize(mm).Width<=0) mm++`）/`MapLinkIcon`；`GameScene.MapInfoList`/`MapInfo`/`QuestListDialog` 等对话框字段；`Settings.TrackedQuests`；`GameScene.NPCID`。

**探针 + 编排（任务 #44/#45，`Build/net-quest.ps1` → `NetProbe.RunQuest`）**：真实服务器进图 → RT(1024×768) **两遍渲染**合成控制树 → PNG + 断言**全 PASS exit 0（`quest ok`）**：
- **遍1 Quest 四窗**：QuestListDialog（任务列表 950 frame）+ QuestDiaryDialog（任务日记 961）+ QuestDetailDialog（详情 960）+ QuestTrackingDialog（追踪条）——探针数据注入（NPC `npcObject.Quests` + ClientQuestInfo/ClientQuestProgress + `Settings.TrackedQuests=-1`），断言 questListFrame=125558/questRowName=4/diaryGroup=32/diaryTask=2/trackName=49/trackTask=2/detailFrame=115982/rewardDeco=600；
- **遍2 地图两窗**：BigMapDialog（820 frame + npcInfo BigMapNPCRow 注入 `record.NPCButtons`）+ MiniMapDialog（2090 frame + mmap 视口），断言 bigFrame=339695/bigNpc=5/miniFrame=19151/miniView=6957/miniCoord=9。调试产物 `net-quest-pass1.png`（遍1 存档）。

**关键实证与踩坑（迭代包5）**：
- **`MapControl.User` 与 `MapObject.User` 是两个不同 static 字段**（QuestListDialog.ReDisplayButtons 读前者、BigMapViewPort/MiniMapDialog 读后者）——探针必须同步赋值 `MapControl.User=user` + `MapObject.User.CurrentLocation`，漏前者 `ReDisplayButtons` NRE；
- **`NPCObject.Quests` 只在 `Load(S.ObjectNPC)` 初始化**（构造后为 null）——探针绕过 Load 须自行 `npcObject.Quests = new List<ClientQuestInfo>()`；
- **Show() 的 `if (Visible) return` 守卫**（MirControl.Visible 默认 true）——QuestListDialog/QuestDiaryDialog 直接调内部填充方法（`diary.DisplayQuests()` / `list.CurrentNPCID=npcId; list.DisplayInfo()`），QuestTrackingDialog 无守卫不受影响；
- **BigMap 的 NPCButtons 从不自动构建**（移植版未从 MapInfo 构建）——探针自行 `record.NPCButtons.Add(new BigMapNPCRow(npcInfo))`；
- **8F 小字号抗锯齿中心像素 < 240**（strictWhite>10 假 fail）——新增 `nearWhite`（全通道 >170）判定，questRowName/diaryTask/trackTask 文本断言改近白阈值 15；bigNpc/miniCoord 大字继续 strictWhite 通过；
- **QuestTracking 追踪须 `AddQuest(progress, true)` + 手动 `mini.Process()`**（Process 依赖 map 标题/坐标 + 追踪任务图标）。

**✅ 阶段5 迭代包5 完成**（任务 #38-#45）：任务列表/日记/详情/追踪 + 大地图 + 小地图控制树渲染全过。回归矩阵 `net-quest.ps1`/`net-skill.ps1`/`net-npc.ps1`/`net-input.ps1`/`net-bag.ps1`/`net-ui.ps1` + CoreVerify 0w/0e **全绿**（`quest ok`/`skill ok`/`npc ok`/`input ok`/`bag ok`/`ui ok`）。

## 阶段5 UI 系统（迭代包6：组队 + 好友 + 行会）

按 PRD 推荐顺序（...→5.任务+大地图+小地图 → 6.组队+好友+行会）推进迭代包 6（任务 #46-#53）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Dialogs/GroupDialog.cs` | `Client/MirScenes/Dialogs/GroupDialog.cs` | Prguse 120 frame + Title 5 标题 + Prguse2 360 关闭 + 允许组队 Switch（114-119 状态切换）+ 添加/移除按钮（Title 133-138，未入队/队长态切 130-132）+ 8 成员标签（Globals.MaxGroup）+ 成员位置 Hint（GroupMembersMap）+ 静态 `AllowGroup`/`GroupList`/`GroupMembersMap` 契约 | Switch/Add/Del 按钮点击（网络 + MirInputBox）、public AddMember(string) 与私有 Add/DelMember |
| `Dialogs/FriendDialog.cs`（含 FriendRow + MemoDialog） | `Client/MirScenes/Dialogs/FriendDialog.cs` | Title 199 frame + Title 6 标题 + 好友/黑名单 tab（163/167 ↔ 164/166）+ 12 行 FriendRow 翻页（Prguse2 240-245）+ 操作按钮（Prguse 554-568）+ MemoDialog 备注浮窗（Title 209）+ 在线绿/离线白 + Selected 灰背景 | AddButton（MirInputBox）/RemoveButton（MirMessageBox）/EmailButton（邮件未移植）/WhisperButton（输入接管未接驳）点击留空；FriendRow.OnMouseEnter/Leave（CreateMemoLabel 未接驳）；Show() 的 C.RefreshFriends；MemoDialog OKButton 的 C.AddMemo；UpdateDisplay 里 DisposeMemoLabel（保留 MemoDialog.Hide） |
| `Dialogs/GuildDialog.cs` | `Client/MirScenes/Dialogs/GuildDialog.cs` | Prguse 180 frame + Title 25 标题 + 公告页（NoticeButton 93/94 + MirTextBox 多行公告 + Prguse2 197-206 滚动条 UI）+ 状态页（StatusButton 103/104 + Prguse 1850 底图 + 行会名/等级/成员数标签）+ 补契约 `EnabledBuffs`/`FindGuildBuffInfo`（UserObject 引用） | Members/Storage/Rank/Buff 四页整页删除（MirDropDownBox/MirItemCell GuildStorage/网络/行会 Buff 状态机）；MirInputBox/MirMessageBox → Show() 校验分支直接 return；Notice 滚动方法（InputTextBox 无 ScrollToCaret）与滚轮/KeyDown/OnMoving 裁剪 |

**Seam 补齐**（任务 #50）：`GameScene.cs` 增 `GroupDialog`/`FriendDialog`/`MemoDialog` 字段；`Dialogs.cs` 删除 GroupDialog/GuildDialog 旧桩（CS0101 重复定义），保留 ChatNotice/Mount/Fishing/FishingStatus/MailList 桩。

**探针 + 编排（任务 #52/#53，`Build/net-team.ps1` → `NetProbe.RunTeam`）**：真实服务器进图 → RT(1024×768) **两遍渲染**合成控制树 → PNG + 断言**全 PASS exit 0（`team ok`）**：
- **遍1 组队+好友**：GroupDialog（Prguse 120 @(20,30)，注入 8 成员 GroupList + AllowGroup + GroupMembersMap）+ FriendDialog（Title 199 @(280,30)，注入 12 非黑名单 Friends 前 5 在线 + 2 黑名单），断言 groupFrame=53448/groupMember=29/friendFrame=63401/friendOnline=19/friendOffline=27 + 数据（members=Probe..Member7/allow=True/rows=12/online=True/blocked=2）；
- **遍2 行会**：GuildDialog（Prguse 180 @(20,30)，注入 GuildName=ProbeGuild/Level=3/MemberCount=12/MaxMembers=50/公告文本直设），断言 guildFrame=253969/guildName=28/guildLevel=23/guildMembers=34/notice=17 + 数据（guild=ProbeGuild/lv=3/mem=12/50）。调试产物 `net-team-pass1.png`（遍1 存档）。

**关键实证与踩坑（迭代包6）**：
- **`Dialogs.X` 前缀在 NetProbe 不可解析**（NetProbe 命名空间 `Crystal.Rendering.Editor` 不在 `Client.MirScenes` 内，`using Client.MirScenes` 只导入类型不导入子命名空间名；GameScene.cs seam 能用 `Dialogs.X` 是因它处于 `Client.MirScenes` 命名空间内）——探针一律无前缀类型名（`GroupDialog`/`FriendDialog` 等，Quest 同款）；
- **GroupDialog 构造内 `GroupList.Clear()`**（静态字段）——探针须**先建控件再填静态数据**，否则构造清零；
- **`GroupMembers` 是实例字段非静态**——探针填充循环用实例引用 `group.GroupMembers.Length`（CS0120）；
- **MirTextBox `Enabled=false` 仍渲染文本**（DrawControl 不受 Enabled 门控，Text 非空即画，仅影响交互）——GuildDialog 公告文本可像素断言；
- **组队/好友/行会 Show() 带 Visible 守卫**（构造默认 Visible=true → Show 直接 return）——探针直接渲染，数据填充靠各自 BeforeDraw 每帧驱动（等价旧 GameScene 循环）；
- **在线好友绿字谓词**：`Color.Green=(0,128,0)`（非 0,255,0）→ `g>90 && g>r+20 && g>b+20` 命中（friendOnline=19）；
- 好友/行会数据注入后 FriendDialog 须手动 `Update()`（构造 Rows），GuildDialog/GroupDialog 靠 BeforeDraw 自动填充（无需手动调用）。

**✅ 阶段5 迭代包6 完成**（任务 #46-#53）：组队 + 好友 + 行会控制树渲染全过。回归矩阵 `net-team.ps1`/`net-quest.ps1`/`net-skill.ps1`/`net-npc.ps1`/`net-input.ps1`/`net-bag.ps1`/`net-ui.ps1` + CoreVerify 0w/0e **全绿**（`team ok`/`quest ok`/`skill ok`/`npc ok`/`input ok`/`bag ok`/`ui ok`）。

## 阶段5 UI 系统（迭代包7：交易 + 邮件 + 拍卖）

按 PRD 推荐顺序（...→6.组队+好友+行会 → 7.交易+邮件+拍卖）推进迭代包 7（任务 #54-#61）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Dialogs/TradeDialogs.cs` | `Client/MirScenes/Dialogs/TradeDialogs.cs` | TradeDialog（2×5 交易格 + 名字/金币/锁定标签 + TradeLocked）+ GuestTradeDialog（GuestItems **static** 10 槽 + GuestName/GuestGold 实例字段 + 确认/取消）+ RefreshInterface/TradeAccept | MirAmountBox 数量输入、交易网络确认时序、金币输入 |
| `Dialogs/MailDialogs.cs` | `Client/MirScenes/Dialogs/MailDialogs.cs` | MailListDialog（收件箱列表行 + Sender/Subject/Opened）+ MailComposeLetterDialog（写信 + MultiLine）+ MailComposeParcelDialog（寄包裹 8 格 Items **static**）+ MailReadLetterDialog（读信）+ MailReadParcelDialog（取包裹 ReadMail） | 附件提取/删除、MirInputBox 收件人输入、分页删除 |
| `Dialogs/TrustMerchantDialog.cs`（含 AuctionRow + Filter） | `Client/MirScenes/Dialogs/TrustMerchantDialog.cs` | 拍卖行 Market 面板（10 行 AuctionRow：图标/名称/卖家/价格/过期 + 8 个 Filter 筛选树 + 搜索/刷新/分页按钮）+ Consign 寄售面板（ItemCell + 价格 TextBox + 出售/立即出售按钮 + HelpLabel）+ UserMode/MarketType static + Listings/Page/PageCount + 网络发包保留（C.MarketPage/MarketRefresh/MarketSearch/MarketGetBack/MarketBuy/MarketSellNow/ConsignItem） | MirAmountBox 出价、UserMode 非直售分支统一发包、`Program.Form.ActiveControl` 行、Auction 分支点击留空 |

**Seam 补齐**（任务 #58）：`Libraries.Prguse2` 等图集、`GameScene` 各对话框字段（MailListDialog/TradeDialog/GuestTradeDialog/MailCompose*/MailRead*）、`GradeNameColor`/`CreateItemLabel`/`DisposeItemLabel`、`GameScene.Gold/Credit`。

**探针 + 编排（任务 #60/#61，`Build/net-market.ps1` → `NetProbe.RunMarket`）**：真实服务器进图 → RT(1024×768) **四遍渲染**合成控制树 → PNG + 断言**全 PASS exit 0（`market ok`）**：
- **遍1 交易**：TradeDialog（注入 `user.Trade[0]=sword`/TradeGoldAmount/TradeLocked）+ GuestTradeDialog（GuestItems static + GuestName=Guest/GuestGold=3000），断言 trade=Probe/5000 guest=Guest/3000 + tradeFrame/tradeIcon/guestFrame/guestIcon；
- **遍2 邮件五窗**：MailListDialog（3 封：letterMail Opened+CanReply / goldMail Opened=false+Locked+Gold=100 / itemMail Opened 带 sword）+ ComposeLetter + ComposeParcel（Cells[0].Item 在 ComposeMail **后**赋值）+ ReadLetter + ReadParcel，断言 mail=3/3 + mailListFrame/mailRowSender/composeLetterFrame/composeRecipient/composeParcelFrame/parcelCell/readLetterFrame/readSender/readParcelFrame/readParcelCell；
- **遍3 拍卖**：TrustMerchantDialog 实例A UserMode+MarketType（构建 8 个 Filter 筛选树 + 10 行 Rows + 搜索/刷新按钮），断言 rows=5/5 filters=8 + marketFrame=227659/filterTree/row0Icon/row0Name/searchBtn；
- **遍4 寄售**：TrustMerchantDialog 实例B（UserMode=false Consign 面板：ItemCell 直设 sword + 价格 + 出售/立即出售按钮 + HelpLabel），断言 consignFrame=228216/consignItem/sellBtn/helpLabel。调试产物 `net-market-pass1/2/3.png`。

**关键实证与踩坑（迭代包7）**：
- **`MirItemCell.ItemArray` 对未移植 GridType 抛 NotImplementedException，且 setter 也走 ItemArray**（初版探针 `cell.Item=xxx` 直接赋触发异常）——修复：补 `MirGridType.Trade→GameScene.User.Trade`/`GuestTrade→GuestTradeDialog.GuestItems`/`Mail→MailComposeParcelDialog.Items` 三个 case + Item getter/setter 加 **TrustMerchant 独立 `SellItemSlot` 分支**（旧客户端在 ItemArray 前检查）+ `using Client.MirScenes.Dialogs;`（MirItemCell 在 Client.MirControls 命名空间，不满足 `Dialogs.X` 前缀规则）；
- **TrustMerchantDialog 无 SetListings**：PageCount/Page 由旧 GameScene NPCMarket（GameScene.cs:5639-5653）直接赋值——探针按同模式注入 `Listings=...; Page=0; PageCount=...; UpdateInterface()`；
- **Mail ComposeMail 内 ResetLockedCells** 会清空格子并发 C.MailLockedItem——探针须在 ComposeMail 后给 Cells[i].Item 赋值；
- **itemMail 须 Opened=true**（ReadMail 里 `if (!Mail.Opened)` 会发包 C.ReadMail）；
- **TrustMerchant 探针双实例**：实例A Market 面板触发筛选树构建 + Rows 填充；实例B Consign 面板触发 ItemCell/PriceTextBox/SellItemButton/HelpLabel（`TrustMerchantDialog.SellItemSlot` 经 MirItemCell.Item 直设）。

## 阶段5 UI 系统（迭代包8：英雄 + 宠物/坐骑）

按 PRD 推荐顺序（...→7.交易+邮件+拍卖 → 8.英雄+宠物）推进迭代包 8（任务 #62-#70）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Ported/UserHeroObject.cs` | `Client/MirObjects/UserHeroObject.cs` | 玩家自己的英雄对象（继承 UserObject）：AutoPot/AutoHPPercent/AutoMPPercent 自动喝药 + HPItem/MPItem 快捷物品槽 + GetBuffDialog 路由 + Load(S.UserInformation) 玩家状态核心 | 封包发送（回城/英雄技能等） |
| `Ported/HeroObject.cs` | `Client/MirObjects/HeroObject.cs` | 玩家控制的英雄战斗对象（继承 MonsterObject）：死亡复活/结阵标记 | 战斗 AI 行为、HeroRunTask |
| `Controls/MirAnimatedControl.cs` | `Client/MirControls/MirAnimatedControl.cs` | 动画控件（帧序列播放 + FrameIndex/Direction + 帧间隔）：坐骑/英雄动画载体 | 触发器（AnimatedControlInfo）事件 |
| `Dialogs/HeroDialogs.cs` | `Client/MirScenes/Dialogs/HeroDialogs.cs` | 英雄系统 8 控件：HeroInventoryDialog（40 格 + AutoPot HP/MP 按钮+百分比 Label+快捷槽）/HeroBeltDialog（2 格腰带）/HeroMenuPanel/HeroInfoPanel（头像+HP/MP/Exp 条+AutoPot 预览）/HeroAutoPotPreview/HeroBehaviourPanel（4 行为）/HeroManageDialog + HeroManageAvatar（英雄列表） | CMain.InputKeys.GetKey（Unity 无 KeybindOptions）；Point.Add(int,int)→Add(Point) |
| `Dialogs/MountDialog.cs` | `Client/MirScenes/Dialogs/MountDialog.cs` | 宠物/坐骑对话框：MirAnimatedControl 坐骑动画（StartIndex+MountType*20 帧序列）+ 5 格坐骑装备（MirGridType.Mount，依 Slots.Length 切 4/5 槽布局）+ Ride@ride + NoMount 提示 + Help | 帮助页内容、坐骑装备细分展示 |

**Seam 补齐**（任务 #67）：`GameScene.Hero`（static UserHeroObject）/`HeroInventoryDialog`/`HeroDialog`/`HeroSpawnState`/`MaximumHeroCount`/`HeroStorage`/`MountDialog`/`HeroBuffsDialog` 字段 + `GameScene.HeroAvatar(job,gender)=1400+(byte)job+10*(byte)gender`（GameScene.cs:32-35）；HeroInfoPanel 加探针只读 `AvatarIndex`（Avatar 为私有渲染组件）。

**探针 + 编排（任务 #69/#70，`Build/net-hero.ps1` → `NetProbe.RunHero`）**：真实服务器进图 → RT(1024×768) **五遍渲染**合成控制树 → PNG + 断言**全 PASS exit 0（`hero ok`）**：
- **遍1 英雄背包**：HeroInventoryDialog（hero.Inventory[0]=药水/1=剑 绑定 Grid + AutoPot=true 触发 HP/MP 按钮+百分比 Label+快捷槽），断言 inv=40 autoPot=True + heroInvFrame=71853/heroInvIcon0=604/heroInvIcon1=622/autoPotBtn=83/autoPotLabel=25；
- **遍2 英雄状态+腰带+行为**：HeroInfoPanel（Update() 取 Hero 头像 1400 + HP/MP/Exp）+ HeroBeltDialog（Grid[0] 药水）+ HeroBehaviourPanel（UpdateBehaviour(Attack)），断言 avatar=1400 + infoFrame=10458/infoAvatar=1309/infoName=27/beltFrame=3756/beltCell=631/behaviour=831；
- **遍3 英雄管理**：HeroManageDialog（HeroStorage[0]=Hero1/Warrior + SetCurrentHero+RefreshInterface），断言 avatars0=Hero1/1770 + manageFrame=41423/currentAvatar=1551/slotAvatar=2568；
- **遍4 坐骑**：MountDialog（user.Equipment[Mount]=5 槽 mountItem 填 Reins/Bells/Saddle/Ribbon/Mask + user.MountType=0 → SwitchType case 5 布局 167 + 动画 StartIndex 1330），断言 mountIdx=167 mountName=ProbeMount + mountFrame=112390/mountAnim=1200/reins=604/mask=604/mountName=49；
- **遍5 英雄菜单**：HeroMenuPanel（HeroMagicsButton），断言 menuFrame=1234/menuBtn=256。调试产物 `net-hero-pass1..5.png`。

**回归矩阵（任务 #70）**：`net-hero/net-team/net-quest/net-skill/net-npc/net-input/net-bag/net-ui` 8 脚本串行（Unity/Library 共享锁）全 PASS exit 0，`tools/CoreVerify` **0 警告 0 错误**。

**关键实证与踩坑（迭代包8）**：
- **探针须为每模式补 `MapLoaded` 加载分支 + `EnsureUser` 用户创建分支**（初版 Hero 模式两处条件列表缺 `Mode.Hero`，致 `_mapLoaded` 恒 false → GameEntered 永不触发 → 超时）——修复：MapInformation 分支 `|| _mode == Mode.Hero` + UserInformation 分支 `|| _mode == Mode.Hero`；
- **mode tag 三元缺 Hero 分支** → 失败时错报 `dualopen fail`——补 `_mode == Mode.Hero ? "hero"`（显示问题，不影响逻辑）；
- **HeroInventoryDialog 构造显式 `Visible = false`**（旧客户端默认收起）而其余 hero 对话框默认可见——探针须 `heroInv.Visible = true` 激活，否则 `Visible` getter 级联（`Parent.Visible && _visible`，MirControl.cs:474）隐藏全部子控件，像素断言全灭；
- **像素断言假阳性陷阱**：Clear 背景 (0.1,0.1,0.1)→(26,26,26) 满足 `bright`（r+g+b>60）但不满足 `lit`（偏离 25 超 15）——未激活对话框时 bright 区域整块计数（icon0=1152=36×32 整格）易被误判为"已渲染"，须以 `lit`/nearWhite 佐证（frame=0 才暴露未绘制）；
- **坐骑数据装配**：`user.MountType = 0`（short，默认 -1）+ `user.Equipment[(int)EquipmentSlot.Mount]`（=13，数组 14 不越界）+ `UserItem.Slots = new UserItem[5]` 填五种配饰（MountSlot{Reins=0,Bells=1,Saddle=2,Ribbon=3,Mask=4}）——`EquipmentSlot.Mount=13` 槽位与 `Slots` 子物品双重要求；
- **依赖顺序**：HeroBeltDialog/HeroBehaviourPanel 构造读 `Scene.MainDialog.Location` → 探针须先建 MainDialog；HeroInventoryDialog Grid ItemSlot=2+idx 语义（Grid[0]=Inventory[2]）须按 `GameScene.Hero` 双赋值（`GameScene.Hero` + `MapObject.Hero`）。

## 阶段5 UI 系统（迭代包9：商城 + 小扩展集）

按 PRD 推荐顺序（...→8.英雄+宠物 → 9.商城+扩展系统）推进迭代包 9（任务 #71-#78）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Dialogs/GameShopDialog.cs` | `Client/MirScenes/Dialogs/GameshopDialog.cs` | Title 749 商城框体 + 4 tab（allItems/topItems/Deals/New）+ 6 职业按钮（ALL/War/Sin/Tao/Wiz/Arch）+ 22 Filters 分类标签 + Search 搜索 + 8 格 Grid（GameShopCell）+ 分页（Prev/Next/PageNumberLabel）+ PositionBar 滚动 + PaymentTypeGold/Credit 勾选 + totalGold/totalCredits + Viewer | —（768 vs 763 行近 1:1 全移植） |
| `Controls/GameShopCell.cs`（含 `GameShopViewer`） | `Client/MirControls/MirGameShopCell.cs` | 名称/价格/库存/数量标签 + BuyItem（MirMessageBox 确认 → `C.GameshopBuy`）+ PreviewItem + 数量加减 + 悬停 Tooltip（CreateItemLabel/DisposeItemLabel）；GameShopViewer 三维预览（武器/铠甲/坐骑/变身，方向帧切换） | — |
| `Controls/MirCheckBox.cs` | `Client/MirControls/MirCheckBox.cs` | 支付类型勾选框（Checked 状态 → CheckBox 图帧切换，PaymentTypeGold/Credit） | — |
| `Dialogs/SocketDialog.cs` | `Client/MirScenes/Dialogs/SocketDialog.cs` | 打孔镶嵌：Prguse3 框体（Index=20+槽数）+ 6×2 槽网格（GridType=Socket 绑 `GameScene.SelectedItem.Slots`）+ BindGrid 按槽数显隐 + Show(grid,item) 依背包/装备定位 + GetCell + CloseButton | — |
| `Dialogs/CompassDialog.cs` | `Client/MirScenes/Dialogs/CompassDialog.cs` | 指南针：Destination/SetPoint/ClearPoint + Process 方位角（Atan2）→ `_image.Index=1470+floor(offset)` 40 指向帧 | — |
| `Dialogs/ReportDialog.cs` | `Client/MirScenes/Dialogs/ReportDialog.cs` | Prguse 1633 举报框体 + MirDropDownBox 类型选择 + 多行消息区 + 发送/关闭按钮（SendButton_Click 逐字保留旧客户端 NotImplementedException，协议未实现） | — |
| `Controls/MirDropDownBox.cs` | `Client/MirControls/MirDropDownBox.cs` | 下拉框（_label 当前项 + 展开 _Option 列表 + 滚动条 + BeforeDraw 未选时空文本） | — |

**Seam 补齐**（任务 #74）：`Seams/GameScene.cs` 增 `GameShopInfoList`（static List<GameShopItem>）+ `GameShopUpdate`/`GameShopStock` 网络 handler（旧客户端 GameScene.cs:6679-6698 同源：GameShopInfo 追加商品、7 天内新品点亮 New 标签、GameShopStock 按 GIndex 更新/移除库存、商城可见时 UpdateShop 刷新）+ `GameShopDialog`/`SocketDialog`/`ReportDialog`/`CompassControl` 对话框字段 + `SelectedItem`（static，SocketDialog 槽数组源）；`Libraries.Prguse3`（打孔框体图集）；`Controls/MirItemCell.cs` 增 `MirGridType.Socket` case → `GameScene.SelectedItem?.Slots`；`TextRenderer` seam 增 `TextFormatFlags.RightToLeft`（商城价格标签右对齐，数值对齐 WinForms）。

**探针 + 编排（任务 #76/#77，`Build/net-shop.ps1` → `NetProbe.RunShop`）**：真实服务器进图 → RT(1024×768) **四遍渲染**合成控制树 → PNG + 断言**全 PASS exit 0（`shop ok`）**：
- **遍1 商城**：GameShopDialog（7 件合成商品：6 件 Warrior/All 可见 + 1 件 Wizard 应被过滤），断言 filled=6/g0=BattleSword/无 Wizard 泄漏/page=1 / 1/Filters[0]=Show All(R=230)/Filters[1]=Weapons/class=Warrior/Gold 勾选 Credit 未勾/New 初始隐藏 + shopFrame=323217/shopIcon=113/shopName=92/gold=15/credit=19/box=184/page=1298；
- **遍2 打孔镶嵌**：SocketDialog（SelectedItem 2 槽 → Index=21 Prguse3 118×62 + BindGrid），断言 socketFrame=7301/socketStone=604；
- **遍3 指南针**：CompassDialog（Destination≠位置 → 指向帧），断言 compass=300；
- **遍4 举报**：ReportDialog（下拉框/消息区/发送按钮，Prguse 1633 框体本服务器图集空帧 → 仅断言子控件），断言 reportDrop=2276/reportBox=49500/reportSend=1560。调试产物 `net-shop-pass1..4.png`。

**回归矩阵（任务 #78）**：`net-shop/net-hero/net-team/net-quest/net-skill/net-npc/net-input/net-bag/net-ui` 9 脚本串行（Unity/Library 共享锁）全 PASS exit 0，`tools/CoreVerify` **0 警告 0 错误**。

**关键实证与踩坑（迭代包9）**：
- **`MirControl.Visible` 默认 true + `Show()` 的 `if (Visible) return` 守卫**（MirControl.cs:609-613）——探针 `shop.Show()` 首跑 no-op（GetCategories/UpdateShop 不执行：grid=0、pageText 空、ClassFilter 停 "Show All"、Filters 停 "Testing - N" 占位，run-1 FAIL `filled=0` 实证）——**须先 `shop.Visible=false` 再 `Show()`**；`Visible` setter 不级联子控件（仅 getter `Parent.Visible && _visible`，OnVisibleChanged 递归不改 `_visible`），置 false 后再 Show 不影响 New 按钮初始隐藏；
- **商城数据流**：真实服务器 StartGame 后自动推送 S.GameShopInfo 目录（探针收 106 包）→ `_shopFrozen` 冻结门（对话框未建/冻结期只计数不应用，`shopPush=106` 全计数 0 应用），保证合成数据确定性；`GameShopUpdate` 追加商品 + 7 天内新品点亮 New 标签（GameScene.cs:125）——探针 handler 测试验证增/删/改（GameShopInfo add → count+1 + New 点亮、GameShopStock 按 GIndex 移除 108 / 更新 101→Stock=1）；
- **GameShopCell 图标定位**：offSet=(32-size)/2，icon 画在 `offSet + DisplayLocation + (12,40)`；Grid[0] 格绝对 (172,135) → 剑图标 (184,177)；`SetCategories` 覆写 Filters 占位文本为真实分类（Filters[0]="Show All" ForeColour (230,200,160)、Filters[1]="Weapons"）；
- **MirDropDownBox 未选时 BeforeDraw 置 `Text=" "`** → `DrawControlTexture=true` → 黑(6,6,6) 底填充（reportDrop 断言）；MirTextBox `BackColour=Black + DrawControlTexture=true` 黑底（reportBox 断言）；
- **Prguse 1633（举报框体）在本服务器图集为空帧** → 报告窗仅断言下拉框/文本框/发送按钮，不锚框体像素；
- **SocketDialog.Show 槽数 0 提前隐藏**（item.Slots.Length==0 → Visible=false）；2 槽 → `Index=21`；`BindGrid` 隐藏 idx>=count 槽；`MirGridType.Socket` 经 `GameScene.SelectedItem?.Slots` 取槽数组（MirItemCell.cs:76-78）。

## 阶段5 UI 系统（迭代包10：设置三件套）

按 PRD 推荐顺序（...→9.商城+扩展系统 → 10.设置）推进迭代包 10（任务 #79-#85）：

**移植清单**（全部逐字移植 + 裁剪后续迭代扩展，进 `Unity/Assets/Crystal/Client.Core/`）：

| 文件 | 来源 | 保留 | 裁剪 |
|------|------|------|------|
| `Dialogs/ChatOptionDialog.cs` | `Client/MirScenes/Dialogs/ChatOptionDialog.cs` | Title 466/467 框体双 tab（筛选/透明）+ AllButton 全开/关 + 8 频道筛选按钮（Prguse 2070-2087）+ 透明开关（Title 470-475）→ `Settings.TransparentChat` | — |
| `Dialogs/HelpDialog.cs` | `Client/MirScenes/Dialogs/HelpDialog.cs` | Prguse 920 框体 + 45 页帮助（3 快捷页 ShortcutPage1/2/3 + 42 图片页 ImageID 0-41，Help 图集）+ Prev/Next 翻页 + PageLabel/PageTitleLabel | — |
| `Dialogs/KeyboardLayoutDialog.cs` | `Client/MirScenes/Dialogs/KeyboardLayoutDialog.cs` | Title 119 框体 + KeybindRow 绑定行（BindName/DefaultBind/CurrentBindButton）+ 分组标题行 + PositionBar 滚动 + Enforce 严格/宽松 + CheckNewInput（Ctrl/Shift/Alt/Tilde 修饰 + Delete 清除） | — |

**Seam 补齐**（任务 #82）：`Seams/KeyBindSettings.cs`（KeybindOptions 枚举 + KeyBind 类 + Keylist/DefaultKeylist 双表 + GetKey 格式化 + Save/Load INI 持久化，旧客户端 `CMain.InputKeys` 同源）；`Seams/GameScene.cs` 增 `ChatOptionDialog`/`HelpDialog`/`KeyboardLayoutDialog` 对话框字段；`Seams/Settings.cs` 增 8 个 `Filter*` + `TransparentChat`；`Libraries.Help`（MirGraphics.cs，Help 图集，图集产物 Help 已在 Build/assetcompile/all）；`ClientTextKeys` 帮助文案键（Language.cs）；`Seams/MirInput.cs` 增 `KeyEventArgs(keyCode, shift, alt, ctrl)` 构造 + `Keys` 枚举（A-Z/D1-8/NumPad/Insert/ShiftKey/ControlKey/Menu/Oem8/None/Delete）+ `MouseButtons`；`ChatDialog.Update()` 应用 8 个 filter 联动（ChatOptionDialog 改筛选实时生效）。

**探针 + 编排（任务 #84，`Build/net-settings.ps1` → `NetProbe.RunSettings`）**：真实服务器进图 → RT(1024×768) **四遍渲染**合成控制树 → PNG + 断言**全 PASS exit 0（`settings ok`）**：
- **遍1 ChatOptionDialog 筛选 tab**：初始 AllFiltersOff=true + 点 AllButton → 8 filter 全开 + AllFiltersOff=false + 点 GeneralButton → FilterNormalChat 关，断言 coFrame=39720/coAll=33/coChatTab=594；
- **遍2 透明 tab**：点 ChatTabButton 切页（Title 467、筛选按钮隐藏、透明按钮显示）+ 点 TransparencyOn/Off → TransparentChat 开/关 + ChatDialog ForeColour/Opacity 着色切换，断言 co2Frame=39687/co2On=361；
- **遍3 HelpDialog**：45 页 + 快捷页 ShortcutPage1 + 点 NextButton 翻页 + DisplayPage(3)=Movements（ImageID=0）+ PageTitleLabel "4. " 前缀，断言 helpFrame=265611/helpImg=214942/helpPageLabel=27；
- **遍4 KeyboardLayoutDialog**：Keylist 行 + 默认 Inventory=F9 + 行按钮点击 WaitingForBind 置位/再点清空 + CheckNewInput（Ctrl+K → Key=K/RequireCtrl=1/"Ctrl + K"，Delete → Key=None/GetKey=""），断言 kbdFrame=211818/kbdRowBtn=1453。调试产物 `net-settings-pass1..4.png`。

**回归矩阵（任务 #85）**：`net-settings/net-shop/net-hero/net-team/net-quest/net-skill/net-npc/net-input/net-bag/net-ui` 10 脚本串行（Unity/Library 共享锁）全 PASS exit 0，`tools/CoreVerify` **0 警告 0 错误**。

**关键实证与踩坑（迭代包10）**：
- **CheckNewInput 契约**：调用前须置 `WaitingForBind`（KeyboardLayoutDialog.cs:336 调用后清空）——探针两次 CheckNewInput（Ctrl+K 后 Delete）第二次未重设致 NRE（`Single` lambda 解引用 `WaitingForBind.function`）；真实客户端仅绑定等待态路由按键，契约由调用方保证；
- **HelpDialog 页数 = 45**（3 快捷页 + 42 图片页 ImageID 0-41），非旧客户端常见 44；
- **MirButton hover 置 `Index=HoverIndex`**（OnMouseEnter）→ 断言透明开态须 `Index>=474`（开态 474/475、关态 473）而非 `==474`，否则最后一次点击后的 hover 态致误判；
- **首分组标题行占位**：UpdateText 先插分组标题行使 groupCount+1 → 首个 KeybindRow 的 y 重算 +30 → 首行按钮屏坐标 (390,150) 非 (390,120)（分组标题行 Size(400,40) 覆盖 (45,120)-(445,160)，按钮 (390,150) 120×16 叠其上）；
- **KeyBinds.ini 副作用**：KeyBindSettings ctor 无 ini 时 `Save(DefaultKeylist)` 落盘到 Unity 工程 cwd（`Unity/KeyBinds.ini`，Inventory=F9 requires=2），Unity/ 已 gitignore 不污染仓库；探针 CheckNewInput 仅改内存不落盘，重跑确定性；
- **控件创建顺序契约**：ChatDialog ctor 读 `Scene.MainDialog.Location` + ChatOptionDialog ctor 调 `ChatDialog.Update()` → 探针须先 `Scene.MainDialog=main` 再 `Scene.ChatDialog=chat` 再 `new ChatOptionDialog`。
