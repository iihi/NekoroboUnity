// 仕上げの処理。JS版の renderScene の 2b と 3（matBright / matDown / matUp / matMix の finish）を写したもの。
//   0 = 明るい所を抜き出す（半分の大きさへ。しきい値の手前はなだらかに入れる）
//   1 = 縮めながらぼかす（デュアルフィルタの下り）
//   2 = 広げながらぼかして、1つ上の段に足す（上り。足し算で重ねる）
//   3 = 露出を掛け、ブルームを足し、トーンマッピングで画面に収める
// 色はリニアのまま扱う（画面の色への直しは URP が最後にやる。JS版の colorspace_fragment に当たる）。
Shader "Hidden/Nekorobo/Finish"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float4 _Hp;            // 半ドット（元の絵の 0.5 / 大きさ）
    float _Th, _Expo, _Tone, _BloomStr;
    TEXTURE2D(_Bloom);

    float3 Src(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }

    float4 FragBright(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float3 c = (Src(uv + float2(-_Hp.x, -_Hp.y)) + Src(uv + float2(_Hp.x, -_Hp.y))
                  + Src(uv + float2(-_Hp.x,  _Hp.y)) + Src(uv + float2(_Hp.x,  _Hp.y))) * 0.25 * _Expo;
        float l = max(c.r, max(c.g, c.b));
        float knee = _Th * 0.5;
        float soft = clamp(l - _Th + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee + 1e-4);
        float w = max(soft, l - _Th) / max(l, 1e-4);
        return float4(c * w, 1.0);
    }

    float4 FragDown(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float3 c = Src(uv) * 4.0
                 + Src(uv - _Hp.xy) + Src(uv + _Hp.xy)
                 + Src(uv + float2(_Hp.x, -_Hp.y)) + Src(uv - float2(_Hp.x, -_Hp.y));
        return float4(c / 8.0, 1.0);
    }

    float4 FragUp(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float3 c = Src(uv + float2(-_Hp.x * 2.0, 0.0)) + Src(uv + float2(_Hp.x * 2.0, 0.0))
                 + Src(uv + float2(0.0, -_Hp.y * 2.0)) + Src(uv + float2(0.0, _Hp.y * 2.0))
                 + Src(uv + float2(-_Hp.x,  _Hp.y)) * 2.0 + Src(uv + float2(_Hp.x,  _Hp.y)) * 2.0
                 + Src(uv + float2(-_Hp.x, -_Hp.y)) * 2.0 + Src(uv + float2(_Hp.x, -_Hp.y)) * 2.0;
        return float4(c / 12.0, 1.0);
    }

    // ニュートラル（Khronos PBR Neutral）。元の色味をなるべく保ったまま、明るい所だけ丸める
    float3 ToneNeutral(float3 c)
    {
        const float startC = 0.8 - 0.04, desat = 0.15;
        float x = min(c.r, min(c.g, c.b));
        float off = x < 0.08 ? x - 6.25 * x * x : 0.04;
        c -= off;
        float peak = max(c.r, max(c.g, c.b));
        if (peak < startC) return c;
        float d = 1.0 - startC;
        float np = 1.0 - d * d / (peak + d - startC);
        c *= np / peak;
        float g = 1.0 - 1.0 / (desat * (peak - np) + 1.0);
        return lerp(c, np.xxx, g);
    }
    // ACES（映画風）。three.js と同じ近似。GLSL の mat3 は列ごとに並ぶので、ここでは転置して書く
    float3 ToneACES(float3 c)
    {
        c /= 0.6;
        const float3x3 inM = float3x3(0.59719, 0.35458, 0.04823,
                                      0.07600, 0.90834, 0.01566,
                                      0.02840, 0.13383, 0.83777);
        const float3x3 outM = float3x3(1.60475, -0.53108, -0.07367,
                                       -0.10208, 1.10813, -0.00605,
                                       -0.00327, -0.07276, 1.07602);
        c = mul(inM, c);
        float3 a = c * (c + 0.0245786) - 0.000090537;
        float3 b = c * (0.983729 * c + 0.4329510) + 0.238081;
        c = mul(outM, a / b);
        return saturate(c);
    }

    float4 FragFinish(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float3 c = src.rgb * _Expo;
        c += SAMPLE_TEXTURE2D(_Bloom, sampler_LinearClamp, uv).rgb * _BloomStr;
        if (_Tone > 1.5) c = ToneACES(c);
        else if (_Tone > 0.5) c = ToneNeutral(c);
        return float4(c, src.a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass { Name "Bright" HLSLPROGRAM
               #pragma vertex Vert
               #pragma fragment FragBright
               ENDHLSL }
        Pass { Name "Down" HLSLPROGRAM
               #pragma vertex Vert
               #pragma fragment FragDown
               ENDHLSL }
        Pass { Name "Up" Blend One One
               HLSLPROGRAM
               #pragma vertex Vert
               #pragma fragment FragUp
               ENDHLSL }
        Pass { Name "Finish" HLSLPROGRAM
               #pragma vertex Vert
               #pragma fragment FragFinish
               ENDHLSL }
    }
}
