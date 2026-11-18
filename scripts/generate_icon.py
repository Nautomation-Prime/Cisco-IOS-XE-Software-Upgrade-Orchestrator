import os
import math
from PIL import Image, ImageDraw, ImageFilter

def create_cisco_orchestrator_icon(output_dir):
    os.makedirs(output_dir, exist_ok=True)
    size = 512
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    pad = 28
    x0, y0, x1, y1 = pad, pad, size - pad, size - pad
    corner_radius = 96

    # 1. Soft atmospheric cyan drop-shadow behind squircle
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sdraw = ImageDraw.Draw(shadow)
    sdraw.rounded_rectangle([x0+4, y0+14, x1-4, y1+14], radius=corner_radius, fill=(0, 188, 235, 70))
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    img.paste(shadow, (0, 0), shadow)

    # 2. Squircle Mask & Gradient
    mask = Image.new("L", (size, size), 0)
    mdraw = ImageDraw.Draw(mask)
    mdraw.rounded_rectangle([x0, y0, x1, y1], radius=corner_radius, fill=255)

    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bdraw = ImageDraw.Draw(base)
    for y in range(y0, y1):
        ratio = (y - y0) / (y1 - y0)
        # Deep Tech Cisco Slate Gradient: #0B1D30 to #040A12
        r = int(11 * (1 - ratio) + 4 * ratio)
        g = int(29 * (1 - ratio) + 10 * ratio)
        b = int(48 * (1 - ratio) + 18 * ratio)
        bdraw.line([(x0, y), (x1, y)], fill=(r, g, b, 255))
    
    img.paste(base, (0, 0), mask)

    # 3. Squircle Border Strokes (Cisco Cyan Glow)
    border_img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bdraw = ImageDraw.Draw(border_img)
    bdraw.rounded_rectangle([x0, y0, x1, y1], radius=corner_radius, outline=(0, 188, 235, 220), width=6)
    bdraw.rounded_rectangle([x0+3, y0+3, x1-3, y1-3], radius=corner_radius-3, outline=(0, 236, 229, 70), width=2)
    img.paste(border_img, (0, 0), border_img)

    cx = size // 2
    cy = size // 2

    # 4. Circular Network Topology Orbit
    ring_radius = 142
    draw.ellipse([cx - ring_radius, cy - ring_radius, cx + ring_radius, cy + ring_radius], 
                 outline=(0, 188, 235, 80), width=4)

    # Network orbital node beacons
    for angle_deg in [30, 90, 150, 210, 270, 330]:
        rad = math.radians(angle_deg)
        nx = cx + int(ring_radius * math.cos(rad))
        ny = cy + int(ring_radius * math.sin(rad))
        draw.ellipse([nx-11, ny-11, nx+11, ny+11], fill=(0, 236, 229, 240), outline=(255, 255, 255, 220), width=2)

    # Network topology data links (horizontal lines)
    draw.line([(cx - 105, cy + 35), (cx + 105, cy + 35)], fill=(0, 188, 235, 75), width=3)
    draw.line([(cx - 75, cy + 85), (cx + 75, cy + 85)], fill=(0, 188, 235, 60), width=3)

    # 5. Cisco Iconic 9 Vertical Bridge Bars (Network Infrastructure motif)
    bar_heights = [20, 36, 54, 72, 88, 72, 54, 36, 20]
    spacing = 18
    start_x = cx - (len(bar_heights) - 1) * spacing // 2
    base_y = cy + 125
    for i, h in enumerate(bar_heights):
        bx = start_x + i * spacing
        # Pillar body
        draw.rounded_rectangle([bx - 4, base_y - h, bx + 4, base_y], radius=3, fill=(0, 188, 235, 220))
        # Illuminated beacon tip
        draw.ellipse([bx - 4, base_y - h - 3, bx + 4, base_y - h + 5], fill=(0, 236, 229, 255))

    # 6. Glowing Upgrade Ascension Chevrons (Software Modernization / Upgrade motif)
    tip_y = cy - 118
    wing_w = 72
    wing_y = cy - 50
    inner_y = cy - 66

    # Glowing under-layer for top chevron
    glow_arrow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow_arrow)
    gdraw.polygon([(cx, tip_y - 10), (cx + wing_w + 10, wing_y + 4), (cx, inner_y + 4), (cx - wing_w - 10, wing_y + 4)], fill=(0, 236, 229, 160))
    glow_arrow = glow_arrow.filter(ImageFilter.GaussianBlur(10))
    img.paste(glow_arrow, (0, 0), glow_arrow)

    # Top Upgrade Chevron (Vibrant Electric Cyan)
    draw.polygon([(cx, tip_y), (cx + wing_w, wing_y), (cx, inner_y), (cx - wing_w, wing_y)], 
                 fill=(0, 236, 229, 255))

    # Mid Upgrade Chevron (Cisco Signal Blue)
    mid_tip_y = cy - 52
    mid_wing_y = cy + 10
    mid_inner_y = cy - 6
    draw.polygon([(cx, mid_tip_y), (cx + int(wing_w * 0.85), mid_wing_y), (cx, mid_inner_y), (cx - int(wing_w * 0.85), mid_wing_y)], 
                 fill=(0, 188, 235, 240))

    # Data beam core
    draw.rectangle([cx - 8, cy - 20, cx + 8, cy + 32], fill=(255, 255, 255, 240))

    # 7. Subtle Top Specular Glass Curve
    gloss = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(gloss)
    gdraw.ellipse([cx - 180, y0 - 110, cx + 180, y0 + 130], fill=(255, 255, 255, 20))
    img.paste(gloss, (0, 0), mask)

    # Save 512x512 PNG
    png_path = os.path.join(output_dir, "icon.png")
    img.save(png_path, "PNG")
    print(f"[OK] Generated PNG Icon: {png_path}")

    # Generate Multi-Resolution Windows ICO
    ico_path = os.path.join(output_dir, "app.ico")
    icon_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    img.save(ico_path, format="ICO", sizes=icon_sizes)
    print(f"[OK] Generated Multi-Resolution Windows ICO: {ico_path}")

if __name__ == "__main__":
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(script_dir)
    assets_dir = os.path.join(repo_root, "gui", "CiscoUpgradeOrchestrator.Gui", "Assets")
    create_cisco_orchestrator_icon(assets_dir)
