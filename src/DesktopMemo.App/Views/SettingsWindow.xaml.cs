using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopMemo.App.ViewModels;
using DesktopMemo.Core.Contracts;
using DesktopMemo.Core.Models;

namespace DesktopMemo.App.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ILogService? _logService;
    private bool _isApplyingBounds;

    public SettingsWindow(MainViewModel viewModel)
    {
        // 1. 立即附加 Dispatcher 异常处理器（在 InitializeComponent 之前）
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;

        // 2. 提前获取日志服务
        ILogService? logService = null;
        try
        {
            if (System.Windows.Application.Current is App app)
            {
                logService = app.Services.GetService(typeof(ILogService)) as ILogService;
            }
            _logService = logService;
            _logService?.Info("Settings", "开始构造设置窗口");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] 获取日志服务失败: {ex.Message}");
        }

        // 3. 包裹 InitializeComponent 在 try/catch 中
        try
        {
            _logService?.Debug("Settings", "开始 InitializeComponent");
            InitializeComponent();
            _logService?.Debug("Settings", "InitializeComponent 完成");
        }
        catch (System.Windows.Markup.XamlParseException xamlEx)
        {
            _logService?.Error("Settings", "XAML 解析失败", xamlEx);
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] XAML解析异常: {xamlEx.Message}");
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] 行号: {xamlEx.LineNumber}, 位置: {xamlEx.LinePosition}");
            throw; // 重新抛出，让全局处理器处理
        }
        catch (InvalidOperationException invEx)
        {
            _logService?.Error("Settings", "InitializeComponent 无效操作", invEx);
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] InvalidOperationException: {invEx.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logService?.Error("Settings", "InitializeComponent 未知异常", ex);
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] 未知异常: {ex}");
            throw;
        }

        // 4. 验证 ViewModel 并设置 DataContext
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        // 5. 附加其他事件处理器
        try
        {
            Loaded += OnLoaded;
            Closing += OnClosing;
            Closed += OnClosed;
            LocationChanged += OnLocationChanged;
            SizeChanged += OnSizeChanged;
            _viewModel.ThemeChanged += OnThemeChanged;

            _logService?.Info("Settings", "设置窗口构造完成");
        }
        catch (Exception ex)
        {
            _logService?.Error("Settings", "附加事件处理器失败", ex);
            throw;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _logService?.Info("Settings", $"设置窗口已加载: Size={Width}x{Height}, Pos={Left},{Top}, IsLoaded={IsLoaded}");
            ApplySavedBounds(_viewModel.WindowSettings);
            ApplyTheme(_viewModel.SelectedTheme);
            _logService?.Info("Settings", $"设置窗口应用保存尺寸完成: Size={Width}x{Height}, Pos={Left},{Top}");
        }
        catch (Exception ex)
        {
            _logService?.Error("Settings", "OnLoaded 处理失败", ex);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _logService?.Info("Settings", $"设置窗口正在关闭: Size={Width}x{Height}, Pos={Left},{Top}, IsLoaded={IsLoaded}");
            _viewModel.ThemeChanged -= OnThemeChanged;
            Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
            _viewModel.IsLogPanelVisible = false;
            _viewModel.UpdateSettingsWindowBounds(Width, Height, Left, Top, immediateSave: true);
            _logService?.Debug("Settings", "设置窗口关闭前清理完成");
        }
        catch (Exception ex)
        {
            _logService?.Error("Settings", "OnClosing 处理失败", ex);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            _logService?.Info("Settings", $"设置窗口已关闭: IsLoaded={IsLoaded}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] OnClosed 异常: {ex.Message}");
        }
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logService?.Error("Settings", "设置窗口未处理异常", e.Exception);
        e.Handled = true; // 防止异常继续传播

        // 尝试安全关闭窗口
        try
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] 捕获异常，尝试安全关闭: {e.Exception.Message}");
            Close();
        }
        catch
        {
            // 忽略关闭失败
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_isApplyingBounds || !IsLoaded)
        {
            return;
        }

        _viewModel.UpdateSettingsWindowBounds(Width, Height, Left, Top);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isApplyingBounds || !IsLoaded)
        {
            return;
        }

        _viewModel.UpdateSettingsWindowBounds(Width, Height, Left, Top);
    }

    private void ApplySavedBounds(WindowSettings settings)
    {
        _isApplyingBounds = true;
        try
        {
            if (!double.IsNaN(settings.SettingsWindowWidth) &&
                !double.IsInfinity(settings.SettingsWindowWidth) &&
                settings.SettingsWindowWidth > 0)
            {
                Width = settings.SettingsWindowWidth;
            }

            if (!double.IsNaN(settings.SettingsWindowHeight) &&
                !double.IsInfinity(settings.SettingsWindowHeight) &&
                settings.SettingsWindowHeight > 0)
            {
                Height = settings.SettingsWindowHeight;
            }

            if (!double.IsNaN(settings.SettingsWindowLeft) &&
                !double.IsInfinity(settings.SettingsWindowLeft) &&
                !double.IsNaN(settings.SettingsWindowTop) &&
                !double.IsInfinity(settings.SettingsWindowTop))
            {
                var virtualLeft = SystemParameters.VirtualScreenLeft;
                var virtualTop = SystemParameters.VirtualScreenTop;
                var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
                var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

                var targetLeft = settings.SettingsWindowLeft;
                var targetTop = settings.SettingsWindowTop;

                if (targetLeft >= virtualLeft &&
                    targetLeft <= virtualRight - 100 &&
                    targetTop >= virtualTop &&
                    targetTop <= virtualBottom - 100)
                {
                    Left = targetLeft;
                    Top = targetTop;
                }
            }
        }
        finally
        {
            _isApplyingBounds = false;
        }
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        Dispatcher.Invoke(() => ApplyTheme(theme));
    }

    private void ApplyTheme(AppTheme theme)
    {
        var actualTheme = theme;
        if (theme == AppTheme.System)
        {
            actualTheme = IsSystemDarkMode() ? AppTheme.Dark : AppTheme.Light;
        }

        if (SettingsOpacitySlider != null)
        {
            SettingsOpacitySlider.Style = actualTheme == AppTheme.Dark
                ? (Style)FindResource("AppleSliderStyleDark")
                : (Style)FindResource("AppleSliderStyle");
        }
    }

    private bool IsSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    private void SettingsOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_viewModel == null)
        {
            return;
        }

        _viewModel.UpdateBackgroundOpacityFromPercent(e.NewValue);
    }
}
