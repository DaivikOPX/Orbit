using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using Path = System.IO.Path;
using Image = System.Drawing.Image;
using Point = System.Windows.Point;

namespace SaturnWindows {
    // === WIN32 API HELPERS ===
    internal static class Win32 {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public const uint GA_PARENT = 1;
        public const uint GA_ROOT = 2;
        public const uint GA_ROOTOWNER = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT {
            public int X;
            public int Y;
        }

        public const int VK_LBUTTON = 0x01;
        public const int VK_ESCAPE  = 0x1B;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU    = 0x12; // Alt
        public const int VK_F8      = 0x77;
    }

    // === IN-MEMORY LOW-LATENCY SYNTHESIZED SOUNDS ===
    public static class SoundFX {
        private static SoundPlayer tickPlayer;
        private static SoundPlayer chimePlayer;

        public static void Init() {
            try {
                tickPlayer = CreateSoundPlayer(CreateTickWav());
                chimePlayer = CreateSoundPlayer(CreateChimeWav());
            } catch { }
        }

        public static void PlayTick() {
            try {
                if (tickPlayer != null) tickPlayer.Play();
            } catch { }
        }

        public static void PlayChime() {
            try {
                if (chimePlayer != null) chimePlayer.Play();
            } catch { }
        }

        private static SoundPlayer CreateSoundPlayer(byte[] wavBytes) {
            var ms = new MemoryStream(wavBytes);
            var sp = new SoundPlayer(ms);
            sp.Load();
            return sp;
        }

        private static byte[] CreateTickWav() {
            int sampleRate = 44100;
            int samples = (int)(sampleRate * 0.014); // 14ms subtle tactile click
            short[] buffer = new short[samples];
            for (int i = 0; i < samples; i++) {
                double t = (double)i / sampleRate;
                double env = Math.Exp(-t * 350.0);
                double wave = Math.Sin(2.0 * Math.PI * 1600.0 * t);
                buffer[i] = (short)(wave * env * 11000);
            }
            return EncodeWav(buffer, sampleRate);
        }

        private static byte[] CreateChimeWav() {
            int sampleRate = 44100;
            int samples = (int)(sampleRate * 0.14); // 140ms rich chord
            short[] buffer = new short[samples];
            for (int i = 0; i < samples; i++) {
                double t = (double)i / sampleRate;
                double env = Math.Exp(-t * 22.0);
                double wave = (Math.Sin(2.0 * Math.PI * 659.25 * t) +
                               Math.Sin(2.0 * Math.PI * 880.00 * t) +
                               Math.Sin(2.0 * Math.PI * 1318.51 * t)) / 3.0;
                buffer[i] = (short)(wave * env * 15000);
            }
            return EncodeWav(buffer, sampleRate);
        }

        private static byte[] EncodeWav(short[] samples, int sampleRate) {
            using (var ms = new MemoryStream()) {
                using (var bw = new BinaryWriter(ms)) {
                    int dataSize = samples.Length * 2;
                    bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write(dataSize + 36);
                    bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    bw.Write(16);
                    bw.Write((short)1); // PCM
                    bw.Write((short)1); // Mono
                    bw.Write(sampleRate);
                    bw.Write(sampleRate * 2);
                    bw.Write((short)2);
                    bw.Write((short)16);
                    bw.Write(Encoding.ASCII.GetBytes("data"));
                    bw.Write(dataSize);
                    for (int i = 0; i < samples.Length; i++) {
                        bw.Write(samples[i]);
                    }
                }
                return ms.ToArray();
            }
        }
    }

    // === PETAL ACTION MODEL ===
    public class PetalAction {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ExtOrId { get; set; }
        public string Mode { get; set; } // "convert", "tool", "action"
        public string Icon { get; set; }

        public PetalAction(string title, string subtitle, string extOrId, string mode = "convert", string icon = "") {
            Title = title;
            Subtitle = subtitle;
            ExtOrId = extOrId;
            Mode = mode;
            Icon = icon;
        }
    }

    // === SATURN LOGO (PIXEL-PERFECT EMBEDDED + VECTOR FALLBACK) ===
    public static class SaturnLogo {
        private static System.Windows.Media.Imaging.BitmapImage _cachedLogo;
        private const string LogoPngBase64 = "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";

        public static System.Windows.Media.Imaging.BitmapSource GetLogoSource() {
            if (_cachedLogo == null) {
                try {
                    byte[] bytes = Convert.FromBase64String(LogoPngBase64);
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = new MemoryStream(bytes);
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    _cachedLogo = bi;
                } catch { }
            }
            return _cachedLogo;
        }

        public static FrameworkElement CreateVector(double size, bool isDark = false) {
            try {
                var src = GetLogoSource();
                if (src != null) {
                    var img = new System.Windows.Controls.Image {
                        Source = src,
                        Width = size,
                        Height = size,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
                    return img;
                }
            } catch { }

            var c = new Canvas {
                Width = size,
                Height = size,
                ClipToBounds = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            double scale = size / 32.0;

            var bgSquircle = new System.Windows.Shapes.Rectangle {
                Width = size,
                Height = size,
                RadiusX = 7 * scale,
                RadiusY = 7 * scale,
                Fill = Brushes.Black
            };
            c.Children.Add(bgSquircle);

            var planet = new Ellipse {
                Width = 14 * scale,
                Height = 14 * scale,
                Fill = new LinearGradientBrush(
                    Color.FromRgb(255, 167, 38),
                    Color.FromRgb(230, 81, 0),
                    new Point(0.2, 0.1),
                    new Point(0.8, 0.9)
                )
            };
            Canvas.SetLeft(planet, 9 * scale);
            Canvas.SetTop(planet, 9 * scale);
            c.Children.Add(planet);

            var ring = new Ellipse {
                Width = 26 * scale,
                Height = 8 * scale,
                Stroke = new LinearGradientBrush(
                    Color.FromRgb(255, 109, 0),
                    Color.FromRgb(255, 183, 77),
                    new Point(0, 0),
                    new Point(1, 1)
                ),
                StrokeThickness = 2.2 * scale,
                RenderTransform = new RotateTransform(-28, 13 * scale, 4 * scale)
            };
            Canvas.SetLeft(ring, 3 * scale);
            Canvas.SetTop(ring, 12 * scale);
            c.Children.Add(ring);

            var moon = new Ellipse {
                Width = 4.2 * scale,
                Height = 4.2 * scale,
                Fill = new SolidColorBrush(Color.FromRgb(255, 167, 38))
            };
            Canvas.SetLeft(moon, 22 * scale);
            Canvas.SetTop(moon, 7 * scale);
            c.Children.Add(moon);

            return c;
        }
    }
    // === FLUENT SATURN RESULT TOAST CARD (PURE BLACK THEME) ===
    public class SaturnToastWindow : Window {
        private TextBlock titleText;
        private TextBlock descText;
        private System.Windows.Controls.Button btnOpen;
        private System.Windows.Controls.Button btnReveal;
        private string targetPath;
        private System.Windows.Threading.DispatcherTimer dismissTimer;

        public SaturnToastWindow() {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Width = 350;
            Height = 114;

            var card = new Border {
                Background = new SolidColorBrush(Color.FromArgb(252, 0, 0, 0)), // Pure Pitch Black (#000000)
                CornerRadius = new CornerRadius(14),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 36, 36, 40)), // Sleek Dark Obsidian Rim
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(15, 13, 15, 13),
                Effect = new DropShadowEffect {
                    BlurRadius = 32,
                    Opacity = 0.75,
                    ShadowDepth = 6,
                    Color = Color.FromRgb(0, 0, 0)
                }
            };

            var rootStack = new StackPanel();

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            var titleStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(SaturnLogo.CreateVector(19, true));

            titleText = new TextBlock {
                Text = "Action Completed",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)), // Radiant Saturn Gold/Orange
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(titleText);
            Grid.SetColumn(titleStack, 0);
            topRow.Children.Add(titleStack);

            var closeBtn = new TextBlock {
                Text = "✕",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 128)),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeBtn.MouseEnter += (s, e) => closeBtn.Foreground = Brushes.White;
            closeBtn.MouseLeave += (s, e) => closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 128));
            closeBtn.MouseDown += (s, e) => FadeOutAndHide();
            Grid.SetColumn(closeBtn, 1);
            topRow.Children.Add(closeBtn);

            rootStack.Children.Add(topRow);

            descText = new TextBlock {
                Text = "Saved output file.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(215, 215, 220)),
                Margin = new Thickness(0, 5, 0, 9),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            rootStack.Children.Add(descText);

            var btnRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            btnOpen = new System.Windows.Controls.Button {
                Content = "Open File",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(255, 122, 0)), // Saturn Radiant Orange
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10.5,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnOpen.Click += (s, e) => {
                try {
                    if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath)) {
                        Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
                    }
                } catch (Exception ex) {
                    SaturnLog.LogException("btnOpen.Click", ex);
                }
                FadeOutAndHide();
            };
            btnRow.Children.Add(btnOpen);

            btnReveal = new System.Windows.Controls.Button {
                Content = "Show in Folder",
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(Color.FromArgb(255, 24, 24, 27)), // Obsidian black
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 52, 52, 58)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 225)),
                FontSize = 10.5,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnReveal.Click += (s, e) => {
                try {
                    if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath)) {
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", targetPath));
                    }
                } catch (Exception ex) {
                    SaturnLog.LogException("btnReveal.Click", ex);
                }
                FadeOutAndHide();
            };
            btnRow.Children.Add(btnReveal);

            rootStack.Children.Add(btnRow);
            card.Child = rootStack;
            Content = card;

            dismissTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            dismissTimer.Tick += (s, e) => {
                dismissTimer.Stop();
                FadeOutAndHide();
            };
        }

        public void ShowResult(string title, string details, string path) {
            bool isError = title.Contains("✕") || title.ToLower().Contains("fail") || title.ToLower().Contains("error");
            titleText.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
                : new SolidColorBrush(Color.FromRgb(255, 140, 0));
            titleText.Text = title;
            descText.Text = details;
            targetPath = path;

            bool isFile = !string.IsNullOrEmpty(path) && File.Exists(path);
            btnOpen.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
            btnReveal.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;

            Opacity = 0.0;
            Show();

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);

            dismissTimer.Stop();
            dismissTimer.Start();
        }

        private void FadeOutAndHide() {
            var fadeOut = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(200)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) => Hide();
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
    // === SMOOTH FULLSCREEN DISMISSIBLE RADIAL OVERLAY ===
    public class RadialWheelWindow : Window {
        private Canvas rootCanvas;
        private Canvas wheelCanvas;
        private TransformGroup wheelTransforms;
        private ScaleTransform wheelScale;

        private Border centerHub;
        private TextBlock hubBadge;
        private TextBlock hubActionName;
        private TextBlock hubFileName;

        private List<System.Windows.Shapes.Path> petalPaths = new List<System.Windows.Shapes.Path>();
        private List<System.Windows.Shapes.Path> petalActiveOverlays = new List<System.Windows.Shapes.Path>();
        private List<ScaleTransform> petalBaseScales = new List<ScaleTransform>();
        private List<ScaleTransform> petalActiveScales = new List<ScaleTransform>();
        private List<TranslateTransform> petalContentTranslates = new List<TranslateTransform>();
        private List<double> petalMidAngles = new List<double>();
        private List<StackPanel> petalContents = new List<StackPanel>();
        private List<TextBlock> petalTitles = new List<TextBlock>();
        private List<TextBlock> petalSubs = new List<TextBlock>();
        private List<PetalAction> currentItems = new List<PetalAction>();

        private int currentHoverIndex = -1;
        private bool isClosing = false;

        // Radiant Indigo Palette
        private SolidColorBrush plateBg = new SolidColorBrush(Color.FromArgb(248, 255, 255, 255));
        private SolidColorBrush plateBorder = new SolidColorBrush(Color.FromArgb(50, 203, 213, 225));
        private SolidColorBrush petalNormalFill = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        private SolidColorBrush petalNormalStroke = new SolidColorBrush(Color.FromArgb(30, 148, 163, 184));

        private LinearGradientBrush petalActiveGrad;
        private LinearGradientBrush centerHubGrad;

        public string ActiveFilePath { get; set; }
        public Action<PetalAction, string> OnPetalChosen;
        public Action OnWheelClosed;
        public Canvas WheelCanvas { get { return wheelCanvas; } }

        public RadialWheelWindow() {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            // 1-alpha transparent catcher to guarantee immediate outside dismiss anywhere on screen
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            WindowState = WindowState.Maximized;
            AllowDrop = true;

            // Gradient for active petal: Vibrant Indigo to Electric Violet
            petalActiveGrad = new LinearGradientBrush();
            petalActiveGrad.StartPoint = new Point(0, 0);
            petalActiveGrad.EndPoint = new Point(1, 1);
            petalActiveGrad.GradientStops.Add(new GradientStop(Color.FromRgb(99, 102, 241), 0.0));  // Indigo 500
            petalActiveGrad.GradientStops.Add(new GradientStop(Color.FromRgb(139, 92, 246), 1.0)); // Violet 500

            // Obsidian Dark Glass Center Hub
            centerHubGrad = new LinearGradientBrush();
            centerHubGrad.StartPoint = new Point(0, 0);
            centerHubGrad.EndPoint = new Point(1, 1);
            centerHubGrad.GradientStops.Add(new GradientStop(Color.FromRgb(10, 10, 12), 0.0));
            centerHubGrad.GradientStops.Add(new GradientStop(Color.FromRgb(18, 18, 24), 1.0));

            rootCanvas = new Canvas { Background = Brushes.Transparent, AllowDrop = true };
            Content = rootCanvas;

            // 360x360 Wheel Container
            wheelCanvas = new Canvas {
                Width = 360,
                Height = 360,
                Background = Brushes.Transparent,
                AllowDrop = true,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            wheelTransforms = new TransformGroup();
            wheelScale = new ScaleTransform(1.0, 1.0);
            wheelTransforms.Children.Add(wheelScale);
            wheelCanvas.RenderTransform = wheelTransforms;

            rootCanvas.Children.Add(wheelCanvas);

            // Center Hub (Obsidian Glass Orb)
            centerHub = new Border {
                Width = 98,
                Height = 98,
                CornerRadius = new CornerRadius(49),
                Background = centerHubGrad,
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 39, 39, 46)),
                BorderThickness = new Thickness(1.5),
                Cursor = System.Windows.Input.Cursors.Hand,
                Effect = new DropShadowEffect {
                    BlurRadius = 24,
                    Opacity = 0.38,
                    ShadowDepth = 4,
                    Color = Color.FromRgb(0, 0, 0)
                }
            };

            var hubStack = new StackPanel {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var hubLogo = SaturnLogo.CreateVector(20, true);
            hubLogo.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            hubLogo.Margin = new Thickness(0, 0, 0, 2);

            hubBadge = new TextBlock {
                Text = "SATURN",
                FontWeight = FontWeights.ExtraBold,
                FontSize = 8.5,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            hubActionName = new TextBlock {
                Text = "SELECT",
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                Foreground = Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 1, 0, 1)
            };

            hubFileName = new TextBlock {
                Text = "✕ Close",
                FontSize = 8.5,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 78
            };

            hubStack.Children.Add(hubLogo);
            hubStack.Children.Add(hubBadge);
            hubStack.Children.Add(hubActionName);
            hubStack.Children.Add(hubFileName);
            centerHub.Child = hubStack;

            // Center hub click closes or browses
            centerHub.MouseDown += (s, e) => {
                e.Handled = true;
                if (string.IsNullOrEmpty(ActiveFilePath)) {
                    BrowseForFileAndOpen();
                } else {
                    CloseWithAnimation();
                }
            };

            // Clicking ANYWHERE on the screen outside the wheel immediately closes
            rootCanvas.MouseDown += (s, e) => {
                CloseWithAnimation();
            };

            wheelCanvas.MouseDown += (s, e) => {
                Point p = e.GetPosition(wheelCanvas);
                double dist = Math.Sqrt(Math.Pow(p.X - 180, 2) + Math.Pow(p.Y - 180, 2));
                if (dist > 165 || dist < 48) {
                    CloseWithAnimation();
                }
            };

            // Real-time hover tracking when mouse moves across the wheel
            wheelCanvas.MouseMove += (s, e) => {
                if (currentItems.Count == 0) return;
                Point p = e.GetPosition(wheelCanvas);
                double dx = p.X - 180;
                double dy = p.Y - 180;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist >= 48 && dist <= 165) {
                    double count = currentItems.Count;
                    double angleStep = (2 * Math.PI) / count;
                    double rotationOffset = -Math.PI / 2.0 - (angleStep / 2.0);
                    double angle = Math.Atan2(dy, dx);
                    double relAngle = angle - rotationOffset;
                    while (relAngle < 0) relAngle += 2 * Math.PI;
                    while (relAngle >= 2 * Math.PI) relAngle -= 2 * Math.PI;
                    int idx = (int)(relAngle / angleStep) % currentItems.Count;
                    if (idx >= 0 && idx < currentItems.Count) {
                        HighlightPetal(idx);
                    }
                }
            };

            PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Escape) {
                    CloseWithAnimation();
                }
            };

            DragOver += (s, e) => {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
            };

            Drop += OnWindowDrop;
        }

        public void PositionAt(double dipX, double dipY) {
            double left = dipX - 180;
            double top = dipY - 180;

            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;

            if (left < 10) left = 10;
            if (top < 10) top = 10;
            if (left + 360 > screenW - 10) left = screenW - 370;
            if (top + 360 > screenH - 10) top = screenH - 370;

            Canvas.SetLeft(wheelCanvas, left);
            Canvas.SetTop(wheelCanvas, top);
        }

        public void OpenWithBloom() {
            isClosing = false;
            Opacity = 1.0;
            Topmost = true;
            Show();
            Activate();
            Focus();

            // Smooth instantaneous 140ms bloom with Quartic ease-out
            var scaleAnimX = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(140)) {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleAnimY = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(140)) {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            wheelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
            wheelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
            wheelCanvas.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        public void CloseWithAnimation() {
            if (isClosing) return;
            isClosing = true;

            var scaleAnimX = new DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(95)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleAnimY = new DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(95)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var fadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(95)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeAnim.Completed += (s, e) => {
                Hide();
                isClosing = false;
                if (OnWheelClosed != null) OnWheelClosed();
            };

            wheelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
            wheelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
            wheelCanvas.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        public void LoadPetalsForFile(string filePath, string mode) {
            ActiveFilePath = filePath;
            wheelCanvas.Children.Clear();
            petalPaths.Clear();
            petalActiveOverlays.Clear();
            petalBaseScales.Clear();
            petalActiveScales.Clear();
            petalContentTranslates.Clear();
            petalMidAngles.Clear();
            petalContents.Clear();
            petalTitles.Clear();
            petalSubs.Clear();

            string ext = !string.IsNullOrEmpty(filePath) ? Path.GetExtension(filePath).ToLower().Replace(".", "") : "jpg";
            currentItems = SaturnCatalog.GetPetalsForExtension(ext, mode);
            currentHoverIndex = -1;

            double cx = 180;
            double cy = 180;
            double rInner = 52;
            double rOuter = 158;
            int count = currentItems.Count;
            double angleStep = (2 * Math.PI) / count;

            // Translucent Glass Plate
            var plate = new Ellipse {
                Width = (rOuter + 8) * 2,
                Height = (rOuter + 8) * 2,
                Fill = plateBg,
                Stroke = plateBorder,
                StrokeThickness = 1.5,
                Effect = new DropShadowEffect {
                    BlurRadius = 32,
                    Opacity = 0.24,
                    ShadowDepth = 6,
                    Color = Color.FromRgb(15, 23, 42)
                }
            };
            Canvas.SetLeft(plate, cx - (rOuter + 8));
            Canvas.SetTop(plate, cy - (rOuter + 8));
            wheelCanvas.Children.Add(plate);

            for (int i = 0; i < count; i++) {
                var item = currentItems[i];
                double rotationOffset = -Math.PI / 2.0 - (angleStep / 2.0);
                double startAngle = i * angleStep + rotationOffset;
                double endAngle = (i + 1) * angleStep + rotationOffset;
                double midAngle = startAngle + angleStep / 2.0;

                double gap = count > 6 ? 0.038 : 0.055;
                double a1 = startAngle + gap;
                double a2 = endAngle - gap;

                Point p1 = new Point(cx + rInner * Math.Cos(a1), cy + rInner * Math.Sin(a1));
                Point p2 = new Point(cx + rOuter * Math.Cos(a1), cy + rOuter * Math.Sin(a1));
                Point p3 = new Point(cx + rOuter * Math.Cos(a2), cy + rOuter * Math.Sin(a2));
                Point p4 = new Point(cx + rInner * Math.Cos(a2), cy + rInner * Math.Sin(a2));

                PathGeometry geom = new PathGeometry();
                PathFigure fig = new PathFigure { StartPoint = p1, IsClosed = true, IsFilled = true };
                fig.Segments.Add(new LineSegment(p2, true));
                fig.Segments.Add(new ArcSegment(p3, new System.Windows.Size(rOuter, rOuter), 0, (a2 - a1) > Math.PI, SweepDirection.Clockwise, true));
                fig.Segments.Add(new LineSegment(p4, true));
                fig.Segments.Add(new ArcSegment(p1, new System.Windows.Size(rInner, rInner), 0, (a2 - a1) > Math.PI, SweepDirection.Counterclockwise, true));
                geom.Figures.Add(fig);

                var baseScale = new ScaleTransform(1.0, 1.0, cx, cy);
                var basePath = new System.Windows.Shapes.Path {
                    Data = geom,
                    Fill = petalNormalFill,
                    Stroke = petalNormalStroke,
                    StrokeThickness = 1,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    IsHitTestVisible = true,
                    RenderTransform = baseScale
                };

                var activeScale = new ScaleTransform(1.0, 1.0, cx, cy);
                var activePath = new System.Windows.Shapes.Path {
                    Data = geom,
                    Fill = petalActiveGrad,
                    Stroke = Brushes.Transparent,
                    Opacity = 0.0,
                    IsHitTestVisible = false,
                    RenderTransform = activeScale
                };

                double textRadius = (rInner + rOuter) / 2.0;
                double tx = cx + textRadius * Math.Cos(midAngle);
                double ty = cy + textRadius * Math.Sin(midAngle);

                var translate = new TranslateTransform(0.0, 0.0);
                var stack = new StackPanel {
                    IsHitTestVisible = false,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    RenderTransform = translate
                };

                var tbTitle = new TextBlock {
                    Text = item.Title,
                    FontSize = count > 7 ? 10.5 : 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                stack.Children.Add(tbTitle);

                TextBlock tbSub = null;
                if (!string.IsNullOrEmpty(item.Subtitle)) {
                    tbSub = new TextBlock {
                        Text = item.Subtitle,
                        FontSize = 8,
                        FontWeight = FontWeights.Normal,
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    };
                    stack.Children.Add(tbSub);
                }

                stack.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(stack, tx - stack.DesiredSize.Width / 2.0);
                Canvas.SetTop(stack, ty - stack.DesiredSize.Height / 2.0);

                int petalIndex = i;
                basePath.MouseEnter += (s, e) => HighlightPetal(petalIndex);
                basePath.MouseDown += (s, e) => {
                    e.Handled = true;
                    SoundFX.PlayTick();
                    ExecuteChoice(item);
                };

                petalPaths.Add(basePath);
                petalActiveOverlays.Add(activePath);
                petalBaseScales.Add(baseScale);
                petalActiveScales.Add(activeScale);
                petalContentTranslates.Add(translate);
                petalMidAngles.Add(midAngle);
                petalContents.Add(stack);
                petalTitles.Add(tbTitle);
                petalSubs.Add(tbSub);

                wheelCanvas.Children.Add(basePath);
                wheelCanvas.Children.Add(activePath);
                wheelCanvas.Children.Add(stack);
            }

            // Center Hub Position
            Canvas.SetLeft(centerHub, cx - 49);
            Canvas.SetTop(centerHub, cy - 49);
            wheelCanvas.Children.Add(centerHub);

            // Update Center Hub display
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) {
                string fn = Path.GetFileName(filePath);
                long sizeBytes = new FileInfo(filePath).Length;
                string sizeStr = sizeBytes > 1024 * 1024
                    ? string.Format("{0:0.#} MB", sizeBytes / 1048576.0)
                    : string.Format("{0} KB", Math.Max(1, sizeBytes / 1024));

                hubBadge.Text = "[" + ext.ToUpper() + "]";
                hubActionName.Text = currentItems.Count > 0 ? currentItems[0].Title : "ACTION";
                hubFileName.Text = fn.Length > 11 ? fn.Substring(0, 9) + ".." : fn;
            } else {
                hubBadge.Text = "SATURN";
                hubActionName.Text = "BROWSE";
                hubFileName.Text = "Click to pick";
            }

            if (currentItems.Count > 0) {
                HighlightPetal(0);
            }
        }

        private void HighlightPetal(int idx) {
            if (idx == currentHoverIndex) return;
            currentHoverIndex = idx;
            SoundFX.PlayTick();

            for (int i = 0; i < petalPaths.Count; i++) {
                bool isActive = (i == idx);
                double targetOpacity = isActive ? 1.0 : 0.0;
                double targetScale = isActive ? 1.026 : 1.000;

                // Smooth active gradient opacity crossfade (75ms CubicEase)
                var fadeAnim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(75)) {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                petalActiveOverlays[i].BeginAnimation(UIElement.OpacityProperty, fadeAnim);

                // Smooth tactile micro-scale outward slide (80ms QuarticEase)
                var scaleAnim = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(80)) {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                petalBaseScales[i].BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                petalBaseScales[i].BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                petalActiveScales[i].BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                petalActiveScales[i].BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

                // Smooth label outward slide along midAngle
                double midAngle = petalMidAngles[i];
                double targetShift = isActive ? 2.8 : 0.0;
                var transAnimX = new DoubleAnimation(targetShift * Math.Cos(midAngle), TimeSpan.FromMilliseconds(80)) {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                var transAnimY = new DoubleAnimation(targetShift * Math.Sin(midAngle), TimeSpan.FromMilliseconds(80)) {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                petalContentTranslates[i].BeginAnimation(TranslateTransform.XProperty, transAnimX);
                petalContentTranslates[i].BeginAnimation(TranslateTransform.YProperty, transAnimY);

                // Text colors
                var tbTitle = petalTitles[i];
                var tbSub = petalSubs[i];

                if (isActive) {
                    if (tbTitle != null) tbTitle.Foreground = Brushes.White;
                    if (tbSub != null) tbSub.Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
                    hubActionName.Text = currentItems[i].Title;
                } else {
                    if (tbTitle != null) tbTitle.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                    if (tbSub != null) tbSub.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }
            }
        }

        private void ExecuteChoice(PetalAction action) {
            CloseWithAnimation();

            string fileToProcess = ActiveFilePath;
            if (string.IsNullOrEmpty(fileToProcess) || !File.Exists(fileToProcess)) {
                var ofd = new OpenFileDialog {
                    Title = "Saturn: Select File to " + action.Title
                };
                if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    fileToProcess = ofd.FileName;
                } else {
                    return;
                }
            }

            if (OnPetalChosen != null) {
                OnPetalChosen(action, fileToProcess);
            }
        }

        private void BrowseForFileAndOpen() {
            var ofd = new OpenFileDialog {
                Title = "Saturn: Choose File"
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                ActiveFilePath = ofd.FileName;
                LoadPetalsForFile(ActiveFilePath, "convert");
            }
        }

        private void OnWindowDrop(object sender, System.Windows.DragEventArgs e) {
            e.Handled = true;
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0) {
                    ActiveFilePath = files[0];
                    Point pos = e.GetPosition(wheelCanvas);
                    double dx = pos.X - 180;
                    double dy = pos.Y - 180;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist >= 45 && dist <= 165 && currentItems.Count > 0) {
                        double count = currentItems.Count;
                        double angleStep = (2 * Math.PI) / count;
                        double rotationOffset = -Math.PI / 2.0 - (angleStep / 2.0);
                        double angle = Math.Atan2(dy, dx);
                        double relAngle = angle - rotationOffset;
                        while (relAngle < 0) relAngle += 2 * Math.PI;
                        while (relAngle >= 2 * Math.PI) relAngle -= 2 * Math.PI;
                        int idx = (int)(relAngle / angleStep) % currentItems.Count;
                        ExecuteChoice(currentItems[idx]);
                        return;
                    }

                    if (currentHoverIndex >= 0 && currentHoverIndex < currentItems.Count) {
                        ExecuteChoice(currentItems[currentHoverIndex]);
                        return;
                    }
                }
            }
            CloseWithAnimation();
        }
    }

    // === SATURN CATALOG (CATEGORIES & TOOLS & ACTIONS) ===
    public static class SaturnCatalog {
        public static List<PetalAction> GetPetalsForExtension(string ext, string mode) {
            var list = new List<PetalAction>();

            // Mode: Quick Actions
            if (mode == "actions") {
                list.Add(new PetalAction("FLOAT", "Pin Shelf", "float", "action"));
                list.Add(new PetalAction("COPY PATH", "Clipboard", "copypath", "action"));
                list.Add(new PetalAction("COPY FILE", "Clipboard", "copy", "action"));
                list.Add(new PetalAction("REVEAL", "Explorer", "reveal", "action"));
                list.Add(new PetalAction("TERMINAL", "PowerShell", "terminal", "action"));
                list.Add(new PetalAction("DUPLICATE", "In-Place", "duplicate", "action"));
                return list;
            }

            // Video Files
            if (ext == "mp4" || ext == "mov" || ext == "mkv" || ext == "webm" || ext == "avi" || ext == "wmv") {
                if (mode == "tools") {
                    list.Add(new PetalAction("COMPRESS", "Web Friendly", "compress", "tool"));
                    list.Add(new PetalAction("METADATA", "Scrub EXIF", "metadata", "tool"));
                    list.Add(new PetalAction("MUTE", "Remove Audio", "mute", "tool"));
                    list.Add(new PetalAction("SNAPSHOT", "Cover Frame", "snapshot", "tool"));
                    list.Add(new PetalAction("CHECKSUM", "SHA-256", "hash", "tool"));
                    list.Add(new PetalAction("FLOAT", "Pin Shelf", "float", "action"));
                } else {
                    list.Add(new PetalAction("MP4", "H.264 Universal", "mp4"));
                    list.Add(new PetalAction("GIF", "Smooth Palettized", "gif"));
                    list.Add(new PetalAction("MP3", "Rip Soundtrack", "mp3"));
                    list.Add(new PetalAction("WEBM", "VP9 Web Stream", "webm"));
                    list.Add(new PetalAction("MKV", "Matroska Master", "mkv"));
                    list.Add(new PetalAction("MOV", "Apple QuickTime", "mov"));
                    list.Add(new PetalAction("AVI", "Legacy Container", "avi"));
                    list.Add(new PetalAction("WAV", "PCM Audio", "wav"));
                }
                return list;
            }

            // Audio Files
            if (ext == "mp3" || ext == "wav" || ext == "flac" || ext == "m4a" || ext == "ogg" || ext == "opus" || ext == "aiff") {
                if (mode == "tools") {
                    list.Add(new PetalAction("NORMALIZE", "-14 LUFS", "normalize", "tool"));
                    list.Add(new PetalAction("COMPRESS", "Bitrate Opt", "compress", "tool"));
                    list.Add(new PetalAction("METADATA", "Scrub Tags", "metadata", "tool"));
                    list.Add(new PetalAction("CHECKSUM", "SHA-256", "hash", "tool"));
                    list.Add(new PetalAction("FLOAT", "Pin Shelf", "float", "action"));
                    list.Add(new PetalAction("COPY PATH", "Clipboard", "copypath", "action"));
                } else {
                    list.Add(new PetalAction("MP3", "320 kbps High", "mp3"));
                    list.Add(new PetalAction("WAV", "Lossless Master", "wav"));
                    list.Add(new PetalAction("FLAC", "Audiophile", "flac"));
                    list.Add(new PetalAction("M4A", "AAC Modern", "m4a"));
                    list.Add(new PetalAction("OPUS", "Low Latency", "opus"));
                    list.Add(new PetalAction("OGG", "Vorbis Stream", "ogg"));
                }
                return list;
            }

            // Documents (PDF, DOCX, TXT, MD, HTML)
            if (ext == "pdf" || ext == "docx" || ext == "txt" || ext == "md" || ext == "html" || ext == "htm") {
                if (mode == "tools") {
                    list.Add(new PetalAction("COMPRESS", "Optimize Size", "compress", "tool"));
                    list.Add(new PetalAction("METADATA", "Scrub Author", "metadata", "tool"));
                    list.Add(new PetalAction("CHECKSUM", "SHA-256", "hash", "tool"));
                    list.Add(new PetalAction("FLOAT", "Pin Shelf", "float", "action"));
                    list.Add(new PetalAction("COPY PATH", "Clipboard", "copypath", "action"));
                } else {
                    list.Add(new PetalAction("PDF", "Standard Document", "pdf"));
                    list.Add(new PetalAction("DOCX", "Word Document", "docx"));
                    list.Add(new PetalAction("HTML", "Styled Webpage", "html"));
                    list.Add(new PetalAction("TXT", "Plain UTF-8", "txt"));
                    list.Add(new PetalAction("MD", "Markdown Document", "md"));
                }
                return list;
            }

            // Data & Spreadsheets (CSV, JSON, XML)
            if (ext == "csv" || ext == "json" || ext == "xml" || ext == "tsv") {
                if (mode == "tools") {
                    list.Add(new PetalAction("CHECKSUM", "SHA-256", "hash", "tool"));
                    list.Add(new PetalAction("DUPLICATE", "In-Place", "duplicate", "action"));
                    list.Add(new PetalAction("FLOAT", "Pin Shelf", "float", "action"));
                    list.Add(new PetalAction("COPY PATH", "Clipboard", "copypath", "action"));
                } else {
                    list.Add(new PetalAction("JSON", "Structured Array", "json"));
                    list.Add(new PetalAction("CSV", "Tabular Delimited", "csv"));
                    list.Add(new PetalAction("TXT", "Raw Plain Text", "txt"));
                    list.Add(new PetalAction("HTML", "Styled Table", "html"));
                    list.Add(new PetalAction("MD", "Markdown Table", "md"));
                }
                return list;
            }

            // Default: Images & All Other Formats
            if (mode == "tools") {
                list.Add(new PetalAction("COMPRESS", "Reduce Size", "compress", "tool"));
                list.Add(new PetalAction("METADATA", "Scrub EXIF/GPS", "metadata", "tool"));
                list.Add(new PetalAction("GRAYSCALE", "B&W Filter", "grayscale", "tool"));
                list.Add(new PetalAction("ROTATE 90°", "Clockwise", "rotate", "tool"));
                list.Add(new PetalAction("CHECKSUM", "SHA-256", "hash", "tool"));
                list.Add(new PetalAction("FLOAT", "Pin Shelf", "float", "action"));
            } else {
                list.Add(new PetalAction("PNG", "Lossless 32-bit", "png"));
                list.Add(new PetalAction("JPG", "High Quality", "jpg"));
                list.Add(new PetalAction("WEBP", "Web Standard", "webp"));
                list.Add(new PetalAction("ICO", "Multi-Size Icon", "ico"));
                list.Add(new PetalAction("BMP", "Bitmap Matrix", "bmp"));
                list.Add(new PetalAction("TIFF", "Archival Quality", "tiff"));
                list.Add(new PetalAction("GIF", "Graphics Format", "gif"));
                list.Add(new PetalAction("PDF", "Document Sheet", "pdf"));
            }
            return list;
        }
    }

    // === FLOATING PINNED SHELF ===
    public class FloatingShelfWindow : Window {
        private StackPanel listPanel;

        public FloatingShelfWindow() {
            Title = "Saturn Pinned Shelf";
            Width = 270;
            Height = 340;
            Topmost = true;
            WindowStyle = WindowStyle.ToolWindow;
            WindowStartupLocation = WindowStartupLocation.Manual;

            Left = SystemParameters.WorkArea.Right - 290;
            Top = SystemParameters.WorkArea.Top + 70;

            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var headerStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headerStack.Children.Add(SaturnLogo.CreateVector(18, true));
            var title = new TextBlock {
                Text = "Saturn Float Shelf",
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerStack.Children.Add(title);
            header.Child = headerStack;
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            listPanel = new StackPanel { Margin = new Thickness(8) };
            scroll.Content = listPanel;
            Grid.SetRow(scroll, 1);
            mainGrid.Children.Add(scroll);

            Content = mainGrid;
        }

        public void AddPinnedFile(string filePath) {
            Show();
            Activate();

            var itemBorder = new Border {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Effect = new DropShadowEffect {
                    BlurRadius = 8,
                    Opacity = 0.08,
                    ShadowDepth = 2
                }
            };

            var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            stack.Children.Add(new TextBlock { Text = "📄 ", FontSize = 13 });
            var nameBlock = new TextBlock {
                Text = Path.GetFileName(filePath),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 190,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            stack.Children.Add(nameBlock);
            itemBorder.Child = stack;

            // Drag out from shelf directly into other applications!
            itemBorder.MouseDown += (s, e) => {
                if (e.LeftButton == MouseButtonState.Pressed) {
                    var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new string[] { filePath });
                    DragDrop.DoDragDrop(itemBorder, data, System.Windows.DragDropEffects.Copy);
                }
            };

            listPanel.Children.Insert(0, itemBorder);
        }
    }

    // === SELF-HEALING DIAGNOSTIC & ERROR LOGGER ===
    public static class SaturnLog {
        private static readonly object lockObj = new object();

        public static void Write(string category, string message) {
            try {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string logFile = Path.Combine(dir, "saturn_debug.log");

                lock (lockObj) {
                    if (File.Exists(logFile) && new FileInfo(logFile).Length > 1024 * 1024) {
                        try { File.Delete(logFile); } catch { }
                    }
                    string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}{3}", 
                        DateTime.Now, category.ToUpper(), message, Environment.NewLine);
                    File.AppendAllText(logFile, line, Encoding.UTF8);
                }
            } catch { }
        }

        public static void LogException(string context, Exception ex) {
            if (ex == null) return;
            string msg = context + ": " + ex.GetType().Name + " - " + ex.Message;
            if (ex.InnerException != null) msg += " -> " + ex.InnerException.Message;
            msg += "\n" + ex.StackTrace;
            Write("ERROR", msg);
        }
    }

    // === CORE REAL CONVERSION & TOOLS ENGINE ===
    public static class SaturnEngine {
        public static string FindFfmpeg() {
            try {
                string appDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string local = Path.Combine(appDir, "ffmpeg.exe");
                if (File.Exists(local)) return local;

                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ffmpeg.exe");
                if (File.Exists(desktop)) return desktop;

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string oneDriveDesktop = Path.Combine(userProfile, "OneDrive", "Desktop", "ffmpeg.exe");
                if (File.Exists(oneDriveDesktop)) return oneDriveDesktop;

                string localSaturn = Path.Combine(userProfile, "AppData", "Local", "Saturn", "ffmpeg.exe");
                if (File.Exists(localSaturn)) return localSaturn;

                // Check system PATH
                string pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv)) {
                    foreach (var rawDir in pathEnv.Split(';')) {
                        string dir = rawDir.Trim();
                        if (string.IsNullOrEmpty(dir)) continue;
                        try {
                            string cand = Path.Combine(dir, "ffmpeg.exe");
                            if (File.Exists(cand)) return cand;
                        } catch { }
                    }
                }

                // Check LocalAppData Programs & Python caches
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string pyCache = Path.Combine(localAppData, @"Packages\PythonSoftwareFoundation.Python.3.13_qbz5n2kfra8p0\LocalCache\local-packages\Python313\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe");
                if (File.Exists(pyCache)) return pyCache;

                string wingetPath = Path.Combine(localAppData, @"Microsoft\WinGet\Links\ffmpeg.exe");
                if (File.Exists(wingetPath)) return wingetPath;
            } catch { }

            return null;
        }

        public static bool RunFfmpeg(string args, int timeoutMs = 120000) {
            string ffmpeg = FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg)) {
                SaturnLog.Write("FFMPEG", "FFmpeg executable not found. Command was: " + args);
                return false;
            }

            try {
                var psi = new ProcessStartInfo {
                    FileName = ffmpeg,
                    Arguments = "-hide_banner -loglevel error -y " + args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var p = Process.Start(psi)) {
                    string err = p.StandardError.ReadToEnd();
                    bool exited = p.WaitForExit(timeoutMs);
                    if (!exited) {
                        try { p.Kill(); } catch { }
                        SaturnLog.Write("FFMPEG", "FFmpeg command timed out after " + timeoutMs + "ms: " + args);
                        return false;
                    }
                    if (p.ExitCode != 0) {
                        SaturnLog.Write("FFMPEG", "FFmpeg error (ExitCode " + p.ExitCode + "): " + err);
                        return false;
                    }
                    return true;
                }
            } catch (Exception ex) {
                SaturnLog.LogException("RunFfmpeg", ex);
                return false;
            }
        }

        // Resilient universal image loader (handles WebP, AVIF, HEIC, TIFF, BMP, PNG, JPG, CMYK, etc.)
        public static Bitmap LoadAnyImageSafely(string imagePath) {
            try {
                using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    using (var raw = Image.FromStream(fs)) {
                        // Create a clean 32-bit ARGB copy detached from any file handles
                        return new Bitmap(raw);
                    }
                }
            } catch {
                // If native GDI+ fails (e.g. WebP, AVIF, HEIC, CMYK, corrupted EXIF), decode via FFmpeg!
                string tempPng = Path.Combine(Path.GetTempPath(), "saturn_decode_" + Guid.NewGuid().ToString("N") + ".png");
                try {
                    if (RunFfmpeg(string.Format("-i \"{0}\" \"{1}\"", imagePath, tempPng)) && File.Exists(tempPng)) {
                        using (var fs = new FileStream(tempPng, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                            using (var raw = Image.FromStream(fs)) {
                                return new Bitmap(raw);
                            }
                        }
                    }
                } catch (Exception ex) {
                    SaturnLog.LogException("LoadAnyImageSafely.FFmpegFallback", ex);
                } finally {
                    try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                }
                throw new InvalidOperationException("Could not decode image format: " + Path.GetFileName(imagePath));
            }
        }

        // Safe automatic output path resolver - guarantees ZERO overwrites via clean disambiguation
        public static string GetAvailableOutputPath(string path, bool forceNew = false) {
            try {
                string dir = Path.GetDirectoryName(path);
                string baseName = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);

                if (!forceNew && !File.Exists(path)) return path;

                // When file exists or forceNew is requested, find the next available unique file name
                for (int i = 2; i <= 999; i++) {
                    string cand = Path.Combine(dir, string.Format("{0}_{1}{2}", baseName, i, ext));
                    if (!File.Exists(cand)) return cand;
                }
            } catch { }
            return path;
        }

        public static string ExecuteConversion(string srcPath, string targetExt, string customOutPath = null) {
            if (!File.Exists(srcPath)) throw new FileNotFoundException("Source file does not exist: " + srcPath);

            string dir = Path.GetDirectoryName(srcPath);
            string baseName = Path.GetFileNameWithoutExtension(srcPath);
            string srcExt = Path.GetExtension(srcPath).ToLower().TrimStart('.');
            targetExt = targetExt.ToLower().TrimStart('.');

            string outPath = customOutPath ?? GetAvailableOutputPath(Path.Combine(dir, baseName + "_saturn." + targetExt));

            // 1. IMAGE CONVERSIONS
            if (IsImageFormat(srcExt)) {
                if (targetExt == "png" || targetExt == "jpg" || targetExt == "jpeg" ||
                    targetExt == "bmp" || targetExt == "tiff" || targetExt == "gif") {
                    
                    try {
                        using (var bmp = LoadAnyImageSafely(srcPath)) {
                            SafeSaveBitmap(bmp, outPath, targetExt, 92L);
                            return outPath;
                        }
                    } catch {
                        if (RunFfmpeg(string.Format("-i \"{0}\" \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                            return outPath;
                        }
                        throw;
                    }
                } else if (targetExt == "webp") {
                    if (RunFfmpeg(string.Format("-i \"{0}\" -c:v libwebp -quality 90 \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                        return outPath;
                    }
                    // Fallback to high-quality PNG if libwebp encoder is missing
                    string fallbackPng = GetAvailableOutputPath(Path.Combine(dir, baseName + "_saturn.png"));
                    using (var bmp = LoadAnyImageSafely(srcPath)) {
                        SafeSaveBitmap(bmp, fallbackPng, "png", 92L);
                        return fallbackPng;
                    }
                } else if (targetExt == "ico") {
                    using (var bmp = LoadAnyImageSafely(srcPath)) {
                        CreateMultiFrameIco(bmp, outPath);
                        return outPath;
                    }
                } else if (targetExt == "pdf") {
                    CreateImagePdf(srcPath, outPath);
                    return outPath;
                } else if (targetExt == "docx") {
                    using (var bmp = LoadAnyImageSafely(srcPath)) {
                        using (var ms = new MemoryStream()) {
                            bmp.Save(ms, ImageFormat.Jpeg);
                            CreateDocxWithContent(outPath, null, new List<byte[]> { ms.ToArray() });
                        }
                    }
                    return outPath;
                }
            }

            // 2. VIDEO CONVERSIONS
            if (IsVideoFormat(srcExt)) {
                string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + "." + targetExt);
                if (targetExt == "mp4") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v libx264 -preset fast -crf 22 -c:a aac -b:a 192k \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "gif") {
                    RunFfmpeg(string.Format("-i \"{0}\" -vf \"fps=12,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "mp3") {
                    RunFfmpeg(string.Format("-i \"{0}\" -vn -c:a libmp3lame -b:a 320k \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "webm") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v libvpx-vp9 -b:v 0 -crf 30 -c:a libopus \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "mkv") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c copy \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "mov") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v libx264 -c:a aac \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "avi") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v mpeg4 -qscale:v 3 -c:a mp3 \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "wav") {
                    RunFfmpeg(string.Format("-i \"{0}\" -vn -c:a pcm_s16le \"{1}\"", srcPath, tempOut));
                } else {
                    RunFfmpeg(string.Format("-i \"{0}\" \"{1}\"", srcPath, tempOut));
                }
                if (File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                    File.Move(tempOut, outPath);
                    return outPath;
                }
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                throw new InvalidOperationException(string.Format("Failed to convert .{0} to .{1}. Please ensure FFmpeg is available.", srcExt, targetExt));
            }

            // 3. AUDIO CONVERSIONS
            if (IsAudioFormat(srcExt)) {
                string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + "." + targetExt);
                if (targetExt == "mp3") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a libmp3lame -b:a 320k \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "wav") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a pcm_s16le \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "flac") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a flac \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "m4a") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a aac -b:a 256k \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "opus") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a libopus -b:a 128k \"{1}\"", srcPath, tempOut));
                } else if (targetExt == "ogg") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a libvorbis -q:a 6 \"{1}\"", srcPath, tempOut));
                } else {
                    RunFfmpeg(string.Format("-i \"{0}\" \"{1}\"", srcPath, tempOut));
                }
                if (File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                    File.Move(tempOut, outPath);
                    return outPath;
                }
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                throw new InvalidOperationException(string.Format("Failed to convert .{0} to .{1}. Please ensure FFmpeg is available.", srcExt, targetExt));
            }

            // 4. DATA & SPREADSHEET CONVERSIONS (CSV, JSON, XML, TSV)
            if (srcExt == "csv" || srcExt == "tsv") {
                string text = File.ReadAllText(srcPath, Encoding.UTF8);
                char delim = srcExt == "tsv" ? '\t' : ',';
                if (targetExt == "json") {
                    return WriteAllTextAtomic(outPath, CsvToJson(text, delim), Encoding.UTF8);
                } else if (targetExt == "html" || targetExt == "htm") {
                    return WriteAllTextAtomic(outPath, CsvToHtml(text, delim, baseName), Encoding.UTF8);
                } else if (targetExt == "md") {
                    return WriteAllTextAtomic(outPath, CsvToMarkdown(text, delim), Encoding.UTF8);
                } else if (targetExt == "txt") {
                    return WriteAllTextAtomic(outPath, text, Encoding.UTF8);
                }
            }

            if (srcExt == "json") {
                string jsonText = File.ReadAllText(srcPath, Encoding.UTF8);
                if (targetExt == "csv") {
                    return WriteAllTextAtomic(outPath, JsonToCsv(jsonText), Encoding.UTF8);
                } else if (targetExt == "html" || targetExt == "htm") {
                    return WriteAllTextAtomic(outPath, JsonToHtml(jsonText, baseName), Encoding.UTF8);
                } else if (targetExt == "md") {
                    return WriteAllTextAtomic(outPath, JsonToMarkdown(jsonText), Encoding.UTF8);
                } else if (targetExt == "txt") {
                    return WriteAllTextAtomic(outPath, jsonText, Encoding.UTF8);
                }
            }

            if (srcExt == "xml") {
                string xmlText = File.ReadAllText(srcPath, Encoding.UTF8);
                if (targetExt == "json") {
                    return WriteAllTextAtomic(outPath, XmlToJson(xmlText), Encoding.UTF8);
                } else if (targetExt == "csv") {
                    return WriteAllTextAtomic(outPath, XmlToCsv(xmlText), Encoding.UTF8);
                } else if (targetExt == "txt") {
                    return WriteAllTextAtomic(outPath, xmlText, Encoding.UTF8);
                } else if (targetExt == "md") {
                    string json = XmlToJson(xmlText);
                    return WriteAllTextAtomic(outPath, JsonToMarkdown(json), Encoding.UTF8);
                } else if (targetExt == "html" || targetExt == "htm") {
                    string json = XmlToJson(xmlText);
                    return WriteAllTextAtomic(outPath, JsonToHtml(json, baseName), Encoding.UTF8);
                }
            }

            // 5. DOCUMENTS (TXT, MD, HTML, DOCX, PDF)
            if (srcExt == "txt" || srcExt == "md" || srcExt == "html" || srcExt == "htm") {
                string content = File.ReadAllText(srcPath, Encoding.UTF8);
                if (targetExt == "pdf") {
                    CreateTextPdf(content, outPath);
                    return outPath;
                } else if (targetExt == "docx") {
                    CreateDocxWithContent(outPath, content, null);
                    return outPath;
                } else if (targetExt == "html" || targetExt == "htm") {
                    return WriteAllTextAtomic(outPath, TextOrMdToHtml(content, baseName), Encoding.UTF8);
                } else if (targetExt == "txt") {
                    string plain = srcExt.StartsWith("htm") ? StripHtml(content) : content;
                    return WriteAllTextAtomic(outPath, plain, Encoding.UTF8);
                } else if (targetExt == "md") {
                    string plain = srcExt.StartsWith("htm") ? StripHtml(content) : content;
                    return WriteAllTextAtomic(outPath, plain, Encoding.UTF8);
                }
            }

            if (srcExt == "pdf") {
                if (targetExt == "txt") {
                    ExtractPdfText(srcPath, outPath);
                    return outPath;
                } else if (targetExt == "docx") {
                    var images = ExtractPdfImages(srcPath);
                    string txt = ExtractPdfRawText(srcPath);
                    CreateDocxWithContent(outPath, txt, images);
                    return outPath;
                } else if (targetExt == "html" || targetExt == "htm") {
                    return PdfToHtml(srcPath, outPath);
                } else if (targetExt == "md") {
                    return PdfToMarkdown(srcPath, outPath);
                } else if (targetExt == "png" || targetExt == "jpg" || targetExt == "jpeg") {
                    var images = ExtractPdfImages(srcPath);
                    if (images.Count > 0) {
                        using (var ms = new MemoryStream(images[0])) {
                            using (var bmp = new Bitmap(ms)) {
                                SafeSaveBitmap(bmp, outPath, targetExt, 92L);
                                return outPath;
                            }
                        }
                    }
                    throw new InvalidOperationException("No extractable raster images found in PDF.");
                } else if (targetExt == "pdf") {
                    File.Copy(srcPath, outPath, true);
                    return outPath;
                }
            }

            if (srcExt == "docx") {
                if (targetExt == "txt") {
                    return DocxToTxt(srcPath, outPath);
                } else if (targetExt == "md") {
                    return DocxToMarkdown(srcPath, outPath);
                } else if (targetExt == "html" || targetExt == "htm") {
                    return DocxToHtml(srcPath, outPath);
                } else if (targetExt == "pdf") {
                    return DocxToPdf(srcPath, outPath);
                } else if (targetExt == "docx") {
                    File.Copy(srcPath, outPath, true);
                    return outPath;
                }
            }

            // Fallback: only copy if identical format; otherwise reject invalid conversions
            if (srcExt == targetExt) {
                File.Copy(srcPath, outPath, true);
                return outPath;
            }

            throw new NotSupportedException(string.Format("Conversion from .{0} to .{1} is not supported.", srcExt, targetExt));
        }

        public static string ExecuteTool(string srcPath, string toolId, string customOutPath = null) {
            if (!File.Exists(srcPath)) throw new FileNotFoundException("Source file does not exist: " + srcPath);

            string dir = Path.GetDirectoryName(srcPath);
            string baseName = Path.GetFileNameWithoutExtension(srcPath);
            string ext = Path.GetExtension(srcPath).ToLower();
            string rawExt = ext.TrimStart('.');

            string outPath = customOutPath ?? GetAvailableOutputPath(Path.Combine(dir, baseName + "_" + toolId + ext));

            if (toolId == "metadata") {
                outPath = GetAvailableOutputPath(Path.Combine(dir, baseName + "_clean" + ext));
                if (IsImageFormat(rawExt) && rawExt != "webp") {
                    try {
                        using (var img = LoadAnyImageSafely(srcPath)) {
                            using (var clone = new Bitmap(img.Width, img.Height)) {
                                using (var g = Graphics.FromImage(clone)) {
                                    g.DrawImage(img, 0, 0, img.Width, img.Height);
                                }
                                SafeSaveBitmap(clone, outPath, rawExt, 95L);
                            }
                        }
                        return outPath;
                    } catch {
                        if (RunFfmpeg(string.Format("-i \"{0}\" -map_metadata -1 \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                            return outPath;
                        }
                        throw;
                    }
                } else if (IsVideoFormat(rawExt) || IsAudioFormat(rawExt)) {
                    if (RunFfmpeg(string.Format("-i \"{0}\" -map_metadata -1 -c copy \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                        return outPath;
                    }
                }
                File.Copy(srcPath, outPath, true);
                return outPath;
            }

            if (toolId == "compress") {
                outPath = GetAvailableOutputPath(Path.Combine(dir, baseName + "_compressed" + ext));
                string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + ext);
                long originalSize = new FileInfo(srcPath).Length;

                if (rawExt == "jpg" || rawExt == "jpeg") {
                    try {
                        using (var bmp = LoadAnyImageSafely(srcPath)) {
                            SafeSaveBitmap(bmp, tempOut, "jpg", 55L);
                        }
                    } catch {
                        RunFfmpeg(string.Format("-i \"{0}\" -q:v 15 \"{1}\"", srcPath, tempOut));
                    }
                } else if (rawExt == "png") {
                    bool ffmpegSuccess = false;
                    string palTemp = Path.Combine(dir, "." + Path.GetFileName(outPath) + ".pal." + Guid.NewGuid().ToString("N") + ".png");
                    try {
                        if (RunFfmpeg(string.Format("-i \"{0}\" -vf \"palettegen=max_colors=128\" \"{1}\"", srcPath, palTemp))) {
                            if (RunFfmpeg(string.Format("-i \"{0}\" -i \"{1}\" -lavfi \"paletteuse=dither=bayer:bayer_scale=1\" \"{2}\"", srcPath, palTemp, tempOut))) {
                                if (File.Exists(tempOut) && new FileInfo(tempOut).Length < originalSize) {
                                    ffmpegSuccess = true;
                                }
                            }
                        }
                    } catch { }
                    finally {
                        try { if (File.Exists(palTemp)) File.Delete(palTemp); } catch { }
                    }

                    if (!ffmpegSuccess) {
                        try {
                            using (var img = LoadAnyImageSafely(srcPath)) {
                                int nw = Math.Max(1, (int)(img.Width * 0.85));
                                int nh = Math.Max(1, (int)(img.Height * 0.85));
                                using (var scaled = new Bitmap(img, nw, nh)) {
                                    SafeSaveBitmap(scaled, tempOut, "png", 92L);
                                }
                            }
                        } catch {
                            RunFfmpeg(string.Format("-i \"{0}\" -vf \"scale=iw*0.85:-1\" \"{1}\"", srcPath, tempOut));
                        }
                    }
                } else if (IsVideoFormat(rawExt)) {
                    RunFfmpeg(string.Format("-i \"{0}\" -vf \"scale='min(1280,iw)':-2\" -c:v libx264 -crf 28 -preset fast -c:a aac -b:a 128k \"{1}\"", srcPath, tempOut));
                } else if (IsAudioFormat(rawExt)) {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a libmp3lame -b:a 128k \"{1}\"", srcPath, tempOut));
                }

                if (File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                    long newSize = new FileInfo(tempOut).Length;
                    if (newSize < originalSize) {
                        File.Move(tempOut, outPath);
                        return outPath;
                    }
                    try { File.Delete(tempOut); } catch { }
                    throw new InvalidOperationException(string.Format("File is already optimally compressed ({0:0.#} KB).", originalSize / 1024.0));
                }
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                throw new InvalidOperationException("Compression was unable to reduce the file size.");
            }

            if (toolId == "grayscale") {
                outPath = GetAvailableOutputPath(Path.Combine(dir, baseName + "_bw" + ext));
                try {
                    using (var img = LoadAnyImageSafely(srcPath)) {
                        using (var gray = new Bitmap(img.Width, img.Height)) {
                            using (var g = Graphics.FromImage(gray)) {
                                var cm = new ColorMatrix(new float[][] {
                                    new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
                                    new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
                                    new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
                                    new float[] { 0,      0,      0,      1, 0 },
                                    new float[] { 0,      0,      0,      0, 1 }
                                });
                                using (var ia = new ImageAttributes()) {
                                    ia.SetColorMatrix(cm);
                                    g.DrawImage(img, new System.Drawing.Rectangle(0, 0, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                                }
                            }
                            SafeSaveBitmap(gray, outPath, rawExt, 92L);
                        }
                    }
                    return outPath;
                } catch {
                    string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + ext);
                    if (RunFfmpeg(string.Format("-i \"{0}\" -vf \"format=gray\" \"{1}\"", srcPath, tempOut)) && File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                        File.Move(tempOut, outPath);
                        return outPath;
                    }
                    try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                    throw;
                }
            }

            if (toolId == "rotate") {
                outPath = GetAvailableOutputPath(Path.Combine(dir, baseName + "_rotated" + ext));
                try {
                    using (var img = LoadAnyImageSafely(srcPath)) {
                        using (var clone = new Bitmap(img)) {
                            clone.RotateFlip(RotateFlipType.Rotate90FlipNone);
                            SafeSaveBitmap(clone, outPath, rawExt, 92L);
                        }
                    }
                    return outPath;
                } catch {
                    string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + ext);
                    if (RunFfmpeg(string.Format("-i \"{0}\" -vf \"transpose=1\" \"{1}\"", srcPath, tempOut)) && File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                        File.Move(tempOut, outPath);
                        return outPath;
                    }
                    try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                    throw;
                }
            }

            if (toolId == "mute") {
                outPath = GetAvailableOutputPath(Path.Combine(dir, baseName + "_muted" + ext));
                string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + ext);
                bool ok = RunFfmpeg(string.Format("-i \"{0}\" -c:v copy -an \"{1}\"", srcPath, tempOut));
                if (ok && File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                    File.Move(tempOut, outPath);
                    return outPath;
                }
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                throw new InvalidOperationException("Failed to mute audio track. Please ensure FFmpeg is available.");
            }

            if (toolId == "snapshot") {
                outPath = GetAvailableOutputPath(Path.Combine(dir, baseName + "_cover.jpg"));
                string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + ".jpg");
                bool ok = RunFfmpeg(string.Format("-ss 00:00:01 -i \"{0}\" -vframes 1 -q:v 2 \"{1}\"", srcPath, tempOut));
                if (!ok || !File.Exists(tempOut) || new FileInfo(tempOut).Length == 0) {
                    ok = RunFfmpeg(string.Format("-ss 00:00:00 -i \"{0}\" -vframes 1 -q:v 2 \"{1}\"", srcPath, tempOut));
                }
                if (ok && File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                    File.Move(tempOut, outPath);
                    return outPath;
                }
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                throw new InvalidOperationException("Failed to extract snapshot frame. Please ensure FFmpeg is available.");
            }

            if (toolId == "normalize") {
                outPath = GetAvailableOutputPath(Path.Combine(dir, baseName + "_normalized" + ext));
                string tempOut = Path.Combine(dir, "." + Path.GetFileName(outPath) + "." + Guid.NewGuid().ToString("N") + ext);
                bool ok = RunFfmpeg(string.Format("-i \"{0}\" -af loudnorm \"{1}\"", srcPath, tempOut));
                if (ok && File.Exists(tempOut) && new FileInfo(tempOut).Length > 0) {
                    File.Move(tempOut, outPath);
                    return outPath;
                }
                try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                throw new InvalidOperationException("Failed to normalize audio track. Please ensure FFmpeg is available.");
            }

            if (toolId == "hash") {
                string h = ComputeSha256(srcPath);
                SafeSetClipboardText(h);
                return h;
            }

            throw new NotSupportedException("Unknown tool action: " + toolId);
        }

        public static bool SafeSetClipboardText(string text) {
            for (int attempt = 0; attempt < 10; attempt++) {
                try {
                    System.Windows.Clipboard.SetText(text);
                    return true;
                } catch {
                    System.Threading.Thread.Sleep(40);
                }
            }
            return false;
        }

        // --- FORMAT CHECKS ---
        public static bool IsImageFormat(string ext) {
            return ext == "png" || ext == "jpg" || ext == "jpeg" || ext == "webp" ||
                   ext == "bmp" || ext == "tiff" || ext == "gif" || ext == "ico";
        }

        public static bool IsVideoFormat(string ext) {
            return ext == "mp4" || ext == "mov" || ext == "mkv" || ext == "webm" ||
                   ext == "avi" || ext == "wmv" || ext == "m4v";
        }

        public static bool IsAudioFormat(string ext) {
            return ext == "mp3" || ext == "wav" || ext == "flac" || ext == "m4a" ||
                   ext == "ogg" || ext == "opus" || ext == "aiff";
        }

        // --- PDF GENERATOR (PURE C# 100% SPEC COMPLIANT) ---
        public static void CreateImagePdf(string imagePath, string pdfPath) {
            using (var img = LoadAnyImageSafely(imagePath)) {
                int w = img.Width;
                int h = img.Height;

                byte[] jpegBytes;
                using (var ms = new MemoryStream()) {
                    img.Save(ms, ImageFormat.Jpeg);
                    jpegBytes = ms.ToArray();
                }

                    double pdfW = (w / (double)img.HorizontalResolution) * 72.0;
                    double pdfH = (h / (double)img.VerticalResolution) * 72.0;
                    if (pdfW <= 0) pdfW = w;
                    if (pdfH <= 0) pdfH = h;

                    string contentStream = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "q\n{0:0.##} 0 0 {1:0.##} 0 0 cm\n/Im1 Do\nQ\n",
                        pdfW, pdfH);
                    byte[] contentBytes = Encoding.ASCII.GetBytes(contentStream);

                    byte[] pdfData;
                    using (var msOut = new MemoryStream()) {
                        using (var bw = new BinaryWriter(msOut)) {
                            var offsets = new List<long>();
                            bw.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));

                            // 1: Catalog
                            offsets.Add(msOut.Position);
                            bw.Write(Encoding.ASCII.GetBytes("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"));

                            // 2: Pages
                            offsets.Add(msOut.Position);
                            bw.Write(Encoding.ASCII.GetBytes("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"));

                            // 3: Page
                            offsets.Add(msOut.Position);
                            string pageHeader = string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.##} {1:0.##}] /Resources << /XObject << /Im1 6 0 R >> >> /Contents 5 0 R >>\nendobj\n",
                                pdfW, pdfH);
                            bw.Write(Encoding.ASCII.GetBytes(pageHeader));

                            // 4: Empty (placeholder)
                            offsets.Add(msOut.Position);
                            bw.Write(Encoding.ASCII.GetBytes("4 0 obj\n<< >>\nendobj\n"));

                            // 5: Contents
                            offsets.Add(msOut.Position);
                            bw.Write(Encoding.ASCII.GetBytes("5 0 obj\n<< /Length " + contentBytes.Length + " >>\nstream\n"));
                            bw.Write(contentBytes);
                            bw.Write(Encoding.ASCII.GetBytes("endstream\nendobj\n"));

                            // 6: Image XObject
                            offsets.Add(msOut.Position);
                            string imgHeader = string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                "6 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n",
                                w, h, jpegBytes.Length);
                            bw.Write(Encoding.ASCII.GetBytes(imgHeader));
                            bw.Write(jpegBytes);
                            bw.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));

                            // XRef
                            long xrefPos = msOut.Position;
                            bw.Write(Encoding.ASCII.GetBytes("xref\n0 7\n0000000000 65535 f \n"));
                            for (int i = 0; i < offsets.Count; i++) {
                                bw.Write(Encoding.ASCII.GetBytes(string.Format("{0:D10} 00000 n \n", offsets[i])));
                            }

                            // Trailer
                            bw.Write(Encoding.ASCII.GetBytes("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n" + xrefPos + "\n%%EOF\n"));
                        }
                        pdfData = msOut.ToArray();
                    }
                    WriteAllBytesAtomic(pdfPath, pdfData);
                }
            }

        public static void CreateTextPdf(string text, string outputPath) {
            var lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            var streamSb = new StringBuilder();
            streamSb.AppendLine("BT");
            streamSb.AppendLine("/F1 11 Tf");
            streamSb.AppendLine("50 780 Td");
            streamSb.AppendLine("15 TL");

            int lineCount = 0;
            foreach (var l in lines) {
                string escaped = l.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                streamSb.AppendLine("(" + escaped + ") '");
                lineCount++;
                if (lineCount > 48) break; // First page limit
            }
            streamSb.AppendLine("ET");
            byte[] contentBytes = Encoding.ASCII.GetBytes(streamSb.ToString());

            byte[] pdfData;
            using (var msOut = new MemoryStream()) {
                using (var bw = new BinaryWriter(msOut)) {
                    var offsets = new List<long>();
                    bw.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));

                    // 1: Catalog
                    offsets.Add(msOut.Position);
                    bw.Write(Encoding.ASCII.GetBytes("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"));

                    // 2: Pages
                    offsets.Add(msOut.Position);
                    bw.Write(Encoding.ASCII.GetBytes("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"));

                    // 3: Page
                    offsets.Add(msOut.Position);
                    bw.Write(Encoding.ASCII.GetBytes("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n"));

                    // 4: Font
                    offsets.Add(msOut.Position);
                    bw.Write(Encoding.ASCII.GetBytes("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"));

                    // 5: Contents
                    offsets.Add(msOut.Position);
                    bw.Write(Encoding.ASCII.GetBytes("5 0 obj\n<< /Length " + contentBytes.Length + " >>\nstream\n"));
                    bw.Write(contentBytes);
                    bw.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));

                    // XRef
                    long xrefPos = msOut.Position;
                    bw.Write(Encoding.ASCII.GetBytes("xref\n0 6\n0000000000 65535 f \n"));
                    for (int i = 0; i < offsets.Count; i++) {
                        bw.Write(Encoding.ASCII.GetBytes(string.Format("{0:D10} 00000 n \n", offsets[i])));
                    }

                    // Trailer
                    bw.Write(Encoding.ASCII.GetBytes("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xrefPos + "\n%%EOF\n"));
                }
                pdfData = msOut.ToArray();
            }
            WriteAllBytesAtomic(outputPath, pdfData);
        }

        // --- DOCX GENERATOR (VALID OPENXML ZIP PACKAGE WITH DRAWINGML SUPPORT) ---
        public static void CreateTextDocx(string text, string outputPath) {
            CreateDocxWithContent(outputPath, text, null);
        }

        public static void CreateDocxWithContent(string outputPath, string text, List<byte[]> images) {
            if (images == null) images = new List<byte[]>();

            byte[] docxData;
            using (var msDocx = new MemoryStream()) {
                using (var zip = new ZipArchive(msDocx, ZipArchiveMode.Create, true)) {
                    // [Content_Types].xml
                    var entry1 = zip.CreateEntry("[Content_Types].xml");
                    using (var sw = new StreamWriter(entry1.Open(), Encoding.UTF8)) {
                        sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                            "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
                            "<Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>" +
                            "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                            "</Types>");
                    }

                    // _rels/.rels
                    var entry2 = zip.CreateEntry("_rels/.rels");
                    using (var sw = new StreamWriter(entry2.Open(), Encoding.UTF8)) {
                        sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                            "</Relationships>");
                    }

                    // word/_rels/document.xml.rels
                    if (images.Count > 0) {
                        var entryRels = zip.CreateEntry("word/_rels/document.xml.rels");
                        using (var sw = new StreamWriter(entryRels.Open(), Encoding.UTF8)) {
                            var sbRels = new StringBuilder();
                            sbRels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                            sbRels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                            for (int i = 0; i < images.Count; i++) {
                                sbRels.Append(string.Format("<Relationship Id=\"rIdImg{0}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image{0}.jpg\"/>", i + 1));
                            }
                            sbRels.Append("</Relationships>");
                            sw.Write(sbRels.ToString());
                        }

                        // word/media/image{i}.jpg
                        for (int i = 0; i < images.Count; i++) {
                            var entryImg = zip.CreateEntry(string.Format("word/media/image{0}.jpg", i + 1));
                            using (var imgStream = entryImg.Open()) {
                                imgStream.Write(images[i], 0, images[i].Length);
                            }
                        }
                    }

                    // word/document.xml
                    var entry3 = zip.CreateEntry("word/document.xml");
                    using (var sw = new StreamWriter(entry3.Open(), Encoding.UTF8)) {
                        var sb = new StringBuilder();
                        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                        sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                                  "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                                  "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                                  "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                                  "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
                        sb.Append("<w:body>");

                        // 1. Embed Images
                        for (int i = 0; i < images.Count; i++) {
                            int imgW = 400;
                            int imgH = 300;
                            try {
                                using (var ms = new MemoryStream(images[i])) {
                                    using (var tempImg = Image.FromStream(ms)) {
                                        imgW = tempImg.Width;
                                        imgH = tempImg.Height;
                                    }
                                }
                            } catch { }

                            long cx = (long)imgW * 9525L;
                            long cy = (long)imgH * 9525L;
                            long maxW = 5029200L; // 5.5 inches
                            long maxH = 6858000L; // 7.5 inches
                            if (cx > maxW) {
                                cy = (long)((double)cy * maxW / cx);
                                cx = maxW;
                            }
                            if (cy > maxH) {
                                cx = (long)((double)cx * maxH / cy);
                                cy = maxH;
                            }

                            int imgId = i + 1;
                            sb.Append("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>");
                            sb.Append("<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">");
                            sb.Append(string.Format("<wp:extent cx=\"{0}\" cy=\"{1}\"/>", cx, cy));
                            sb.Append("<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>");
                            sb.Append(string.Format("<wp:docPr id=\"{0}\" name=\"Picture {0}\"/>", imgId));
                            sb.Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>");
                            sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
                            sb.Append("<pic:pic><pic:nvPicPr>");
                            sb.Append(string.Format("<pic:cNvPr id=\"{0}\" name=\"Picture {0}\"/>", imgId));
                            sb.Append("<pic:cNvPicPr/></pic:nvPicPr>");
                            sb.Append("<pic:blipFill>");
                            sb.Append(string.Format("<a:blip r:embed=\"rIdImg{0}\"/>", imgId));
                            sb.Append("<a:stretch><a:fillRect/></a:stretch></pic:blipFill>");
                            sb.Append("<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/>");
                            sb.Append(string.Format("<a:ext cx=\"{0}\" cy=\"{1}\"/>", cx, cy));
                            sb.Append("</a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>");
                            sb.Append("</pic:pic></a:graphicData></a:graphic>");
                            sb.Append("</wp:inline></w:drawing></w:r></w:p>");
                        }

                        // 2. Embed Text
                        if (!string.IsNullOrEmpty(text)) {
                            var lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                            foreach (var line in lines) {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                string safe = System.Security.SecurityElement.Escape(line);
                                sb.Append("<w:p><w:r><w:t xml:space=\"preserve\">" + safe + "</w:t></w:r></w:p>");
                            }
                        }

                        if (images.Count == 0 && string.IsNullOrWhiteSpace(text)) {
                            sb.Append("<w:p/>");
                        }

                        sb.Append("<w:sectPr/>");
                        sb.Append("</w:body></w:document>");
                        sw.Write(sb.ToString());
                    }
                }
                docxData = msDocx.ToArray();
            }
            WriteAllBytesAtomic(outputPath, docxData);
        }

        // --- ICO GENERATOR (MULTI-FRAME WINDOWS ICON) ---
        public static void CreateMultiFrameIco(Bitmap srcBmp, string icoPath) {
            int[] sizes = new int[] { 64, 32, 16 };
            var pngBuffers = new List<byte[]>();

            foreach (int s in sizes) {
                using (var iconBmp = new Bitmap(srcBmp, s, s)) {
                    using (var ms = new MemoryStream()) {
                        iconBmp.Save(ms, ImageFormat.Png);
                        pngBuffers.Add(ms.ToArray());
                    }
                }
            }

            byte[] icoData;
            using (var msIco = new MemoryStream()) {
                using (var bw = new BinaryWriter(msIco)) {
                    bw.Write((short)0); // Reserved
                    bw.Write((short)1); // Type 1 = Icon
                    bw.Write((short)sizes.Length); // Count of images

                    int offset = 6 + (16 * sizes.Length);
                    for (int i = 0; i < sizes.Length; i++) {
                        int sz = sizes[i];
                        byte[] png = pngBuffers[i];
                        bw.Write((byte)(sz >= 256 ? 0 : sz)); // Width
                        bw.Write((byte)(sz >= 256 ? 0 : sz)); // Height
                        bw.Write((byte)0);  // Colors
                        bw.Write((byte)0);  // Reserved
                        bw.Write((short)1); // Color planes
                        bw.Write((short)32);// Bits per pixel
                        bw.Write(png.Length);
                        bw.Write(offset);
                        offset += png.Length;
                    }

                    for (int i = 0; i < sizes.Length; i++) {
                        bw.Write(pngBuffers[i]);
                    }
                }
                icoData = msIco.ToArray();
            }
            WriteAllBytesAtomic(icoPath, icoData);
        }

        // --- DATA CONVERSION HELPERS ---
        public static string CsvToJson(string csvText, char delimiter = ',') {
            var lines = csvText.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return "[]";

            var headers = ParseDelimitedLine(lines[0], delimiter);
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 1; i < lines.Length; i++) {
                var vals = ParseDelimitedLine(lines[i], delimiter);
                sb.Append("  {");
                for (int c = 0; c < headers.Count; c++) {
                    string h = headers[c];
                    string v = c < vals.Count ? vals[c] : "";
                    sb.Append(string.Format("\"{0}\": \"{1}\"", EscapeJson(h), EscapeJson(v)));
                    if (c < headers.Count - 1) sb.Append(", ");
                }
                sb.Append("}");
                if (i < lines.Length - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("]");
            return sb.ToString();
        }

        public static string CsvToHtml(string csvText, char delimiter, string title) {
            var lines = csvText.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.AppendLine("<title>" + System.Security.SecurityElement.Escape(title) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0f172a; color: #f8fafc; padding: 2rem; margin: 0; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; max-width: 960px; margin: 0 auto; background: #1e293b; border-radius: 8px; overflow: hidden; box-shadow: 0 10px 25px rgba(0,0,0,0.3); }");
            sb.AppendLine("th { background: linear-gradient(135deg, #6366f1, #8b5cf6); color: #fff; padding: 12px 16px; text-align: left; font-weight: 600; font-size: 14px; }");
            sb.AppendLine("td { padding: 10px 16px; border-bottom: 1px solid #334155; font-size: 13px; color: #cbd5e1; }");
            sb.AppendLine("tr:hover { background: #283548; }");
            sb.AppendLine("h1 { text-align: center; color: #818cf8; font-size: 20px; margin-bottom: 1.5rem; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>" + System.Security.SecurityElement.Escape(title) + "</h1>");
            sb.AppendLine("<table>");

            if (lines.Length > 0) {
                var headers = ParseDelimitedLine(lines[0], delimiter);
                sb.AppendLine("<thead><tr>");
                foreach (var h in headers) {
                    sb.AppendLine("<th>" + System.Security.SecurityElement.Escape(h) + "</th>");
                }
                sb.AppendLine("</tr></thead><tbody>");

                for (int i = 1; i < lines.Length; i++) {
                    var vals = ParseDelimitedLine(lines[i], delimiter);
                    sb.AppendLine("<tr>");
                    foreach (var v in vals) {
                        sb.AppendLine("<td>" + System.Security.SecurityElement.Escape(v) + "</td>");
                    }
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody>");
            }
            sb.AppendLine("</table></body></html>");
            return sb.ToString();
        }

        public static string CsvToMarkdown(string csvText, char delimiter) {
            var lines = csvText.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return "";

            var headers = ParseDelimitedLine(lines[0], delimiter);
            var sb = new StringBuilder();
            sb.Append("| ");
            foreach (var h in headers) sb.Append(h + " | ");
            sb.AppendLine();
            sb.Append("| ");
            foreach (var h in headers) sb.Append("--- | ");
            sb.AppendLine();

            for (int i = 1; i < lines.Length; i++) {
                var vals = ParseDelimitedLine(lines[i], delimiter);
                sb.Append("| ");
                for (int c = 0; c < headers.Count; c++) {
                    string v = c < vals.Count ? vals[c] : "";
                    sb.Append(v + " | ");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string JsonToCsv(string jsonText) {
            var objects = ParseJsonObjects(jsonText);
            if (objects.Count == 0) return "";

            var allKeys = new List<string>();
            foreach (var dict in objects) {
                foreach (var k in dict.Keys) {
                    if (!allKeys.Contains(k)) allKeys.Add(k);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", allKeys.ToArray()));

            foreach (var dict in objects) {
                var rowVals = new List<string>();
                foreach (var k in allKeys) {
                    string val = dict.ContainsKey(k) ? dict[k] : "";
                    if (val.Contains(",") || val.Contains("\"") || val.Contains("\n")) {
                        val = "\"" + val.Replace("\"", "\"\"") + "\"";
                    }
                    rowVals.Add(val);
                }
                sb.AppendLine(string.Join(",", rowVals.ToArray()));
            }
            return sb.ToString();
        }

        public static string JsonToHtml(string jsonText, string title) {
            string csv = JsonToCsv(jsonText);
            return CsvToHtml(csv, ',', title);
        }

        public static string JsonToMarkdown(string jsonText) {
            string csv = JsonToCsv(jsonText);
            return CsvToMarkdown(csv, ',');
        }

        public static string XmlToJson(string xmlText) {
            try {
                var doc = XDocument.Parse(xmlText);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"" + doc.Root.Name.LocalName + "\": [");
                var children = new List<XElement>(doc.Root.Elements());
                for (int i = 0; i < children.Count; i++) {
                    var el = children[i];
                    sb.Append("    {");
                    var sub = new List<XElement>(el.Elements());
                    if (sub.Count > 0) {
                        for (int j = 0; j < sub.Count; j++) {
                            sb.Append(string.Format("\"{0}\": \"{1}\"", EscapeJson(sub[j].Name.LocalName), EscapeJson(sub[j].Value)));
                            if (j < sub.Count - 1) sb.Append(", ");
                        }
                    } else {
                        sb.Append(string.Format("\"{0}\": \"{1}\"", EscapeJson(el.Name.LocalName), EscapeJson(el.Value)));
                    }
                    sb.Append("}");
                    if (i < children.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                return sb.ToString();
            } catch {
                return "{ \"xml\": \"" + EscapeJson(xmlText) + "\" }";
            }
        }

        public static string XmlToCsv(string xmlText) {
            string json = XmlToJson(xmlText);
            return JsonToCsv(json);
        }

        public static string TextOrMdToHtml(string text, string title) {
            var lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.AppendLine("<title>" + System.Security.SecurityElement.Escape(title) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0f172a; color: #f8fafc; padding: 2rem; margin: 0; line-height: 1.6; }");
            sb.AppendLine(".container { max-width: 800px; margin: 0 auto; background: #1e293b; padding: 2.5rem; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.3); }");
            sb.AppendLine("h1, h2, h3 { color: #818cf8; margin-top: 1.5rem; }");
            sb.AppendLine("p { color: #cbd5e1; margin-bottom: 1rem; }");
            sb.AppendLine("code { background: #334155; padding: 2px 6px; border-radius: 4px; font-family: monospace; color: #a5b4fc; }");
            sb.AppendLine("</style></head><body><div class='container'>");

            foreach (var l in lines) {
                string trimmed = l.Trim();
                if (trimmed.StartsWith("# ")) {
                    sb.AppendLine("<h1>" + System.Security.SecurityElement.Escape(trimmed.Substring(2)) + "</h1>");
                } else if (trimmed.StartsWith("## ")) {
                    sb.AppendLine("<h2>" + System.Security.SecurityElement.Escape(trimmed.Substring(3)) + "</h2>");
                } else if (trimmed.StartsWith("### ")) {
                    sb.AppendLine("<h3>" + System.Security.SecurityElement.Escape(trimmed.Substring(4)) + "</h3>");
                } else if (string.IsNullOrEmpty(trimmed)) {
                    sb.AppendLine("<br/>");
                } else {
                    sb.AppendLine("<p>" + System.Security.SecurityElement.Escape(trimmed) + "</p>");
                }
            }
            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        public static string StripHtml(string html) {
            string text = Regex.Replace(html, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            return text.Trim();
        }

        public static string PdfToMarkdown(string pdfPath, string outMdPath) {
            string baseDir = Path.GetDirectoryName(outMdPath);
            string baseName = Path.GetFileNameWithoutExtension(outMdPath);
            string txt = ExtractPdfRawText(pdfPath);
            var images = ExtractPdfImages(pdfPath);

            var sb = new StringBuilder();

            if (images.Count > 0) {
                for (int i = 0; i < images.Count; i++) {
                    string imgFileName = string.Format("{0}_img{1}.jpg", baseName, i + 1);
                    string imgFullPath = Path.Combine(baseDir, imgFileName);
                    File.WriteAllBytes(imgFullPath, images[i]);
                    sb.AppendLine(string.Format("![{0}]({1})", imgFileName, imgFileName));
                    sb.AppendLine();
                }
            }

            if (!string.IsNullOrWhiteSpace(txt)) {
                var lines = txt.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                bool isFirst = true;
                foreach (var line in lines) {
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (isFirst && images.Count == 0 && !t.StartsWith("#")) {
                        sb.AppendLine("# " + t);
                        sb.AppendLine();
                        isFirst = false;
                    } else {
                        sb.AppendLine(t);
                        sb.AppendLine();
                    }
                }
            } else if (images.Count == 0) {
                sb.AppendLine("# " + Path.GetFileNameWithoutExtension(pdfPath));
                sb.AppendLine();
                sb.AppendLine("*No readable text or images found in document.*");
            }

            string mdContent = sb.ToString().TrimEnd();
            return WriteAllTextAtomic(outMdPath, mdContent, Encoding.UTF8);
        }

        public static string PdfToHtml(string pdfPath, string outHtmlPath) {
            string baseDir = Path.GetDirectoryName(outHtmlPath);
            string baseName = Path.GetFileNameWithoutExtension(outHtmlPath);
            string txt = ExtractPdfRawText(pdfPath);
            var images = ExtractPdfImages(pdfPath);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.AppendLine("<title>" + System.Security.SecurityElement.Escape(baseName) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0f172a; color: #f8fafc; padding: 2rem; margin: 0; line-height: 1.6; }");
            sb.AppendLine(".container { max-width: 800px; margin: 0 auto; background: #1e293b; padding: 2.5rem; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.3); }");
            sb.AppendLine("h1, h2 { color: #818cf8; }");
            sb.AppendLine("p { color: #cbd5e1; margin-bottom: 1rem; }");
            sb.AppendLine("img { max-width: 100%; height: auto; border-radius: 8px; margin: 1rem 0; display: block; }");
            sb.AppendLine("</style></head><body><div class='container'>");

            if (images.Count > 0) {
                for (int i = 0; i < images.Count; i++) {
                    string imgFileName = string.Format("{0}_img{1}.jpg", baseName, i + 1);
                    string imgFullPath = Path.Combine(baseDir, imgFileName);
                    File.WriteAllBytes(imgFullPath, images[i]);
                    sb.AppendLine(string.Format("<img src='{0}' alt='{0}'/>", System.Security.SecurityElement.Escape(imgFileName)));
                }
            }

            if (!string.IsNullOrWhiteSpace(txt)) {
                var lines = txt.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var l in lines) {
                    string t = l.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.AppendLine("<p>" + System.Security.SecurityElement.Escape(t) + "</p>");
                }
            }

            sb.AppendLine("</div></body></html>");
            return WriteAllTextAtomic(outHtmlPath, sb.ToString(), Encoding.UTF8);
        }

        public static string ExtractDocxText(string docxPath) {
            try {
                using (var fs = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read)) {
                        var entry = zip.GetEntry("word/document.xml");
                        if (entry != null) {
                            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) {
                                string xml = reader.ReadToEnd();
                                var sb = new StringBuilder();
                                var pMatches = Regex.Matches(xml, @"<w:p(?:\s[^>]*)?>.*?</w:p>", RegexOptions.Singleline);
                                foreach (Match pm in pMatches) {
                                    var tMatches = Regex.Matches(pm.Value, @"<w:t(?:\s[^>]*)?>(.*?)</w:t>", RegexOptions.Singleline);
                                    var lineSb = new StringBuilder();
                                    foreach (Match tm in tMatches) {
                                        lineSb.Append(tm.Groups[1].Value);
                                    }
                                    string line = System.Net.WebUtility.HtmlDecode(lineSb.ToString()).Trim();
                                    if (line.Length > 0) sb.AppendLine(line);
                                }
                                if (sb.Length > 0) return sb.ToString();
                            }
                        }
                    }
                }
            } catch { }
            return "";
        }

        public static List<byte[]> ExtractDocxImages(string docxPath) {
            var list = new List<byte[]>();
            try {
                using (var fs = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read)) {
                        foreach (var entry in zip.Entries) {
                            if (entry.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)) {
                                using (var ms = new MemoryStream()) {
                                    using (var s = entry.Open()) {
                                        s.CopyTo(ms);
                                    }
                                    list.Add(ms.ToArray());
                                }
                            }
                        }
                    }
                }
            } catch { }
            return list;
        }

        public static string DocxToMarkdown(string docxPath, string outMdPath) {
            string baseDir = Path.GetDirectoryName(outMdPath);
            string baseName = Path.GetFileNameWithoutExtension(outMdPath);
            string txt = ExtractDocxText(docxPath);
            var images = ExtractDocxImages(docxPath);

            var sb = new StringBuilder();
            if (images.Count > 0) {
                for (int i = 0; i < images.Count; i++) {
                    string imgFileName = string.Format("{0}_img{1}.jpg", baseName, i + 1);
                    string imgFullPath = Path.Combine(baseDir, imgFileName);
                    File.WriteAllBytes(imgFullPath, images[i]);
                    sb.AppendLine(string.Format("![{0}]({1})", imgFileName, imgFileName));
                    sb.AppendLine();
                }
            }

            if (!string.IsNullOrWhiteSpace(txt)) {
                var lines = txt.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                bool isFirst = true;
                foreach (var line in lines) {
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (isFirst && images.Count == 0 && !t.StartsWith("#")) {
                        sb.AppendLine("# " + t);
                        sb.AppendLine();
                        isFirst = false;
                    } else {
                        sb.AppendLine(t);
                        sb.AppendLine();
                    }
                }
            } else if (images.Count == 0) {
                sb.AppendLine("# " + Path.GetFileNameWithoutExtension(docxPath));
                sb.AppendLine();
                sb.AppendLine("*No readable text or images found in document.*");
            }

            return WriteAllTextAtomic(outMdPath, sb.ToString().TrimEnd(), Encoding.UTF8);
        }

        public static string DocxToHtml(string docxPath, string outHtmlPath) {
            string txt = ExtractDocxText(docxPath);
            string baseName = Path.GetFileNameWithoutExtension(docxPath);
            return WriteAllTextAtomic(outHtmlPath, TextOrMdToHtml(txt, baseName), Encoding.UTF8);
        }

        public static string DocxToPdf(string docxPath, string outPdfPath) {
            string txt = ExtractDocxText(docxPath);
            CreateTextPdf(txt, outPdfPath);
            return outPdfPath;
        }

        public static string DocxToTxt(string docxPath, string outTxtPath) {
            string txt = ExtractDocxText(docxPath);
            return WriteAllTextAtomic(outTxtPath, txt, Encoding.UTF8);
        }

        public static List<byte[]> ExtractPdfImages(string pdfPath) {
            var images = new List<byte[]>();
            try {
                byte[] data = File.ReadAllBytes(pdfPath);
                int len = data.Length;

                // 1. Scan for embedded JPEG images (SOI: FF D8 FF ... EOI: FF D9)
                int idx = 0;
                while (idx < len - 3) {
                    if (data[idx] == 0xFF && data[idx + 1] == 0xD8 && data[idx + 2] == 0xFF) {
                        int soi = idx;
                        int eoi = -1;
                        for (int j = soi + 4; j < len - 1; j++) {
                            if (data[j] == 0xFF && data[j + 1] == 0xD9) {
                                eoi = j + 1;
                                break;
                            }
                        }
                        if (eoi != -1 && (eoi - soi + 1) > 32) {
                            int imgLen = eoi - soi + 1;
                            byte[] candidate = new byte[imgLen];
                            Buffer.BlockCopy(data, soi, candidate, 0, imgLen);

                            bool isValid = false;
                            try {
                                using (var ms = new MemoryStream(candidate)) {
                                    using (var testImg = Image.FromStream(ms)) {
                                        if (testImg.Width > 0 && testImg.Height > 0) isValid = true;
                                    }
                                }
                            } catch { }

                            if (isValid) {
                                images.Add(candidate);
                                idx = eoi + 1;
                                continue;
                            }
                        }
                    }
                    idx++;
                }

                // 2. Scan for FlateDecode image streams if no JPEGs were found
                if (images.Count == 0) {
                    string rawAscii = Encoding.Default.GetString(data);
                    var imgMatches = Regex.Matches(rawAscii, @"<<[^>]*?/Subtype\s*/Image[^>]*?>>\s*stream[\r\n]+");
                    foreach (Match m in imgMatches) {
                        string dict = m.Value;
                        int streamStart = m.Index + m.Length;
                        int streamEnd = rawAscii.IndexOf("endstream", streamStart);
                        if (streamEnd > streamStart) {
                            var wMatch = Regex.Match(dict, @"/Width\s+(\d+)");
                            var hMatch = Regex.Match(dict, @"/Height\s+(\d+)");
                            if (wMatch.Success && hMatch.Success) {
                                int w = int.Parse(wMatch.Groups[1].Value);
                                int h = int.Parse(hMatch.Groups[1].Value);
                                int streamLen = streamEnd - streamStart;
                                if (streamLen > 2) {
                                    try {
                                        byte[] streamBytes = new byte[streamLen];
                                        Buffer.BlockCopy(data, streamStart, streamBytes, 0, streamLen);
                                        using (var msIn = new MemoryStream(streamBytes, 2, streamBytes.Length - 2)) {
                                            using (var def = new DeflateStream(msIn, CompressionMode.Decompress)) {
                                                using (var msOut = new MemoryStream()) {
                                                    def.CopyTo(msOut);
                                                    byte[] rawPixels = msOut.ToArray();
                                                    if (rawPixels.Length >= w * h * 3) {
                                                        using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb)) {
                                                            var bData = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                                                            int copyLen = Math.Min(rawPixels.Length, bData.Stride * h);
                                                            Marshal.Copy(rawPixels, 0, bData.Scan0, copyLen);
                                                            bmp.UnlockBits(bData);
                                                            using (var msJpg = new MemoryStream()) {
                                                                bmp.Save(msJpg, ImageFormat.Jpeg);
                                                                images.Add(msJpg.ToArray());
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    } catch { }
                                }
                            }
                        }
                    }
                }
            } catch { }
            return images;
        }

        public static void ExtractPdfText(string pdfPath, string outTxtPath) {
            string raw = ExtractPdfRawText(pdfPath);
            if (string.IsNullOrWhiteSpace(raw)) {
                var images = ExtractPdfImages(pdfPath);
                if (images.Count > 0) {
                    raw = string.Format("[Visual PDF document containing {0} embedded image(s). Convert to DOCX or image to view.]", images.Count);
                } else {
                    raw = "[Empty document]";
                }
            }
            WriteAllTextAtomic(outTxtPath, raw, Encoding.UTF8);
        }

        public static string ExtractPdfRawText(string pdfPath) {
            try {
                byte[] bytes = File.ReadAllBytes(pdfPath);
                var sb = new StringBuilder();

                // 1. Decompress and extract from all FlateDecode and raw content streams
                var streams = ExtractAllPdfStreams(bytes);
                foreach (string streamText in streams) {
                    ExtractTextFromStream(streamText, sb);
                }

                // 2. Fallback: check uncompressed raw PDF text if no stream yielded text
                if (sb.Length == 0) {
                    string rawContent = Encoding.Default.GetString(bytes);
                    ExtractTextFromStream(rawContent, sb);
                }

                if (sb.Length > 0) return sb.ToString().Trim();
            } catch (Exception ex) {
                SaturnLog.LogException("ExtractPdfRawText", ex);
            }
            return "";
        }

        public static List<string> ExtractAllPdfStreams(byte[] data) {
            var list = new List<string>();
            try {
                int len = data.Length;
                int idx = 0;
                while (idx < len - 10) {
                    if (data[idx] == 's' && data[idx + 1] == 't' && data[idx + 2] == 'r' &&
                        data[idx + 3] == 'e' && data[idx + 4] == 'a' && data[idx + 5] == 'm') {
                        
                        int streamStart = idx + 6;
                        if (streamStart < len && data[streamStart] == '\r') streamStart++;
                        if (streamStart < len && data[streamStart] == '\n') streamStart++;

                        int streamEnd = -1;
                        for (int j = streamStart; j < len - 9; j++) {
                            if (data[j] == 'e' && data[j + 1] == 'n' && data[j + 2] == 'd' &&
                                data[j + 3] == 's' && data[j + 4] == 't' && data[j + 5] == 'r' &&
                                data[j + 6] == 'e' && data[j + 7] == 'a' && data[j + 8] == 'm') {
                                streamEnd = j;
                                break;
                            }
                        }

                        if (streamEnd > streamStart) {
                            int streamLen = streamEnd - streamStart;
                            int dictSearchStart = Math.Max(0, idx - 400);
                            string dictHeader = Encoding.ASCII.GetString(data, dictSearchStart, idx - dictSearchStart);
                            bool isFlate = dictHeader.IndexOf("/FlateDecode", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isFlate && streamLen > 6) {
                                try {
                                    using (var msIn = new MemoryStream(data, streamStart + 2, streamLen - 6)) {
                                        using (var def = new DeflateStream(msIn, CompressionMode.Decompress)) {
                                            using (var msOut = new MemoryStream()) {
                                                def.CopyTo(msOut);
                                                string inflated = Encoding.UTF8.GetString(msOut.ToArray());
                                                if (!string.IsNullOrWhiteSpace(inflated)) list.Add(inflated);
                                            }
                                        }
                                    }
                                } catch {
                                    try {
                                        using (var msIn = new MemoryStream(data, streamStart, streamLen)) {
                                            using (var def = new DeflateStream(msIn, CompressionMode.Decompress)) {
                                                using (var msOut = new MemoryStream()) {
                                                    def.CopyTo(msOut);
                                                    string inflated = Encoding.UTF8.GetString(msOut.ToArray());
                                                    if (!string.IsNullOrWhiteSpace(inflated)) list.Add(inflated);
                                                }
                                            }
                                        }
                                    } catch { }
                                }
                            } else if (!isFlate) {
                                string plain = Encoding.UTF8.GetString(data, streamStart, streamLen);
                                if (!string.IsNullOrWhiteSpace(plain)) list.Add(plain);
                            }

                            idx = streamEnd + 9;
                            continue;
                        }
                    }
                    idx++;
                }
            } catch { }
            return list;
        }

        private static void ExtractTextFromStream(string streamText, StringBuilder sb) {
            if (string.IsNullOrWhiteSpace(streamText)) return;

            var btMatches = Regex.Matches(streamText, @"BT\s+(.*?)\s+ET", RegexOptions.Singleline);
            foreach (Match bt in btMatches) {
                string block = bt.Groups[1].Value;

                var tjArrayMatches = Regex.Matches(block, @"\[(.*?)\]\s*TJ", RegexOptions.Singleline);
                foreach (Match tam in tjArrayMatches) {
                    var strMatches = Regex.Matches(tam.Groups[1].Value, @"\(([^)]*)\)");
                    var lineSb = new StringBuilder();
                    foreach (Match sm in strMatches) {
                        lineSb.Append(UnescapePdfString(sm.Groups[1].Value));
                    }
                    string line = lineSb.ToString().Trim();
                    if (IsMostlyPrintableText(line)) sb.AppendLine(line);
                }

                var tjMatches = Regex.Matches(block, @"\(([^)]*)\)\s*(?:Tj|'|"")");
                foreach (Match tm in tjMatches) {
                    string line = UnescapePdfString(tm.Groups[1].Value).Trim();
                    if (IsMostlyPrintableText(line)) sb.AppendLine(line);
                }

                var hexMatches = Regex.Matches(block, @"<([0-9A-Fa-f]+)>\s*(?:Tj|'|"")");
                foreach (Match hm in hexMatches) {
                    string hex = hm.Groups[1].Value;
                    if (hex.Length % 2 == 0) {
                        try {
                            var hexBytes = new byte[hex.Length / 2];
                            for (int i = 0; i < hexBytes.Length; i++) {
                                hexBytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                            }
                            string line = Encoding.UTF8.GetString(hexBytes).Trim();
                            if (IsMostlyPrintableText(line)) sb.AppendLine(line);
                        } catch { }
                    }
                }
            }
        }

        private static string UnescapePdfString(string s) {
            return s.Replace("\\(", "(")
                    .Replace("\\)", ")")
                    .Replace("\\\\", "\\")
                    .Replace("\\r", "\r")
                    .Replace("\\n", "\n")
                    .Replace("\\t", "\t");
        }

        private static bool IsMostlyPrintableText(string s) {
            if (string.IsNullOrWhiteSpace(s)) return false;
            int printable = 0;
            foreach (char c in s) {
                if (c >= 32 && c <= 126 || c >= 160 || c == '\t' || c == '\r' || c == '\n')
                    printable++;
            }
            return (double)printable / s.Length >= 0.75;
        }

        private static List<string> ParseDelimitedLine(string line, char delimiter) {
            var list = new List<string>();
            bool inQuotes = false;
            var cur = new StringBuilder();

            for (int i = 0; i < line.Length; i++) {
                char ch = line[i];
                if (ch == '"') {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') {
                        cur.Append('"');
                        i++;
                    } else {
                        inQuotes = !inQuotes;
                    }
                } else if (ch == delimiter && !inQuotes) {
                    list.Add(cur.ToString().Trim());
                    cur.Length = 0;
                } else {
                    cur.Append(ch);
                }
            }
            list.Add(cur.ToString().Trim());
            return list;
        }

        // Robust Pure C# JSON Parser (Zero Regex, Full Support for Nested Objects & Arrays)
        private static List<Dictionary<string, string>> ParseJsonObjects(string json) {
            return SaturnJson.ToTable(json);
        }

        private static string EscapeJson(string str) {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        public static string WriteAllTextAtomic(string path, string content, Encoding encoding) {
            byte[] bytes = encoding.GetBytes(content);
            return WriteAllBytesAtomic(path, bytes);
        }

        public static string WriteAllBytesAtomic(string path, byte[] data) {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            path = GetAvailableOutputPath(path);
            string temp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(temp, data);

            bool saved = false;
            for (int attempt = 0; attempt < 5; attempt++) {
                try {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                    saved = true;
                    break;
                } catch {
                    Thread.Sleep(50);
                }
            }

            if (!saved) {
                string altPath = GetAvailableOutputPath(path, true);
                File.Move(temp, altPath);
                path = altPath;
            }

            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return path;
        }

        public static string SafeSaveBitmap(Bitmap bmp, string path, string ext, long quality = 92L) {
            ext = ext.ToLower().TrimStart('.');
            byte[] data;
            using (var ms = new MemoryStream()) {
                if (ext == "jpg" || ext == "jpeg") {
                    ImageCodecInfo jpgEncoder = null;
                    foreach (var codec in ImageCodecInfo.GetImageEncoders()) {
                        if (codec.FormatID == ImageFormat.Jpeg.Guid) {
                            jpgEncoder = codec;
                            break;
                        }
                    }
                    if (jpgEncoder != null) {
                        using (var encoderParams = new EncoderParameters(1)) {
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                            bmp.Save(ms, jpgEncoder, encoderParams);
                        }
                    } else {
                        bmp.Save(ms, ImageFormat.Jpeg);
                    }
                } else if (ext == "bmp") {
                    bmp.Save(ms, ImageFormat.Bmp);
                } else if (ext == "tiff") {
                    bmp.Save(ms, ImageFormat.Tiff);
                } else if (ext == "gif") {
                    bmp.Save(ms, ImageFormat.Gif);
                } else {
                    bmp.Save(ms, ImageFormat.Png);
                }
                data = ms.ToArray();
            }

            return WriteAllBytesAtomic(path, data);
        }

        private static void SaveByExt(Bitmap bmp, string path, string ext) {
            SafeSaveBitmap(bmp, path, ext, 92L);
        }

        public static string ComputeSha256(string filePath) {
            using (var sha = SHA256.Create()) {
                using (var stream = File.OpenRead(filePath)) {
                    byte[] hash = sha.ComputeHash(stream);
                    var sb = new StringBuilder();
                    for (int i = 0; i < hash.Length; i++) {
                        sb.Append(hash[i].ToString("x2"));
                    }
                    return sb.ToString();
                }
            }
        }
    }

    // === ROBUST JSON PARSER & TABULAR CONVERTER (ZERO REGEX, ZERO EXTERNAL DEPENDENCIES) ===
    public static class SaturnJson {
        public static object Parse(string json) {
            if (string.IsNullOrWhiteSpace(json)) return null;
            int idx = 0;
            SkipWhitespace(json, ref idx);
            return ParseValue(json, ref idx);
        }

        private static void SkipWhitespace(string s, ref int idx) {
            while (idx < s.Length && (s[idx] == ' ' || s[idx] == '\t' || s[idx] == '\r' || s[idx] == '\n')) idx++;
        }

        private static object ParseValue(string s, ref int idx) {
            SkipWhitespace(s, ref idx);
            if (idx >= s.Length) return null;
            char c = s[idx];
            if (c == '{') return ParseObject(s, ref idx);
            if (c == '[') return ParseArray(s, ref idx);
            if (c == '"') return ParseString(s, ref idx);
            if (c == 't' || c == 'T' || c == 'f' || c == 'F') return ParseBool(s, ref idx);
            if (c == 'n' || c == 'N') return ParseNull(s, ref idx);
            if (char.IsDigit(c) || c == '-') return ParseNumber(s, ref idx);
            return null;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int idx) {
            var dict = new Dictionary<string, object>();
            idx++; // skip '{'
            SkipWhitespace(s, ref idx);
            if (idx < s.Length && s[idx] == '}') { idx++; return dict; }

            while (idx < s.Length) {
                SkipWhitespace(s, ref idx);
                if (idx >= s.Length || s[idx] != '"') break;
                string key = ParseString(s, ref idx);
                SkipWhitespace(s, ref idx);
                if (idx < s.Length && s[idx] == ':') idx++;
                object val = ParseValue(s, ref idx);
                dict[key] = val;
                SkipWhitespace(s, ref idx);
                if (idx < s.Length && s[idx] == ',') { idx++; continue; }
                if (idx < s.Length && s[idx] == '}') { idx++; break; }
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int idx) {
            var list = new List<object>();
            idx++; // skip '['
            SkipWhitespace(s, ref idx);
            if (idx < s.Length && s[idx] == ']') { idx++; return list; }

            while (idx < s.Length) {
                object val = ParseValue(s, ref idx);
                list.Add(val);
                SkipWhitespace(s, ref idx);
                if (idx < s.Length && s[idx] == ',') { idx++; continue; }
                if (idx < s.Length && s[idx] == ']') { idx++; break; }
            }
            return list;
        }

        private static string ParseString(string s, ref int idx) {
            idx++; // skip opening '"'
            var sb = new StringBuilder();
            while (idx < s.Length) {
                char c = s[idx++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && idx < s.Length) {
                    char esc = s[idx++];
                    if (esc == '"') sb.Append('"');
                    else if (esc == '\\') sb.Append('\\');
                    else if (esc == '/') sb.Append('/');
                    else if (esc == 'b') sb.Append('\b');
                    else if (esc == 'f') sb.Append('\f');
                    else if (esc == 'n') sb.Append('\n');
                    else if (esc == 'r') sb.Append('\r');
                    else if (esc == 't') sb.Append('\t');
                    else if (esc == 'u' && idx + 4 <= s.Length) {
                        string hex = s.Substring(idx, 4);
                        idx += 4;
                        try { sb.Append((char)Convert.ToInt32(hex, 16)); } catch { }
                    } else sb.Append(esc);
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static bool ParseBool(string s, ref int idx) {
            if (s.Substring(idx).StartsWith("true", StringComparison.OrdinalIgnoreCase)) { idx += 4; return true; }
            if (s.Substring(idx).StartsWith("false", StringComparison.OrdinalIgnoreCase)) { idx += 5; return false; }
            idx++;
            return false;
        }

        private static object ParseNull(string s, ref int idx) {
            if (s.Substring(idx).StartsWith("null", StringComparison.OrdinalIgnoreCase)) idx += 4;
            else idx++;
            return null;
        }

        private static object ParseNumber(string s, ref int idx) {
            int start = idx;
            if (s[idx] == '-') idx++;
            while (idx < s.Length && (char.IsDigit(s[idx]) || s[idx] == '.' || s[idx] == 'e' || s[idx] == 'E' || s[idx] == '+' || s[idx] == '-')) {
                idx++;
            }
            return s.Substring(start, idx - start);
        }

        public static List<Dictionary<string, string>> ToTable(string json) {
            var list = new List<Dictionary<string, string>>();
            try {
                object root = Parse(json);
                if (root is List<object>) {
                    var arr = (List<object>)root;
                    foreach (var item in arr) {
                        if (item is Dictionary<string, object>) {
                            var obj = (Dictionary<string, object>)item;
                            var row = new Dictionary<string, string>();
                            foreach (var kv in obj) {
                                row[kv.Key] = kv.Value != null ? kv.Value.ToString() : "";
                            }
                            list.Add(row);
                        }
                    }
                } else if (root is Dictionary<string, object>) {
                    var obj = (Dictionary<string, object>)root;
                    foreach (var kv in obj) {
                        if (kv.Value is List<object>) {
                            var subArr = (List<object>)kv.Value;
                            foreach (var item in subArr) {
                                if (item is Dictionary<string, object>) {
                                    var subObj = (Dictionary<string, object>)item;
                                    var row = new Dictionary<string, string>();
                                    foreach (var subKv in subObj) {
                                        row[subKv.Key] = subKv.Value != null ? subKv.Value.ToString() : "";
                                    }
                                    list.Add(row);
                                }
                            }
                            if (list.Count > 0) return list;
                        }
                    }
                    var singleRow = new Dictionary<string, string>();
                    foreach (var kv in obj) {
                        singleRow[kv.Key] = kv.Value != null ? kv.Value.ToString() : "";
                    }
                    list.Add(singleRow);
                }
            } catch { }
            return list;
        }
    }

    // === APPLICATION CONTROLLER ===
    public class SaturnApp {
        private NotifyIcon trayIcon;
        private RadialWheelWindow wheelWindow;
        private FloatingShelfWindow shelfWindow;
        private SaturnToastWindow toastWindow;
        private Thread hotkeyThread;
        private bool isRunning = true;
        private double dpiScale = 1.0;
        private volatile bool isWheelOpen = false;

        public void Run(string initialFile = null) {
            SaturnLog.Write("RUN", "Run started");
            Win32.SetProcessDPIAware();
            CalculateDpiScale();
            SoundFX.Init();

            SaturnLog.Write("RUN", "Creating windows...");
            wheelWindow = new RadialWheelWindow();
            shelfWindow = new FloatingShelfWindow();
            toastWindow = new SaturnToastWindow();
            wheelWindow.OnPetalChosen = HandleRealAction;
            wheelWindow.OnWheelClosed = () => { isWheelOpen = false; };

            SaturnLog.Write("RUN", "Setting up tray icon and listeners...");
            SetupTrayIcon();
            RegisterShellContextMenu();
            StartNamedPipeListener();

            hotkeyThread = new Thread(DedicatedHotkeyListener) { IsBackground = true };
            hotkeyThread.Start();
            SaturnLog.Write("RUN", "Run completed successfully, background listeners active.");

            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile)) {
                OpenDialForPath(initialFile, "convert");
            }
        }

        private void SetupTrayIcon() {
            Icon iconObj = SystemIcons.Application;
            try {
                string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string icoPath = Path.Combine(exeDir, "Saturn.ico");
                if (File.Exists(icoPath)) {
                    iconObj = new Icon(icoPath);
                }
            } catch { }

            trayIcon = new NotifyIcon {
                Text = "Saturn for Windows (F8)",
                Visible = true,
                Icon = iconObj
            };

            var menu = new ContextMenuStrip();
            var header = menu.Items.Add("Saturn for Windows");
            header.Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
            header.Enabled = false;

            menu.Items.Add("-");
            menu.Items.Add("🪐 Open Saturn for Selected File (F8)", null, (s, e) => OpenDialForSelected("convert"));
            menu.Items.Add("⚡ Open Tools Wheel (Ctrl+F8)", null, (s, e) => OpenDialForSelected("tools"));
            menu.Items.Add("📌 Open Actions Wheel (Alt+F8)", null, (s, e) => OpenDialForSelected("actions"));
            menu.Items.Add("-");
            menu.Items.Add("📂 Choose File to Convert...", null, (s, e) => {
                var ofd = new OpenFileDialog { Title = "Select File for Saturn" };
                if (ofd.ShowDialog() == DialogResult.OK) {
                    OpenDialForPath(ofd.FileName, "convert");
                }
            });
            menu.Items.Add("📌 Toggle Float Shelf", null, (s, e) => {
                if (shelfWindow.IsVisible) shelfWindow.Hide();
                else shelfWindow.Show();
            });
            menu.Items.Add("🔧 Register Windows Right-Click Menu", null, (s, e) => {
                RegisterShellContextMenu();
                MessageBox.Show("Saturn has been added to your Windows Right-Click Context Menu!\nRight-click any file or folder to use Saturn.", "Saturn Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            menu.Items.Add("-");
            menu.Items.Add("❌ Exit Saturn", null, (s, e) => {
                isRunning = false;
                trayIcon.Visible = false;
                Application.Current.Shutdown();
            });

            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += (s, e) => OpenDialForSelected("convert");

            trayIcon.ShowBalloonTip(3000, "Saturn is Ready", "Select any file in Explorer or Desktop and press F8 to activate Saturn!\nOr right-click any file in Windows.", ToolTipIcon.Info);
        }

        private void RegisterShellContextMenu() {
            try {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string[] subKeys = new string[] {
                    @"Software\Classes\*\shell\Saturn",
                    @"Software\Classes\Directory\shell\Saturn",
                    @"Software\Classes\Directory\Background\shell\Saturn",
                    @"Software\Classes\Drive\shell\Saturn"
                };

                foreach (var subKey in subKeys) {
                    using (var key = Registry.CurrentUser.CreateSubKey(subKey)) {
                        if (key != null) {
                            key.SetValue("", "Saturn Quick Tools");
                            key.SetValue("Icon", "\"" + exePath + "\",0");
                            using (var cmd = key.CreateSubKey("command")) {
                                if (cmd != null) {
                                    string argParam = subKey.Contains("Background") ? "\"%V\"" : "\"%1\"";
                                    cmd.SetValue("", "\"" + exePath + "\" " + argParam);
                                }
                            }
                        }
                    }
                }
            } catch { }
        }

        private void StartNamedPipeListener() {
            new Thread(() => {
                while (isRunning) {
                    try {
                        using (var server = new NamedPipeServerStream("Saturn_SingleInstance_Pipe", PipeDirection.In)) {
                            server.WaitForConnection();
                            using (var reader = new StreamReader(server)) {
                                string filePath = reader.ReadLine();
                                string mode = reader.ReadLine();
                                if (string.IsNullOrEmpty(mode)) mode = "convert";

                                Application.Current.Dispatcher.Invoke(() => {
                                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) {
                                        OpenDialForPath(filePath, mode);
                                    } else {
                                        OpenDialForSelected(mode);
                                    }
                                });
                            }
                        }
                    } catch {
                        Thread.Sleep(100);
                    }
                }
            }) { IsBackground = true }.Start();
        }

        private void CalculateDpiScale() {
            try {
                IntPtr hdc = Win32.GetDC(IntPtr.Zero);
                int dpiX = Win32.GetDeviceCaps(hdc, 88);
                Win32.ReleaseDC(IntPtr.Zero, hdc);
                dpiScale = dpiX / 96.0;
                if (dpiScale <= 0) dpiScale = 1.0;
            } catch {
                dpiScale = 1.0;
            }
        }

        // Dedicated Hotkey Listener (F8, Ctrl+F8, Alt+F8)
        private void DedicatedHotkeyListener() {
            while (isRunning) {
                try {
                    Thread.Sleep(20);

                    short f8Key = Win32.GetAsyncKeyState(Win32.VK_F8);
                    short ctrlKey = Win32.GetAsyncKeyState(Win32.VK_CONTROL);
                    short altKey = Win32.GetAsyncKeyState(Win32.VK_MENU);

                    bool f8 = (f8Key & 0x8000) != 0;
                    bool ctrl = (ctrlKey & 0x8000) != 0;
                    bool alt = (altKey & 0x8000) != 0;

                    if (f8) {
                        if (isWheelOpen) {
                            Application.Current.Dispatcher.Invoke(() => {
                                wheelWindow.CloseWithAnimation();
                            });
                            Thread.Sleep(350);
                            continue;
                        }

                        string mode = "convert";
                        if (ctrl) mode = "tools";
                        else if (alt) mode = "actions";

                        OpenDialForSelected(mode);
                        Thread.Sleep(400); // Debounce
                    }
                } catch {
                    Thread.Sleep(50);
                }
            }
        }

        public void OpenDialForSelected(string mode) {
            Application.Current.Dispatcher.Invoke(() => {
                string selectedFile = TryGetSelectedFile();
                OpenDialForPath(selectedFile, mode);
            });
        }

        public void OpenDialForPath(string filePath, string mode) {
            Application.Current.Dispatcher.Invoke(() => {
                isWheelOpen = true;
                Win32.POINT pt;
                Win32.GetCursorPos(out pt);

                double dipX = pt.X / dpiScale;
                double dipY = pt.Y / dpiScale;

                wheelWindow.PositionAt(dipX, dipY);
                wheelWindow.LoadPetalsForFile(filePath, mode);
                wheelWindow.OpenWithBloom();
            });
        }

        // Safely retrieves selected file from active Explorer window, tab, desktop, or clipboard
        private string TryGetSelectedFile() {
            IntPtr fgHwnd = Win32.GetForegroundWindow();
            IntPtr rootHwnd = (fgHwnd != IntPtr.Zero) ? Win32.GetAncestor(fgHwnd, Win32.GA_ROOT) : IntPtr.Zero;

            // Strategy 1: Active Shell COM Automation (Matches both root window and tab handles)
            try {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null) {
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic windows = shell.Windows();
                    if (windows != null) {
                        // Pass 1: Strict match for current foreground/root window
                        foreach (dynamic window in windows) {
                            try {
                                if (window == null) continue;
                                long wHwnd = (long)window.HWND;
                                bool isMatch = (wHwnd == rootHwnd.ToInt64() || wHwnd == fgHwnd.ToInt64() ||
                                               (int)wHwnd == (int)rootHwnd || (int)wHwnd == (int)fgHwnd);
                                if (isMatch) {
                                    dynamic sel = window.Document.SelectedItems();
                                    if (sel != null && sel.Count > 0) {
                                        string p = sel.Item(0).Path;
                                        if (File.Exists(p) || Directory.Exists(p)) return p;
                                    }
                                    try {
                                        string folderPath = window.Document.Folder.Self.Path;
                                        if (Directory.Exists(folderPath)) return folderPath;
                                    } catch { }
                                }
                            } catch { }
                        }

                        // Pass 2: Check any Explorer tab that has selected files
                        foreach (dynamic window in windows) {
                            try {
                                if (window == null) continue;
                                dynamic sel = window.Document.SelectedItems();
                                if (sel != null && sel.Count > 0) {
                                    string p = sel.Item(0).Path;
                                    if (File.Exists(p) || Directory.Exists(p)) return p;
                                }
                            } catch { }
                        }
                    }
                }
            } catch (Exception ex) {
                SaturnLog.LogException("TryGetSelectedFile.ShellCOM", ex);
            }

            // Strategy 2: Physical Key Release + Clean Clipboard Simulation
            try {
                // Ensure F8 physical key is released before sending Control+C
                int waitCount = 0;
                while ((Win32.GetAsyncKeyState(Win32.VK_F8) & 0x8000) != 0 && waitCount < 25) {
                    Thread.Sleep(10);
                    waitCount++;
                }

                // Preserve existing clipboard text in case it's a file path
                string oldText = null;
                try {
                    if (System.Windows.Clipboard.ContainsText()) {
                        oldText = System.Windows.Clipboard.GetText().Trim().Trim('\"', '\'');
                    }
                } catch { }

                System.Windows.Clipboard.Clear();
                Win32.keybd_event(0x11, 0, 0, UIntPtr.Zero); // VK_CONTROL down
                Win32.keybd_event(0x43, 0, 0, UIntPtr.Zero); // VK_C down
                Thread.Sleep(35);
                Win32.keybd_event(0x43, 0, 2, UIntPtr.Zero); // VK_C up
                Win32.keybd_event(0x11, 0, 2, UIntPtr.Zero); // VK_CONTROL up

                // Wait up to 240ms for Explorer to populate FileDropList
                for (int i = 0; i < 16; i++) {
                    Thread.Sleep(15);
                    if (System.Windows.Clipboard.ContainsFileDropList()) {
                        var files = System.Windows.Clipboard.GetFileDropList();
                        if (files != null && files.Count > 0) {
                            string p = files[0];
                            if (File.Exists(p) || Directory.Exists(p)) return p;
                        }
                    }
                }

                // If no file drop list received, check if old clipboard was a valid file path
                if (!string.IsNullOrEmpty(oldText) && (File.Exists(oldText) || Directory.Exists(oldText))) {
                    return oldText;
                }
            } catch (Exception ex) {
                SaturnLog.LogException("TryGetSelectedFile.ClipboardCopy", ex);
            }

            return null;
        }

        // === ASYNCHRONOUS NON-BLOCKING ACTION HANDLER ===
        private void HandleRealAction(PetalAction action, string filePath) {
            if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath))) {
                if (Application.Current != null && Application.Current.Dispatcher != null) {
                    Application.Current.Dispatcher.Invoke(() => {
                        var ofd = new OpenFileDialog { Title = "Saturn: Select File to " + action.Title };
                        if (ofd.ShowDialog() == DialogResult.OK) {
                            HandleRealAction(action, ofd.FileName);
                        }
                    });
                }
                return;
            }

            string dir = Path.GetDirectoryName(filePath);
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string ext = Path.GetExtension(filePath).ToLower();

            // 1. Instant Actions
            if (action.Mode == "action") {
                try {
                    if (action.ExtOrId == "float") {
                        shelfWindow.AddPinnedFile(filePath);
                        SoundFX.PlayChime();
                        toastWindow.ShowResult("📌 Pinned to Shelf", Path.GetFileName(filePath), filePath);
                    } else if (action.ExtOrId == "copypath") {
                        System.Windows.Clipboard.SetText(filePath);
                        SoundFX.PlayChime();
                        toastWindow.ShowResult("📋 Path Copied", filePath, filePath);
                    } else if (action.ExtOrId == "copy") {
                        var coll = new System.Collections.Specialized.StringCollection { filePath };
                        System.Windows.Clipboard.SetFileDropList(coll);
                        SoundFX.PlayChime();
                        toastWindow.ShowResult("📄 File Copied to Clipboard", Path.GetFileName(filePath), filePath);
                    } else if (action.ExtOrId == "reveal") {
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", filePath));
                    } else if (action.ExtOrId == "terminal") {
                        Process.Start(new ProcessStartInfo {
                            FileName = "powershell.exe",
                            WorkingDirectory = dir
                        });
                    } else if (action.ExtOrId == "duplicate") {
                        string dupPath = SaturnEngine.GetAvailableOutputPath(Path.Combine(dir, baseName + "_copy" + ext));
                        File.Copy(filePath, dupPath, true);
                        SoundFX.PlayChime();
                        toastWindow.ShowResult("✓ File Duplicated", Path.GetFileName(dupPath), dupPath);
                    }
                } catch (Exception ex) {
                    SaturnLog.LogException("HandleRealAction." + action.ExtOrId, ex);
                }
                return;
            }

            // 2. Real Conversions & Tools in Background Thread
            ThreadPool.QueueUserWorkItem(state => {
                try {
                    string resultPath = null;
                    string title = "";

                    if (action.Mode == "tool") {
                        resultPath = SaturnEngine.ExecuteTool(filePath, action.ExtOrId);
                        title = "✓ " + action.Title;
                    } else {
                        resultPath = SaturnEngine.ExecuteConversion(filePath, action.ExtOrId);
                        title = "✓ Converted to " + action.ExtOrId.ToUpper();
                    }

                    if (Application.Current != null && Application.Current.Dispatcher != null) {
                        Application.Current.Dispatcher.Invoke(() => {
                            SoundFX.PlayChime();
                            toastWindow.ShowResult(title, Path.GetFileName(resultPath), resultPath);
                        });
                    }
                } catch (Exception ex) {
                    SaturnLog.LogException("HandleRealAction." + action.ExtOrId, ex);
                    if (Application.Current != null && Application.Current.Dispatcher != null) {
                        Application.Current.Dispatcher.Invoke(() => {
                            toastWindow.ShowResult("✕ Action Failed", ex.Message, null);
                        });
                    }
                }
            });
        }
    }

    // === MAIN ENTRY POINT (CLI + GUI SUPPORT) ===
    public class Program {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        public static void Main(string[] args) {
            // CLI Mode: Saturn.exe convert <file> --to <ext>
            if (args.Length >= 4 && args[0].Equals("convert", StringComparison.OrdinalIgnoreCase)) {
                AttachConsole(ATTACH_PARENT_PROCESS);
                string src = args[1];
                string target = args[3];
                try {
                    Console.WriteLine("Saturn CLI: Converting {0} to {1}...", src, target);
                    string res = SaturnEngine.ExecuteConversion(src, target);
                    Console.WriteLine("SUCCESS: {0}", res);
                } catch (Exception ex) {
                    Console.WriteLine("ERROR: {0}", ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            // CLI Mode: Saturn.exe tool <file> --action <toolId>
            if (args.Length >= 4 && args[0].Equals("tool", StringComparison.OrdinalIgnoreCase)) {
                AttachConsole(ATTACH_PARENT_PROCESS);
                string src = args[1];
                string toolId = args[3];
                try {
                    Console.WriteLine("Saturn CLI: Running tool {0} on {1}...", toolId, src);
                    string res = SaturnEngine.ExecuteTool(src, toolId);
                    Console.WriteLine("SUCCESS: {0}", res);
                } catch (Exception ex) {
                    Console.WriteLine("ERROR: {0}", ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            Process current = Process.GetCurrentProcess();
            SaturnLog.Write("STARTUP", "PID " + current.Id + " started, args: " + string.Join(" ", args));
            bool hasOtherInstance = false;
            foreach (var p in Process.GetProcessesByName(current.ProcessName)) {
                if (p.Id != current.Id) {
                    hasOtherInstance = true;
                    break;
                }
            }

            if (hasOtherInstance) {
                string targetFile = (args.Length > 0) ? args[0] : "";
                try {
                    using (var client = new NamedPipeClientStream(".", "Saturn_SingleInstance_Pipe", PipeDirection.Out)) {
                        client.Connect(800);
                        using (var writer = new StreamWriter(client)) {
                            writer.WriteLine(targetFile);
                            writer.WriteLine("convert");
                            writer.Flush();
                        }
                        return; // Forwarded to active instance!
                    }
                } catch {
                    // Previous instance did not respond, take over
                }
            }

            try {
                // Global Crash Prevention & Self-Healing Handlers
                AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                    SaturnLog.LogException("AppDomain.UnhandledException", e.ExceptionObject as Exception);
                };
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => {
                    SaturnLog.LogException("TaskScheduler.UnobservedTaskException", e.Exception);
                    e.SetObserved(); // Suppresses crash
                };

                // Unlock 120 FPS frame rate across all animations and double transforms
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata(120)
                );

                var app = new Application();
                app.DispatcherUnhandledException += (s, e) => {
                    SaturnLog.LogException("DispatcherUnhandledException", e.Exception);
                    e.Handled = true; // Suppresses UI crash and keeps app alive
                };

                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var saturn = new SaturnApp();
                string initialArg = (args.Length > 0) ? args[0] : null;
                saturn.Run(initialArg);
                app.Run();
            } catch (Exception ex) {
                SaturnLog.LogException("Program.Main", ex);
            }
        }
    }
}
