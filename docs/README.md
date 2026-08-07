# docs 文档索引

> 主会话维护。每个新任务会话开工前先读 `three-platform-migration-plan.md` 的当前任务 + `migration-status.md`。

| 文档 | 角色 | 状态 |
| --- | --- | --- |
| `three-platform-migration-plan.md` | **权威计划（Unity 三端）**：阶段/任务/状态/验收 | ✅ 现行 |
| `migration-status.md` | 已完成工作流水账 + 踩坑记录 + 当前任务 | ✅ 现行 |
| `compat-matrix.md` | PC 功能验收清单（🟩/🟨） | ✅ 现行 |
| `backlog.md` | 范围门禁登记（简报外问题） | ✅ 现行 |
| `runtime-snapshot.md` | G0 运行时基线快照（2026-08-04） | 🗄 历史快照 |
| `golden-shots/` | 黄金截图目录（验收路径已改探针，暂无内容） | 🗄 空 |
| `monogame-client-migration-prd.md` | 旧 MonoGame PRD | ⚠️ 废弃（指标/风险仍可参考） |
| `monogame-client-migration-solo-plan.md` | 旧 MonoGame 单人计划 | ⚠️ 废弃 |

**文档规则：**
- 计划与状态以 `three-platform-migration-plan.md` + `migration-status.md` 为准，两者冲突时以计划为准并修正状态。
- 新任务开工：读计划「当前进行中任务」→ 读 migration-status 对应文件所有权 → 写任务简报。
- 任务完成：更新 migration-status + 勾选计划任务状态 + 一 commit。
