Shader "Crystal/SpriteOutline"
{
    // 复刻 sanduan OutLine.shader 的精灵描边语义（效果语义 → 本项目 Crystal/Sprite 风格 shader）。
    // 关键差异：sanduan 每图一独立 Texture2D，UV 邻域采样可直接取到"本图外=透明"；
    // 本项目图集批处理（quad UV 紧贴帧矩形）中，UV±偏移会串读相邻帧 → 不能按 sanduan 做法
    // 在 frag 里做邻域描边。等价实现：此 shader 输出"平涂描边色"（alpha<0.5 或阴影例外则丢弃），
    // CrystalSpriteBatch.DrawOutline 画 4 个 ±1px 偏移副本后再压原图 → 轮廓外 1px 描边光环
    // （与 MirLabel 文本描边的 4 向重绘同款模式）。sanduan 的"边缘像素被描边色替换"与
    // 本方案"轮廓外光环"视觉等价（高亮描边），取后者以兼容图集。
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (1, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _OutlineColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texcol = tex2D(_MainTex, i.uv);
                // sanduan OutLine：a<0.5 视为透明（轮廓判据）；阴影图案例外（16/8/8 或 r<0.01）不描边。
                if (texcol.a < 0.5) discard;
                if (all(texcol.rgb == fixed3(16.0 / 255.0, 8.0 / 255.0, 8.0 / 255.0))) discard;
                if (texcol.r < 0.01) discard;
                // 平涂描边色（alpha 来自描边色，忽略顶点色——副本画的是纯描边）。
                return fixed4(_OutlineColor.rgb, _OutlineColor.a);
            }
            ENDCG
        }
    }
}
