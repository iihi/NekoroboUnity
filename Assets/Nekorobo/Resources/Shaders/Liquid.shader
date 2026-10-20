// 川・海・マグマの水面（JS版 liqMaterial の onBeforeCompile を写したもの）。
// 届いた絵は岸（草・砂・岩場）ごと描いた止まった絵。**岸は絵のまま止めておき、水（マグマ）の所だけ全面の絵を流して描く。**
//   _MainTex … 岸つきのまとめ画像。透明度が「岸からの距離」（0＝岸、16px 入ると 1）
//   _Surf    … 全面の絵（1m で1枚）。2枚を少しずらして重ね、さざ波でゆらし、明るさもゆっくり揺らす
//   岸ぎわ   … 距離の帯に、岸へ寄せては返す泡（マグマは光）を描く
// 位置と流れは three.js の向き（z を反転）で数える（模様の流れ方を JS版と同じにするため）。流れは2つめの UV。
// マグマは光の影響を受けない（MeshBasicMaterial）、水と海は光を受けて少し透ける（MeshStandardMaterial）。
Shader "Hidden/Nekorobo/Liquid"
{
    Properties
    {
        _MainTex ("Shore", 2D) = "white" {}
        _Surf ("Surface", 2D) = "white" {}
        _Wob ("Wobble", Float) = 0.035
        _Foam ("Foam", Color) = (0.5, 0.5, 0.5, 1)
        _Alpha ("Alpha", Float) = 0.9
        _Lava ("Lava", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
        [HideInInspector] _SrcBlend ("SrcBlend", Float) = 5
        [HideInInspector] _DstBlend ("DstBlend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Liquid"
            Tags { "LightMode" = "UniversalForward" }
            // マグマは不透明（奥行きも書く。書かないと下の底の板に上書きされる）、水と海は半透明
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Surf);    SAMPLER(sampler_Surf);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Surf_ST;
                float _Wob;
                float4 _Foam;
                float _Alpha;
                float _Lava;
            CBUFFER_END
            float _NkLiqTime;                 // 流れの時計（Game.TickLiquid が進める）

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 flow : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1;
                float2 wxz : TEXCOORD2;       // three.js の向きの (x, z)
                float3 positionWS : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.flow = v.flow;
                o.wxz = float2(ws.x, -ws.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _NkLiqTime;
                half4 shore = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float2 w = i.wxz;
                float2 q = w - i.flow * t + _Wob * float2(sin(w.y * 2.7 + t * 1.4), cos(w.x * 2.3 + t * 1.1));
                float3 wa = SAMPLE_TEXTURE2D(_Surf, sampler_Surf, q).rgb;
                float3 wb = SAMPLE_TEXTURE2D(_Surf, sampler_Surf, w * 0.61 - i.flow * t * 0.55 + float2(0.31, 0.17)
                                  + _Wob * float2(cos(w.y * 1.9 - t), sin(w.x * 2.1 + t * 0.8))).rgb;
                float3 liq = lerp(wa, wb, 0.38);
                float sh = 0.5 + 0.5 * sin((w.x * 1.7 - w.y * 1.3) * 2.2 + t * (_Lava > 0.5 ? 0.9 : 1.7));
                liq *= _Lava > 0.5 ? (0.82 + 0.36 * sh) : (0.95 + 0.1 * sh);
                float d = shore.a;                                       // 岸からの距離（0＝岸）
                float wet = smoothstep(0.0, 0.12, d);
                float band = wet * (1.0 - smoothstep(0.35, 1.0, d));
                float wave = smoothstep(0.55, 1.0, 0.5 + 0.5 * sin(d * 9.0 + t * 2.2 + (w.x + w.y) * 0.9));
                float3 col = lerp(shore.rgb, liq, wet) + _Foam.rgb * band * (0.35 + 0.65 * wave);
                float a = lerp(1.0, _Alpha, wet);
                if (_Lava > 0.5) return half4(col, 1);
                // 光（主の光の当たり具合と影 ＋ まわりの明るさ）。つやは省く
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(sc);
                float3 n = normalize(i.normalWS);
                float3 lit = L.color * (saturate(dot(n, L.direction)) * L.shadowAttenuation * L.distanceAttenuation) + SampleSH(n);
                return half4(col * lit, a);
            }
            ENDHLSL
        }
    }
}
