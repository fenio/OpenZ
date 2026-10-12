// 1 = sunburst, 2 = halftone, 3 = speed lines, 4 = flat colour; with _Cut = 1 the RenderTexture is a transparent cut-out
// (a robot on its own layer) composited over the flat comic background with a thin ink rim (STORYBOARD comic_rules.units_look).
Shader "OpenZ/UI/ComicPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Style ("Style", Float) = 0
        _Cut ("Cut-out", Float) = 0
        _Col1 ("Colour 1", Color) = (1,0.8,0.2,1)
        _Col2 ("Colour 2", Color) = (1,0.55,0.15,1)
        _Ink ("Ink", Color) = (0.07,0.06,0.07,1)
        _Size ("Panel px (w, h)", Vector) = (800, 600, 0, 0)
        _Centre ("Centre uv (y down)", Vector) = (0.5, 0.5, 0, 0)
        _Rays ("Rays", Float) = 20
        _Dot ("Dot px", Float) = 14
        _Corner ("Halftone corner (x, y down)", Vector) = (1, 1, 0, 0)
        _Rim ("Cut-out ink rim px", Float) = 3
        _Grain ("Paper grain", Float) = 0.035
        _Seed ("Seed", Float) = 0
        _WhiteTex ("Cut-out twin on a white clear (difference matte)", 2D) = "black" {}
        _Matte ("Difference matte", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            sampler2D _MainTex; float4 _MainTex_TexelSize; sampler2D _WhiteTex; float _Matte;
            float _Style, _Cut, _Rays, _Dot, _Rim, _Grain, _Seed;
            float4 _Col1, _Col2, _Ink, _Size, _Centre, _Corner;
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            // cut-out coverage: the RT alpha, or (difference matte) 1 - (white render - black render)
            float CutA(float2 uv)
            {
                float4 b = tex2D(_MainTex, uv);
                if (_Matte < 0.5) return b.a;
                float4 w = tex2D(_WhiteTex, uv);
                return saturate(1 - max(max(w.r - b.r, w.g - b.g), w.b - b.b));
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed) * 43758.5453); }

            float3 background(float2 uv)
            {
                float2 px = float2(uv.x, 1 - uv.y) * _Size.xy;                       // panel px, y down like the storyboard
                float2 c = _Centre.xy * _Size.xy;
                float2 d = px - c;
                if (_Style < 1.5)                                                     // sunburst: alternating wedges + a soft centre glow
                {
                    float a = atan2(d.y, d.x) / 6.2831853 + 0.5;
                    float s = frac(a * _Rays);
                    float edge = fwidth(a * _Rays) * 1.5;
                    float m = smoothstep(0.5 - edge, 0.5 + edge, s) * (1 - smoothstep(1 - edge, 1, s)) + (1 - smoothstep(0, edge, s)) * 0;
                    float3 col = lerp(_Col1.rgb, _Col2.rgb, m);
                    float r = length(d) / max(_Size.x, _Size.y);
                    return lerp(col * 1.08, col * 0.92, saturate(r * 1.4));
                }
                if (_Style < 2.5)                                                     // halftone: a 45 deg dot screen growing toward the corner
                {
                    float2 q = float2(px.x + px.y, px.x - px.y) * 0.70710678 / _Dot;
                    float2 f = frac(q) - 0.5;
                    float2 cn = _Corner.xy * _Size.xy;
                    float g = saturate(1 - length(px - cn) / (1.25 * length(_Size.xy)));
                    float rad = lerp(0.12, 0.47, g);
                    float dd = length(f);
                    float aa = fwidth(dd) * 1.2;
                    float m = 1 - smoothstep(rad - aa, rad + aa, dd);
                    return lerp(_Col1.rgb, _Col2.rgb, m);
                }
                if (_Style < 3.5)                                                     // speed lines from the vanishing point
                {
                    float a = atan2(d.y, d.x) / 6.2831853 + 0.5;
                    float k = a * 260;
                    float cell = floor(k);
                    float h = hash(float2(cell, 3.1));
                    float w = 0.12 + 0.35 * h;
                    float s = abs(frac(k) - 0.5) * 2;
                    float r = length(d) / length(_Size.xy);
                    float start = 0.08 + 0.35 * hash(float2(cell, 7.7));
                    float on = step(0.45, h) * smoothstep(start, start + 0.12, r);
                    float m = on * (1 - smoothstep(w - fwidth(s), w + fwidth(s), s));
                    float3 col = lerp(_Col1.rgb, _Col1.rgb * 1.12, saturate(r));
                    return lerp(col, _Col2.rgb, m);
                }
                return _Col1.rgb;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float4 tex = tex2D(_MainTex, i.uv);
                float3 col;
                if (_Style < 0.5) col = tex.rgb;
                else
                {
                    col = background(i.uv);
                    if (_Cut > 0.5)
                    {
                        float2 t = _MainTex_TexelSize.xy * _Rim;
                        float n = 0;
                        n = max(n, CutA(i.uv + float2( t.x, 0))); n = max(n, CutA(i.uv + float2(-t.x, 0)));
                        n = max(n, CutA(i.uv + float2(0,  t.y))); n = max(n, CutA(i.uv + float2(0, -t.y)));
                        n = max(n, CutA(i.uv + t * 0.7071));       n = max(n, CutA(i.uv - t * 0.7071));
                        n = max(n, CutA(i.uv + float2(t.x, -t.y) * 0.7071)); n = max(n, CutA(i.uv + float2(-t.x, t.y) * 0.7071));
                        col = lerp(col, _Ink.rgb, saturate(n * 1.6));
                        float ca = CutA(i.uv);
                        float a = saturate(ca * 1.15);
                        col = lerp(col, saturate(tex.rgb / max(ca, 0.02)), a);
                    }
                }
                float g = (hash(floor(i.uv * _Size.xy)) - 0.5) * _Grain;              // paper grain
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = saturate(col + g * lerp(0.22, 1.0, saturate(lum * 2.2)));       // ink-dark areas (space) take little grain: no TV-static look
                return fixed4(col, i.color.a);
            }
            ENDCG
        }
    }
}
