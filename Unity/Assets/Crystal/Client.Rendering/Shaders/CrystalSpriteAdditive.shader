Shader "Crystal/SpriteAdditive"
{
    // 旧客户端 SetBlend(true) 的 SrcAlpha, One（叠加/发光混合）。
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
        Blend SrcAlpha One
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
                float luma = dot(texcol.rgb, fixed3(0.30, 0.59, 0.11));
                c.rgb = lerp(c.rgb, luma.xxx, _Grayscale);
                return c;
            }
            ENDCG
        }
    }
}
