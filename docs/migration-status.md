# 迁移状态（权威共享文件）

> 主会话唯一维护。每个新任务会话开工前先读本文件，确认文件所有权与已确立模式，避免重做/打架。
> 更新规则：任务完成后由主会话更新本文件；进行中条目由对应任务会话写入。

## 当前阶段

**G0 可重复基线（进行中）**

## 已确立的模式 / 决策（ADR 摘要）

| 决策 | 内容 |
| --- | --- |
| 客户端引擎 | Unity 6 LTS + 内置渲染管线 + uGUI，不引 DOTS |
| 服务端 | 保留 C#，网络层现代化（SocketAsyncEventArgs + 每连接 SPSC 队列 + 主循环预算） |
| Shared 共享 | `Shared.csproj` multi-target `netstandard2.1;net8.0`，Unity 引 netstandard2.1 产物 |
| 构建入口 | `build.ps1`（跳过后台遗留网站项目 PatcherWebSite，它需 VS2022 全 MSBuild） |
| SDK 固定 | `global.json` 锁 8.0.418 |
| 加密 | 双端口 + 滚动 XOR 混淆（一期），明文端口过渡保留一个版本 |
| 运营后台 | 保留 WinForms SMain 编辑器 + 进程内嵌 Web 面板 |
| 迁移方法论 | 沿用 `docs/monogame-client-migration-prd.md` 的绞杀式 + Gate 门禁 + 回放/黄金截图验证 |

## 文件所有权（任务分配/进行中/已完成）

| 区域 | 文件/模块 | 状态 | 属主任务 |
| --- | --- | --- | --- |
| 协议 | `Shared/` 全部 | 未迁移（可原样移植） | — |
| 地图解析 | `Client/MirObjects/MapCode.cs` | 未迁移（可原样移植） | — |
| 渲染门面 | `Client/MirGraphics/MLibrary.cs`、`DXManager.cs` | 未迁移（需重写 GPU 面） | — |
| 对象模型 | `Client/MirObjects/*` | 未迁移（可原样移植，去平台类型） | — |
| 场景 | `Client/MirScenes/GameScene.cs`（12.5k 行） | 未迁移（需先拆分） | — |
| UI | `Client/MirControls/`、`MirScenes/Dialogs/` | 未迁移（需 uGUI 化） | — |
| 网络(客户端) | `Client/MirNetwork/Network.cs` | 未迁移 | — |
| 网络(服务端) | `Server/MirNetwork/MirConnection.cs` | 未迁移（需 APM→SAEA） | — |
| 主循环 | `Server/MirEnvir/Envir.cs` | 未迁移 | — |
| 音频 | `Client/MirSounds/` | 未迁移 | — |
| 输入 | `Client/KeyBindSettings.cs` | 未迁移 | — |

## 基线快照（G0 已记录）

- 构建产物哈希：`docs/build-artifact-hashes.txt`（2026-08-04 Debug）
- 运行时数据状态：见 `docs/backlog.md` 的「资源缺失」条目
- 游戏画面/封包基线：**未录制**（见 `docs/backlog.md` 阻塞项）
