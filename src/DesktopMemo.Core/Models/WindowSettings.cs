using System;
using DesktopMemo.Core.Constants;

namespace DesktopMemo.Core.Models;

/// <summary>
/// 表示窗口相关的用户设置。
/// </summary>
public sealed record WindowSettings(
    double Width,
    double Height,
    double Left,
    double Top,
    double Transparency,
    bool IsTopMost,
    bool IsDesktopMode,
    bool IsClickThrough,
    bool ShowDeleteConfirmation = true,
    bool ShowExitConfirmation = true,
    bool DefaultExitToTray = true,
    string PreferredLanguage = "zh-CN",
    string CurrentPage = "memo",
    bool TodoInputVisible = false,
    AppTheme Theme = AppTheme.Light,
    bool IsWindowPinned = false,
    bool IsTrayEnabled = true,
    double SettingsWindowWidth = double.NaN,
    double SettingsWindowHeight = double.NaN,
    double SettingsWindowLeft = double.NaN,
    double SettingsWindowTop = double.NaN)
{
    public static WindowSettings Default => new(380, 300, double.NaN, double.NaN, WindowConstants.DEFAULT_TRANSPARENCY, false, true, false, true, true, true, "zh-CN", "memo", false, AppTheme.System, false, true, double.NaN, double.NaN, double.NaN, double.NaN);

    public WindowSettings WithLocation(double left, double top)
        => this with { Left = left, Top = top };

    public WindowSettings WithSize(double width, double height)
        => this with { Width = width, Height = height };

    public WindowSettings WithAppearance(double transparency, bool topMost, bool desktopMode, bool clickThrough)
        => this with
        {
            Transparency = transparency,
            IsTopMost = topMost,
            IsDesktopMode = desktopMode,
            IsClickThrough = clickThrough
        };
}

