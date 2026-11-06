// 凍りの床（JS版 iceMaterial の onBeforeCompile を写したもの）。
// 届いた絵は、マスごとに同じひびの入った濃い水色で、並べると**プールの水**に見えた（担当）。
//   ・岸（雪のふち）は絵のまま。中の水色の所（青が赤よりずっと強い所）だけ、盤面ぜんたいでつながる淡い氷の絵（_Ice）に差し替える
//   ・つや：空と天井の明かりを映す（_Env。正距円筒の絵）。見る向きで明るい映り込みが入り、水とも白い床とも違って見える
// 位置は three.js の向き（z を反転）で数える（氷の絵の並びを JS版と同じにするため）
Shader "Hidden/Nekorobo/Ice"
{
    Properties
    {
        _MainTex ("Skin", 2D) = "white" {}
        _Ice ("Ice", 2D) = "white" {}
        _Env ("Env", 2D) = "white" {}
        _IceK ("Ice scale (1/m)", Float) = 0.1666667
        _Reflect ("Reflect", Float) = 0.75
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Ice"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Ice);     SAMPLER(sampler_Ice);
            TEXTURE2D(_Env);     SAMPLER(sampler_Env);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _IceK;
                float _Reflect;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 skin = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // 水色の所（青が赤よりずっと強い）だけ、氷の絵に差し替える。岸の雪（白っぽい）はそのまま
                float inner = smoothstep(0.42, 0.62, skin.b - skin.r);
                float2 w = float2(i.positionWS.x, -i.positionWS.z) * _IceK;
                half3 ice = SAMPLE_TEXTURE2D(_Ice, sampler_Ice, w).rgb;
                half3 alb = lerp(skin.rgb, ice, inner);
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(sc);
                half3 lit = L.color * (L.distanceAttenuation * L.shadowAttenuation) * saturate(dot(N, L.direction));
                #if defined(_ADDITIONAL_LIGHTS)
                uint n = GetAdditionalLightsCount();
                for (uint k = 0; k < n; k++)
                {
                    Light A = GetAdditionalLight(k, i.positionWS);
                    lit += A.color * A.distanceAttenuation * saturate(dot(N, A.direction));
                }
                #endif
                half3 amb = SampleSH(N);
                half3 col = alb * (lit + amb);
                // 映り込み。three.js の向き（z 反転）で正距円筒の絵を引く（u=0.25 が奥、上が空）
                float3 R = reflect(-V, N);
                float3 r3 = float3(R.x, R.y, -R.z);
                float u = atan2(r3.z, r3.x) / (2 * PI) + 0.5;
                float vv = asin(clamp(r3.y, -1, 1)) / PI + 0.5;
                half3 env = SAMPLE_TEXTURE2D(_Env, sampler_Env, float2(u, vv)).rgb;
                float fr = 0.04 + 0.96 * pow(1 - saturate(dot(N, V)), 5);
                // three.js の映り込みの絵（envMap）は、光沢の映り込みだけでなく「まわりからの光」としても効く
                // （上を向いた面は空の白さで明るくなる）。上半分の空の平均を、まわりからの光として足す
                half3 irr = half3(0.62, 0.72, 0.80);
                col += alb * irr * _Reflect;
                col += env * _Reflect * fr;
                return half4(col, 1);
            }
            ENDHLSL
        }
        // 影を落とす・奥行きを書く（URP の決まりの Pass）
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
