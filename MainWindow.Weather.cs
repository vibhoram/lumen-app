// D-Island (personal fork) - the idle weather as its own round bubble beside the clock pill.

using System.Windows;
using DIsland.Services;

namespace DIsland;

public partial class MainWindow
{
    private string? _idleWeatherKey;

    private void UpdateIdleWeatherBubble()
    {
        if (_state is null || IdleWeatherBubble is null) return;
        var weather = _settings.WeatherEnabled && _settings.WeatherInIdlePill && _settings.WeatherSeparate ? _weather.Current : null;
        var show = weather is not null && _currentView == ClockView && !_state.IsExpanded && !IslandHidden && !BarIsIdle
                   && _settings.IdleStyle != IdleStyle.Minimal && _state.Transient is null;
        if (show && weather is not null)
        {
            var key = $"{weather.Kind}|{weather.IsDay}|{Degrees(weather.Temperature)}";
            if (key != _idleWeatherKey)
            {
                _idleWeatherKey = key;
                IdleWeatherIcon.Child = WeatherIcons.Create(weather.Kind, weather.IsDay, 16);
                IdleWeatherTemp.Text = Degrees(weather.Temperature);
                IdleWeatherBubble.ToolTip = $"{weather.Condition}, feels like {Degrees(weather.FeelsLike)}";
            }
        }
        if (ShowBubble(IdleWeatherBubble, show)) ApplyGeometry();
    }
}
