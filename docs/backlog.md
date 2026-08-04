# Backlog（范围门禁登记）

> 简报外"无关但重要"的问题登记处。格式：`问题 + 文件 + 建议`。
> 主会话维护。

## 阻塞项（G0 发现，影响基线录制/真机验收）

- [ ] **游戏客户端资源缺失** — `Build/Client/Debug/Data/` 为空（0 个 .lib/.wzl/.wil），`Map/`、`Sound/` 均为空。客户端没有任何可渲染素材。
  - 建议：从 LOMCN / 原版资源包获取 `.Lib` 图片库、`.map` 地图、音频，落到固定资源快照目录，并记录哈希。
- [ ] **服务器内容为空** — `Build/Server/Debug/Maps/` 空；`Envir/NPCs` 仅 11 个占位脚本；`Server.MirDB` 是 240 字节空库；无测试账号/角色/物品。
  - 建议：准备 3 个测试角色 + 5 张代表地图 + 基础装备，跑通服务器并固定快照。
- [ ] **旧客户端 30 分钟基线无法录制** — 依赖上面两项。截图/帧时间/内存/网络 trace 都需要真实资源和可进图的服务器。
  - 建议：资源就位后按 PRD 第 0 阶段录制，产出 golden-shots 与封包 trace。

## 低优先级

- [ ] **PatcherWebSite 遗留网站项目**（.NET Framework 4.8）阻塞解决方案级 `dotnet build`（MSB4249）— `Legend of Mir.sln`。
  - 建议：与三端迁移无关；要么从解决方案卸载，要么文档注明仅 VS2022 全 MSBuild 可构建。
- [ ] `docs/build-artifact-hashes.txt` 首字节带 UTF-8 BOM（PowerShell `Set-Content`），不影响使用。
