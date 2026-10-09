using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Nekorobo
{
    /// <summary>
    /// 仕上げの処理（露出・ブルーム・トーンマッピング）。JS版の CAM.post / expo / tone / bloom* と renderScene を写したもの。
    ///
    /// URP のポストエフェクトは使わない。URP のトーンマッピングは JS版（Khronos PBR Neutral）と式が違い、
    /// ブルームも段の重ね方が違うので、同じ式を自前で描く（Resources/Shaders/Finish.shader）。
    /// 接地の陰（AO）は URP の SSAO を使う（PC_Renderer に入っている）。
    ///
    /// 描く順は「物を描き終えた絵（リニア・HDR）→ ここ → 画面の色に直す（URP）→ UI」。
    /// </summary>
    public static class PostFx
    {
        // JS版 CAM の既定値と同じ
        public static bool post = true;         // 仕上げを掛けるか
        public static float expo = 1.2f;        // 露出
        public static int tone = 1;             // 0 = なし / 1 = ニュートラル / 2 = ACES
        public static bool bloom = true;
        public static float bloomStr = 0.6f, bloomTh = 0.85f;
        public static int bloomRad = 5;         // 広がり（段数 2〜6）

        static FinishPass pass;
        static bool hooked;

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBegin;
        }

        public static void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            RenderPipelineManager.beginCameraRendering -= OnBegin;
        }

        static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!post || cam.cameraType != CameraType.Game) return;      // シーンビューには掛けない
            if (pass == null)
            {
                var sh = Resources.Load<Shader>("Shaders/Finish");
                if (sh == null) sh = Shader.Find("Hidden/Nekorobo/Finish");
                if (sh == null) return;
                pass = new FinishPass(sh);
            }
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null && data.scriptableRenderer != null) data.scriptableRenderer.EnqueuePass(pass);
        }

        class FinishPass : ScriptableRenderPass
        {
            const int MAX = 6;
            readonly Material bright, fin;
            // 段ごとに別の素材にする。1つを使い回すと、描く時点で最後に入れた値（大きさ）になってしまう
            readonly Material[] down = new Material[MAX], up = new Material[MAX];

            class Data { public TextureHandle src, bloom; public Material mat; public int pass; }

            public FinishPass(Shader sh)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
                bright = New(sh); fin = New(sh);
                for (int i = 0; i < MAX; i++) { down[i] = New(sh); up[i] = New(sh); }
            }

            static Material New(Shader sh) { return new Material(sh) { hideFlags = HideFlags.HideAndDontSave }; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frame)
            {
                var res = frame.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                var src = res.activeColorTexture;
                var desc = rg.GetTextureDesc(src);
                int w = desc.width, h = desc.height;

                // ---- ブルーム：明るい所を抜き出し、縮めながらぼかして、広げながら足し戻す
                var bloomTex = TextureHandle.nullHandle;
                float bs = 0;
                if (bloom && bloomStr > 0)
                {
                    int n = Mathf.Clamp(bloomRad, 2, MAX);
                    var B = new TextureHandle[n];
                    var sz = new Vector2Int[n];
                    int bw = w, bh = h;
                    for (int i = 0; i < n; i++)
                    {
                        bw = Mathf.Max(1, Mathf.RoundToInt(bw / 2f)); bh = Mathf.Max(1, Mathf.RoundToInt(bh / 2f));
                        sz[i] = new Vector2Int(bw, bh);
                        B[i] = rg.CreateTexture(new TextureDesc(bw, bh)
                        {
                            format = GraphicsFormat.R16G16B16A16_SFloat, filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp, clearBuffer = false, name = "NkBloom" + i,
                        });
                    }
                    bright.SetVector("_Hp", new Vector4(0.5f / w, 0.5f / h));
                    bright.SetFloat("_Th", bloomTh); bright.SetFloat("_Expo", expo);
                    Draw(rg, src, B[0], bright, 0, false);
                    for (int i = 1; i < n; i++)
                    {
                        down[i].SetVector("_Hp", new Vector4(0.5f / sz[i - 1].x, 0.5f / sz[i - 1].y));
                        Draw(rg, B[i - 1], B[i], down[i], 1, false);
                    }
                    for (int i = n - 1; i > 0; i--)
                    {
                        up[i].SetVector("_Hp", new Vector4(0.5f / sz[i].x, 0.5f / sz[i].y));
                        Draw(rg, B[i], B[i - 1], up[i], 2, true);            // 1つ上の段に足す
                    }
                    bloomTex = B[0];
                    bs = bloomStr / n;                                       // 段を重ねるほど明るくなるので、段の数で割る
                }

                // ---- 露出・ブルーム・トーンマッピングを掛けて、新しい絵へ
                desc.name = "NkFinish"; desc.clearBuffer = false;
                var dst = rg.CreateTexture(desc);
                fin.SetFloat("_Expo", expo); fin.SetFloat("_Tone", tone); fin.SetFloat("_BloomStr", bs);
                using (var b = rg.AddRasterRenderPass<Data>("NkFinish", out var d))
                {
                    d.src = src; d.bloom = bloomTex; d.mat = fin; d.pass = 3;
                    b.UseTexture(src);
                    if (bloomTex.IsValid()) b.UseTexture(bloomTex);
                    b.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    b.SetRenderFunc((Data x, RasterGraphContext c) =>
                    {
                        if (x.bloom.IsValid()) { RTHandle bt = x.bloom; x.mat.SetTexture("_Bloom", bt); }
                        else x.mat.SetTexture("_Bloom", Texture2D.blackTexture);
                        Blitter.BlitTexture(c.cmd, x.src, new Vector4(1, 1, 0, 0), x.mat, x.pass);
                    });
                }
                res.cameraColor = dst;
            }

            static void Draw(RenderGraph rg, TextureHandle s, TextureHandle t, Material m, int p, bool add)
            {
                using (var b = rg.AddRasterRenderPass<Data>("NkBloom", out var d))
                {
                    d.src = s; d.mat = m; d.pass = p;
                    b.UseTexture(s);
                    b.SetRenderAttachment(t, 0, add ? AccessFlags.ReadWrite : AccessFlags.Write);
                    b.SetRenderFunc((Data x, RasterGraphContext c) => Blitter.BlitTexture(c.cmd, x.src, new Vector4(1, 1, 0, 0), x.mat, x.pass));
                }
            }
        }
    }
}
