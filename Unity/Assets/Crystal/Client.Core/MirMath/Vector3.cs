namespace Crystal.Client.Core.MirMath
{
    // SlimDX.Vector3 的纯 C# 等价物：对象模型仅用 ctor(float,float,float) 作为绘制参数容器。
    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
