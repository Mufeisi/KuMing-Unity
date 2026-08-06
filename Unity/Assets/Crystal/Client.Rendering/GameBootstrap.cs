using Client;
using Client.MirNetwork;
using UnityEngine;
using C = ClientPackets;

namespace Crystal.Client.Rendering
{
    // PC Player 引导壳（C3）：每帧驱动 GameRuntime.Tick 渲染到屏幕，并把 WASD/Shift 轮询翻译为移动包。
    // 由 BuildPC 幂等挂载 Main.unity 场景（C5 场景接线），batchmode 探针不经过此组件（直接调 GameRuntime.Tick）。
    public sealed class GameBootstrap : MonoBehaviour
    {
        const long MoveIntervalMs = 500; // 走格节流（Mir2 walk 约 0.5s/格）
        long _lastMoveAt;
        MirDirection _lastDir = MirDirection.Up;

        void Update()
        {
            GameRuntime.ScreenW = Screen.width;
            GameRuntime.ScreenH = Screen.height;
            PollInput();
            GameRuntime.Tick(null);
        }

        // WASD → 8 方向移动包；按住 Shift 变跑（C.Run）。轮询态驱动，节流走格速率。
        void PollInput()
        {
            if (GameSession.State != GameSessionState.InGame) return;
            bool up = Input.GetKey(KeyCode.W), down = Input.GetKey(KeyCode.S);
            bool left = Input.GetKey(KeyCode.A), right = Input.GetKey(KeyCode.D);
            if (!up && !down && !left && !right) return;

            MirDirection dir;
            if (up && right) dir = MirDirection.UpRight;
            else if (right && down) dir = MirDirection.DownRight;
            else if (down && left) dir = MirDirection.DownLeft;
            else if (left && up) dir = MirDirection.UpLeft;
            else if (up) dir = MirDirection.Up;
            else if (down) dir = MirDirection.Down;
            else if (left) dir = MirDirection.Left;
            else dir = MirDirection.Right;

            if (CMain.Time - _lastMoveAt < MoveIntervalMs) return;
            _lastMoveAt = CMain.Time;
            _lastDir = dir;
            bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Network.Enqueue(run ? new C.Run { Direction = dir } : new C.Walk { Direction = dir });
        }
    }
}
