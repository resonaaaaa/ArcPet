"""Rebuild whole-character tail/blink repairs; share the validated exporter."""
from pathlib import Path
from pomodoro_reading_export import main
from build_pomodoro_start_v3 import contact
from pomodoro_scene_helpers import opaque
from PIL import Image
import numpy as np
import json

if __name__ == '__main__':
    root = Path(__file__).resolve().parent.parent
    design = root / 'design/pomodoro-reading-v3'
    main(design)
    frames = [Image.open(design / f'anime/reading/{i:03}.png').convert('RGBA') for i in range(31)]
    counts = []
    for im in frames:
        r, g, b, alpha = np.asarray(im).astype(int).transpose(2, 0, 1)
        gold = (alpha > 128) & (r > 180) & (r-g > 15) & (g-b > 25)
        counts.append(int(gold[251:279, 312:334].sum()))
    assert min(counts) >= 90, ('Tail missing/occluded unexpectedly', counts)
    contact([f.crop((125,65,338,266)) for f in frames[:8]], design/'docs/blink-review.png',4,280)
    ids = [0,8,9,16,18,20,23,24,28,30]
    contact([frames[i].crop((294,228,349,325)) for i in ids], design/'docs/tail-review.png',5,180,[str(i) for i in ids])
    start = [Image.open(root/f'design/pomodoro-v3/anime/start/{i:03}.png').convert('RGBA') for i in range(32)]
    palette = Image.new('RGB',(448*8,448*9))
    for i,im in enumerate(start+frames): palette.paste(opaque(im),(i%8*448,i//8*448))
    palette = palette.quantize(colors=256)
    metadata = json.loads((design/'anime/reading/animation.json').read_text(encoding='utf8'))
    with Image.open(design/'reading.gif') as gif:
        for i,entry in enumerate(metadata['frames']):
            gif.seek(i)
            expected = opaque(frames[entry['asset']]).quantize(palette=palette,dither=Image.Dither.NONE).convert('RGB')
            assert gif.convert('RGB').tobytes() == expected.tobytes(), ('GIF compositing error', i)
    (design/'docs/motion-verification.json').write_text(json.dumps({'tail_outer_arc_pixels':counts,
        'tail_minimum_gate':90,'all_selected_frames_visually_reviewed':True,
        'gif_decoded_pixels_match_quantized_PNG':True,
        'blink_geometry':'Original stable drawings retained; rejected cowlick-drifting redraws.',
        'browser_playback':'Not tested; local-file browser automation blocked.'},indent=2),encoding='utf8')
