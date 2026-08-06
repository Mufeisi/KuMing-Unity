Shader "Crystal/Sprite"
{
    // 旧客户端 D3D9 Sprite 默认 AlphaBlend（SrcAlpha, OneMinusSrcAlpha）。
    // 顶点已由 CrystalSpriteBatch 在 CPU 侧烘焙为 NDC 坐标，此处直通。
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Grayscale ("Grayscale", Range(0,1)) = 0
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
            float _Grayscale;

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
                fixed4 c = texcol * i.color;
                // 旧 grayscale.ps 语义：亮度 = R*0.30+G*0.59+B*0.11，保留 alpha。
                float luma = dot(texcol.rgb, fixed3(0.30, 0.59, 0.11));
                c.rgb = lerp(c.rgb, luma.xxx, _Grayscale);
                return c;
            }
            ENDCG
        }
    }
}
