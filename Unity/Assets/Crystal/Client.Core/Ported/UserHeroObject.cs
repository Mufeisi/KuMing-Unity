using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Crystal.Client.Core.MirMath;
using S = ServerPackets;

namespace Client.MirObjects
{
    // 逐字移植（2026-08-06）：Client/MirObjects/UserHeroObject.cs
    // 玩家自己的英雄对象（继承 UserObject）：自动喝药开关/百分比 + HP/MP 快捷物品槽 +
    // Buff 对话框路由 + Load(S.UserInformation) 玩家状态核心复用。
    public class UserHeroObject : UserObject
    {
        public bool AutoPot;
        public uint AutoHPPercent;
        public uint AutoMPPercent;

        public UserItem[] HPItem = new UserItem[1];
        public UserItem[] MPItem = new UserItem[1];
        public override BuffDialog GetBuffDialog => GameScene.Scene.HeroBuffsDialog;
        public UserHeroObject(uint objectID)
        {
            ObjectID = objectID;
            Stats = new Stats();
            Frames = FrameSet.Player;
        }

        public override void Load(S.UserInformation info)
        {
            Name = info.Name;
            NameColour = Color.FromArgb(info.NameColour.ToArgb());
            Class = info.Class;
            Gender = info.Gender;
            Level = info.Level;
            Hair = info.Hair;

            HP = info.HP;
            MP = info.MP;

            Experience = info.Experience;
            MaxExperience = info.MaxExperience;

            Inventory = info.Inventory;
            Equipment = info.Equipment;

            Magics = info.Magics;
            for (int i = 0; i < Magics.Count; i++)
            {
                Magics[i].CastTime += CMain.Time;
            }

            BindAllItems();
        }
    }
}
