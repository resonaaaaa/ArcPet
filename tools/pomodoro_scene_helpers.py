"""Whole-scene registration/QA helpers; never assemble character parts."""
import hashlib
import numpy as np
from PIL import Image
from animation_geometry import connected_regions


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def desk_anchor(im):
    alpha = np.asarray(im)[:, :, 3] > 128
    counts = alpha.sum(axis=1)
    counts[:round(im.height * .55)] = 0
    wide = counts >= counts.max() * .86
    mid = int(counts.argmax())
    top, bottom = mid, mid
    while top and wide[top - 1]: top -= 1
    while bottom + 1 < im.height and wide[bottom + 1]: bottom += 1
    xs = np.where(alpha[mid])[0]
    return {'center': float(xs.min() + xs.max()) / 2, 'row': bottom,
            'width': int(xs.max() - xs.min() + 1), 'band': [top, bottom]}


def opaque(frame):
    out = Image.new('RGBA', frame.size, '#f7f4ee')
    out.alpha_composite(frame)
    return out.convert('RGB')


def encode(path, frames, times, palette, loop):
    indexed = [opaque(f).quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    expected = 1 + sum(a.tobytes() != b.tobytes() for a, b in zip(indexed, indexed[1:]))
    indexed[0].save(path, save_all=True, append_images=indexed[1:], duration=times,
                    disposal=2, optimize=False, **({'loop': 0} if loop else {}))
    with Image.open(path) as im:
        assert im.n_frames == expected, (path, im.n_frames, expected)
        assert ('loop' in im.info) == loop
        duration = 0
        for i in range(im.n_frames):
            im.seek(i)
            duration += im.info['duration']
        assert duration == sum(times)
        return im.n_frames


def measurement(im):
    a = np.asarray(im).astype(float)
    r, g, b, al = a.transpose(2, 0, 1)
    red = (al > 128) & (r > g * 2.3) & (r > b * 2.3) & (r > 100)
    red[280:] = False
    ys = np.where(red)[0]
    eye = float(ys.mean()) if len(ys) > 5 else 219.0
    mask = al > 128
    mask[round(eye + 42):] = False
    bb = connected_regions(mask)[0]['bbox']
    hair = (al > 200) & (r > 185) & (g > 130) & (r - g > 12) & (g - b > 24)
    hair[:115] = False; hair[185:] = False; hair[:, :170] = False; hair[:, 290:] = False
    color = np.median(a[hair, :3], axis=0).tolist()
    skin = (al > 180) & (r > 220) & (g > 160) & (b > 120) & (r - g > 23) & (r - g < 60) & (g - b > 8) & (g - b < 50)
    skin[:262] = False; skin[323:] = False; skin[:, :145] = False; skin[:, 320:] = False
    paws = [{'area': reg['area'], 'bbox': reg['bbox'],
             'center': [(reg['bbox'][0] + reg['bbox'][2]) / 2, (reg['bbox'][1] + reg['bbox'][3]) / 2]}
            for reg in connected_regions(skin) if reg['area'] > 70]
    return {'head_bbox_diagnostic': bb, 'head_width_diagnostic': bb[2] - bb[0],
            'eye_center_y': eye, 'hair_patch_median_rgb': color, 'skin_components_diagnostic': paws}
