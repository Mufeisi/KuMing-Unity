using System;
using Client;
using Crystal.Client.Rendering;
using UnityEditor;
using UnityEngine;

namespace Crystal.Rendering.Editor
{
    // P2 分辨率缩放统一（对照 sanduan SizeRatio）确定性验证：
    //   sanduan 的 SizeRatio 黑边缩放是死代码（恒 1.0 从未赋值、OnRenderObject 缩放矩阵被注释）；
    //   本项目原生 1:1 渲染 + 边缘锚点自适应布局，无逻辑分辨率 → 不引入黑边（决策见 docs/sanduan-extraction.md C2）。
    //   本探针钉死坐标契约：①ScreenMetrics 单一扇出（渲染真值→触摸翻转基准+对话框布局，早退 no-op）；
    //   ②ToUi 纯镜像 y 翻转（屏角精确对屏角=无缩放无黑边、中点不动=ratio 1、换分辨率基准更新）；
    //   ③HUD/背包边缘锚点布局随屏重算；④MinTouchSize 触控尺寸下限。batchmode：-executeMethod ...Run -quit
    //   全过输出 [resolutionverify] PASS exit 0。
    static class ResolutionVerify
    {
        static int _fail;

        static void Check(bool cond, string name)
        {
            if (cond) { Console.WriteLine($"  ok  {name}"); }
            else { _fail++; Console.WriteLine($"  FAIL {name}"); }
        }

        public static void Run()
        {
            int cases = 0;

            // 1. ScreenMetrics 扇出：渲染真值 → 触摸翻转基准 + 对话框布局
            cases++;
            ScreenMetrics.Set(2400, 1080);
            Check(ScreenMetrics.W == 2400 && ScreenMetrics.H == 1080
                  && MobileUiAdapter.ScreenW == 2400 && MobileUiAdapter.ScreenH == 1080
                  && Settings.ScreenWidth == 2400 && Settings.ScreenHeight == 1080,
                  "screen set fans out to adapter + settings");

            // 2. 早退 no-op：值未变不重复扇出
            cases++;
            ScreenMetrics.Set(2400, 1080);
            Check(MobileUiAdapter.ScreenW == 2400 && Settings.ScreenWidth == 2400, "unchanged set is no-op");

            // 3-6. ToUi 纯镜像（无缩放/无黑边）：屏角精确对屏角、中点不动、换分辨率基准更新
            cases++;
            Check(MobileUiAdapter.ToUi(new Vector2(2400, 0)) == new Vector2(2400, 1080), "toUi bottom-left -> top-left");
            cases++;
            Check(MobileUiAdapter.ToUi(new Vector2(0, 1080)) == new Vector2(0, 0), "toUi top-left -> bottom-left");
            cases++;
            Check(MobileUiAdapter.ToUi(new Vector2(2400, 1080)) == new Vector2(2400, 0), "toUi bottom-right -> top-right (no letterbox)");
            cases++;
            Check(MobileUiAdapter.ToUi(new Vector2(1200, 540)) == new Vector2(1200, 540), "toUi center invariant (1:1)");
            cases++;
            ScreenMetrics.Set(1280, 720);
            Check(MobileUiAdapter.ToUi(new Vector2(1280, 0)) == new Vector2(1280, 720), "toUi follows new screen height");

            // 8-9. HUD 边缘锚点布局重算（右下攻击圆：右缘 90px/底缘 160px）
            cases++;
            var hud = new MobileHud(1280, 720);
            Check(hud.AttackCenter == new Vector2(1280 - 90f, 720 - 160f), "hud anchors bottom-right at 720p");
            cases++;
            hud.SetScreen(2400, 1080);
            Check(hud.AttackCenter == new Vector2(2400 - 90f, 1080 - 160f), "hud re-anchors at 1080p");

            // 10-12. 背包右上锚点布局重算（右缘 90px/顶缘 140px，72x54）
            cases++;
            var bag = new MobileBag(1280, 720);
            var r1 = bag.ButtonRect;
            Check(r1.x == 1280 - 72 - 90 && r1.y == 140 && r1.width == 72 && r1.height == 54, "bag anchors top-right at 720p");
            cases++;
            bag.SetScreen(2400, 1080);
            var r2 = bag.ButtonRect;
            Check(r2.x == 2400 - 72 - 90 && r2.y == 140 && r2.width == 72, "bag re-anchors at 1080p");
            cases++;
            bag.SetMargin(new Vector2(90f, 140 + 54 + 8f)); // 装备按钮下移（同 MobileBootstrap 锚点）
            Check(bag.ButtonRect.x == 2400 - 72 - 90 && bag.ButtonRect.y == 140 + 54 + 8f, "bag margin recompute");

            // 13-14. MinTouchSize 触控尺寸下限（短边不足 44px 中心外扩，合规尺寸不缩）
            cases++;
            var tr = MobileUiAdapter.TouchRect(new Vector2(100, 100), new Vector2(20, 20));
            Check(tr.width == MobileUiAdapter.MinTouchSize && tr.height == MobileUiAdapter.MinTouchSize, "touch rect enforces min touch size");
            cases++;
            var tr2 = MobileUiAdapter.TouchRect(new Vector2(100, 100), new Vector2(72, 54));
            Check(tr2.width == 72 && tr2.height == 54, "touch rect keeps compliant size");

            Console.WriteLine($"resolution-verify: cases={cases} fail={_fail}");
            if (_fail == 0)
            {
                Console.WriteLine("[resolutionverify] PASS cases=" + cases);
                EditorApplication.Exit(0);
            }
            else
            {
                Console.WriteLine($"[resolutionverify] FAIL cases={cases} fail={_fail}");
                EditorApplication.Exit(1);
            }
        }
    }
}
