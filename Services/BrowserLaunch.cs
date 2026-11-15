// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Diagnostics;
using System.IO;

namespace DIsland.Services;

/// <summary>Open a URL in a specific browser (e.g. Perplexity Comet) instead of the Windows default.</summary>
public static class BrowserLaunch
{
    public const string Comet = "comet";

    /// <summary>
    /// Opens <paramref name="url"/>. When <paramref name="browser"/> is "comet", uses Comet if installed;
    /// otherwise falls back to the system default browser.
    /// </summary>
    public static void Open(string url, string? browser = null)
    {
        try
        {
            if (string.Equals(browser, Comet, StringComparison.OrdinalIgnoreCase)
                && FindComet() is { } comet)
            {
                Process.Start(new ProcessStartInfo(comet, Quote(url)) { UseShellExecute = false })?.Dispose();
                return;
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
            catch (Exception) { /* nowhere to open */ }
        }
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    public static string? FindComet()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Perplexity", "Comet", "Application", "comet.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Perplexity", "Comet", "Application", "comet.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Perplexity", "Comet", "Application", "comet.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Comet", "Application", "comet.exe"),
                 })
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
