# 运行时快照（G0 基线）

> 服务器/客户端运行时数据的固定状态。记录于 2026-08-04。
> 构建产物哈希见 `docs/build-artifact-hashes.txt`。

## 服务器运行时（Build/Server/Debug/）

| 项 | 状态 |
| --- | --- |
| Configs/ | 27 个 INI（BaseStats*/ExpList/Awakening/Fishing/Gem/Goods/Hero*/Mail/Marriage/Mentor/Mines/MonsterRarity/OrbsExpList/RandomItemStats/RefineSystem/Setup/WorldMap），见下表哈希 |
| Envir/ | 11 个占位脚本（Drops 5 + NPCs 3 + DisabledChars/LineMessage/Notice），均为出厂默认 |
| Maps/ | **空**（无 .map） |
| Server.MirDB | 240 字节空库 |
| Server.MirADB | 不存在（无账号） |
| 测试角色/装备 | 无 |

## 客户端运行时（Build/Client/Debug/）

| 项 | 状态 |
| --- | --- |
| Data/ | **空**（无 .lib 图片库） |
| Map/ | **空** |
| Sound/ | **空** |
| Localization/ | Chinese.json + English.json |
| KeyBinds.ini / Mir2Test.ini | 出厂默认 |

## Configs 哈希（SHA256 前 16 字节，`Build/Server/Debug/Configs/`）

```
ebbeab5c326ad0d1  AwakeningSystem.ini
5ad3745137aa8f7d  BaseStatsArcher.ini        (同 HeroBaseStatsArcher)
fe43eaa62b4b30b4  BaseStatsAssassin.ini      (同 HeroBaseStatsAssassin)
ca21c50cee91f9f7  BaseStatsTaoist.ini        (同 HeroBaseStatsTaoist)
cb8b9d58cb40ed2f  BaseStatsWarrior.ini       (同 HeroBaseStatsWarrior)
b463f5b05cb9dee3  BaseStatsWizard.ini        (同 HeroBaseStatsWizard)
89d7d788b50f0eee  ExpList.ini               (同 HeroExpList)
4cde4e3f3ae2595b  FishingSystem.ini
d65041c21db5138a  GemSystem.ini
2028189b5cc1bd5f  GoodsSystem.ini
0d61eb5588740ae2  HeroSettings.ini
d91d67d67dc96c82  MailSystem.ini
b3f8f02eb81deaa9  MarriageSystem.ini
97ad51b4617561f3  MentorSystem.ini
b41bf73f8e93f916  Mines.ini
d054142b76d81d8f  MonsterRarity.ini
4383e487138a61e1  OrbsExpList.ini
df0f3a29a9b0e981  RandomItemStats.ini
35e2d5977844919c  RefineSystem.ini
44627cf47b0d6a4c  Setup.ini
7f87b3a846a9a011  WorldMap.ini
```

## 结论

- 可复现构建：✅ 已固定（build.ps1 + global.json + 产物哈希）
- 服务器可启动性：待验证（需先准备 .map 与账号数据）
- **画面/封包基线：未录制**，被客户端资源缺失阻塞（见 `docs/backlog.md`）
