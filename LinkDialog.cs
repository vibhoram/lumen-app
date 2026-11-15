// D-Island (personal fork) - a small dialog to add a quick link (name + web address).

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DIsland;

public sealed class LinkDialog : Window
{
    private readonly TextBox _name = new() { Padding = new Thickness(8, 6, 8, 6) };
    private readonly TextBox _url = new() { Padding = new Thickness(8, 6, 8, 6) };
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x60)), FontSize = 12, Visibility = Visibility.Collapsed };

    public string LinkName => _name.Text.Trim();
    public string LinkUrl { get; private set; } = "";

    public LinkDialog(string? prefillUrl = null)
    {
        Title = "Add a link";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x20));
        Foreground = Brushes.White;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var soft = Application.Current.TryFindResource("SoftField") as Style;
        if (soft is not null) { _name.Style = soft; _url.Style = soft; }
        if (!string.IsNullOrEmpty(prefillUrl)) _url.Text = prefillUrl;

        var ok = new Button { Content = "Add", Padding = new Thickness(18, 6, 18, 6), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
        ok.Click += (_, _) => Accept();
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Children =
            {
                new TextBlock { Text = "Add a link to the Apps page", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) },
                new TextBlock { Text = "Name", FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 0, 0, 3) },
                _name,
                new TextBlock { Text = "Web address", FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 10, 0, 3) },
                _url,
                new TextBlock { Text = "Tip: for a second Gmail account use https://mail.google.com/mail/u/1/", FontSize = 11, Opacity = 0.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap },
                _error,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { ok, cancel } },
            },
        };
        Loaded += (_, _) => (string.IsNullOrEmpty(prefillUrl) ? _url : _name).Focus();
    }

    private void Accept()
    {
        var url = _url.Text.Trim();
        if (url.Length > 0 && !url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.'))
        {
            _error.Text = "That doesn't look like a web address.";
            _error.Visibility = Visibility.Visible;
            return;
        }
        LinkUrl = uri.ToString();
        if (_name.Text.Trim().Length == 0) _name.Text = uri.Host.Replace("www.", "");
        DialogResult = true;
    }
}
