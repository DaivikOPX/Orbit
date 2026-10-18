using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace OrbitLogoGenerator {
    class Program {
        static void Main() {
            string outDir = @"C:\Users\daivi\.gemini\antigravity\scratch\orbit-windows";
            Directory.CreateDirectory(outDir);

            // 1. Generate Clean Vector SVG
            string svgPath = Path.Combine(outDir, "orbit_logo.svg");
            string svgContent = @"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 512 512"" width=""100%"" height=""100%"">
  <defs>
    <linearGradient id=""orbitGrad"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">
      <stop offset=""0%"" stop-color=""#6366F1""/>
      <stop offset=""100%"" stop-color=""#A855F7""/>
    </linearGradient>
    <linearGradient id=""bgGrad"" x1=""0%"" y1=""0%"" x2=""100%"" y2=""100%"">
      <stop offset=""0%"" stop-color=""#0F172A""/>
      <stop offset=""100%"" stop-color=""#020617""/>
    </linearGradient>
  </defs>
  <!-- Background Squircle -->
  <rect width=""512"" height=""512"" rx=""116"" fill=""url(#bgGrad)"" stroke=""rgba(255,255,255,0.08)"" stroke-width=""3""/>
  
  <!-- Outer Minimalist Orbital Track -->
  <circle cx=""256"" cy=""256"" r=""148"" fill=""none"" stroke=""rgba(255,255,255,0.18)"" stroke-width=""14""/>
  
  <!-- Accent Orbital Arc -->
  <path d=""M 256 108 A 148 148 0 0 1 404 256"" fill=""none"" stroke=""url(#orbitGrad)"" stroke-width=""14"" stroke-linecap=""round""/>
  
  <!-- Central Core -->
  <circle cx=""256"" cy=""256"" r=""46"" fill=""url(#orbitGrad)""/>
  
  <!-- Orbiting Satellite Dot -->
  <circle cx=""360"" cy=""152"" r=""26"" fill=""#FFFFFF""/>
  <circle cx=""360"" cy=""152"" r=""14"" fill=""url(#orbitGrad)""/>
</svg>";
            File.WriteAllText(svgPath, svgContent);

            // Also copy SVG to Desktop
            File.WriteAllText(@"C:\Users\daivi\OneDrive\Desktop\orbit_logo.svg", svgContent);

            // 2. Render Bitmaps for All Icon Sizes
            int[] sizes = new int[] { 256, 128, 64, 48, 32, 16 };
            byte[][] pngBuffers = new byte[sizes.Length][];

            for (int i = 0; i < sizes.Length; i++) {
                int s = sizes[i];
                using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb)) {
                    using (var g = Graphics.FromImage(bmp)) {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);

                        float scale = s / 512f;

                        // Squircle Background
                        using (var path = GetRoundedRect(new RectangleF(2 * scale, 2 * scale, (512 - 4) * scale, (512 - 4) * scale), 116 * scale)) {
                            using (var bgBrush = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), Color.FromArgb(15, 23, 42), Color.FromArgb(2, 6, 23))) {
                                g.FillPath(bgBrush, path);
                            }
                            using (var strokePen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, 3f * scale))) {
                                g.DrawPath(strokePen, path);
                            }
                        }

                        // Orbital Ring
                        float cx = 256 * scale;
                        float cy = 256 * scale;
                        float r = 148 * scale;
                        float strokeW = Math.Max(1.5f, 14f * scale);

                        using (var ringPen = new Pen(Color.FromArgb(50, 255, 255, 255), strokeW)) {
                            g.DrawEllipse(ringPen, cx - r, cy - r, r * 2, r * 2);
                        }

                        // Accent Arc
                        using (var arcPen = new Pen(Color.FromArgb(99, 102, 241), strokeW)) {
                            arcPen.StartCap = LineCap.Round;
                            arcPen.EndCap = LineCap.Round;
                            g.DrawArc(arcPen, cx - r, cy - r, r * 2, r * 2, -90, 90);
                        }

                        // Center Core
                        float coreR = Math.Max(2f, 46 * scale);
                        using (var coreBrush = new LinearGradientBrush(
                            new PointF(cx - coreR, cy - coreR),
                            new PointF(cx + coreR, cy + coreR),
                            Color.FromArgb(99, 102, 241),
                            Color.FromArgb(168, 85, 247))) {
                            g.FillEllipse(coreBrush, cx - coreR, cy - coreR, coreR * 2, coreR * 2);
                        }

                        // Satellite Dot
                        float satX = 360 * scale;
                        float satY = 152 * scale;
                        float satR = Math.Max(2.5f, 26 * scale);

                        using (var whiteBrush = new SolidBrush(Color.White)) {
                            g.FillEllipse(whiteBrush, satX - satR, satY - satR, satR * 2, satR * 2);
                        }

                        if (s >= 32) {
                            float innerSatR = Math.Max(1.5f, 14 * scale);
                            using (var satInnerBrush = new SolidBrush(Color.FromArgb(99, 102, 241))) {
                                g.FillEllipse(satInnerBrush, satX - innerSatR, satY - innerSatR, innerSatR * 2, innerSatR * 2);
                            }
                        }
                    }

                    using (var ms = new MemoryStream()) {
                        bmp.Save(ms, ImageFormat.Png);
                        pngBuffers[i] = ms.ToArray();
                    }

                    if (s == 256) {
                        bmp.Save(Path.Combine(outDir, "orbit_logo.png"), ImageFormat.Png);
                        bmp.Save(@"C:\Users\daivi\OneDrive\Desktop\orbit_logo.png", ImageFormat.Png);
                    }
                }
            }

            // 3. Package Multi-Resolution .ICO File
            string icoPath = Path.Combine(outDir, "Orbit.ico");
            string desktopIcoPath = @"C:\Users\daivi\OneDrive\Desktop\Orbit.ico";

            byte[] icoBytes = BuildIco(sizes, pngBuffers);
            File.WriteAllBytes(icoPath, icoBytes);
            File.WriteAllBytes(desktopIcoPath, icoBytes);

            Console.WriteLine("Successfully created logo assets: SVG, PNG, and multi-resolution ICO!");
        }

        private static GraphicsPath GetRoundedRect(RectangleF bounds, float radius) {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2f;
            SizeF size = new SizeF(diameter, diameter);
            RectangleF arc = new RectangleF(bounds.Location, size);

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static byte[] BuildIco(int[] sizes, byte[][] pngData) {
            using (var ms = new MemoryStream()) {
                using (var bw = new BinaryWriter(ms)) {
                    int count = sizes.Length;
                    // ICO Header: 6 bytes
                    bw.Write((short)0); // Reserved
                    bw.Write((short)1); // Type 1 = ICO
                    bw.Write((short)count); // Number of images

                    int dataOffset = 6 + (16 * count);

                    // Directory entries
                    for (int i = 0; i < count; i++) {
                        int s = sizes[i];
                        byte w = (byte)(s >= 256 ? 0 : s);
                        byte h = (byte)(s >= 256 ? 0 : s);
                        byte colors = 0;
                        byte reserved = 0;
                        short planes = 1;
                        short bpp = 32;
                        int bytesInRes = pngData[i].Length;

                        bw.Write(w);
                        bw.Write(h);
                        bw.Write(colors);
                        bw.Write(reserved);
                        bw.Write(planes);
                        bw.Write(bpp);
                        bw.Write(bytesInRes);
                        bw.Write(dataOffset);

                        dataOffset += bytesInRes;
                    }

                    // PNG ImageData
                    for (int i = 0; i < count; i++) {
                        bw.Write(pngData[i]);
                    }
                }
                return ms.ToArray();
            }
        }
    }
}
