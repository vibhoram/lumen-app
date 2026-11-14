// Liquid Glass refraction for the island (personal fork). Compiled to LiquidGlass.ps with fxc (ps_3_0):
//   fxc /T ps_3_0 /E main /O3 /Fo Shaders\LiquidGlass.ps Shaders\LiquidGlass.fx
// The input is a screen copy of what's behind the island; the pill's rounded-rectangle SDF bends it.

sampler2D Input : register(s0);

float4 rect : register(c0); // pill centre x,y and half width,height, in input pixels
float4 p1   : register(c1); // corner radius, image width, image height, refraction strength (px)
float4 p2   : register(c2); // bevel width (px), blur radius (px), light angle (rad), tint (0..1)
float4 p3   : register(c3); // shininess (0..1), dispersion (0..1), saturation boost, unused

float sdRoundRect(float2 p, float2 hb, float r)
{
    float2 q = abs(p) - hb + r;
    return min(max(q.x, q.y), 0) + length(max(q, 0)) - r;
}

float3 sampleBlur(float2 pos, float2 invSize, float radius)
{
    // 8 taps on a golden-angle spiral plus the centre: soft, cheap, no banding at these radii.
    float3 sum = tex2D(Input, pos * invSize).rgb;
    float2 dir = float2(1, 0);
    const float2x2 rot = float2x2(-0.7374, 0.6755, -0.6755, -0.7374); // 137.5 degrees
    for (int i = 1; i <= 8; i++)
    {
        float k = sqrt(i / 8.0) * radius;
        sum += tex2D(Input, (pos + dir * k) * invSize).rgb;
        dir = mul(dir, rot);
    }
    return sum / 9.0;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 size = p1.yz;
    float2 invSize = 1.0 / size;
    float2 pos = uv * size;
    float2 p = pos - rect.xy;
    float r = p1.x;

    float d = sdRoundRect(p, rect.zw, r);   // < 0 inside
    float mask = saturate(0.5 - d);
    if (mask <= 0) return float4(0, 0, 0, 0);

    // Outward normal from the SDF gradient.
    float2 g = float2(
        sdRoundRect(p + float2(1, 0), rect.zw, r) - sdRoundRect(p - float2(1, 0), rect.zw, r),
        sdRoundRect(p + float2(0, 1), rect.zw, r) - sdRoundRect(p - float2(0, 1), rect.zw, r));
    float2 n = g / max(length(g), 0.0001);

    float depth = max(-d, 0);
    float bevel = max(p2.x, 1);
    float t = saturate(1 - depth / bevel);        // 1 at the rim, 0 past the bevel
    float lens = t * t * (3 - 2 * t);              // smooth bump
    lens = lens * lens + 0.35 * lens;              // keep a long, gentle slope inside the bevel

    // Refraction: the rim pulls in content from outside, magnified toward the edge.
    float2 shift = n * (p1.w * lens);
    float disp = p3.y;

    float3 col;
    col.r = sampleBlur(pos + shift * (1 + disp), invSize, p2.y).r;
    col.g = sampleBlur(pos + shift,              invSize, p2.y).g;
    col.b = sampleBlur(pos + shift * (1 - disp), invSize, p2.y).b;

    // Vibrancy: glass lifts saturation a little.
    float luma = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(luma.xxx, col, p3.z);

    // Tint and a faint top-down body light.
    col = lerp(col, float3(0.10, 0.10, 0.12), p2.w);
    float vert = saturate((pos.y - (rect.y - rect.w)) / (2 * rect.w));
    col += (1 - vert) * 0.05 * p3.x;

    // Specular rim: a crisp bright line on the lit side, a softer echo on the far side, plus a wider
    // inner glow that fades toward the middle (the "light caught in the glass" look).
    float2 L = float2(cos(p2.z), sin(p2.z));
    float facing = dot(n, L);
    float sharp = max(bevel * 0.07, 0.9);
    float wide = max(bevel * 0.30, 3.0);
    float rimSharp = exp(-depth * depth / (2 * sharp * sharp));
    float rimWide = exp(-depth * depth / (2 * wide * wide));
    float lit = pow(saturate(facing), 1.6);
    float unlit = pow(saturate(-facing), 1.6);
    float shine = 0.35 + 0.65 * p3.x;
    float spec = rimSharp * (lit * 1.0 + unlit * 0.55) * shine
               + rimWide * (lit * 0.34 + unlit * 0.16) * shine
               + pow(saturate(facing * 0.5 + 0.5), 4.0) * t * 0.12 * p3.x;
    col += spec;

    // Thin inner dark edge so the glass reads against bright backgrounds.
    col *= 1 - 0.12 * smoothstep(0, 1.5, 1.5 - depth) * (1 - p3.x * 0.5);

    return float4(col * mask, mask);
}
