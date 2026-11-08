"""User-authorized RGB-only material calibration. Never change alpha/geometry."""
import hashlib
import json
import numpy as np
from PIL import Image


def masks(image, skin_bottom=324):
    a = np.asarray(image).astype(float)
    r, g, b, alpha = a.transpose(2, 0, 1)
    y, x = np.indices(alpha.shape)
    opaque = alpha > 160
    warm = opaque & (r > 150) & (r - g > 12) & (r - g < 75) & (g - b > 8)
    hair_area = ((x >= 125) & (x < 341) & (y >= 60) & (y < 270)) | ((x >= 297) & (x < 331) & (y >= 244) & (y < 321))
    hair_area &= ~((x < 155) & (y > 247))
    skin_area = (x >= 145) & (x < 321) & (y >= 185) & (y < skin_bottom)
    # Soft color boundaries prevent hard seams between cel fill/antialias tones.
    hair_w = hair_area * warm * np.clip((g - b - 23) / 5, 0, 1) * np.clip((r - 150) / 40, 0, 1)
    skin_w = skin_area * warm * np.clip((29 - (g - b)) / 5, 0, 1) * np.clip((r - 205) / 20, 0, 1)
    skin_w *= 1 - hair_w
    white_w = ((x >= 145) & (x < 320) & (y >= 248) & (y < 326) & opaque) * np.clip((25 - (np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b]))) / 10, 0, 1) * np.clip((np.minimum.reduce([r,g,b]) - 150) / 45, 0, 1)
    navy_w = ((x >= 160) & (x < 300) & (y >= 334) & (y < 384) & opaque) * np.clip((b-r-4)/8, 0, 1) * np.clip((b-g)/8, 0, 1)
    boots_w = ((x >= 177) & (x < 283) & (y >= 373) & (y < 432) & opaque & (r < 170) & (g < 140)) * np.clip((r-g-5)/8, 0, 1) * np.clip((g-b-1)/6, 0, 1) * np.clip((r-85)/18, 0, 1)
    weights = {'hair': hair_w, 'skin': skin_w, 'white': white_w, 'navy': navy_w, 'boots': boots_w}
    sample_areas = {
        'hair': (x >= 170) & (x < 290) & (y >= 115) & (y < 185),
        'skin': (x >= 182) & (x < 283) & (y >= 229) & (y < 249),
        'white': (x >= 178) & (x < 287) & (y >= 252) & (y < 280),
        'navy': (x >= 180) & (x < 276) & (y >= 355) & (y < 380),
        'boots': (x >= 190) & (x < 267) & (y >= 385) & (y < 422),
    }
    samples = {name: (w >= .98) & sample_areas[name] for name, w in weights.items()}
    return weights, samples


def material_stats(image, skin_bottom=324):
    a = np.asarray(image)
    _, samples = masks(image, skin_bottom)
    return {name: {'pixels': int(mask.sum()), 'median_rgb': np.median(a[mask, :3], axis=0).tolist()}
            for name, mask in samples.items() if mask.sum() >= 12}


def calibrate(frames, reference, report_path, reference_first=True, blink_indices=(2, 3, 4), method='quantile', skin_bottom=324):
    assert method in ('quantile', 'translation')
    target = np.asarray(reference).astype(float)
    _, target_masks = masks(reference, skin_bottom)
    reference_stats = material_stats(reference, skin_bottom)
    quantiles = [5, 20, 40, 50, 60, 80, 95]
    corrected, audits = [], []
    for index, frame in enumerate(frames):
        source = np.asarray(frame).astype(float)
        before = material_stats(frame, skin_bottom)
        if index == 0 and reference_first:
            corrected.append(frame.copy())
            audits.append({'asset': index, 'shared_reference': True, 'before': before, 'after': before})
            continue
        weights, sample_masks = masks(frame, skin_bottom)
        delta = np.zeros_like(source[:, :, :3])
        total_w = np.zeros(source.shape[:2])
        profiles = {}
        for name, w in weights.items():
            src_mask, dst_mask = sample_masks[name], target_masks[name]
            if src_mask.sum() < 12 or dst_mask.sum() < 12:
                profiles[name] = {'skipped': 'insufficient unoccluded sample'}
                continue
            sq = np.percentile(source[src_mask, :3], quantiles, axis=0)
            tq = np.percentile(target[dst_mask, :3], quantiles, axis=0)
            mapped = np.empty_like(source[:, :, :3])
            for channel in range(3):
                if method == 'translation':
                    # Flat cel fills must not gain contrast/grain from steep
                    # channel-wise quantile curves. A constant material shift
                    # preserves the painted differences and shading slopes.
                    shift = np.median(target[dst_mask, channel])-np.median(source[src_mask, channel])
                    mapped[:, :, channel] = source[:, :, channel] + np.clip(shift, -18, 18)
                else:
                    xp, inv = np.unique(sq[:, channel], return_inverse=True)
                    fp = np.array([np.mean(tq[inv == j, channel]) for j in range(len(xp))])
                    # Clip correction magnitude to avoid changing material identity.
                    fp = xp + np.clip(fp - xp, -18, 18)
                    xp = np.r_[0, xp, 255]; fp = np.r_[0, fp, 255]
                    mapped[:, :, channel] = np.interp(source[:, :, channel], xp, fp)
            delta += (mapped - source[:, :, :3]) * w[:, :, None]
            total_w += w
            profiles[name] = {'sample_pixels': int(src_mask.sum()), 'source_quantiles': sq.tolist(), 'target_quantiles': tq.tolist()}
        result = source.copy()
        result[:, :, :3] += delta / np.maximum(total_w, 1)[:, :, None]
        result = np.rint(np.clip(result, 0, 255)).astype(np.uint8)
        # Remove residual median drift caused by rounding and soft boundaries.
        # Recompute material samples, but never expand beyond the original
        # color-correction support or change spatial/alpha data.
        for _ in range(5):
            current = Image.fromarray(result)
            # Fixed source supports are important: reclassifying dark boot
            # pixels after every correction can create a feedback loop.
            current_stats = {name: {'median_rgb': np.median(result[mask, :3], axis=0).tolist()}
                             for name, mask in sample_masks.items() if mask.sum() >= 12}
            current_weights = weights
            residual = np.zeros_like(source[:, :, :3])
            residual_w = np.zeros(source.shape[:2])
            for name, stats in current_stats.items():
                if name not in reference_stats: continue
                shift = np.array(reference_stats[name]['median_rgb']) - np.array(stats['median_rgb'])
                w = current_weights[name] * (total_w > 0)
                residual += shift * w[:, :, None]
                residual_w += w
            updated = result.astype(float)
            updated[:, :, :3] += residual / np.maximum(residual_w, 1)[:, :, None]
            result = np.rint(np.clip(updated, 0, 255)).astype(np.uint8)
        # Blink lids must use face peach, not the near-white eye fill left by
        # some painted frames. RGB-only, with dark ink and white eyes excluded.
        if index in blink_indices:
            rr, gg, bb = result[:, :, :3].astype(float).transpose(2, 0, 1)
            yy, xx = np.indices(rr.shape)
            lid = ((xx >= 174) & (xx < 283) & (yy >= 199) & (yy < 232)
                   & (result[:, :, 3] > 160) & (rr-gg > 12) & (rr-gg < 65) & (gg-bb > 5) & (gg-bb < 26))
            lw = lid * (total_w > 0) * np.clip((rr-232)/10, 0, 1) * np.clip((26-(gg-bb))/4, 0, 1)
            base = np.array(reference_stats['skin']['median_rgb'])
            filled = result[:, :, :3].astype(float) * (1-lw[:, :, None]) + base * lw[:, :, None]
            result[:, :, :3] = np.rint(filled).astype(np.uint8)
        # RGB under fully transparent pixels is preserved too, for clean diffs.
        result[source[:, :, 3] == 0] = source[source[:, :, 3] == 0].astype(np.uint8)
        assert np.array_equal(result[:, :, 3], np.asarray(frame)[:, :, 3])
        assert np.array_equal(result[total_w == 0], np.asarray(frame)[total_w == 0])
        out = Image.fromarray(result)
        corrected.append(out)
        audits.append({'asset': index, 'before': before, 'after': material_stats(out, skin_bottom), 'profiles': profiles,
                       'fixed_sample_after': {name: np.median(result[mask, :3], axis=0).tolist()
                                              for name, mask in sample_masks.items() if mask.sum() >= 12},
                       'alpha_sha256': hashlib.sha256(result[:, :, 3].tobytes()).hexdigest(),
                       'changed_rgb_pixels': int(np.any(result[:, :, :3] != source[:, :, :3], axis=2).sum())})
    report = {'method': 'User-authorized material color calibration; RGB only', 'mapping': method, 'skin_bottom': skin_bottom,
              'reference': reference_stats, 'alpha_unchanged_all_frames': True,
              'geometry_and_timing_unchanged': True, 'assets': audits}
    for audit in audits:
        for name, rgb in audit.get('fixed_sample_after', {}).items():
            if name not in reference_stats: continue
            assert np.max(np.abs(np.array(rgb) - np.array(reference_stats[name]['median_rgb']))) <= 1, (audit['asset'], name, rgb)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
    return corrected
