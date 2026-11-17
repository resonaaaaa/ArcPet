"""Export complete fortune-draw poses and animate a sample result for review."""
import argparse
from collections import OrderedDict
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFont

from character_atlas_review import ROOT, contact, dump, extract

DESIGN = ROOT / 'design/fortune-draw-v4'
ASSETS = ROOT / 'app/assets/moods/clouds'
SAMPLE = '上上签'

SHAKE = list(zip(
    [f'plain-{i:02d}' for i in range(1, 17)],
    [74, 76, 82, 87, 94, 83, 76, 81, 80, 78, 76, 87, 88, 82, 75, 72],
))
DRAW = [
    ('plain-01', 82),
    ('draw-01', 75),
    ('draw-02', 70),
    ('draw-03', 62),
    ('draw-04', 58),
    ('draw-05', 60),
    ('draw-06', 58),
    ('draw-07', 62),
    ('draw-08', 64),
    ('draw-09', 62),
    ('draw-10', 62),
    ('draw-11', 68),
    ('draw-12', 76),
    ('draw-13', 72),
    ('draw-14', 72),
    ('draw-15', 74),
    ('draw-16', 90),
]
REVEAL = [('draw-16', ms) for ms in (70, 70, 70, 70, 70, 70, 70, 1440)]


def save_webp(path, frames, durations, loop):
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=durations,
                   loop=loop, lossless=True, method=1)
    with Image.open(path) as webp:
        assert webp.n_frames == len(frames)
        for i, (frame, ms) in enumerate(zip(frames, durations)):
            webp.seek(i)
            webp.load()
            assert frame.convert('RGBa').tobytes() == webp.convert('RGBa').tobytes(), i
            assert webp.info['duration'] == ms, i


def make_bubble(font):
    layers = [Image.open(ASSETS / f'{name}.png').convert('RGBA')
              for name in ('small', 'medium', 'main')]
    for index, layer in enumerate(layers):
        width = (15, 25, 128)[index]
        layers[index] = layer.resize((width, round(width * layer.height / layer.width)),
                                     Image.Resampling.NEAREST)
    text = Image.new('RGBA', layers[2].size)
    draw = ImageDraw.Draw(text)
    draw.text((text.width/2, text.height/2 - 1), SAMPLE, font=font,
              fill='#51352e', anchor='mm')
    ink = text.getchannel('A').getbbox()
    assert ink is not None and min(ink[0], ink[1], text.width-ink[2], text.height-ink[3]) >= 12
    assert layers[2].getchannel('A').crop(ink).getextrema() == (255, 255)
    return list(zip(layers + [text], [(249,115), (266,97), (282,13), (282,13)],
                    [0, 110, 230, 300]))


def motion_differences(poses, timeline, loop):
    frames = [np.asarray(poses[name].convert('RGBa'), dtype=np.float32) for name, _ in timeline]
    if loop:
        frames.append(frames[0])
    return [round(float(np.abs(a - b).mean()), 2) for a, b in zip(frames, frames[1:])]

def fit_whole_cel(image, reference_height):
    rgba = np.asarray(image.convert('RGB'), dtype=np.int16)
    red, green, blue = rgba.transpose(2, 0, 1)
    foreground_y = np.where(green - np.maximum(red, blue) < 30)[0]
    source_height = int(foreground_y.max() - foreground_y.min()) + 1
    scale = reference_height / source_height
    return image.resize((round(image.width * scale), round(image.height * scale)),
                        Image.Resampling.LANCZOS)


def main(font_file, directory):
    shake, draw, reveal = SHAKE, DRAW, REVEAL
    framing = (416, 320)
    actor_x = 44
    folder = directory / 'frames'
    folder.mkdir(parents=True, exist_ok=True)
    atlases = {}
    poses = OrderedDict()
    metadata = {}
    for key, _ in shake + draw + reveal:
        if key in poses:
            continue
        atlas, cell = key.rsplit('-', 1)
        if atlas not in atlases:
            atlases[atlas] = Image.open(DESIGN / 'sources' / f'{atlas}.png')
        pose, geometry = extract(atlases[atlas], int(cell), chroma_ceiling=50)
        if key == 'plain-11':
            # One red cap survived the atlas pass. Use the redrawn full cel,
            # at its neighbors' scale, while retaining the same sole alignment.
            box = geometry['bbox']
            corrected = Image.open(DESIGN / 'sources' / 'plain-11.png')
            corrected = fit_whole_cel(corrected, (box[3] - box[1]) / 0.885)
            pose, geometry = extract(corrected, 1, chroma_ceiling=50, grid_size=1)
            atlas = 'plain-11'
        assert pose.getpixel((10, 10))[3] == 0
        poses[key] = pose
        metadata[key] = {'atlas': atlas, **geometry}
        pose.save(folder / f'{key}.png')

    def entries(timeline):
        return [{'file': f'frames/{name}.png', 'duration_ms': ms} for name, ms in timeline]

    save_webp(directory / 'shake.webp', [poses[name] for name, _ in shake],
              [ms for _, ms in shake], loop=0)
    font = ImageFont.truetype(font_file, 23)
    bubble = make_bubble(font)
    for layer, (x, y), _ in bubble:
        assert x + layer.width <= framing[0] and y + layer.height <= framing[1]
        for name in dict.fromkeys(name for name, _ in reveal):
            actor_alpha = poses[name].getchannel('A').crop(
                (x-actor_x, y, x-actor_x+layer.width, y+layer.height))
            assert ImageChops.multiply(actor_alpha, layer.getchannel('A')).getbbox() is None
    preview = []
    times = []
    bubble_ms = 0
    for name, ms, show in [(name, ms, False) for name, ms in shake*2 + draw] + [
            (name, ms, True) for name, ms in reveal]:
        frame = Image.new('RGBA', framing)
        frame.alpha_composite(poses[name], (actor_x, 0))
        if show:
            bubble_ms += ms if ms < 100 else 140
            for layer, position, delay in bubble:
                opacity = min(1, max(0, (bubble_ms-delay)/210))
                if opacity > 0:
                    shaded = layer.copy()
                    shaded.putalpha(layer.getchannel('A').point(lambda a: round(a*opacity)))
                    frame.alpha_composite(shaded, position)
        preview.append(frame)
        times.append(ms)
    save_webp(directory / 'fortune-draw.webp', preview, times, loop=1)
    preview[-1].save(directory / 'result-sample.png')
    for color, name in [('#f3eee7', 'light'), ('#26252a', 'dark')]:
        contact(poses, color, directory / f'poses-{name}.png')
    dump(directory / 'animation.json', {
        'canvas': [320, 320],
        'shake': {'loop': True, 'frames': entries(shake)},
        'draw': {'loop': False, 'frames': entries(draw)},
        'reveal': {'loop': False, 'frames': entries(reveal), 'bubble_starts_on_entry': True},
    })
    dump(directory / 'verification.json', {
        'whole_character_drawings': len(poses), 'shake_duration_ms': sum(ms for _, ms in shake),
        'draw_duration_ms': sum(ms for _, ms in draw), 'preview_duration_ms': sum(times),
        'preview_frame_count': len(preview), 'source_poses': metadata,
        'shake_frame_mean_rgba_changes': motion_differences(poses, shake, loop=True),
        'draw_and_reveal_mean_rgba_changes': motion_differences(poses, draw + reveal, loop=False),
        'webp_decode_and_frame_times_verified': True,
    })
    print(f'{len(poses)} drawings, {len(preview)} preview frames, {sum(times)} ms')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--font', type=Path, default=Path('C:/Windows/Fonts/msyhbd.ttc'))
    parser.add_argument('--output', type=Path, default=DESIGN)
    args = parser.parse_args()
    main(str(args.font), args.output)
