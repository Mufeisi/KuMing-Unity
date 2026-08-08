using System.Collections.Generic;
using System.IO;
using Client;

namespace Crystal.Rendering.Editor
{
    // 探针中文语言包：LoadClientLanguage 的键数校验要求语言 JSON 与 ClientTextMap 全量键数一致，
    // 否则会把文件覆写回英文默认。因此首先生成"完整映射"中文包（英文基底 + 本类可见键中文覆盖），
    // 此后直接加载。覆盖 net-settings 探针三件套（HelpDialog 页标题 / KeyboardLayoutDialog 绑定名
    // / ChatOptionDialog）实际显示的 ClientTextKeys。
    public static class ProbeLang
    {
        const string JsonPath = "Localization/Chinese.json";
        static bool _loaded;

        public static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;
            if (!File.Exists(JsonPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                GameLanguage.SaveClientLanguage(JsonPath);
                ApplyOverrides();
                GameLanguage.SaveClientLanguage(JsonPath);
            }
            GameLanguage.LoadClientLanguage(JsonPath);
            // KeyBindSettings 在 CMain 静态初始化（探针最早处）即用英文 Description 建表；
            // 语言包加载后重建以拾取中文（仅刷新 Description，键位/结构不变）。
            CMain.InputKeys.Keylist.Clear();
            CMain.InputKeys.DefaultKeylist.Clear();
            CMain.InputKeys.New(CMain.InputKeys.Keylist);
            CMain.InputKeys.New(CMain.InputKeys.DefaultKeylist);
        }

        static void ApplyOverrides()
        {
            foreach (var kv in Map)
                if (GameLanguage.ClientTextMap.Text.ContainsKey(kv.Key.ToString()))
                    GameLanguage.ClientTextMap.Text[kv.Key.ToString()] = kv.Value;
        }

        static readonly Dictionary<ClientTextKeys, string> Map = new Dictionary<ClientTextKeys, string>
        {
            // HelpDialog 页标题。
            { ClientTextKeys.ShortcutInformation, "快捷键信息" },
            { ClientTextKeys.ChatShortcuts, "聊天快捷键" },
            { ClientTextKeys.Movements, "移动" },
            { ClientTextKeys.Attacking, "攻击" },
            { ClientTextKeys.CollectingItems, "拾取物品" },
            { ClientTextKeys.Health, "生命" },
            { ClientTextKeys.Skills, "技能" },
            { ClientTextKeys.Mana, "魔法" },
            { ClientTextKeys.Chatting, "聊天" },
            { ClientTextKeys.Groups, "组队" },
            { ClientTextKeys.Durability, "持久：" },
            { ClientTextKeys.Purchasing, "购买" },
            { ClientTextKeys.Selling, "出售" },
            { ClientTextKeys.Repairing, "修理" },
            { ClientTextKeys.Trading, "交易" },
            { ClientTextKeys.Inspecting, "查看" },
            { ClientTextKeys.Statistics, "属性" },
            { ClientTextKeys.Quests, "任务" },
            { ClientTextKeys.Fishing, "钓鱼" },
            { ClientTextKeys.Heroes, "英雄" },
            { ClientTextKeys.Mounts, "坐骑" },
            { ClientTextKeys.Shortcuts, "快捷键" },
            { ClientTextKeys.Awakening, "觉醒" },
            { ClientTextKeys.GemsAndOrbs, "宝石与宝珠" },
            { ClientTextKeys.GuildBuffs, "公会增益" },
            { ClientTextKeys.Information, "信息" },

            // KeyboardLayoutDialog 标题/按钮。
            { ClientTextKeys.KeyboardSettings, "键盘设置" },
            { ClientTextKeys.KeyboardSettingsResetDefault, "键盘设置已重置为默认值。" },
            { ClientTextKeys.AssignRuleStrict, "分配规则：严格" },
            { ClientTextKeys.AssignRuleRelaxed, "分配规则：宽松" },

            // KeyBindSettings 绑定描述（对话框开/关 + 功能键）。
            { ClientTextKeys.InventoryOpenClose, "背包开/关" },
            { ClientTextKeys.InventoryOpenCloseAlt, "背包开/关(备用)" },
            { ClientTextKeys.InventoryWindowOpenClose, "背包窗口(开/关)" },
            { ClientTextKeys.EquipmentOpenClose, "装备开/关" },
            { ClientTextKeys.EquipmentOpenCloseAlt, "装备开/关(备用)" },
            { ClientTextKeys.SkillsOpenClose, "技能开/关" },
            { ClientTextKeys.SkillsOpenCloseAlt, "技能开/关(备用)" },
            { ClientTextKeys.SkillWindowOpenClose, "技能窗口(开/关)" },
            { ClientTextKeys.HeroInventoryOpenClose, "英雄背包开/关" },
            { ClientTextKeys.HeroEquipmentOpenClose, "英雄装备开/关" },
            { ClientTextKeys.HeroSkillsOpenClose, "英雄技能开/关" },
            { ClientTextKeys.CreaturesOpenClose, "宠物窗口开/关" },
            { ClientTextKeys.MountOpenClose, "坐骑开/关" },
            { ClientTextKeys.FishingOpenClose, "钓鱼开/关" },
            { ClientTextKeys.OpenCloseFishingWindow, "打开/关闭钓鱼窗口" },
            { ClientTextKeys.SkillbarOpenClose, "技能栏开/关" },
            { ClientTextKeys.MentorOpenClose, "师徒开/关" },
            { ClientTextKeys.FriendsOpenClose, "好友开/关" },
            { ClientTextKeys.FriendWindowOpenClose, "好友窗口(开/关)" },
            { ClientTextKeys.GroupOpenClose, "组队开/关" },
            { ClientTextKeys.GroupWindowOpenClose, "组队窗口(开/关)" },
            { ClientTextKeys.GuildOpenClose, "公会开/关" },
            { ClientTextKeys.GuildWindowOpenClose, "公会窗口(开/关)" },
            { ClientTextKeys.GameshopOpenClose, "商城开/关" },
            { ClientTextKeys.GameshopWindowOpenClose, "商城窗口(开/关)" },
            { ClientTextKeys.HelpOpenClose, "帮助开/关" },
            { ClientTextKeys.HelpWindowOpenClose, "帮助窗口(开/关)" },
            { ClientTextKeys.MinimapOpenClose, "小地图开/关" },
            { ClientTextKeys.MinimapWindowOpenClose, "小地图窗口(开/关)" },
            { ClientTextKeys.BigmapOpenClose, "大地图开/关" },
            { ClientTextKeys.KeybindsOpenClose, "按键绑定开/关" },
            { ClientTextKeys.OptionsOpenClose, "选项开/关" },
            { ClientTextKeys.OptionsOpenCloseAlt, "选项开/关(备用)" },
            { ClientTextKeys.OptionWindowOpenClose, "选项窗口(开/关)" },
            { ClientTextKeys.RankingOpenClose, "排行榜开/关" },
            { ClientTextKeys.QuestDiaryOpenClose, "任务日志开/关" },
            { ClientTextKeys.RelationshipOpenClose, "关系窗口开/关" },
            { ClientTextKeys.EngagementWindowOpenClose, "婚约窗口(开/关)" },
            { ClientTextKeys.StatusWindowOpenClose, "状态窗口(开/关)" },
            { ClientTextKeys.TradeWindowOpenClose, "交易窗口(开/关)" },
            { ClientTextKeys.BeltOpenClose, "腰带开/关" },
            { ClientTextKeys.BeltWindowOpenClose, "腰带窗口(开/关)" },
            { ClientTextKeys.BeltSlot, "腰带栏位 {0}" },
            { ClientTextKeys.BeltSlotAlt, "腰带栏位 {0} 备用" },
            { ClientTextKeys.SkillbarSlot, "技能栏栏位" },
            { ClientTextKeys.SkillbarAltSlot, "技能栏备用栏位" },
            { ClientTextKeys.HeroSkillbarSlot, "英雄技能栏栏位" },

            // 功能键描述。
            { ClientTextKeys.ExitGame, "退出游戏" },
            { ClientTextKeys.Exit, "退出" },
            { ClientTextKeys.Logout, "退出登录" },
            { ClientTextKeys.LogOut, "退出登录" },
            { ClientTextKeys.CloseAllWindows, "关闭所有窗口" },
            { ClientTextKeys.AutoRunOnOff, "自动跑步开/关" },
            { ClientTextKeys.ToggleAutorun, "切换自动跑步" },
            { ClientTextKeys.RotateBelt, "旋转腰带" },
            { ClientTextKeys.RequestTrade, "请求交易" },
            { ClientTextKeys.RecruitGroupMember, "招募组队成员" },
            { ClientTextKeys.PickupFloorItem, "拾取地面物品" },
            { ClientTextKeys.MountDismount, "上马/下马" },
            { ClientTextKeys.MountDismountRide, "骑乘/下骑" },
            { ClientTextKeys.ShowFieldMap, "显示地图" },
            { ClientTextKeys.ShowHideInterface, "显示/隐藏界面" },
            { ClientTextKeys.ShowSkillBar, "显示技能栏" },
            { ClientTextKeys.ShowOtherPlayersKits, "显示其他玩家装备" },
            { ClientTextKeys.HighlightPickupItems, "高亮/拾取物品" },
            { ClientTextKeys.TakeScreenshot, "截屏" },
            { ClientTextKeys.ScreenCapture, "截屏" },
            { ClientTextKeys.CtrlRightClick, "Ctrl + 鼠标右键" },
            { ClientTextKeys.CommandWhisperOthers, "向其他玩家私聊命令" },
            { ClientTextKeys.CommandShoutNearby, "向附近玩家喊话命令" },
            { ClientTextKeys.CommandGuildChat, "公会聊天命令" },
            { ClientTextKeys.LockSpellOnTargetNotCursor, "锁定法术到目标而非光标位置" },
            { ClientTextKeys.HoldEnableTargetSpellLockOn, "按住以启用目标法术锁定" },
            { ClientTextKeys.CreatureAutoPickup, "宠物自动拾取" },
            { ClientTextKeys.CreatureItemPickup, "宠物拾取物品" },
            { ClientTextKeys.CreaturePickupSingleMouseTarget, "宠物拾取(单点鼠标目标)" },
            { ClientTextKeys.CreaturePickupMultiMouseTarget, "宠物拾取(多点鼠标目标)" },
            { ClientTextKeys.SkillButtons, "技能按钮" },

            // 攻击/宠物模式。
            { ClientTextKeys.ToggleAttackMode, "切换攻击模式" },
            { ClientTextKeys.SetAttackModeAll, "设置攻击模式：全部" },
            { ClientTextKeys.SetAttackModePeace, "设置攻击模式：和平" },
            { ClientTextKeys.SetAttackModeGroup, "设置攻击模式：组队" },
            { ClientTextKeys.SetAttackModeGuild, "设置攻击模式：公会" },
            { ClientTextKeys.SetAttackModeEnemyGuild, "设置攻击模式：敌对方公会" },
            { ClientTextKeys.SetAttackModeRedBrown, "设置攻击模式：红名/褐名" },
            { ClientTextKeys.TogglePlayerAttackMode, "切换玩家攻击模式" },
            { ClientTextKeys.PeaceModeAttackMonstersOnly, "和平模式：仅攻击怪物" },
            { ClientTextKeys.GroupModeAttackExceptMembers, "组队模式：攻击除队友外所有目标" },
            { ClientTextKeys.GuildModeAttackExceptMembers, "公会模式：攻击除公会成员外所有目标" },
            { ClientTextKeys.GoodEvilModeAttackPKAndMonsters, "正邪模式：攻击 PK 玩家与怪物" },
            { ClientTextKeys.AllAttackModeAllSubjects, "全部模式：攻击所有目标" },
            { ClientTextKeys.TogglePetMode, "切换宠物模式" },
            { ClientTextKeys.TogglePetAttackPet, "切换宠物攻击" },
            { ClientTextKeys.SetPetModeBoth, "设置宠物模式：攻击与移动" },
            { ClientTextKeys.SetPetModeMoveOnly, "设置宠物模式：仅移动" },
            { ClientTextKeys.SetPetModeAttackOnly, "设置宠物模式：仅攻击" },
            { ClientTextKeys.SetPetModeNone, "设置宠物模式：无" },
            { ClientTextKeys.SetPetModeFocusMasterTarget, "设置宠物模式：专注主人目标" },
            { ClientTextKeys.ToggleCameraMode, "切换视角模式" },
            { ClientTextKeys.ToggleDropview, "切换掉落显示" },

            // 交易（8-7-1）：TradeRequest 弹窗标题 / MirAmountBox 金币输入标题（探针渲染断言文本）。
            { ClientTextKeys.PlayerRequestedTrade, "玩家 {0} 要求与你交易" },
            { ClientTextKeys.TradeAmount, "交易金额:" },

            // 邮件（8-7-2）：发信/寄包裹 MirInputBox 收件人提示、寄包裹金币 MirAmountBox 标题、
            // ParcelCollected 领取结果提示（探针渲染断言文本）。
            { ClientTextKeys.EnterMailToName, "请输入收件人姓名。" },
            { ClientTextKeys.SendAmount, "发送金额:" },
            { ClientTextKeys.NoParcelsToCollect, "没有可领取的包裹。" },
            { ClientTextKeys.AllParcelsCollected, "所有包裹已领取。" },
        };
    }
}
