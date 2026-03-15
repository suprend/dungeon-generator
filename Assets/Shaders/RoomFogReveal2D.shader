Shader "Custom/RoomFogReveal2D"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RevealOrigin ("Reveal Origin", Vector) = (0,0,0,0)
        _RevealRadius ("Reveal Radius", Float) = 0
        _RevealSoftness ("Reveal Softness", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _RevealOrigin;
            float _RevealRadius;
            float _RevealSoftness;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                output.worldPos = mul(unity_ObjectToWorld, input.vertex).xy;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.texcoord) * input.color;
                float softness = max(_RevealSoftness, 0.0001);
                float inner = max(0, _RevealRadius - softness);
                float dist = distance(input.worldPos, _RevealOrigin.xy);
                float fogAlpha = saturate((dist - inner) / softness);
                color.a *= fogAlpha;
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
