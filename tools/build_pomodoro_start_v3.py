"""Export whole-scene drawings. Never split, resize or assemble character parts."""
from pathlib import Path
import hashlib
import json
import re
import subprocess

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from animation_geometry import connected_regions

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / 'design/pomodoro-v3'
SIZE, DESK_WIDTH, DESK_ROW = 448, 288, 322


def dump(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf8')


def extract(path, rows, cols):
    sheet = Image.open(path).convert('RGBA')
    pixels = np.asarray(sheet)
    regions = [r for r in connected_regions(pixels[:, :, 3] >= 128) if r['area'] > 2000]
    assert len(regions) == rows * cols, (path, len(regions))
    regions.sort(key=lambda r: (r['bbox'][1] + r['bbox'][3]) / 2)
    ordered = []
    for row in range(rows):
        ordered.extend(sorted(regions[row * cols:(row + 1) * cols], key=lambda r: r['bbox'][0]))
    images, audit = [], []
    for i, region in enumerate(ordered):
        l, t, r, b = region['bbox']
        margin = min(l, t, sheet.width - r, sheet.height - b)
        assert margin >= 2, (path, i, 'source clipped', region['bbox'])
        box = (max(0, l - 2), max(0, t - 2), min(sheet.width, r + 2), min(sheet.height, b + 2))
        a = np.array(sheet.crop(box))
        keep = np.zeros(a.shape[:2], np.uint8)
        for y, x0, x1 in region['runs']:
            keep[y - box[1], x0 - box[0]:x1 - box[0]] = 255
        a[np.asarray(Image.fromarray(keep).filter(ImageFilter.MaxFilter(3))) == 0] = 0
        images.append(Image.fromarray(a))
        audit.append({'cell': i, 'box': list(box), 'opaque_bbox': region['bbox'], 'outer_margin': margin})
    return images, audit


def desk_geometry(im):
    a = np.asarray(im).astype(int)
    r, g, b, al = a.transpose(2, 0, 1)
    wood = (al > 128) & (r > g * 1.2) & (g > b * 1.2) & (r > 70) & (r < 205)
    wood[:round(im.height * .58)] = False
    counts = wood.sum(axis=1)
    row = int(np.where(counts >= counts.max() * .96)[0][0])
    xs = np.where(wood[row])[0]
    return {'center': float(xs.min() + xs.max()) / 2, 'row': row,
            'width': int(xs.max() - xs.min() + 1)}


def contact(frames, path, cols=8, width=168, labels=None):
    out = Image.new('RGB', (cols * width, ((len(frames) + cols - 1) // cols) * (width + 24)), '#f7f4ee')
    draw = ImageDraw.Draw(out)
    for i, frame in enumerate(frames):
        thumb = frame.copy()
        thumb.thumbnail((width, width), Image.Resampling.LANCZOS)
        x, y = (i % cols) * width, (i // cols) * (width + 24)
        out.paste(thumb, (x + (width - thumb.width) // 2, y), thumb)
        draw.text((x + 5, y + width + 4), labels[i] if labels else str(i + 1), fill='#44372c')
    out.save(path)


def measure(im):
    a = np.asarray(im).astype(int)
    r, g, b, al = a.transpose(2, 0, 1)
    # Measure the largest connected golden patch near the eyes, not a fixed
    # width window (which would conceal growth by clipping the measurement).
    red = (al > 128) & (r > g * 2.3) & (r > b * 2.3) & (r > 100)
    red[280:] = False
    eye_y = float(np.where(red)[0].mean())
    gold = (al > 128) & (r > 155) & (r > g + 8) & (g > b + 25)
    gold[round(eye_y + 45):] = False
    main = connected_regions(gold)[0]
    l, t, rr, btm = main['bbox']
    colors = np.concatenate([a[y, x0:x1, :3] for y, x0, x1 in main['runs']])
    head_mask = al > 128
    head_mask[round(eye_y + 42):] = False
    head_bbox = connected_regions(head_mask)[0]['bbox']
    return {'head_silhouette_bbox': head_bbox, 'head_silhouette_width': head_bbox[2] - head_bbox[0],
            'hair_patch_bbox': main['bbox'], 'hair_width': rr - l,
            'hair_patch_median_rgb': np.median(colors, axis=0).tolist()}


def main():
    frames, entries, audits = [], [], {}
    input_names = ['start']
    batches = {}
    for name in input_names:
        art, crops = extract(DESIGN / f'sources/{name}.png', 4, 8)
        geometry = [desk_geometry(im) for im in art]
        factor = DESK_WIDTH / 167.0
        sheet_size = Image.open(DESIGN / f'sources/{name}.png').size
        assert sheet_size == (1536, 1024), 'Grid anchor must be visually recalibrated for a new sheet.'
        out = []
        for i, (im, geom) in enumerate(zip(art, geometry)):
            # All 32 scenes use the SAME atlas grid anchor. Do not let color-
            # threshold variations shift a whole frame and make the desk bob.
            cell = Image.new('RGBA', (192, 256))
            box = crops[i]['box']
            cell.paste(im, (box[0] - i % 8 * 192, box[1] - i // 8 * 256))
            scaled = cell.resize((round(192 * factor), round(256 * factor)), Image.Resampling.LANCZOS)
            x, y = round(SIZE / 2 - 105 * factor), round(DESK_ROW - 185 * factor)
            frame = Image.new('RGBA', (SIZE, SIZE))
            frame.alpha_composite(scaled, (x, y))
            box = frame.getbbox()
            assert min(box[0], box[1], SIZE - box[2], SIZE - box[3]) >= 8, (name, i, box)
            out.append(frame)
        batches[name] = out
        audits[name] = {'scale': factor, 'crops': crops, 'desk_source_geometry': geometry,
                        'fixed_cell_size': [192, 256], 'fixed_cell_anchor': [105, 185],
                        'fixed_output_offset': [x, y]}
    for i, frame in enumerate(batches['start']):
        phase = 'sit' if i < 6 else 'breathe' if i < 14 else 'reach' if i < 18 else 'lift' if i < 24 else 'open'
        for source, pose in [('start', i)] + ([('inbetweens', i)] if len(input_names) == 2 and i < 31 else []):
            frames.append(batches[source][pose])
            entries.append({'file': f'{len(frames) - 1:03}.png', 'source': source,
                            'source_pose': pose, 'phase': phase,
                            'duration_ms': (100 if phase == 'breathe' else 50) if len(input_names) == 2
                            else (200 if phase == 'breathe' else 100)})
    outdir = DESIGN / 'anime/start'
    outdir.mkdir(parents=True, exist_ok=True)
    for f, entry in zip(frames, entries):
        f.save(outdir / entry['file'])
    duration = sum(e['duration_ms'] for e in entries)
    metadata = {'label': '整体重绘：坐下、呼吸、拿书、开书', 'loop': False,
                'canvas': [SIZE, SIZE], 'frame_count': len(frames), 'duration_ms': duration, 'frames': entries}
    dump(outdir / 'animation.json', metadata)
    contact(frames, DESIGN / 'start-frames.png')
    keys = [0, 5, 9, 17, 22, 31]
    contact([batches['start'][i] for i in keys], DESIGN / 'key-poses.png', 3, 300)
    contact([f.crop((120, 100, 330, 325)) for f in frames], DESIGN / 'docs/head-neck-review.png', 8, 192)
    opaque = []
    for f in frames:
        bg = Image.new('RGBA', f.size, '#f7f4ee')
        bg.alpha_composite(f)
        opaque.append(bg.convert('RGB'))
    palette_source = Image.new('RGB', (SIZE * 8, SIZE * ((len(frames) + 7) // 8)))
    for i, f in enumerate(opaque):
        palette_source.paste(f, (i % 8 * SIZE, i // 8 * SIZE))
    palette = palette_source.quantize(colors=256)
    indexed = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in opaque]
    times = [e['duration_ms'] for e in entries]
    indexed[0].save(DESIGN / 'start.gif', save_all=True, append_images=indexed[1:],
                    duration=times, disposal=2, optimize=False)
    frames[0].save(DESIGN / 'start.webp', save_all=True, append_images=frames[1:],
                   duration=times, loop=1, lossless=True)
    with Image.open(DESIGN / 'start.gif') as gif:
        assert 'loop' not in gif.info and gif.n_frames == len(frames)
        assert sum((gif.seek(i) or gif.info['duration']) for i in range(gif.n_frames)) == duration
    with Image.open(DESIGN / 'start.webp') as webp:
        assert webp.n_frames == len(frames)
    hashes = [hashlib.sha256(f.tobytes()).hexdigest() for f in frames]
    assert len(set(hashes)) == len(frames)
    measurements = [measure(f) for f in frames]
    breath_patches = [np.asarray(frames[i])[270:312, 174:278].copy() for i in range(6, 14)]
    breathing_unique = len({hashlib.sha256(a.tobytes()).digest() for a in breath_patches})
    assert breathing_unique == 8
    report = {'scope': 'start only; whole-scene drawings; no runtime integration',
              'frames': len(frames), 'duration_ms': duration,
              'composition': 'Each PNG derives from one whole character+scene drawing; no character-part assembly.',
              'transforms': 'One uniform scale per atlas, rigid translation of entire scene by desk anchor only.',
              'sources': audits, 'measurements': measurements,
              'unique_frames': len(set(hashes)), 'gif_single_play_and_timing': True,
              'painted_breathing_poses': breathing_unique,
              'hair_width_range': [min(m['hair_width'] for m in measurements), max(m['hair_width'] for m in measurements)],
              'head_silhouette_width_range': [min(m['head_silhouette_width'] for m in measurements), max(m['head_silhouette_width'] for m in measurements)],
              'visual_status': 'Requires user approval; pixel statistics do not establish anatomical correctness.'}
    dump(DESIGN / 'docs/verification.json', report)
    template = (DESIGN / 'docs/review-template.html').read_text(encoding='utf8')
    template = template.replace('__FRAMES__', json.dumps(entries))
    (DESIGN / 'review.html').write_text(template, encoding='utf8')
    script = re.search(r'<script>(.*?)</script>', template, re.S).group(1)
    checked = subprocess.run(['node', '--check', '-'], input=script, text=True, capture_output=True)
    assert checked.returncode == 0, checked.stderr
    print({k: report[k] for k in ('frames', 'duration_ms', 'hair_width_range')})


if __name__ == '__main__':
    main()
