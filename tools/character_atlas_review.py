"""Extract complete character drawings from chroma-key atlases for review."""
from pathlib import Path
import json

import numpy as np
from PIL import Image, ImageDraw

from animation_geometry import connected_regions

ROOT = Path(__file__).resolve().parent.parent
SCALE = 0.885
FLOOR = 292
IDLE_CENTER = 136


def dump(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf8')


def extract(sheet, cell, chroma_ceiling=70, grid_size=4):
    x, y = (cell - 1) % grid_size, (cell - 1) // grid_size
    box = (x * sheet.width // grid_size, y * sheet.height // grid_size,
           (x + 1) * sheet.width // grid_size, (y + 1) * sheet.height // grid_size)
    rgb = np.asarray(sheet.crop(box).convert('RGB')).astype(float)
    r, g, b = rgb.transpose(2, 0, 1)
    # Only remove the chroma background. The complete drawing is carried to
    # the output with one common scale and a translation based on both soles.
    green = g - np.maximum(r, b)
    coverage = np.clip((chroma_ceiling - green) / (chroma_ceiling - 13), 0, 1)
    regions = connected_regions(coverage >= 0.5)
    assert regions[0]['area'] > 18000 and all(part['area'] < 16 for part in regions[1:])
    left, top, right, bottom = regions[0]['bbox']
    assert min(left, top, rgb.shape[1] - right, rgb.shape[0] - bottom) >= 8
    edges = (coverage > 0) & (coverage < 1)
    rgb[:, :, 1][edges] = np.minimum(g[edges], np.maximum(r[edges], b[edges]) + 4)
    rgba = np.dstack([rgb, coverage * 255]).round().astype('uint8')
    rgba[rgba[:, :, 3] == 0] = 0
    soles = connected_regions(coverage[bottom - 6:bottom] >= .5)[:2]
    centers = sorted((part['bbox'][0] + part['bbox'][2]) / 2 for part in soles)
    assert len(centers) == 2 and centers[1] - centers[0] >= 55
    center = sum(centers) / 2

    # Check the distance between the two red pupil centers, which is
    # independent of head tilt. Exclude the mouth and dark outlines.
    pupils = connected_regions((coverage > .85) & (r > 100) & (r > g * 2.7)
                               & (r > b * 2.45) & (g < 95) & (b < 110))
    pupils = [part for part in pupils if part['area'] > 45
              and part['bbox'][3] - part['bbox'][1] >= 20]
    pupil_spacing = None
    if len(pupils) == 2:
        p, q = [np.reshape(part['bbox'], (2, 2)).mean(axis=0) for part in pupils]
        pupil_spacing = round(float(np.linalg.norm(p - q) * SCALE), 2)

    source = Image.fromarray(rgba)
    scaled = source.resize((round(source.width * SCALE), round(source.height * SCALE)),
                           Image.Resampling.LANCZOS)
    resized_bottom = scaled.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox()[3]
    position = (round(IDLE_CENTER - center * SCALE), FLOOR - resized_bottom)
    assert position[0] + left * SCALE >= 5
    assert position[1] + top * SCALE >= 5
    assert position[0] + right * SCALE <= 315
    frame = Image.new('RGBA', (320, 320))
    frame.alpha_composite(scaled, position)
    silhouette = frame.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox()
    assert silhouette[3] == FLOOR and min(silhouette[0], silhouette[1], 320-silhouette[2]) >= 5
    return frame, {'cell': cell, 'source_box': box, 'bbox': silhouette,
                   'boot_spacing_px': round((centers[1] - centers[0])*SCALE, 2),
                   'pupil_spacing_px': pupil_spacing}


def contact(frames, background, path):
    size = 184
    grid = Image.new('RGB', (size * 7, (size + 16) * ((len(frames) + 6) // 7)), background)
    ink = 'white' if background == '#26252a' else '#342b27'
    labels = ImageDraw.Draw(grid)
    for i, (name, image) in enumerate(frames.items()):
        x, y = i % 7 * size, i // 7 * (size + 16)
        thumb = image.resize((size, size), Image.Resampling.LANCZOS)
        grid.paste(thumb, (x, y), thumb)
        labels.text((x + 8, y + size), name, fill=ink)
    grid.save(path)
