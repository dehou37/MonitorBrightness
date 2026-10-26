
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MonitorBrightness
{
    public partial class MainWindow : Window
    {
        // Windows global hotkey functions
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);

        // Hotkey IDs
        private const int HOTKEY_DECREASE = 1;
        private const int HOTKEY_INCREASE = 2;

        // Modifier keys
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int WM_HOTKEY = 0x0312;

        private const uint VK_J = 0x4A;
        private const uint VK_K = 0x4B;

        // Existing monitor service
        private readonly MonitorService _service;

        // Timer used to apply brightness changes
        private readonly DispatcherTimer _applyTimer;

        // Timer used to hide the popup after two seconds
        private readonly DispatcherTimer _popupTimer;

        private HwndSource? _hotkeySource;
        private Forms.NotifyIcon? _trayIcon;
        private Window? _brightnessPopup;

        private bool _loading = true;
        private bool _isExiting;
        private bool _decreaseRegistered;
        private bool _increaseRegistered;

        private MonitorDevice? SelectedMonitor =>
            MonitorList.SelectedItem as MonitorDevice;

        public MainWindow()
        {
            InitializeComponent();

            _service = new MonitorService();

            _applyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(60)
            };

            _applyTimer.Tick += ApplyTimer_Tick;

            _popupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            _popupTimer.Tick += PopupTimer_Tick;

            InitializeTrayIcon();

            // Load the available monitors.
            MonitorList.ItemsSource = _service.Monitors;

            // Allow the selection event to load actual brightness.
            _loading = false;

            if (MonitorList.Items.Count > 0)
            {
                MonitorList.SelectedIndex = 0;
            }
            else
            {
                StatusText.Text =
                    "No compatible monitors found. Check that DDC/CI is enabled.";
            }

            Closed += MainWindow_Closed;
        }

        // =====================================================
        // SYSTEM TRAY
        // =====================================================

        private void InitializeTrayIcon()
        {
            var menu = new Forms.ContextMenuStrip();

            menu.Items.Add(
                "Open Monitor Brightness",
                null,
                (_, _) => ShowMainWindow());

            menu.Items.Add(
                "Exit",
                null,
                (_, _) =>
                {
                    _isExiting = true;
                    Close();
                });

            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Monitor Brightness",
                Icon = Drawing.SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            ShowInTaskbar = true;

            if (!IsVisible)
            {
                Show();
            }

            WindowState = WindowState.Normal;
            Activate();
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void MainWindow_StateChanged(
            object? sender,
            EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                HideToTray();
            }
        }

        private void MainWindow_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (!_isExiting)
            {
                // Clicking X hides the app instead of exiting.
                e.Cancel = true;
                HideToTray();
            }
        }

        // =====================================================
        // GLOBAL KEYBOARD SHORTCUTS
        // =====================================================

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hotkeySource = HwndSource.FromHwnd(
                new WindowInteropHelper(this).Handle);

            if (_hotkeySource == null)
            {
                StatusText.Text =
                    "Could not initialize keyboard shortcuts.";
                return;
            }

            _hotkeySource.AddHook(HotkeyHook);

            uint modifiers =
                MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;

            _decreaseRegistered = RegisterHotKey(
                _hotkeySource.Handle,
                HOTKEY_DECREASE,
                modifiers,
                VK_J);

            _increaseRegistered = RegisterHotKey(
                _hotkeySource.Handle,
                HOTKEY_INCREASE,
                modifiers,
                VK_K);

            if (!_decreaseRegistered || !_increaseRegistered)
            {
                StatusText.Text =
                    "Warning: one or more shortcuts could not be registered. " +
                    "Another application may already be using them.";
            }
            else if (MonitorList.Items.Count > 0)
            {
                StatusText.Text =
                    "Ready. Ctrl+Alt+J: -5% | Ctrl+Alt+K: +5%.";
            }
        }

        private IntPtr HotkeyHook(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();

                if (id == HOTKEY_DECREASE)
                {
                    ChangeBrightness(-5);
                    handled = true;
                }
                else if (id == HOTKEY_INCREASE)
                {
                    ChangeBrightness(5);
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private void ChangeBrightness(int amount)
        {
            if (SelectedMonitor == null)
            {
                StatusText.Text = "Select a monitor first.";
                return;
            }

            double newValue = Math.Clamp(
                BrightnessSlider.Value + amount,
                BrightnessSlider.Minimum,
                BrightnessSlider.Maximum);

            BrightnessSlider.Value = newValue;
            BrightnessLabel.Text = $"{newValue:0}%";

            // The slider event schedules the actual hardware update.
            // The popup is shown after that update succeeds.
        }

        // =====================================================
        // MONITOR SELECTION
        // =====================================================

        private void MonitorList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            _applyTimer.Stop();

            if (SelectedMonitor == null)
            {
                StatusText.Text = "No monitor selected.";
                return;
            }

            try
            {
                _loading = true;

                int brightness = SelectedMonitor.GetBrightness();

                BrightnessSlider.Value = Math.Clamp(
                    brightness,
                    (int)BrightnessSlider.Minimum,
                    (int)BrightnessSlider.Maximum);

                BrightnessLabel.Text =
                    $"{BrightnessSlider.Value:0}%";

                StatusText.Text = "Monitor selected.";
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    $"Could not read monitor brightness: {ex.Message}";
            }
            finally
            {
                _loading = false;
            }
        }

        // =====================================================
        // BRIGHTNESS SLIDER AND HARDWARE UPDATE
        // =====================================================

        private void BrightnessSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading)
                return;

            BrightnessLabel.Text =
                $"{BrightnessSlider.Value:0}%";

            if (SelectedMonitor == null)
                return;

            // Restart the timer to avoid excessive hardware calls
            // while the slider is moving.
            _applyTimer.Stop();
            _applyTimer.Start();
        }

        private void ApplyTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _applyTimer.Stop();

            if (SelectedMonitor == null)
                return;

            try
            {
                int brightness = (int)Math.Round(
                    BrightnessSlider.Value);

                SelectedMonitor.SetBrightness(brightness);

                BrightnessLabel.Text = $"{brightness}%";
                StatusText.Text =
                    $"Brightness set to {brightness}%.";

                // Display the percentage after a successful update.
                ShowBrightnessPopup(brightness);
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    $"Could not set monitor brightness: {ex.Message}";
            }
        }

        // =====================================================
        // BRIGHTNESS POPUP
        // =====================================================

        private void ShowBrightnessPopup(int brightness)
        {
            // Reset the countdown if another brightness change occurs.
            _popupTimer.Stop();

            if (_brightnessPopup != null)
            {
                Window oldPopup = _brightnessPopup;
                _brightnessPopup = null;
                oldPopup.Close();
            }

            var text = new TextBlock
            {
                Text = $"Brightness: {brightness}%",
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(40, 40, 40)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(24, 16, 24, 16),
                Child = text
            };

            var popup = new Window
            {
                Width = 250,
                Height = 85,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = border
            };

            // Position near the bottom-right of the primary screen.
            Rect workArea = SystemParameters.WorkArea;

            popup.Left = workArea.Right - popup.Width - 24;
            popup.Top = workArea.Bottom - popup.Height - 24;

            _brightnessPopup = popup;
            popup.Show();

            _popupTimer.Start();
        }

        private void PopupTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _popupTimer.Stop();

            if (_brightnessPopup != null)
            {
                Window oldPopup = _brightnessPopup;
                _brightnessPopup = null;
                oldPopup.Close();
            }
        }

        // =====================================================
        // PRESET BUTTONS
        // =====================================================

        private void Preset_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button ||
                button.Tag == null)
            {
                return;
            }

            if (double.TryParse(
                button.Tag.ToString(),
                out double brightness))
            {
                BrightnessSlider.Value = Math.Clamp(
                    brightness,
                    BrightnessSlider.Minimum,
                    BrightnessSlider.Maximum);
            }
        }

        // =====================================================
        // CLEANUP
        // =====================================================

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _applyTimer.Stop();
            _popupTimer.Stop();

            if (_brightnessPopup != null)
            {
                Window oldPopup = _brightnessPopup;
                _brightnessPopup = null;
                oldPopup.Close();
            }

            if (_hotkeySource != null)
            {
                IntPtr handle = _hotkeySource.Handle;

                if (_decreaseRegistered)
                {
                    UnregisterHotKey(
                        handle,
                        HOTKEY_DECREASE);
                }

                if (_increaseRegistered)
                {
                    UnregisterHotKey(
                        handle,
                        HOTKEY_INCREASE);
                }

                _hotkeySource.RemoveHook(HotkeyHook);
                _hotkeySource = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _service.Dispose();
        }
    }
}
