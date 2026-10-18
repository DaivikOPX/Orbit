import os
import math
from PIL import Image, ImageDraw

def render_ultra_minimal_logo():
    # 1. Generate ultra-minimal SVG with 100% full dark black background
    svg_content = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="100%" height="100%">
  <!-- Full Dark Black Minimalist Background (#000000) -->
  <rect width="512" height="512" rx="128" fill="#000000"/>
  
  <!-- Razor Thin Orbital Track -->
  <circle cx="256" cy="256" r="140" fill="none" stroke="#222226" stroke-width="8"/>
  
  <!-- Pure Electric Indigo Orbit Arc -->
  <path d="M 256 116 A 140 140 0 0 1 396 256" fill="none" stroke="#6366F1" stroke-width="12" stroke-linecap="round"/>
  
  <!-- Solid White Minimalist Central Core -->
  <circle cx="256" cy="256" r="32" fill="#FFFFFF"/>
  
  <!-- Crisp Light Indigo Satellite Node -->
  <circle cx="355" cy="157" r="16" fill="#818CF8"/>
</svg>
'''

    # Paths
    desktop_dir = r"C:\Users\daivi\OneDrive\Desktop"
    scratch_dir = r"C:\Users\daivi\.gemini\antigravity\scratch\orbit-windows"

    for d in [desktop_dir, scratch_dir]:
        svg_path = os.path.join(d, "orbit_logo.svg")
        with open(svg_path, "w", encoding="utf-8") as f:
            f.write(svg_content)
        print(f"Saved SVG to {svg_path}")

    # 2. Render high-res 1024x1024 with 4x supersampling (4096 x 4096) for razor-sharp antialiasing
    SS = 4
    SIZE = 1024 * SS
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Full pitch black squircle (#000000)
    corner_radius = 128 * SS
    draw.rounded_rectangle([0, 0, SIZE, SIZE], radius=corner_radius, fill=(0, 0, 0, 255))

    cx = 256 * SS
    cy = 256 * SS
    r = 140 * SS

    # Razor thin orbital track (#222226)
    track_width = 8 * SS
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(34, 34, 38, 255), width=track_width)

    # Electric Indigo Arc (270 deg to 360 deg -> top to right)
    # PIL arc draws from start angle to end angle in degrees clockwise from 3 o'clock
    # 270 deg is top (12 o'clock), 360/0 deg is right (3 o'clock)
    arc_width = 12 * SS
    draw.arc([cx - r, cy - r, cx + r, cy + r], start=270, end=360, fill=(99, 102, 241, 255), width=arc_width)

    # Smooth rounded end caps on the arc
    cap_r = arc_width // 2
    # Top cap at (cx, cy - r)
    draw.ellipse([cx - cap_r, cy - r - cap_r, cx + cap_r, cy - r + cap_r], fill=(99, 102, 241, 255))
    # Right cap at (cx + r, cy)
    draw.ellipse([cx + r - cap_r, cy - cap_r, cx + r + cap_r, cy + cap_r], fill=(99, 102, 241, 255))

    # Pristine Solid White Central Core (#FFFFFF)
    core_r = 32 * SS
    draw.ellipse([cx - core_r, cy - core_r, cx + core_r, cy + core_r], fill=(255, 255, 255, 255))

    # Orbiting Satellite Node at angle ~315 deg (or 45 deg between 12 and 3)
    # cx + r * cos(-45 deg) = cx + r * sqrt(2)/2
    sat_angle_rad = math.radians(315) # 45 deg above horizontal
    sat_x = cx + r * math.cos(sat_angle_rad)
    sat_y = cy + r * math.sin(sat_angle_rad)
    sat_r = 16 * SS
    draw.ellipse([sat_x - sat_r, sat_y - sat_r, sat_x + sat_r, sat_y + sat_r], fill=(129, 140, 248, 255))

    # Downsample using Lanczos
    final_png = img.resize((1024, 1024), Image.Resampling.LANCZOS)

    for d in [desktop_dir, scratch_dir]:
        png_path = os.path.join(d, "orbit_logo.png")
        final_png.save(png_path, "PNG")
        print(f"Saved PNG to {png_path}")

    # 3. Create Multi-Res Windows ICO (256, 128, 64, 48, 32, 16)
    ico_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    for d in [desktop_dir, scratch_dir]:
        ico_path = os.path.join(d, "Orbit.ico")
        final_png.save(ico_path, format="ICO", sizes=ico_sizes)
        print(f"Saved ICO to {ico_path}")

if __name__ == "__main__":
    render_ultra_minimal_logo()
