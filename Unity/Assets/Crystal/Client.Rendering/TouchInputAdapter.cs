using UnityEngine;
// 顶层 Client 命名空间类型全限定（本命名空间 Crystal.Client.Rendering 内裸 `Client.*` 会
// 被解析到 Crystal.Client.* 而非顶层 Client，见 MLibraryUnity.cs namespace Client.MirGraphics 规避同因）。
using CMain = global::Client.CMain;
using GameScene = global::Client.MirScenes.GameScene;
using MouseEventArgs = global::Client.MirControls.MouseEventArgs;
using MouseButtons = global::Client.MirControls.MouseButtons;

namespace Crystal.Client.Rendering
{
    // 阶段7 第 3 项（触控 Input Adapter）：Unity 触摸流 → Mir 鼠标语义分发组件。
    // Update 轮询 Input.touches（主触点触摸手势）→ TouchInputMapper 翻译 →
    // 更新 CMain.MPoint（控件 hit-test 基准）→ GameScene.Scene.OnMouseXxx 分发
    // （复用探针 ClickControl 同链路：Move 更新 MouseControl/hover → Down 置 ActiveControl → Up+Click）。
    // 鼠标回退：PC/模拟器 adb 鼠标测试（Input.GetMouseButton*，与触摸同语义）。
    // 空场景（GameScene.Scene 未初始化）分发跳过，接入游戏逻辑后自然生效。
    public class TouchInputAdapter : MonoBehaviour
    {
        [System.NonSerialized] public TouchInputMapper Mapper = new TouchInputMapper();
        int _primaryFinger = -1;

        void Start()
        {
            Debug.Log("[touch-input] adapter started (touch->mouse)");
        }

        void Update()
        {
            if (Input.touchCount > 0) HandleTouches();
            else HandleMouseFallback();
        }

        void HandleTouches()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    // 首个触点成为主触点（多指手势留待战斗 HUD，次触点忽略）。
                    if (_primaryFinger == -1)
                    {
                        _primaryFinger = t.fingerId;
                        Mapper.OnTouchDown(t.position.x, t.position.y);
                        SetMPoint(t.position.x, t.position.y);
                        DispatchMove();
                        DispatchDown();
                    }
                }
                else if (t.fingerId == _primaryFinger)
                {
                    if (t.phase == TouchPhase.Moved)
                    {
                        Mapper.OnTouchMove(t.position.x, t.position.y);
                        SetMPoint(t.position.x, t.position.y);
                        DispatchMove();
                    }
                    else if (t.phase == TouchPhase.Ended)
                    {
                        bool click = Mapper.OnTouchUp(t.position.x, t.position.y);
                        SetMPoint(t.position.x, t.position.y);
                        DispatchUp();
                        if (click) DispatchClick();
                        _primaryFinger = -1;
                    }
                    else if (t.phase == TouchPhase.Canceled)
                    {
                        Mapper.OnTouchCancel();
                        _primaryFinger = -1;
                    }
                }
            }
        }

        void HandleMouseFallback()
        {
            Vector3 m = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                Mapper.OnTouchDown(m.x, m.y);
                SetMPoint(m.x, m.y);
                DispatchMove();
                DispatchDown();
            }
            else if (Input.GetMouseButton(0))
            {
                if (Mapper.IsTouching)
                {
                    Mapper.OnTouchMove(m.x, m.y);
                    SetMPoint(m.x, m.y);
                    DispatchMove();
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                bool click = Mapper.OnTouchUp(m.x, m.y);
                SetMPoint(m.x, m.y);
                DispatchUp();
                if (click) DispatchClick();
            }
        }

        static void SetMPoint(float x, float y)
        {
            // 唯一翻转点（8-0 适配层）：Unity touch/鼠标 y 上（左下原点）→ Mir 鼠标事件左上空间。
            // CMain.MPoint 经 IsMouseOver 与 DisplayRectangle.Contains 判定（X-1 实证 MirControl 左上 rect），
            // 未翻转即 y 镜像（tap 可见按钮命中镜像侧）——阶段7 前置未翻转，8-0 收口到 MobileUiAdapter.ToUiPoint。
            CMain.MPoint = MobileUiAdapter.ToUiPoint(x, y);
        }

        static void DispatchMove()
        {
            if (GameScene.Scene == null) return;
            var p = CMain.MPoint;
            GameScene.Scene.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
        }

        static void DispatchDown()
        {
            if (GameScene.Scene == null) return;
            var p = CMain.MPoint;
            GameScene.Scene.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        }

        static void DispatchUp()
        {
            if (GameScene.Scene == null) return;
            var p = CMain.MPoint;
            GameScene.Scene.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        }

        static void DispatchClick()
        {
            if (GameScene.Scene == null) return;
            var p = CMain.MPoint;
            GameScene.Scene.OnMouseClick(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        }
    }
}
