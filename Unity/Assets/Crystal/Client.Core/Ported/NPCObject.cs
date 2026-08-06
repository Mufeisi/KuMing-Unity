using Client.MirControls;
using Client.MirGraphics;
using Client.MirScenes;
using Crystal.Client.Core.MirMath;
using S = ServerPackets;

namespace Client.MirObjects
{
    // NPCObject 移植（裁 Quest 系统）：Load→LoadLibrary→SetAction 渲染核心，Draw 用 DrawTinted。
    // Quest 图标系统裁剪（UpdateBestQuestIcon 空体，QuestIcon 恒 None）；Quests 列表保留契约（QuestInfoList 空）。
    public class NPCObject : MapObject
    {
        public override ObjectType Race
        {
            get { return ObjectType.Merchant; }
        }
        public override bool Blocking
        {
            get { return true; }
        }

        public FrameSet Frames;
        public Frame Frame;

        public long QuestTime;
        public int BaseIndex, FrameIndex, FrameInterval,
            EffectFrameIndex, EffectFrameInterval, QuestIndex;

        public ushort Image;
        public Color Colour = Color.White;

        public QuestIcon QuestIcon = QuestIcon.None;

        private bool _canChangeDir = true;

        public bool CanChangeDir
        {
            get { return _canChangeDir; }
            set
            {
                _canChangeDir = value;
                if (value == false) Direction = 0;
            }
        }

        public List<ClientQuestInfo> Quests;


        public NPCObject(uint objectID) : base(objectID)
        {
        }

        public void Load(S.ObjectNPC info)
        {
            Name = info.Name;
            NameColour = Color.FromArgb(info.NameColour.ToArgb());
            CurrentLocation = new Point(info.Location.X, info.Location.Y);
            Direction = info.Direction;
            Movement = CurrentLocation;
            MapLocation = CurrentLocation;
            GameScene.Scene.MapControl.AddObject(this);

            Quests = GameScene.QuestInfoList.Where(c => c.NPCIndex == ObjectID).ToList();

            Image = info.Image;
            Colour = Color.FromArgb(info.Colour.ToArgb());

            LoadLibrary();

            Frames = BodyLibrary.Frames ?? FrameSet.DefaultNPC;

            Light = 10;
            BaseIndex = 0;

            SetAction(true);
        }

        public void LoadLibrary()
        {
            if (Image < Libraries.NPCs.Length)
                BodyLibrary = Libraries.NPCs[Image];
            else if (Image >= 1000 && Image < 1100)
                BodyLibrary = Libraries.Flags[Image - 1000];
        }

        public override void Process()
        {
            bool update = CMain.Time >= NextMotion || GameScene.CanMove;

            ProcessFrames();

            if (update)
            {
                UpdateBestQuestIcon();
            }

            if (Frame == null)
            {
                DrawFrame = 0;
                DrawWingFrame = 0;
            }
            else
            {
                DrawFrame = Frame.Start + (Frame.OffSet * (byte)Direction) + FrameIndex;
                DrawWingFrame = Frame.EffectStart + (Frame.EffectOffSet * (byte)Direction) + EffectFrameIndex;
            }

            DrawY = CurrentLocation.Y;

            DrawLocation = new Point((Movement.X - User.Movement.X + MapControl.OffSetX) * MapControl.CellWidth, (Movement.Y - User.Movement.Y + MapControl.OffSetY) * MapControl.CellHeight);
            DrawLocation.Offset(User.OffSetMove);
            DrawLocation.Offset(GlobalDisplayLocationOffset);

            if (BodyLibrary != null)
                FinalDrawLocation = DrawLocation.Add(BodyLibrary.GetOffSet(DrawFrame));

            if (BodyLibrary != null && update)
            {
                FinalDrawLocation = DrawLocation.Add(BodyLibrary.GetOffSet(DrawFrame));
                DisplayRectangle = new Rectangle(DrawLocation, BodyLibrary.GetTrueSize(DrawFrame));
            }

            for (int i = 0; i < Effects.Count; i++)
                Effects[i].Process();

            Color colour = DrawColour;

            switch (Poison)
            {
                case PoisonType.None:
                    DrawColour = Color.White;
                    break;
                case PoisonType.Green:
                    DrawColour = Color.Green;
                    break;
                case PoisonType.Red:
                    DrawColour = Color.Red;
                    break;
                case PoisonType.Bleeding:
                    DrawColour = Color.DarkRed;
                    break;
                case PoisonType.Slow:
                    DrawColour = Color.Purple;
                    break;
                case PoisonType.Stun:
                case PoisonType.Dazed:
                    DrawColour = Color.Yellow;
                    break;
                case PoisonType.Blindness:
                    DrawColour = Color.MediumVioletRed;
                    break;
                case PoisonType.Frozen:
                    DrawColour = Color.Blue;
                    break;
                case PoisonType.Paralysis:
                case PoisonType.LRParalysis:
                    DrawColour = Color.Gray;
                    break;
            }

            if (colour != DrawColour) GameScene.Scene.MapControl.TextureValid = false;

            if (CMain.Time > QuestTime)
            {
                QuestTime = CMain.Time + 500;
                if (++QuestIndex > 1) QuestIndex = 0;
            }
        }
        public virtual void ProcessFrames()
        {
            if (Frame == null) return;

            switch (CurrentAction)
            {
                case MirAction.Standing:
                case MirAction.Harvest:
                    if (CMain.Time >= NextMotion)
                    {
                        GameScene.Scene.MapControl.TextureValid = false;

                        if (SkipFrames) UpdateFrame();

                        if (UpdateFrame() >= Frame.Count)
                        {
                            FrameIndex = Frame.Count - 1;
                            SetAction();
                        }
                        else
                        {
                            NextMotion += FrameInterval;
                        }
                    }

                    if (EffectFrameInterval > 0)
                        if (CMain.Time >= NextMotion2)
                        {
                            GameScene.Scene.MapControl.TextureValid = false;

                            if (SkipFrames) UpdateFrame2();

                            if (UpdateFrame2() >= Frame.EffectCount)
                                EffectFrameIndex = Frame.EffectCount - 1;
                            else
                                NextMotion2 += EffectFrameInterval;
                        }
                    break;

            }

        }
        public int UpdateFrame()
        {
            if (Frame == null) return 0;

            if (Frame.Reverse) return Math.Abs(--FrameIndex);

            return ++FrameIndex;
        }

        public int UpdateFrame2()
        {
            if (Frame == null) return 0;

            if (Frame.Reverse) return Math.Abs(--EffectFrameIndex);

            return ++EffectFrameIndex;
        }

        public virtual void SetAction(bool randomStartFrame = false)
        {
            if (ActionFeed.Count == 0)
            {
                if (CMain.Random.Next(2) == 0 && Frames.Count > 1)
                    CurrentAction = MirAction.Harvest;
                else
                    CurrentAction = MirAction.Standing;

                Frames.TryGetValue(CurrentAction, out Frame);

                if (randomStartFrame)
                {
                    var frameIndex = CMain.Random.Next(Frame.Count);

                    FrameIndex = frameIndex;
                    EffectFrameIndex = Math.Min(Frame.EffectCount, frameIndex);
                }
                else
                {
                    FrameIndex = 0;
                    EffectFrameIndex = 0;
                }

                if (MapLocation != CurrentLocation)
                {
                    GameScene.Scene.MapControl.RemoveObject(this);
                    MapLocation = CurrentLocation;
                    GameScene.Scene.MapControl.AddObject(this);
                }

                if (Frame == null) return;

                FrameInterval = Frame.Interval;
                EffectFrameInterval = Frame.EffectInterval;
            }
            else
            {
                QueuedAction action = ActionFeed[0];
                ActionFeed.RemoveAt(0);

                CurrentAction = action.Action;
                CurrentLocation = action.Location;

                FrameIndex = 0;
                EffectFrameIndex = 0;

                if (Frame == null) return;

                FrameInterval = Frame.Interval;
                EffectFrameInterval = Frame.EffectInterval;
            }

            NextMotion = CMain.Time + FrameInterval;
            NextMotion2 = CMain.Time + EffectFrameInterval;

            GameScene.Scene.MapControl.TextureValid = false;

        }
        public override void Draw()
        {
            if (BodyLibrary == null) return;

            BodyLibrary.DrawTinted(DrawFrame, DrawLocation, DrawColour, Colour, true);

            if (QuestIcon == QuestIcon.None) return;

            var offSet = BodyLibrary.GetOffSet(BaseIndex);
            var size = BodyLibrary.GetSize(BaseIndex);

            int imageIndex = 981 + ((int)QuestIcon * 2) + QuestIndex;

            Libraries.Prguse.Draw(imageIndex, DrawLocation.Add(offSet).Add(new Point(size.Width / 2 - 28, -40)), Color.White, false);
        }

        public override bool MouseOver(Point p)
        {
            return MapControl.MapLocation == CurrentLocation || BodyLibrary != null && BodyLibrary.VisiblePixel(DrawFrame, p.Subtract(FinalDrawLocation), false);
        }

        public override void DrawBehindEffects(bool effectsEnabled)
        {
        }

        public override void DrawEffects(bool effectsEnabled)
        {
            if (!effectsEnabled) return;

            if (BodyLibrary == null) return;

            if (DrawWingFrame > 0)
                BodyLibrary.DrawBlend(DrawWingFrame, DrawLocation, Color.White, true);
        }

        public override void DrawName()
        {
            if (!Name.Contains("_"))
            {
                base.DrawName();
                return;
            }

            string[] splitName = Name.Split('_');

            for (int s = 0; s < splitName.Length; s++)
            {
                CreateNPCLabel(splitName[s], s);
                TempLabel.Location = new Point(DisplayRectangle.X + (48 - TempLabel.Size.Width) / 2, DisplayRectangle.Y - (32 - TempLabel.Size.Height / 2) + (Dead ? 35 : 8) - (((splitName.Length - 1) * 10) / 2) + (s * 12));
                TempLabel.Draw();
            }
        }

        public void CreateNPCLabel(string word, int wordOrder)
        {
            TempLabel = null;
            var setForeColour = (wordOrder == 0 ? NameColour : Color.White);
            for (int i = 0; i < LabelList.Count; i++)
            {
                if (LabelList[i].Text != word || LabelList[i].ForeColour != setForeColour) continue;
                TempLabel = LabelList[i];
                break;
            }

            if (TempLabel != null && !TempLabel.IsDisposed) return;

            TempLabel = new MirLabel
            {
                AutoSize = true,
                BackColour = Color.Transparent,
                ForeColour = setForeColour,
                OutLine = true,
                OutLineColour = Color.Black,
                Text = word,
            };

            TempLabel.Disposing += (o, e) => LabelList.Remove(TempLabel);
            LabelList.Add(TempLabel);
        }

        // Quest 系统裁剪：QuestIcon 恒 None，Process 中 UpdateBestQuestIcon 空体。
        public void UpdateBestQuestIcon()
        {
        }
    }
}
