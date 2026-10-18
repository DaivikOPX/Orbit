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

namespace OrbitWindows {
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

    // === MINIMALIST ORBIT VECTOR LOGO GENERATOR ===
    public static class OrbitLogo {
        public static Canvas CreateVector(double size, bool isDark = false) {
            var c = new Canvas {
                Width = size,
                Height = size,
                ClipToBounds = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            double scale = size / 32.0;

            // Full Pitch-Black Minimalist Squircle Background (#000000)
            var bgSquircle = new System.Windows.Shapes.Rectangle {
                Width = size,
                Height = size,
                RadiusX = 8 * scale,
                RadiusY = 8 * scale,
                Fill = Brushes.Black
            };
            c.Children.Add(bgSquircle);

            // Razor Subtle Orbital Track (#26262B)
            var ring = new Ellipse {
                Width = 20 * scale,
                Height = 20 * scale,
                Stroke = new SolidColorBrush(Color.FromRgb(38, 38, 43)),
                StrokeThickness = Math.Max(0.9, 1.2 * scale)
            };
            Canvas.SetLeft(ring, 6 * scale);
            Canvas.SetTop(ring, 6 * scale);
            c.Children.Add(ring);

            // Radiant Electric Indigo Accent Arc (#6366F1)
            var arcGeom = new PathGeometry();
            var arcFig = new PathFigure {
                StartPoint = new Point(16 * scale, 6 * scale),
                IsClosed = false
            };
            arcFig.Segments.Add(new ArcSegment(
                new Point(26 * scale, 16 * scale),
                new System.Windows.Size(10 * scale, 10 * scale),
                0, false, SweepDirection.Clockwise, true));
            arcGeom.Figures.Add(arcFig);

            var arcPath = new System.Windows.Shapes.Path {
                Data = arcGeom,
                Stroke = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                StrokeThickness = Math.Max(1.2, 1.8 * scale),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            c.Children.Add(arcPath);

            // Solid White Minimalist Central Core (#FFFFFF)
            var core = new Ellipse {
                Width = 5.6 * scale,
                Height = 5.6 * scale,
                Fill = Brushes.White
            };
            Canvas.SetLeft(core, 13.2 * scale);
            Canvas.SetTop(core, 13.2 * scale);
            c.Children.Add(core);

            // Light Indigo Orbiting Satellite Node (#818CF8)
            var sat = new Ellipse {
                Width = 3.6 * scale,
                Height = 3.6 * scale,
                Fill = new SolidColorBrush(Color.FromRgb(129, 140, 248))
            };
            Canvas.SetLeft(sat, 21.2 * scale);
            Canvas.SetTop(sat, 6.8 * scale);
            c.Children.Add(sat);

            return c;
        }
    }

    // === FLUENT RESULT TOAST CARD ===
    public class OrbitToastWindow : Window {
        private TextBlock titleText;
        private TextBlock descText;
        private System.Windows.Controls.Button btnOpen;
        private System.Windows.Controls.Button btnReveal;
        private string targetPath;
        private System.Windows.Threading.DispatcherTimer dismissTimer;

        public OrbitToastWindow() {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Width = 340;
            Height = 110;

            var card = new Border {
                Background = new SolidColorBrush(Color.FromArgb(245, 15, 23, 42)), // Deep obsidian slate
                CornerRadius = new CornerRadius(14),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 99, 102, 241)), // Indigo rim
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(14, 12, 14, 12),
                Effect = new DropShadowEffect {
                    BlurRadius = 24,
                    Opacity = 0.45,
                    ShadowDepth = 6,
                    Color = Color.FromRgb(0, 0, 0)
                }
            };

            var rootStack = new StackPanel();

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            var titleStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(OrbitLogo.CreateVector(18, true));

            titleText = new TextBlock {
                Text = "Action Completed",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(129, 140, 248)), // Neon light indigo
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(titleText);
            Grid.SetColumn(titleStack, 0);
            topRow.Children.Add(titleStack);

            var closeBtn = new TextBlock {
                Text = "✕",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            closeBtn.MouseDown += (s, e) => FadeOutAndHide();
            Grid.SetColumn(closeBtn, 1);
            topRow.Children.Add(closeBtn);

            rootStack.Children.Add(topRow);

            descText = new TextBlock {
                Text = "Saved output file.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Margin = new Thickness(0, 4, 0, 8),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            rootStack.Children.Add(descText);

            var btnRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            btnOpen = new System.Windows.Controls.Button {
                Content = "Open File",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnOpen.Click += (s, e) => {
                try {
                    if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath)) {
                        Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
                    }
                } catch (Exception ex) {
                    OrbitLog.LogException("btnOpen.Click", ex);
                }
                FadeOutAndHide();
            };
            btnRow.Children.Add(btnOpen);

            btnReveal = new System.Windows.Controls.Button {
                Content = "Show in Folder",
                Padding = new Thickness(10, 3, 10, 3),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 10,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnReveal.Click += (s, e) => {
                try {
                    if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath)) {
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", targetPath));
                    }
                } catch (Exception ex) {
                    OrbitLog.LogException("btnReveal.Click", ex);
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
                : new SolidColorBrush(Color.FromRgb(129, 140, 248));
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
            dismissTimer.Stop();
            var fadeOut = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(140)) {
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

            var hubLogo = OrbitLogo.CreateVector(20, true);
            hubLogo.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            hubLogo.Margin = new Thickness(0, 0, 0, 2);

            hubBadge = new TextBlock {
                Text = "ORBIT",
                FontWeight = FontWeights.ExtraBold,
                FontSize = 8.5,
                Foreground = new SolidColorBrush(Color.FromRgb(129, 140, 248)),
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

            Deactivated += (s, e) => {
                CloseWithAnimation();
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
            Show();
            Activate();

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
            currentItems = OrbitCatalog.GetPetalsForExtension(ext, mode);
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
                hubBadge.Text = "ORBIT";
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
                    Title = "Orbit: Select File to " + action.Title
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
                Title = "Orbit: Choose File"
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

    // === ORBIT CATALOG (CATEGORIES & TOOLS & ACTIONS) ===
    public static class OrbitCatalog {
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

    // === FLOATING PINNED SHELF (Tangerine Float Feature) ===
    public class FloatingShelfWindow : Window {
        private StackPanel listPanel;

        public FloatingShelfWindow() {
            Title = "Orbit Pinned Shelf";
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
            headerStack.Children.Add(OrbitLogo.CreateVector(18, true));
            var title = new TextBlock {
                Text = "Orbit Float Shelf",
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
    public static class OrbitLog {
        private static readonly object lockObj = new object();

        public static void Write(string category, string message) {
            try {
                string dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string logFile = Path.Combine(dir, "orbit_debug.log");

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
    public static class OrbitEngine {
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

                string scratch = Path.Combine(userProfile, @".gemini\antigravity\scratch\orbit-windows\ffmpeg.exe");
                if (File.Exists(scratch)) return scratch;

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
                OrbitLog.Write("FFMPEG", "FFmpeg executable not found. Command was: " + args);
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
                        OrbitLog.Write("FFMPEG", "FFmpeg command timed out after " + timeoutMs + "ms: " + args);
                        return false;
                    }
                    if (p.ExitCode != 0) {
                        OrbitLog.Write("FFMPEG", "FFmpeg error (ExitCode " + p.ExitCode + "): " + err);
                        return false;
                    }
                    return true;
                }
            } catch (Exception ex) {
                OrbitLog.LogException("RunFfmpeg", ex);
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
                string tempPng = Path.Combine(Path.GetTempPath(), "orbit_decode_" + Guid.NewGuid().ToString("N") + ".png");
                try {
                    if (RunFfmpeg(string.Format("-i \"{0}\" \"{1}\"", imagePath, tempPng)) && File.Exists(tempPng)) {
                        using (var fs = new FileStream(tempPng, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                            using (var raw = Image.FromStream(fs)) {
                                return new Bitmap(raw);
                            }
                        }
                    }
                } catch (Exception ex) {
                    OrbitLog.LogException("LoadAnyImageSafely.FFmpegFallback", ex);
                } finally {
                    try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                }
                throw new InvalidOperationException("Could not decode image format: " + Path.GetFileName(imagePath));
            }
        }

        // Safe automatic output path resolver (prevents file lock IOExceptions when file is open)
        public static string GetAvailableOutputPath(string path, bool forceNew = false) {
            try {
                string dir = Path.GetDirectoryName(path);
                string baseName = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);

                if (!forceNew && !File.Exists(path)) return path;

                if (!forceNew && File.Exists(path)) {
                    try {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                            return path; // File is not locked, safe to overwrite
                        }
                    } catch { }
                }

                for (int i = 2; i <= 999; i++) {
                    string cand = Path.Combine(dir, string.Format("{0}_{1}{2}", baseName, i, ext));
                    if (!File.Exists(cand)) return cand;
                    try {
                        using (var fs = new FileStream(cand, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                            return cand;
                        }
                    } catch { }
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

            string outPath = customOutPath ?? GetAvailableOutputPath(Path.Combine(dir, baseName + "_orbit." + targetExt));

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
                    string fallbackPng = GetAvailableOutputPath(Path.Combine(dir, baseName + "_orbit.png"));
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
                }
            }

            // 2. VIDEO CONVERSIONS
            if (IsVideoFormat(srcExt)) {
                if (targetExt == "mp4") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v libx264 -preset fast -crf 22 -c:a aac -b:a 192k \"{1}\"", srcPath, outPath));
                } else if (targetExt == "gif") {
                    RunFfmpeg(string.Format("-i \"{0}\" -vf \"fps=12,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{1}\"", srcPath, outPath));
                } else if (targetExt == "mp3") {
                    RunFfmpeg(string.Format("-i \"{0}\" -vn -c:a libmp3lame -b:a 320k \"{1}\"", srcPath, outPath));
                } else if (targetExt == "webm") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v libvpx-vp9 -b:v 0 -crf 30 -c:a libopus \"{1}\"", srcPath, outPath));
                } else if (targetExt == "mkv") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c copy \"{1}\"", srcPath, outPath));
                } else if (targetExt == "mov") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v libx264 -c:a aac \"{1}\"", srcPath, outPath));
                } else if (targetExt == "avi") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:v mpeg4 -qscale:v 3 -c:a mp3 \"{1}\"", srcPath, outPath));
                } else if (targetExt == "wav") {
                    RunFfmpeg(string.Format("-i \"{0}\" -vn -c:a pcm_s16le \"{1}\"", srcPath, outPath));
                } else {
                    RunFfmpeg(string.Format("-i \"{0}\" \"{1}\"", srcPath, outPath));
                }
                if (File.Exists(outPath)) return outPath;
            }

            // 3. AUDIO CONVERSIONS
            if (IsAudioFormat(srcExt)) {
                if (targetExt == "mp3") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a libmp3lame -b:a 320k \"{1}\"", srcPath, outPath));
                } else if (targetExt == "wav") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a pcm_s16le \"{1}\"", srcPath, outPath));
                } else if (targetExt == "flac") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a flac \"{1}\"", srcPath, outPath));
                } else if (targetExt == "m4a") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a aac -b:a 256k \"{1}\"", srcPath, outPath));
                } else if (targetExt == "opus") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a libopus -b:a 128k \"{1}\"", srcPath, outPath));
                } else if (targetExt == "ogg") {
                    RunFfmpeg(string.Format("-i \"{0}\" -c:a libvorbis -q:a 6 \"{1}\"", srcPath, outPath));
                } else {
                    RunFfmpeg(string.Format("-i \"{0}\" \"{1}\"", srcPath, outPath));
                }
                if (File.Exists(outPath)) return outPath;
            }

            // 4. DATA & SPREADSHEET CONVERSIONS (CSV, JSON, XML, TSV)
            if (srcExt == "csv" || srcExt == "tsv") {
                string text = File.ReadAllText(srcPath, Encoding.UTF8);
                char delim = srcExt == "tsv" ? '\t' : ',';
                if (targetExt == "json") {
                    File.WriteAllText(outPath, CsvToJson(text, delim), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "html" || targetExt == "htm") {
                    File.WriteAllText(outPath, CsvToHtml(text, delim, baseName), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "md") {
                    File.WriteAllText(outPath, CsvToMarkdown(text, delim), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "txt") {
                    File.WriteAllText(outPath, text, Encoding.UTF8);
                    return outPath;
                }
            }

            if (srcExt == "json") {
                string jsonText = File.ReadAllText(srcPath, Encoding.UTF8);
                if (targetExt == "csv") {
                    File.WriteAllText(outPath, JsonToCsv(jsonText), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "html" || targetExt == "htm") {
                    File.WriteAllText(outPath, JsonToHtml(jsonText, baseName), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "md") {
                    File.WriteAllText(outPath, JsonToMarkdown(jsonText), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "txt") {
                    File.WriteAllText(outPath, jsonText, Encoding.UTF8);
                    return outPath;
                }
            }

            if (srcExt == "xml") {
                string xmlText = File.ReadAllText(srcPath, Encoding.UTF8);
                if (targetExt == "json") {
                    File.WriteAllText(outPath, XmlToJson(xmlText), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "csv") {
                    File.WriteAllText(outPath, XmlToCsv(xmlText), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "txt") {
                    File.WriteAllText(outPath, xmlText, Encoding.UTF8);
                    return outPath;
                }
            }

            // 5. DOCUMENTS (TXT, MD, HTML, DOCX, PDF)
            if (srcExt == "txt" || srcExt == "md" || srcExt == "html" || srcExt == "htm") {
                string content = File.ReadAllText(srcPath, Encoding.UTF8);
                if (targetExt == "pdf") {
                    CreateTextPdf(content, outPath);
                    return outPath;
                } else if (targetExt == "docx") {
                    CreateTextDocx(content, outPath);
                    return outPath;
                } else if (targetExt == "html" || targetExt == "htm") {
                    File.WriteAllText(outPath, TextOrMdToHtml(content, baseName), Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "txt") {
                    string plain = srcExt.StartsWith("htm") ? StripHtml(content) : content;
                    File.WriteAllText(outPath, plain, Encoding.UTF8);
                    return outPath;
                } else if (targetExt == "md") {
                    File.WriteAllText(outPath, content, Encoding.UTF8);
                    return outPath;
                }
            }

            if (srcExt == "pdf") {
                if (targetExt == "txt") {
                    ExtractPdfText(srcPath, outPath);
                    return outPath;
                } else if (targetExt == "docx") {
                    string txt = ExtractPdfRawText(srcPath);
                    CreateTextDocx(txt, outPath);
                    return outPath;
                } else if (targetExt == "html") {
                    string txt = ExtractPdfRawText(srcPath);
                    File.WriteAllText(outPath, TextOrMdToHtml(txt, baseName), Encoding.UTF8);
                    return outPath;
                }
            }

            // Fallback: Copy if no converter applied
            File.Copy(srcPath, outPath, true);
            return outPath;
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
                if (rawExt == "jpg" || rawExt == "jpeg") {
                    try {
                        using (var bmp = LoadAnyImageSafely(srcPath)) {
                            SafeSaveBitmap(bmp, outPath, "jpg", 55L);
                        }
                        return outPath;
                    } catch {
                        if (RunFfmpeg(string.Format("-i \"{0}\" -q:v 15 \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                            return outPath;
                        }
                    }
                } else if (rawExt == "png") {
                    try {
                        using (var img = LoadAnyImageSafely(srcPath)) {
                            int nw = Math.Max(1, (int)(img.Width * 0.85));
                            int nh = Math.Max(1, (int)(img.Height * 0.85));
                            using (var scaled = new Bitmap(img, nw, nh)) {
                                SafeSaveBitmap(scaled, outPath, "png", 92L);
                            }
                        }
                        return outPath;
                    } catch {
                        if (RunFfmpeg(string.Format("-i \"{0}\" -vf \"scale=iw*0.85:-1\" \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                            return outPath;
                        }
                    }
                } else if (IsVideoFormat(rawExt)) {
                    if (RunFfmpeg(string.Format("-i \"{0}\" -vf \"scale='min(1280,iw)':-2\" -c:v libx264 -crf 28 -preset fast -c:a aac -b:a 128k \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                        return outPath;
                    }
                } else if (IsAudioFormat(rawExt)) {
                    if (RunFfmpeg(string.Format("-i \"{0}\" -c:a libmp3lame -b:a 128k \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                        return outPath;
                    }
                }
                File.Copy(srcPath, outPath, true);
                return outPath;
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
                    if (RunFfmpeg(string.Format("-i \"{0}\" -vf \"format=gray\" \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                        return outPath;
                    }
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
                    if (RunFfmpeg(string.Format("-i \"{0}\" -vf \"transpose=1\" \"{1}\"", srcPath, outPath)) && File.Exists(outPath)) {
                        return outPath;
                    }
                    throw;
                }
            }

            if (toolId == "mute") {
                outPath = Path.Combine(dir, baseName + "_muted" + ext);
                RunFfmpeg(string.Format("-i \"{0}\" -c:v copy -an \"{1}\"", srcPath, outPath));
                return outPath;
            }

            if (toolId == "snapshot") {
                outPath = Path.Combine(dir, baseName + "_cover.jpg");
                RunFfmpeg(string.Format("-ss 00:00:01 -i \"{0}\" -vframes 1 -q:v 2 \"{1}\"", srcPath, outPath));
                if (!File.Exists(outPath)) {
                    RunFfmpeg(string.Format("-ss 00:00:00 -i \"{0}\" -vframes 1 -q:v 2 \"{1}\"", srcPath, outPath));
                }
                return outPath;
            }

            if (toolId == "normalize") {
                outPath = Path.Combine(dir, baseName + "_normalized" + ext);
                RunFfmpeg(string.Format("-i \"{0}\" -af loudnorm \"{1}\"", srcPath, outPath));
                return outPath;
            }

            if (toolId == "hash") {
                string h = ComputeSha256(srcPath);
                System.Windows.Clipboard.SetText(h);
                return srcPath;
            }

            File.Copy(srcPath, outPath, true);
            return outPath;
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

        // --- DOCX GENERATOR (VALID OPENXML ZIP PACKAGE) ---
        public static void CreateTextDocx(string text, string outputPath) {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            using (var fs = new FileStream(outputPath, FileMode.Create)) {
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create)) {
                    // [Content_Types].xml
                    var entry1 = zip.CreateEntry("[Content_Types].xml");
                    using (var sw = new StreamWriter(entry1.Open(), Encoding.UTF8)) {
                        sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
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

                    // word/document.xml
                    var entry3 = zip.CreateEntry("word/document.xml");
                    using (var sw = new StreamWriter(entry3.Open(), Encoding.UTF8)) {
                        var sb = new StringBuilder();
                        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                        sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
                        sb.Append("<w:body>");
                        var lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                        foreach (var line in lines) {
                            string safe = System.Security.SecurityElement.Escape(line);
                            sb.Append("<w:p><w:r><w:t xml:space=\"preserve\">" + safe + "</w:t></w:r></w:p>");
                        }
                        sb.Append("<w:sectPr/>");
                        sb.Append("</w:body></w:document>");
                        sw.Write(sb.ToString());
                    }
                }
            }
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

        public static void ExtractPdfText(string pdfPath, string outTxtPath) {
            string raw = ExtractPdfRawText(pdfPath);
            File.WriteAllText(outTxtPath, raw, Encoding.UTF8);
        }

        public static string ExtractPdfRawText(string pdfPath) {
            try {
                byte[] bytes = File.ReadAllBytes(pdfPath);
                string content = Encoding.ASCII.GetString(bytes);
                var sb = new StringBuilder();
                var matches = Regex.Matches(content, @"\(([^)]*)\)\s*['Tj]");
                foreach (Match m in matches) {
                    if (m.Groups.Count > 1) {
                        sb.AppendLine(m.Groups[1].Value.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\"));
                    }
                }
                if (sb.Length > 0) return sb.ToString();
            } catch { }
            return "Document contents extracted by Orbit.";
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

        private static List<Dictionary<string, string>> ParseJsonObjects(string json) {
            var list = new List<Dictionary<string, string>>();
            var objMatches = Regex.Matches(json, @"\{([^{}]+)\}");
            foreach (Match m in objMatches) {
                var dict = new Dictionary<string, string>();
                var kvMatches = Regex.Matches(m.Groups[1].Value, @"""([^""]+)""\s*:\s*""?([^"",\r\n}]+)""?");
                foreach (Match kv in kvMatches) {
                    dict[kv.Groups[1].Value.Trim()] = kv.Groups[2].Value.Trim();
                }
                if (dict.Count > 0) list.Add(dict);
            }
            return list;
        }

        private static string EscapeJson(string str) {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
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
                    File.Copy(temp, path, true);
                    saved = true;
                    break;
                } catch {
                    Thread.Sleep(50);
                }
            }

            if (!saved) {
                // If path is locked by another program, save with safe alternate name
                string altPath = GetAvailableOutputPath(path, true);
                File.Copy(temp, altPath, true);
                path = altPath;
            }

            try { File.Delete(temp); } catch { }
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

    // === APPLICATION CONTROLLER ===
    public class OrbitApp {
        private NotifyIcon trayIcon;
        private RadialWheelWindow wheelWindow;
        private FloatingShelfWindow shelfWindow;
        private OrbitToastWindow toastWindow;
        private Thread hotkeyThread;
        private bool isRunning = true;
        private double dpiScale = 1.0;
        private volatile bool isWheelOpen = false;

        public void Run(string initialFile = null) {
            Win32.SetProcessDPIAware();
            CalculateDpiScale();
            SoundFX.Init();

            wheelWindow = new RadialWheelWindow();
            shelfWindow = new FloatingShelfWindow();
            toastWindow = new OrbitToastWindow();
            wheelWindow.OnPetalChosen = HandleRealAction;
            wheelWindow.OnWheelClosed = () => { isWheelOpen = false; };

            SetupTrayIcon();
            RegisterShellContextMenu();
            StartNamedPipeListener();

            hotkeyThread = new Thread(DedicatedHotkeyListener) { IsBackground = true };
            hotkeyThread.Start();

            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile)) {
                OpenDialForPath(initialFile, "convert");
            }
        }

        private void SetupTrayIcon() {
            Icon iconObj = SystemIcons.Application;
            try {
                string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string icoPath = Path.Combine(exeDir, "Orbit.ico");
                if (File.Exists(icoPath)) {
                    iconObj = new Icon(icoPath);
                }
            } catch { }

            trayIcon = new NotifyIcon {
                Text = "Orbit for Windows (F8)",
                Visible = true,
                Icon = iconObj
            };

            var menu = new ContextMenuStrip();
            var header = menu.Items.Add("Orbit for Windows");
            header.Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
            header.Enabled = false;

            menu.Items.Add("-");
            menu.Items.Add("🎯 Open Orbit for Selected File (F8)", null, (s, e) => OpenDialForSelected("convert"));
            menu.Items.Add("⚡ Open Tools Wheel (Ctrl+F8)", null, (s, e) => OpenDialForSelected("tools"));
            menu.Items.Add("📌 Open Actions Wheel (Alt+F8)", null, (s, e) => OpenDialForSelected("actions"));
            menu.Items.Add("-");
            menu.Items.Add("📂 Choose File to Orbit...", null, (s, e) => {
                var ofd = new OpenFileDialog { Title = "Select File for Orbit" };
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
                MessageBox.Show("Orbit has been added to your Windows Right-Click Context Menu!\nRight-click any file to Orbit it.", "Orbit Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            menu.Items.Add("-");
            menu.Items.Add("❌ Exit Orbit", null, (s, e) => {
                isRunning = false;
                trayIcon.Visible = false;
                Application.Current.Shutdown();
            });

            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += (s, e) => OpenDialForSelected("convert");

            trayIcon.ShowBalloonTip(3000, "Orbit is Ready", "Select any file in Explorer or Desktop and press F8 to Orbit!\nOr right-click any file in Windows.", ToolTipIcon.Info);
        }

        private void RegisterShellContextMenu() {
            try {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\Orbit")) {
                    if (key != null) {
                        key.SetValue("", "Orbit Quick Tools");
                        key.SetValue("Icon", "\"" + exePath + "\",0");
                        using (var cmd = key.CreateSubKey("command")) {
                            if (cmd != null) {
                                cmd.SetValue("", "\"" + exePath + "\" \"%1\"");
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
                        using (var server = new NamedPipeServerStream("Orbit_SingleInstance_Pipe", PipeDirection.In)) {
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
                    Thread.Sleep(25);

                    // If wheel is showing and user clicks outside, close immediately!
                    if (isWheelOpen) {
                        short lbtn = Win32.GetAsyncKeyState(Win32.VK_LBUTTON);
                        if ((lbtn & 0x8000) != 0) {
                            Application.Current.Dispatcher.Invoke(() => {
                                try {
                                    if (wheelWindow != null && wheelWindow.IsVisible) {
                                        Win32.POINT pt;
                                        Win32.GetCursorPos(out pt);
                                        double dipX = pt.X / dpiScale;
                                        double dipY = pt.Y / dpiScale;
                                        double wheelLeft = Canvas.GetLeft(wheelWindow.WheelCanvas);
                                        double wheelTop = Canvas.GetTop(wheelWindow.WheelCanvas);
                                        double wheelCenterX = wheelLeft + 180;
                                        double wheelCenterY = wheelTop + 180;
                                        double dist = Math.Sqrt(Math.Pow(dipX - wheelCenterX, 2) + Math.Pow(dipY - wheelCenterY, 2));
                                        if (dist > 185) {
                                            wheelWindow.CloseWithAnimation();
                                            isWheelOpen = false;
                                        }
                                    }
                                } catch { }
                            });
                            Thread.Sleep(150);
                        }
                    }

                    short f8Key = Win32.GetAsyncKeyState(Win32.VK_F8);
                    short ctrlKey = Win32.GetAsyncKeyState(Win32.VK_CONTROL);
                    short altKey = Win32.GetAsyncKeyState(Win32.VK_MENU);

                    bool f8 = (f8Key & 0x8000) != 0;
                    bool ctrl = (ctrlKey & 0x8000) != 0;
                    bool alt = (altKey & 0x8000) != 0;

                    if (f8) {
                        string mode = "convert";
                        if (ctrl) mode = "tools";
                        else if (alt) mode = "actions";

                        OpenDialForSelected(mode);
                        Thread.Sleep(450); // Debounce
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

        // Safely retrieves selected file from active Explorer window or Desktop
        private string TryGetSelectedFile() {
            // 1. Shell COM automation
            try {
                IntPtr fgHwnd = Win32.GetForegroundWindow();
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null) {
                    dynamic shell = Activator.CreateInstance(shellType);
                    foreach (dynamic window in shell.Windows()) {
                        try {
                            if (window.HWND == (long)fgHwnd || fgHwnd == IntPtr.Zero) {
                                dynamic sel = window.Document.SelectedItems();
                                if (sel != null && sel.Count > 0) {
                                    string p = sel.Item(0).Path;
                                    if (File.Exists(p)) return p;
                                }
                            }
                        } catch { }
                    }
                    foreach (dynamic window in shell.Windows()) {
                        try {
                            dynamic sel = window.Document.SelectedItems();
                            if (sel != null && sel.Count > 0) {
                                string p = sel.Item(0).Path;
                                if (File.Exists(p)) return p;
                            }
                        } catch { }
                    }
                }
            } catch { }

            // 2. Fallback: Query active selection by simulating Ctrl+C (works everywhere on Windows)
            try {
                System.Windows.Clipboard.Clear();
                Win32.keybd_event(0x11, 0, 0, UIntPtr.Zero); // VK_CONTROL down
                Win32.keybd_event(0x43, 0, 0, UIntPtr.Zero); // VK_C down
                Thread.Sleep(20);
                Win32.keybd_event(0x43, 0, 2, UIntPtr.Zero); // VK_C up
                Win32.keybd_event(0x11, 0, 2, UIntPtr.Zero); // VK_CONTROL up
                Thread.Sleep(60);

                if (System.Windows.Clipboard.ContainsFileDropList()) {
                    var files = System.Windows.Clipboard.GetFileDropList();
                    if (files != null && files.Count > 0 && File.Exists(files[0])) {
                        return files[0];
                    }
                }
            } catch { }

            // 3. Fallback: Pre-existing clipboard file drop list
            try {
                if (System.Windows.Clipboard.ContainsFileDropList()) {
                    var files = System.Windows.Clipboard.GetFileDropList();
                    if (files != null && files.Count > 0 && File.Exists(files[0])) {
                        return files[0];
                    }
                }
            } catch { }

            return null;
        }

        // === ASYNCHRONOUS NON-BLOCKING ACTION HANDLER ===
        private void HandleRealAction(PetalAction action, string filePath) {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
                if (Application.Current != null && Application.Current.Dispatcher != null) {
                    Application.Current.Dispatcher.Invoke(() => {
                        toastWindow.ShowResult("✕ File Not Found", "The selected file is no longer accessible.", null);
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
                        string dupPath = OrbitEngine.GetAvailableOutputPath(Path.Combine(dir, baseName + "_copy" + ext));
                        File.Copy(filePath, dupPath, true);
                        SoundFX.PlayChime();
                        toastWindow.ShowResult("✓ File Duplicated", Path.GetFileName(dupPath), dupPath);
                    }
                } catch (Exception ex) {
                    OrbitLog.LogException("HandleRealAction." + action.ExtOrId, ex);
                }
                return;
            }

            // 2. Real Conversions & Tools in Background Thread
            ThreadPool.QueueUserWorkItem(state => {
                try {
                    string resultPath = null;
                    string title = "";

                    if (action.Mode == "tool") {
                        resultPath = OrbitEngine.ExecuteTool(filePath, action.ExtOrId);
                        title = "✓ " + action.Title;
                    } else {
                        resultPath = OrbitEngine.ExecuteConversion(filePath, action.ExtOrId);
                        title = "✓ Converted to " + action.ExtOrId.ToUpper();
                    }

                    if (Application.Current != null && Application.Current.Dispatcher != null) {
                        Application.Current.Dispatcher.Invoke(() => {
                            SoundFX.PlayChime();
                            toastWindow.ShowResult(title, Path.GetFileName(resultPath), resultPath);
                        });
                    }
                } catch (Exception ex) {
                    OrbitLog.LogException("HandleRealAction." + action.ExtOrId, ex);
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
        [STAThread]
        public static void Main(string[] args) {
            // CLI Mode: Orbit.exe convert <file> --to <ext>
            if (args.Length >= 4 && args[0].Equals("convert", StringComparison.OrdinalIgnoreCase)) {
                string src = args[1];
                string target = args[3];
                try {
                    Console.WriteLine("Orbit CLI: Converting {0} to {1}...", src, target);
                    string res = OrbitEngine.ExecuteConversion(src, target);
                    Console.WriteLine("SUCCESS: {0}", res);
                } catch (Exception ex) {
                    Console.WriteLine("ERROR: {0}", ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            // CLI Mode: Orbit.exe tool <file> --action <toolId>
            if (args.Length >= 4 && args[0].Equals("tool", StringComparison.OrdinalIgnoreCase)) {
                string src = args[1];
                string toolId = args[3];
                try {
                    Console.WriteLine("Orbit CLI: Running tool {0} on {1}...", toolId, src);
                    string res = OrbitEngine.ExecuteTool(src, toolId);
                    Console.WriteLine("SUCCESS: {0}", res);
                } catch (Exception ex) {
                    Console.WriteLine("ERROR: {0}", ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            Process current = Process.GetCurrentProcess();
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
                    using (var client = new NamedPipeClientStream(".", "Orbit_SingleInstance_Pipe", PipeDirection.Out)) {
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
                    OrbitLog.LogException("AppDomain.UnhandledException", e.ExceptionObject as Exception);
                };
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => {
                    OrbitLog.LogException("TaskScheduler.UnobservedTaskException", e.Exception);
                    e.SetObserved(); // Suppresses crash
                };

                // Unlock 120 FPS frame rate across all animations and double transforms
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata(120)
                );

                var app = new Application();
                app.DispatcherUnhandledException += (s, e) => {
                    OrbitLog.LogException("DispatcherUnhandledException", e.Exception);
                    e.Handled = true; // Suppresses UI crash and keeps app alive
                };

                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var orbit = new OrbitApp();
                string initialArg = (args.Length > 0) ? args[0] : null;
                orbit.Run(initialArg);
                app.Run();
            } catch (Exception ex) {
                OrbitLog.LogException("Program.Main", ex);
            }
        }
    }
}
