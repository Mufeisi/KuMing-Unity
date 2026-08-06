using Client.MirControls;
using Client.MirScenes.Dialogs;
using Crystal.Client.Core.MirMath;
using S = ServerPackets;

namespace Client.MirObjects
{
    // 逐字移植（2026-08-06）：Client/MirObjects/HeroObject.cs
    // 他人视角的英雄对象（继承 PlayerObject）：OwnerName + OwnerLabel（"某人的英雄"名签）+
    // Load(S.ObjectHero) 复用 ObjectPlayer 加载 + ShouldDrawHealth 组队/本人可见规则。
    public class HeroObject : PlayerObject
    {
        public override ObjectType Race
        {
            get { return ObjectType.Hero; }
        }

        public string OwnerName;
        public MirLabel OwnerLabel;

        public override bool ShouldDrawHealth()
        {
            if (GroupDialog.GroupList.Contains(OwnerName) || OwnerName == User.Name)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public HeroObject(uint objectID) : base(objectID)
        {
        }

        public void Load(S.ObjectHero info)
        {
            Load((S.ObjectPlayer)info);
            OwnerName = info.OwnerName;

            if (info.ObjectID == Hero?.ObjectID)
                Hero.CurrentLocation = new Point(info.Location.X, info.Location.Y);
        }

        public override void CreateLabel()
        {
            base.CreateLabel();

            OwnerLabel = null;
            string ownerText = GameLanguage.ClientTextMap.GetLocalization((ClientTextKeys.OwnerHero), OwnerName);

            for (int i = 0; i < LabelList.Count; i++)
            {
                if (LabelList[i].Text != ownerText || LabelList[i].ForeColour != NameColour) continue;
                OwnerLabel = LabelList[i];
                break;
            }

            if (OwnerLabel != null && !OwnerLabel.IsDisposed) return;

            OwnerLabel = new MirLabel
            {
                AutoSize = true,
                BackColour = Color.Transparent,
                ForeColour = NameColour,
                OutLine = true,
                OutLineColour = Color.Black,
                Text = ownerText,
            };
            OwnerLabel.Disposing += (o, e) => LabelList.Remove(OwnerLabel);
            LabelList.Add(OwnerLabel);
        }

        public override void DrawName()
        {
            CreateLabel();

            if (NameLabel == null || OwnerLabel == null) return;

            NameLabel.Location = new Point(DisplayRectangle.X + (50 - NameLabel.Size.Width) / 2, DisplayRectangle.Y - (42 - NameLabel.Size.Height / 2) + (Dead ? 35 : 8));
            NameLabel.Draw();

            OwnerLabel.Location = new Point(DisplayRectangle.X + (50 - OwnerLabel.Size.Width) / 2, NameLabel.Location.Y + NameLabel.Size.Height - 1);
            OwnerLabel.Draw();
        }
    }
}
