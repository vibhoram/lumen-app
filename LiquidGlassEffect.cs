// D-Island (personal fork) - Liquid Glass refraction pixel shader (see Shaders/LiquidGlass.fx).

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace DIsland;

/// <summary>Bends, disperses and lights a screen copy through the island's rounded-rectangle shape.</summary>
public sealed class LiquidGlassEffect : ShaderEffect
{
    private static readonly PixelShader Shader = new() { UriSource = new Uri("pack://application:,,,/Shaders/LiquidGlass.ps") };

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(LiquidGlassEffect), 0);
    public static readonly DependencyProperty RectProperty = Reg(nameof(Rect), 0);
    public static readonly DependencyProperty P1Property = Reg(nameof(P1), 1);
    public static readonly DependencyProperty P2Property = Reg(nameof(P2), 2);
    public static readonly DependencyProperty P3Property = Reg(nameof(P3), 3);

    public LiquidGlassEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(RectProperty);
        UpdateShaderValue(P1Property);
        UpdateShaderValue(P2Property);
        UpdateShaderValue(P3Property);
    }

    private static DependencyProperty Reg(string name, int register) =>
        DependencyProperty.Register(name, typeof(Point4D), typeof(LiquidGlassEffect),
            new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(register)));

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    /// <summary>Pill centre x, y and half width, height, in capture pixels.</summary>
    public Point4D Rect { get => (Point4D)GetValue(RectProperty); set => SetValue(RectProperty, value); }
    /// <summary>Corner radius, capture width, capture height, refraction strength (px).</summary>
    public Point4D P1 { get => (Point4D)GetValue(P1Property); set => SetValue(P1Property, value); }
    /// <summary>Bevel width (px), blur radius (px), light angle (rad), tint.</summary>
    public Point4D P2 { get => (Point4D)GetValue(P2Property); set => SetValue(P2Property, value); }
    /// <summary>Shininess, dispersion, saturation, unused.</summary>
    public Point4D P3 { get => (Point4D)GetValue(P3Property); set => SetValue(P3Property, value); }
}
