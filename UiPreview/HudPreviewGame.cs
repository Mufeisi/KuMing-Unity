using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace UiPreview;

internal sealed class HudPreviewGame : Game
{
    private const int CanvasWidth = 1600;
    private const int CanvasHeight = 900;

    private readonly GraphicsDeviceManager _graphics;
    private readonly string? _capturePath;
    private SpriteBatch _batch = null!;
    private Texture2D _pixel = null!;
    private Texture2D _world = null!;
    private Texture2D _redOrb = null!;
    private Texture2D _blueOrb = null!;
    private PixelFont _font = null!;
    private RenderTarget2D? _captureTarget;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private bool _showCharacter = true;
    private bool _showChat = true;
    private bool _captureFinished;
    private int _selectedSkill = 3;

    private readonly Rectangle[] _skillSlots = new Rectangle[10];

    public HudPreviewGame(string? capturePath)
    {
        _capturePath = capturePath;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = CanvasWidth,
            PreferredBackBufferHeight = CanvasHeight,
            SynchronizeWithVerticalRetrace = true,
        };

        Window.Title = "MonoGame Procedural HUD Preview";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        Window.AllowUserResizing = false;
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _font = new PixelFont(_pixel);

        _world = CreateWorldTexture(CanvasWidth, CanvasHeight);
        _redOrb = CreateOrbTexture(72, new Color(245, 70, 45), new Color(90, 8, 8));
        _blueOrb = CreateOrbTexture(72, new Color(40, 165, 255), new Color(7, 31, 93));

        for (int index = 0; index < _skillSlots.Length; index++)
        {
            _skillSlots[index] = new Rectangle(553 + index * 51, 748, 45, 45);
        }

        if (_capturePath is not null)
        {
            _captureTarget = new RenderTarget2D(
                GraphicsDevice,
                CanvasWidth,
                CanvasHeight,
                false,
                SurfaceFormat.Color,
                DepthFormat.None);
        }
    }

    protected override void UnloadContent()
    {
        _captureTarget?.Dispose();
        _redOrb?.Dispose();
        _blueOrb?.Dispose();
        _world?.Dispose();
        _pixel?.Dispose();
        _batch?.Dispose();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();

        if (Pressed(keyboard, Keys.Escape))
        {
            Exit();
        }

        if (Pressed(keyboard, Keys.C))
        {
            _showCharacter = !_showCharacter;
        }

        if (Pressed(keyboard, Keys.Tab))
        {
            _showChat = !_showChat;
        }

        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
        {
            Point cursor = new(mouse.X, mouse.Y);
            for (int index = 0; index < _skillSlots.Length; index++)
            {
                if (_skillSlots[index].Contains(cursor))
                {
                    _selectedSkill = index;
                    break;
                }
            }
        }

        _previousKeyboard = keyboard;
        _previousMouse = mouse;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_captureTarget is not null && !_captureFinished)
        {
            GraphicsDevice.SetRenderTarget(_captureTarget);
            RenderScene();
            GraphicsDevice.SetRenderTarget(null);

            _batch.Begin(samplerState: SamplerState.PointClamp);
            _batch.Draw(_captureTarget, new Rectangle(0, 0, CanvasWidth, CanvasHeight), Color.White);
            _batch.End();

            string? directory = Path.GetDirectoryName(_capturePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream output = File.Create(_capturePath!);
            _captureTarget.SaveAsPng(output, CanvasWidth, CanvasHeight);
            _captureFinished = true;
            Exit();
            return;
        }

        RenderScene();
        base.Draw(gameTime);
    }

    private bool Pressed(KeyboardState keyboard, Keys key)
    {
        return keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
    }

    private void RenderScene()
    {
        GraphicsDevice.Clear(new Color(3, 5, 9));
        _batch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
        _batch.Draw(_world, new Rectangle(0, 0, CanvasWidth, CanvasHeight), Color.White);

        DrawTopBroadcast();
        DrawQuestTracker();
        DrawMiniStatus();
        if (_showChat)
        {
            DrawChat();
        }

        DrawBottomHud();
        DrawUtilityButtons();

        if (_showCharacter)
        {
            DrawCharacterPanel();
        }

        DrawInteractionHints();
        _batch.End();
    }

    private void DrawTopBroadcast()
    {
        Rectangle bar = new(430, 10, 740, 40);
        Fill(bar, new Color(3, 8, 12, 225));
        Fill(new Rectangle(bar.X, bar.Bottom - 2, bar.Width, 2), new Color(126, 92, 26));
        Fill(new Rectangle(bar.X + 14, bar.Y + 7, 3, 26), new Color(210, 157, 50));
        _font.Draw(_batch, "FOLLOW THE HOST - RETURN TO BATTLE", new Vector2(493, 21), new Color(125, 238, 150), 2);
    }

    private void DrawQuestTracker()
    {
        Rectangle panel = new(18, 74, 310, 184);
        Panel(panel, new Color(2, 6, 11, 205), new Color(77, 100, 112), new Color(26, 36, 43));
        _font.Draw(_batch, "FIELD ORDERS", new Vector2(34, 88), new Color(217, 181, 93), 2);
        Fill(new Rectangle(34, 110, 235, 1), new Color(112, 88, 42));

        string[] lines =
        [
            "[ACTIVE] CLEAR THE RUINS",
            "  SHADOW BEASTS  18 / 25",
            "",
            "[DAILY] LOST SUPPLIES",
            "  RELICS FOUND    3 / 5",
            "",
            "[ZONE] ASHEN CAVERN",
            "  DEPTH  03",
        ];

        int y = 122;
        foreach (string line in lines)
        {
            Color color = line.StartsWith("[") ? new Color(110, 205, 156) : new Color(166, 178, 181);
            _font.Draw(_batch, line, new Vector2(34, y), color, 1);
            y += 15;
        }
    }

    private void DrawMiniStatus()
    {
        Rectangle frame = new(1370, 18, 210, 112);
        Panel(frame, new Color(1, 4, 7, 225), new Color(95, 78, 45), new Color(24, 28, 30));
        _font.Draw(_batch, "ASHEN CAVERN", new Vector2(1390, 32), new Color(221, 190, 110), 2);
        _font.Draw(_batch, "X 133  Y 209", new Vector2(1403, 58), new Color(170, 186, 190), 1);
        _font.Draw(_batch, "MODE: PEACE", new Vector2(1403, 78), new Color(111, 218, 160), 1);
        _font.Draw(_batch, "PING: 28 MS", new Vector2(1403, 97), new Color(120, 175, 205), 1);
        DrawDiamond(new Point(1389, 81), 7, new Color(212, 60, 40));
    }

    private void DrawChat()
    {
        Rectangle panel = new(18, 661, 490, 200);
        Panel(panel, new Color(2, 5, 9, 218), new Color(64, 85, 91), new Color(17, 25, 30));

        Fill(new Rectangle(18, 661, 490, 28), new Color(7, 13, 18, 238));
        string[] tabs = ["ALL", "TEAM", "GUILD", "SYSTEM"];
        int x = 33;
        foreach (string tab in tabs)
        {
            Color tabColor = tab == "ALL" ? new Color(226, 178, 77) : new Color(104, 124, 129);
            _font.Draw(_batch, tab, new Vector2(x, 671), tabColor, 1);
            x += 76;
        }

        DrawChatLine(704, "SYSTEM", "WELCOME TO THE ASHEN CAVERN.", new Color(209, 162, 70));
        DrawChatLine(724, "GUILD", "NIGHTFALL: REGROUP AT THE EAST GATE.", new Color(99, 189, 135));
        DrawChatLine(744, "TEAM", "ARIA: ELITE SPAWNED NEAR DEPTH 03.", new Color(115, 174, 218));
        DrawChatLine(764, "LOOT", "YOU FOUND ANCIENT IRON GAUNTLETS.", new Color(202, 123, 230));
        DrawChatLine(784, "WORLD", "BLACKSMITH EVENT STARTS IN 02:14.", new Color(207, 188, 137));

        Fill(new Rectangle(31, 821, 461, 26), new Color(0, 0, 0, 180));
        Border(new Rectangle(31, 821, 461, 26), 1, new Color(72, 91, 96));
        _font.Draw(_batch, "SAY >", new Vector2(41, 831), new Color(213, 181, 104), 1);
        _font.Draw(_batch, "PRESS ENTER TO CHAT", new Vector2(94, 831), new Color(93, 110, 116), 1);
    }

    private void DrawChatLine(int y, string channel, string message, Color channelColor)
    {
        _font.Draw(_batch, $"[{channel}]", new Vector2(32, y), channelColor, 1);
        _font.Draw(_batch, message, new Vector2(100, y), new Color(177, 184, 184), 1);
    }

    private void DrawBottomHud()
    {
        Rectangle rail = new(522, 730, 590, 165);
        Fill(rail, new Color(0, 2, 4, 210));
        Fill(new Rectangle(rail.X, rail.Y, rail.Width, 2), new Color(88, 70, 37));

        for (int index = 0; index < _skillSlots.Length; index++)
        {
            Rectangle slot = _skillSlots[index];
            bool selected = index == _selectedSkill;
            Color edge = selected ? new Color(240, 190, 72) : new Color(75, 87, 89);
            Fill(slot, new Color(8, 12, 16, 245));
            Border(slot, selected ? 2 : 1, edge);
            DrawSkillIcon(slot, index);

            string key = index == 9 ? "0" : (index + 1).ToString();
            Fill(new Rectangle(slot.X + 3, slot.Y + 3, 11, 11), new Color(0, 0, 0, 190));
            _font.Draw(_batch, key, new Vector2(slot.X + 5, slot.Y + 5), Color.White, 1);
        }

        _batch.Draw(_redOrb, new Rectangle(571, 778, 116, 116), Color.White);
        _batch.Draw(_blueOrb, new Rectangle(947, 778, 116, 116), Color.White);

        DrawOrbFrame(new Point(629, 836), new Color(180, 115, 45));
        DrawOrbFrame(new Point(1005, 836), new Color(93, 133, 168));

        Rectangle hp = new(682, 810, 267, 18);
        Bar(hp, 0.78f, new Color(178, 29, 27), new Color(70, 9, 12), new Color(226, 74, 51));
        _font.Draw(_batch, "HP  3872 / 4971", new Vector2(744, 816), new Color(240, 218, 196), 1);

        Rectangle mp = new(682, 835, 267, 18);
        Bar(mp, 0.65f, new Color(30, 101, 185), new Color(7, 32, 77), new Color(73, 174, 245));
        _font.Draw(_batch, "MP  945 / 1453", new Vector2(747, 841), new Color(211, 225, 236), 1);

        Rectangle xp = new(702, 866, 227, 8);
        Bar(xp, 0.42f, new Color(169, 83, 207), new Color(42, 12, 58), new Color(214, 127, 238));
        _font.Draw(_batch, "LV 42", new Vector2(792, 881), new Color(213, 180, 101), 1);
    }

    private void DrawSkillIcon(Rectangle slot, int index)
    {
        Color[] colors =
        [
            new(233, 92, 37), new(220, 151, 50), new(106, 91, 222), new(56, 145, 219),
            new(71, 201, 143), new(227, 83, 117), new(162, 92, 218), new(230, 185, 58),
            new(71, 178, 205), new(124, 190, 83),
        ];

        Point center = slot.Center;
        Color color = colors[index];
        DrawDiamond(center, 13, color);
        DrawDiamond(center, 7, Color.Lerp(color, Color.White, 0.35f));
        Fill(new Rectangle(center.X - 2, center.Y - 14, 4, 28), new Color(245, 236, 189, 170));
        Fill(new Rectangle(center.X - 14, center.Y - 2, 28, 4), new Color(245, 236, 189, 125));
    }

    private void DrawOrbFrame(Point center, Color color)
    {
        for (int radius = 62; radius <= 67; radius += 2)
        {
            DrawCircleOutline(center, radius, 2, Color.Lerp(color, new Color(42, 31, 20), (radius - 62) / 8f));
        }

        DrawDiamond(new Point(center.X, center.Y - 66), 7, color);
        DrawDiamond(new Point(center.X, center.Y + 66), 7, color);
    }

    private void DrawUtilityButtons()
    {
        Rectangle rail = new(1194, 847, 385, 34);
        Fill(rail, new Color(2, 5, 8, 220));
        Border(rail, 1, new Color(48, 63, 67));

        string[] labels = ["C", "B", "S", "Q", "G", "M", "F", "P", "T", "O"];
        for (int index = 0; index < labels.Length; index++)
        {
            Rectangle button = new(1204 + index * 36, 852, 29, 24);
            Fill(button, new Color(15, 21, 24));
            Border(button, 1, index == 0 && _showCharacter ? new Color(214, 169, 68) : new Color(65, 79, 81));
            _font.Draw(_batch, labels[index], new Vector2(button.X + 11, button.Y + 8), new Color(178, 187, 185), 1);
        }
    }

    private void DrawCharacterPanel()
    {
        Rectangle panel = new(1180, 154, 392, 665);
        Panel(panel, new Color(5, 8, 10, 247), new Color(148, 110, 50), new Color(31, 31, 28));
        Fill(new Rectangle(panel.X + 2, panel.Y + 2, panel.Width - 4, 46), new Color(20, 20, 18, 250));
        _font.Draw(_batch, "CHARACTER", new Vector2(1203, 172), new Color(229, 196, 112), 2);
        _font.Draw(_batch, "X", new Vector2(1543, 172), new Color(160, 101, 76), 2);

        Rectangle portrait = new(1312, 219, 128, 226);
        Fill(portrait, new Color(9, 13, 15));
        Border(portrait, 1, new Color(72, 68, 54));
        DrawCharacterSilhouette(portrait);

        for (int index = 0; index < 5; index++)
        {
            Rectangle leftSlot = new(1208, 218 + index * 52, 44, 44);
            Rectangle rightSlot = new(1498, 218 + index * 52, 44, 44);
            DrawEquipmentSlot(leftSlot, index);
            DrawEquipmentSlot(rightSlot, index + 5);
        }

        _font.Draw(_batch, "ARCADIAN", new Vector2(1304, 468), new Color(224, 180, 81), 2);
        _font.Draw(_batch, "BLADE WARDEN", new Vector2(1296, 492), new Color(117, 156, 165), 1);

        Fill(new Rectangle(1205, 517, 341, 1), new Color(93, 72, 39));
        DrawStatRow(535, "LEVEL", "42");
        DrawStatRow(559, "POWER", "1284");
        DrawStatRow(583, "DEFENSE", "764");
        DrawStatRow(607, "ACCURACY", "183");
        DrawStatRow(631, "EVASION", "96");
        DrawStatRow(655, "CRITICAL", "18 %");

        Fill(new Rectangle(1205, 688, 341, 1), new Color(62, 70, 67));
        _font.Draw(_batch, "COMBAT RATING", new Vector2(1210, 708), new Color(139, 152, 150), 1);
        _font.Draw(_batch, "7 846", new Vector2(1433, 700), new Color(232, 184, 74), 3);

        Rectangle closeButton = new(1305, 762, 140, 32);
        Fill(closeButton, new Color(43, 34, 21));
        Border(closeButton, 1, new Color(146, 105, 47));
        _font.Draw(_batch, "EQUIPMENT", new Vector2(1322, 773), new Color(218, 190, 126), 1);
    }

    private void DrawEquipmentSlot(Rectangle slot, int index)
    {
        Fill(slot, new Color(4, 7, 9));
        Border(slot, 1, new Color(75, 70, 54));
        Color rarity = (index % 3) switch
        {
            0 => new Color(186, 92, 223),
            1 => new Color(68, 132, 224),
            _ => new Color(212, 160, 63),
        };
        DrawDiamond(slot.Center, 12, new Color(rarity.R, rarity.G, rarity.B, (byte)190));
        DrawDiamond(slot.Center, 5, Color.Lerp(rarity, Color.White, 0.45f));
    }

    private void DrawCharacterSilhouette(Rectangle bounds)
    {
        Color shadow = new(47, 53, 54);
        Color armor = new(72, 76, 72);
        DrawCircle(new Point(bounds.Center.X, bounds.Y + 30), 18, new Color(116, 95, 70));
        DrawDiamond(new Point(bounds.Center.X, bounds.Y + 81), 47, armor);
        Fill(new Rectangle(bounds.Center.X - 24, bounds.Y + 103, 48, 75), new Color(63, 68, 66));
        Fill(new Rectangle(bounds.Center.X - 43, bounds.Y + 71, 20, 103), shadow);
        Fill(new Rectangle(bounds.Center.X + 23, bounds.Y + 71, 20, 103), shadow);
        Fill(new Rectangle(bounds.Center.X - 28, bounds.Y + 172, 23, 49), shadow);
        Fill(new Rectangle(bounds.Center.X + 5, bounds.Y + 172, 23, 49), shadow);
        DrawDiamond(new Point(bounds.Center.X, bounds.Y + 93), 12, new Color(206, 151, 55));
    }

    private void DrawStatRow(int y, string label, string value)
    {
        _font.Draw(_batch, label, new Vector2(1211, y), new Color(125, 142, 142), 1);
        _font.Draw(_batch, value, new Vector2(1457, y), new Color(213, 213, 196), 1);
        Fill(new Rectangle(1210, y + 17, 330, 1), new Color(25, 32, 33));
    }

    private void DrawInteractionHints()
    {
        Rectangle hint = new(640, 66, 320, 25);
        Fill(hint, new Color(0, 0, 0, 165));
        _font.Draw(_batch, "[C] CHARACTER   [TAB] CHAT", new Vector2(668, 75), new Color(103, 127, 129), 1);

        Point cursor = new(_previousMouse.X, _previousMouse.Y);
        for (int index = 0; index < _skillSlots.Length; index++)
        {
            if (!_skillSlots[index].Contains(cursor))
            {
                continue;
            }

            Rectangle tip = new(cursor.X + 16, cursor.Y - 56, 194, 46);
            Panel(tip, new Color(3, 6, 8, 245), new Color(147, 108, 47), new Color(19, 25, 27));
            _font.Draw(_batch, $"SKILL {index + 1}", new Vector2(tip.X + 10, tip.Y + 9), new Color(226, 187, 90), 1);
            _font.Draw(_batch, "CLICK TO EQUIP", new Vector2(tip.X + 10, tip.Y + 27), new Color(146, 162, 162), 1);
            break;
        }
    }

    private void Panel(Rectangle rectangle, Color fill, Color outer, Color inner)
    {
        Fill(rectangle, fill);
        Border(rectangle, 2, outer);
        Border(new Rectangle(rectangle.X + 4, rectangle.Y + 4, rectangle.Width - 8, rectangle.Height - 8), 1, inner);
        Fill(new Rectangle(rectangle.X + 8, rectangle.Y + 8, 16, 2), outer);
        Fill(new Rectangle(rectangle.Right - 24, rectangle.Y + 8, 16, 2), outer);
        Fill(new Rectangle(rectangle.X + 8, rectangle.Bottom - 10, 16, 2), outer);
        Fill(new Rectangle(rectangle.Right - 24, rectangle.Bottom - 10, 16, 2), outer);
    }

    private void Bar(Rectangle bounds, float amount, Color fillColor, Color emptyColor, Color highlight)
    {
        Fill(bounds, new Color(1, 2, 3));
        Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), emptyColor);
        int width = (int)((bounds.Width - 4) * MathHelper.Clamp(amount, 0f, 1f));
        Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, width, bounds.Height - 4), fillColor);
        Fill(new Rectangle(bounds.X + 3, bounds.Y + 3, Math.Max(0, width - 2), 2), highlight);
        Border(bounds, 1, new Color(87, 76, 58));
    }

    private void Fill(Rectangle rectangle, Color color)
    {
        if (rectangle.Width > 0 && rectangle.Height > 0)
        {
            _batch.Draw(_pixel, rectangle, color);
        }
    }

    private void Border(Rectangle rectangle, int thickness, Color color)
    {
        Fill(new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, thickness), color);
        Fill(new Rectangle(rectangle.X, rectangle.Bottom - thickness, rectangle.Width, thickness), color);
        Fill(new Rectangle(rectangle.X, rectangle.Y, thickness, rectangle.Height), color);
        Fill(new Rectangle(rectangle.Right - thickness, rectangle.Y, thickness, rectangle.Height), color);
    }

    private void DrawDiamond(Point center, int radius, Color color)
    {
        for (int y = -radius; y <= radius; y++)
        {
            int halfWidth = radius - Math.Abs(y);
            Fill(new Rectangle(center.X - halfWidth, center.Y + y, halfWidth * 2 + 1, 1), color);
        }
    }

    private void DrawCircle(Point center, int radius, Color color)
    {
        for (int y = -radius; y <= radius; y++)
        {
            int halfWidth = (int)Math.Sqrt(radius * radius - y * y);
            Fill(new Rectangle(center.X - halfWidth, center.Y + y, halfWidth * 2 + 1, 1), color);
        }
    }

    private void DrawCircleOutline(Point center, int radius, int thickness, Color color)
    {
        const int segments = 80;
        Vector2 previous = new(center.X + radius, center.Y);
        for (int index = 1; index <= segments; index++)
        {
            float angle = MathHelper.TwoPi * index / segments;
            Vector2 current = new(center.X + MathF.Cos(angle) * radius, center.Y + MathF.Sin(angle) * radius);
            DrawLine(previous, current, thickness, color);
            previous = current;
        }
    }

    private void DrawLine(Vector2 start, Vector2 end, int thickness, Color color)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        float rotation = MathF.Atan2(delta.Y, delta.X);
        _batch.Draw(_pixel, start, null, color, rotation, Vector2.Zero, new Vector2(length, thickness), SpriteEffects.None, 0f);
    }

    private Texture2D CreateWorldTexture(int width, int height)
    {
        Texture2D texture = new(GraphicsDevice, width, height);
        Color[] data = new Color[width * height];
        Random random = new(7127);
        Vector2 lightCenter = new(760, 410);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 offset = new(x - lightCenter.X, (y - lightCenter.Y) * 1.18f);
                float distance = offset.Length();
                float light = MathHelper.Clamp(1f - distance / 650f, 0f, 1f);
                float path = MathF.Exp(-MathF.Pow((x - (750 + MathF.Sin(y * 0.011f) * 160)) / 260f, 2));
                float noise = (float)random.NextDouble() * 0.08f;
                float value = 0.035f + light * 0.13f + path * light * 0.14f + noise;
                data[y * width + x] = new Color(
                    (byte)(value * 92),
                    (byte)(value * 99),
                    (byte)(value * 105));
            }
        }

        texture.SetData(data);
        return texture;
    }

    private Texture2D CreateOrbTexture(int radius, Color top, Color bottom)
    {
        int size = radius * 2;
        Texture2D texture = new(GraphicsDevice, size, size);
        Color[] data = new Color[size * size];
        Vector2 center = new(radius - 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) / radius;
                if (distance > 1f)
                {
                    data[y * size + x] = Color.Transparent;
                    continue;
                }

                float vertical = y / (float)(size - 1);
                Color liquid = Color.Lerp(top, bottom, vertical);
                float edge = MathHelper.Clamp((1f - distance) * 5f, 0f, 1f);
                float gleam = MathHelper.Clamp(1f - Vector2.Distance(new Vector2(x, y), new Vector2(radius * 0.68f, radius * 0.52f)) / (radius * 0.42f), 0f, 1f);
                data[y * size + x] = Color.Lerp(new Color(12, 10, 9), Color.Lerp(liquid, Color.White, gleam * 0.27f), edge);
            }
        }

        texture.SetData(data);
        return texture;
    }
}
