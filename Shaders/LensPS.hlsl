// Liquid Glass lens ring (D3D11, ps_4_0). Draws only the curved bevel of the island: the screen behind
// it is bent inward along the rim, split slightly by colour, lit by a specular edge; the flat middle is
// left transparent so the DirectComposition blur underneath shows through, sharp and jitter-free.
cbuffer Params : register(b0)
{
    float4 rect;  // pill centre x,y and half width,height (window pixels)
    float4 p1;    // corner radius, bevel width, refraction strength (px), light angle (rad)
    float4 p2;    // monitor width, monitor height, window origin x, y (monitor pixels)
    float4 p3;    // blur radius (px), dispersion (0..1), shininess (0..1), saturation
};

Texture2D tex : register(t0);
SamplerState smp : register(s0);

float sdRoundRect(float2 p, float2 hb, float r)
{
    float2 q = abs(p) - hb + r;
    return min(max(q.x, q.y), 0) + length(max(q, 0)) - r;
}

float3 spiral(float2 monPx, float2 invMon, float radius)
{
    float3 sum = tex.SampleLevel(smp, monPx * invMon, 0).rgb;
    float2 dir = float2(1, 0);
    [unroll]
    for (int i = 1; i <= 8; i++)
    {
        float k = sqrt(i / 8.0) * radius;
        sum += tex.SampleLevel(smp, (monPx + dir * k) * invMon, 0).rgb;
        dir = float2(dir.x * -0.7374 - dir.y * 0.6755, dir.x * 0.6755 + dir.y * -0.7374); // +137.5 degrees
    }
    return sum / 9.0;
}

float4 main(float4 sv : SV_Position) : SV_Target
{
    float2 pos = sv.xy;
    float2 p = pos - rect.xy;
    float r = p1.x;
    float d = sdRoundRect(p, rect.zw, r);
    float mask = saturate(0.5 - d);
    if (mask <= 0) return float4(0, 0, 0, 0);

    float2 g = float2(
        sdRoundRect(p + float2(1, 0), rect.zw, r) - sdRoundRect(p - float2(1, 0), rect.zw, r),
        sdRoundRect(p + float2(0, 1), rect.zw, r) - sdRoundRect(p - float2(0, 1), rect.zw, r));
    float2 n = g / max(length(g), 0.0001);          // outward normal

    float depth = max(-d, 0);
    float bevel = max(p1.y, 2);
    float t = saturate(1 - depth / bevel);           // 1 at the rim, 0 at the inner edge of the bevel
    float lens = t * t;                              // grows steadily toward the rim
    lens = lens * 0.8 + t * t * t * t * 0.2;

    // Bend. The screen copy contains the island itself, so the lens never samples under the pill: it
    // reads the background just OUTSIDE the edge, along the outward normal, and squeezes it into the
    // bevel (like the rim of a thick glass dome gathering the surroundings). Samples reach farther out the
    // deeper the pixel is, which compresses a wide band of background into the thin rim.
    float reach = depth + 4.0 + p1.z * lens * 0.45;
    float2 mon = pos + p2.zw;
    float2 invMon = 1.0 / p2.xy;
    float disp = p3.y;

    float3 col;
    col.r = spiral(mon + n * reach * (1 + disp), invMon, p3.x).r;
    col.g = spiral(mon + n * reach,              invMon, p3.x).g;
    col.b = spiral(mon + n * reach * (1 - disp), invMon, p3.x).b;

    float luma = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(luma.xxx, col, p3.w);                 // vibrancy
    col = lerp(col, float3(0.086, 0.086, 0.102), 0.12); // keep the same graphite core as the island fill

    // How much of the lens shows: full at the rim, fading into the blurred middle.
    float ringA = smoothstep(0.0, 0.85, t);

    // Specular edge: crisp line on the lit side (top-left), softer on the far side, plus a wide glow.
    float2 L = float2(cos(p1.w), sin(p1.w));
    float facing = dot(n, L);
    float sharp = max(bevel * 0.06, 0.9);
    float wide = max(bevel * 0.35, 3.0);
    float rimSharp = exp(-depth * depth / (2 * sharp * sharp));
    float rimWide = exp(-depth * depth / (2 * wide * wide));
    float lit = pow(saturate(facing), 1.6);
    float unlit = pow(saturate(-facing), 1.6);
    float shine = 0.35 + 0.65 * p3.z;
    float spec = rimSharp * (lit * 0.85 + unlit * 0.40) * shine
               + rimWide * (lit * 0.13 + unlit * 0.05) * shine;

    float a = saturate(ringA + spec) * mask;
    float3 rgb = (col * ringA + spec.xxx) * mask;
    return float4(rgb, a);
}

