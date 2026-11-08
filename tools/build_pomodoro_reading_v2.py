"""Export corrected reading artwork, preserving the user-approved start."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import numpy as np
from PIL import Image
from build_pomodoro_start_v3 import extract, contact
from pomodoro_scene_helpers import desk_anchor, digest, opaque, encode, measurement
from pomodoro_color_calibration import calibrate

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / 'design/pomodoro-reading-v2'
START = ROOT / 'design/pomodoro-v3'


def dump(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf8')


def export_atlas(name, rows, cols, target):
    path = DESIGN / f'sources/{name}.png'
    drawings, crops = extract(path, rows, cols)
    anchors = [desk_anchor(im) for im in drawings]
    factor = target['width'] / float(np.median([a['width'] for a in anchors]))
    frames, audit = [], []
    for i, (im, anchor) in enumerate(zip(drawings, anchors)):
        # Same whole-image scaling filter as the approved start. Registration
        # translates complete scenes only. No skin/hair recoloring or warps.
        scaled = im.resize((round(im.width * factor), round(im.height * factor)), Image.Resampling.LANCZOS)
        xy = (round(target['center'] - anchor['center'] * factor), round(target['row'] - anchor['row'] * factor))
        frame = Image.new('RGBA', (448, 448))
        frame.alpha_composite(scaled, xy)
        box = frame.getbbox()
        assert min(box[0], box[1], 448 - box[2], 448 - box[3]) >= 8, (name, i, box)
        frames.append(frame)
        audit.append({'source': crops[i], 'desk_anchor': anchor, 'whole_scene_translation': xy, 'output_bbox': box})
    return frames, {'sha256': digest(path), 'uniform_scale': factor, 'frames': audit}


def main(design=None):
    global DESIGN
    if design is not None: DESIGN = Path(design)
    protected = [START / 'sources/start.png', START / 'start.gif', START / 'start.webp']
    protected += sorted((START / 'anime/start').glob('*'))
    before = {str(p.relative_to(ROOT)): digest(p) for p in protected}
    ref = Image.open(START / 'anime/start/031.png').convert('RGBA')
    target = desk_anchor(ref)
    batches, audits = {}, {}
    for name, rows, cols in [('breath', 2, 4), ('turn-keys', 2, 4), ('early', 2, 4), ('late', 2, 4)]:
        batches[name], audits[name] = export_atlas(name, rows, cols, target)
    assets = [('breath', i) for i in range(8)] + [('turn-keys', i) for i in range(8)]
    # Late pose 0 has an extra paw and is excluded as a complete scene.
    assets += [('early', i) for i in range(8)] + [('late', i) for i in range(1, 8)]
    frames = [batches[name][i] for name, i in assets]
    if (DESIGN / 'sources/blink.png').exists():
        blink, audits['blink'] = export_atlas('blink', 1, 3, target)
        for pose, asset in enumerate((2, 3, 4)):
            frames[asset] = blink[pose]
            assets[asset] = ('blink', pose)
    # One shared WHOLE boundary frame guarantees the approved start endpoint;
    # the following whole-person drawings must also match, verified separately.
    native_loop = DESIGN.name == 'pomodoro-reading-v3'
    if not native_loop: frames[0] = ref.copy()
    uncalibrated = [f.copy() for f in frames]
    frames = calibrate(frames, ref, DESIGN / 'docs/color-calibration.json', reference_first=not native_loop)
    contact([f for i in (0, 2, 8, 16, 19, 24, 29, 15) for f in (uncalibrated[i], frames[i])],
            DESIGN / 'docs/color-before-after.png', 4, 240,
            [label for i in (0, 2, 8, 16, 19, 24, 29, 15) for label in (f'{i} before', f'{i} after')])
    # Source gestures are ordered by their actual drawn paw trajectory, not
    # merely by atlas numbering. The release/withdrawal poses are distinct.
    phase = [16, 17, 18, 9, 19, 10, 20, 21, 23, 22, 24, 11, 12, 25, 26, 13, 27, 28, 29, 30, 14, 15]
    breath_times = [120, 250, 70, 70, 70, 250, 250, 250]
    sequence = [(i, ms) for _ in range(3) for i, ms in enumerate(breath_times)]
    sequence += [(8, 80)] + [(i, 70) for i in phase]
    outdir = DESIGN / 'anime/reading'
    outdir.mkdir(parents=True, exist_ok=True)
    for stale in outdir.glob('[0-9][0-9][0-9].png'):
        if int(stale.stem) >= len(frames):
            assert stale.resolve().parent == outdir.resolve()
            stale.unlink()
    for i, f in enumerate(frames): f.save(outdir / f'{i:03}.png')
    entries = [{'file': f'{i:03}.png', 'duration_ms': ms, 'asset': i} for i, ms in sequence]
    meta = {'label': '阅读修订 · 手带动翻页', 'loop': True, 'canvas': [448, 448],
            'unique_frame_count': len(frames), 'frame_count': len(entries),
            'duration_ms': sum(ms for i, ms in sequence),
            'assets': [{'file': f'{i:03}.png', 'source': 'approved start/031' if i == 0 and not native_loop else name, 'pose': pose}
                       for i, (name, pose) in enumerate(assets)], 'frames': entries}
    dump(outdir / 'animation.json', meta)
    contact(frames, DESIGN / 'all-frames.png')
    page_ids = [8] + phase
    contact([frames[i].crop((140, 238, 321, 326)) for i in page_ids], DESIGN / 'docs/page-contact.png', 7, 200,
            [str(i) for i in page_ids])
    contact([ref, frames[0], frames[1], frames[7], frames[15], ref], DESIGN / 'docs/entry-seam.png', 3, 300,
            ['approved start end', 'reading entry', 'next painted pose', 'breath end', 'loop last', 'loop first'])
    contact([ref] + [frames[i] for i in (1, 9, 10, 22, 11, 12, 27, 15)], DESIGN / 'key-poses.png', 3, 300)
    start_meta = json.loads((START / 'anime/start/animation.json').read_text(encoding='utf8'))
    start_frames = [Image.open(START / 'anime/start' / e['file']).convert('RGBA') for e in start_meta['frames']]
    palette_source = Image.new('RGB', (448 * 8, 448 * 9))
    for i, f in enumerate(start_frames + frames): palette_source.paste(opaque(f), (i % 8 * 448, i // 8 * 448))
    palette = palette_source.quantize(colors=256)
    timeline_frames = [frames[i] for i, ms in sequence]
    times = [ms for i, ms in sequence]
    gif_frames = encode(DESIGN / 'reading.gif', timeline_frames, times, palette, True)
    # Versioned preview avoids displaying a previously cached, uncalibrated GIF.
    if not native_loop: (DESIGN / 'reading-color-fixed.gif').write_bytes((DESIGN / 'reading.gif').read_bytes())
    encode(DESIGN / 'page.gif', [frames[i] for i in [8] + phase], [80] + [70] * len(phase), palette, True)
    timeline_frames[0].save(DESIGN / 'reading.webp', save_all=True, append_images=timeline_frames[1:], duration=times, loop=0, lossless=True)
    combined_frames = encode(DESIGN / 'start-reading.gif', start_frames + timeline_frames * 2,
                             [e['duration_ms'] for e in start_meta['frames']] + times * 2, palette, False)
    assert before == {str(p.relative_to(ROOT)): digest(p) for p in protected}
    if not native_loop: assert frames[0].tobytes() == ref.tobytes()
    assert len({hashlib.sha256(f.tobytes()).digest() for f in frames}) == len(frames)
    assert len({hashlib.sha256(np.asarray(f)[255:315, 150:315].tobytes()).digest() for f in frames[:8]}) == 8
    measurements = [measurement(f) for f in frames]
    ref_m = measurement(ref)
    old_path = DESIGN / 'docs/rejected-reading-entry.png'
    old_m = measurement(Image.open(old_path).convert('RGBA')) if old_path.exists() else None
    rgb = lambda m: np.array(m['hair_patch_median_rgb'])
    new_error = float(np.abs(rgb(measurements[1]) - rgb(ref_m)).mean())
    assert abs(measurements[1]['head_width_diagnostic'] - ref_m['head_width_diagnostic']) <= 6
    assert abs(measurements[1]['eye_center_y'] - ref_m['eye_center_y']) <= 2
    if old_m is not None:
        old_error = float(np.abs(rgb(old_m) - rgb(ref_m)).mean())
        assert new_error < old_error, ('Hair color did not improve', new_error, old_error)
    else: old_error = None
    moving_paws = []
    for asset in [8] + phase:
        parts = [p for p in measurements[asset]['skin_components_diagnostic']
                 if not (p['center'][0] < 190 and p['center'][1] > 283)]
        active = max(parts, key=lambda p: p['area']) if parts else None
        moving_paws.append({'asset': asset, 'moving_paw_diagnostic': active})
    xs = [p['moving_paw_diagnostic']['center'][0] for p in moving_paws if p['moving_paw_diagnostic']]
    assert max(xs) - min(xs) >= 75, 'Page-turn hand is not visibly moving.'
    report = {'scope': 'Artwork only, not integrated', 'unique_frames': len(frames), 'timeline_frames': len(entries),
              'duration_ms': meta['duration_ms'], 'gif_decoded_frames': gif_frames, 'combined_gif_frames': combined_frames,
              'approved_start_unchanged': True, 'approved_start_hashes': before, 'shared_whole_entry_frame': not native_loop,
              'reference': ref_m, 'old_reading_next_pose': old_m, 'sources': audits,
              'measurements': measurements, 'hair_patch_rgb_mean_error_old': old_error,
              'hair_patch_rgb_mean_error_new': new_error,
              'active_paw_horizontal_travel_diagnostic': max(xs) - min(xs),
              'page_motion_diagnostics': moving_paws,
              'excluded_source_cells': {'late': [0]},
              'geometry_and_color': 'Diagnostics support visual comparison; they do not constitute user approval.',
              'body_transform': 'Whole-scene registration followed by user-authorized RGB-only material calibration; alpha and poses unchanged.',
              'browser_playback': 'Local file browser automation is blocked; no bypass attempted.'}
    dump(DESIGN / 'docs/verification.json', report)
    template = (DESIGN / 'docs/review-template.html').read_text(encoding='utf8')
    template = template.replace('__START__', json.dumps(start_meta)).replace('__READING__', json.dumps(meta))
    (DESIGN / 'review.html').write_text(template, encoding='utf8')
    script = re.search(r'<script>(.*?)</script>', template, re.S).group(1)
    checked = subprocess.run(['node', '--check', '-'], input=script, text=True, capture_output=True)
    assert checked.returncode == 0, checked.stderr
    print({k: report[k] for k in ('unique_frames', 'timeline_frames', 'duration_ms', 'hair_patch_rgb_mean_error_old', 'hair_patch_rgb_mean_error_new')})


if __name__ == '__main__': main()
