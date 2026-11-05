"""Register the approved drag artwork using its original canvas and timings."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parent.parent
design = root / 'design/drag-lift-v1/anime'
manifest_path = root / 'manifest.json'
manifest = json.loads(manifest_path.read_text(encoding='utf8'))
manifest['version'] = 'v18-drag-lift'
for phase, label in [('lift', '拖动 · 拎起'), ('hold', '拖动 · 悬挂'), ('land', '拖动 · 放下')]:
    source = design / phase
    clip = json.loads((source / 'animation.json').read_text(encoding='utf8'))
    action = 'drag_' + phase
    target = root / 'anime' / action
    target.mkdir(exist_ok=True)
    for frame in clip['frames']:
        shutil.copyfile(source / frame['file'], target / frame['file'])
        frame.pop('source_pose', None)
    clip['label'] = label
    (target / 'animation.json').write_text(json.dumps(clip, ensure_ascii=False, indent=2), encoding='utf8')
    manifest['actions'][action] = {
        'path': f'anime/{action}/animation.json',
        **{key: clip[key] for key in ('label', 'frame_count', 'loop', 'duration_ms')}
    }
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf8')
