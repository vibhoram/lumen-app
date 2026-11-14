---
name: liquid-glass-lens
description: The exact, working recipe for an iOS-26-style Liquid Glass overlay on Windows (WPF + DirectComposition + Desktop Duplication + D3D11 pixel shader). Use when building any glass pill/panel/overlay on Windows so it looks and feels like the Lumen island. Tag in git: lens-beautiful (commit 82e876a, repo d-island).
---

# Liquid Glass lens (Lumen) - the recipe that landed

Result: a pill whose rim bends the background like thick glass, with a soft light falling from the top-left, a specular edge, a blurred, vivid body, and zero jitter. Reference repo: C:\Users\sanat\Documents\d-island (git tag **lens-beautiful**).

## Architecture (4 layers, bottom to top)
1. **Backdrop blur, GPU, jitter-free** - `GpuBlurBackdrop.cs`. A separate no-activate, topmost, click-through layered window (WS_EX_NOREDIRECTIONBITMAP) that mirrors the windows behind the island using DWM shared thumbnails (private dwmapi API, ordinal 147), put in a DirectComposition visual tree: thumbnails -> Gaussian blur effect -> saturation effect (vibrancy 1.35) -> rounded-rect clip (the pill shape). No CPU capture, no per-frame copies.
2. **Lens ring, D3D11** - `LensRenderer.cs` + `Shaders/LensPS.hlsl`. DXGI Desktop Duplication (on the monitor's own adapter) copies the screen into a texture; a pixel shader draws ONLY the curved bevel ring + interior light into a DirectComposition swap chain (premultiplied alpha) placed above the blur. The flat middle stays transparent so the blur shows through.
3. **The WPF island window** on top (text, buttons), transparent background.
4. WPF fill/outline as a fallback when GPU glass is unavailable.

## Hard-won rules (each one fixed a real bug)
- Call `ID3D11Multithread.SetMultithreadProtected(true)` and lock around capture + render; release the DDA frame before disposing -> fixed random access-violation crashes.
- Skip DDA frames with `LastPresentTime == 0` (mouse-only updates copy black).
- The screen copy CONTAINS the island itself. The lens must sample the background just OUTSIDE the shape (along the outward normal), never under it.
- **Sample reach** (the key look parameter): `reach = depth + 4.0 + strength * lens * 0.85`. Always start >= 4 px outside the edge (smaller shows a doubled copy of the pill's own edge; much larger smears text into vertical streaks on flat edges).
- **Bevel width**: `min(min(w,h)*0.55, 64)` clamped to 16 minimum, then `min(bevel, max(10, radius*1.25))`. A bevel deeper than the corner radius flips the outward normal across the corner diagonal and draws a hard seam.
- Strength passed to the shader: `GlassRefraction(0.6) * 52 * dpiScale * uiScale` px.
- Park the lens instead of disposing it when toggled off (keeps duplication + last frame, instant re-enable); dispose fully only when the monitor changes. Re-creating on each toggle left the ring half-drawn.
- On monitor change: `DisposeLens()` then rebuild so Desktop Duplication targets the new output.
- Never make the island a layered window that gets its own shadow halo: layered windows only pass clicks through fully transparent pixels (use ShadowStrength 0 with glass).
- Screenshot / full-screen overlays look like a "full-screen app": debounce auto-hide (needs ~4 s of fullscreen state).
- Don't use WPF DropShadow on text that appears after a delay: it pops in. Leave text clean.
- Buttons: keep them flat. Boxed/outlined glass buttons looked like Windows 7. (Button default BorderThickness is 1: set it to 0 in the style.)
- Compile shaders with fxc (`/T ps_4_0 /E main /O3`) from PowerShell and write .hlsl without a BOM (Set-Content -Encoding UTF8 adds one and breaks fxc). Embed the .cso files as resources.

## Settings that give the look (defaults)
Blur 0.45 (lower = clearer, more glassy), Shininess 0.9, Edge light 0.1, Vibrancy 0.65, Light bend 0.6, Tint 0.30, engine = "Liquid lens (GPU)". Presets: Crystal (0.30/0.95/0.15/0.75/0.70/0.18), Frosted, Clear, Smoke, Vivid.

## The pixel shader (Shaders/LensPS.hlsl, verbatim)
```hlsl
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
    float reach = depth + 4.0 + p1.z * lens * 0.85;
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

    // Inside the glass: a broad soft light falling from the top-left and a faint cool shade at the bottom-right,
    // so the flat middle reads as a curved glass body and not just frosted blur.
    float2 uv = p / max(rect.zw, 1.0);               // -1..1 across the pill
    float sheen = saturate(1.0 - length((uv - float2(-0.62, -1.05)) * float2(0.55, 1.0)) * 0.80);
    sheen = sheen * sheen;
    float shade = saturate(dot(uv, float2(0.6, 0.8))) * 0.5;
    float bodyLight = sheen * 0.13 * (0.4 + 0.6 * p3.z) * (1 - ringA);
    float bodyShade = shade * shade * 0.10 * (1 - ringA);

    float a = saturate(ringA + spec + bodyLight + bodyShade) * mask;
    float3 rgb = (col * ringA + spec.xxx + bodyLight.xxx + float3(0, 0, 0.01) * bodyShade) * mask;
    return float4(rgb, a);
}
```

## Packages
Vortice.Direct3D11, Vortice.DXGI, Vortice.DirectComposition (3.6.2), .NET 8 WPF (net8.0-windows10.0.19041.0).

## When reusing
Copy GpuBlurBackdrop.cs, LensRenderer.cs, Shaders/*, the NativeMethods they use, then wire: SetBounds(window rect) on every move/resize, SetShape(x,y,w,h,radius,blur,opacity) every animation frame, SetLens(true, strength, shine) once. Verify by eye: bottom edge (no streaks), corners (no seam), edge (no doubled line).
