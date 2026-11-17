"""Register approved fortune poses without rasterizing the demo result bubble."""
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
design = ROOT / 'design/fortune-draw-v4'
target = ROOT / 'anime/fortune'


def load(path):
    return json.loads(path.read_text(encoding='utf8'))


def save(path, document):
    path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + '\n', encoding='utf8')


def main():
    approved = load(design / 'animation.json')
    manifest_path = ROOT / 'manifest.json'
    manifest = load(manifest_path)
    names = {'shake': '摇签筒', 'draw': '提签', 'reveal': '亮签'}
    target.mkdir(exist_ok=True)
    assets = {}
    for phase, name in names.items():
        frames = []
        for entry in approved[phase]['frames']:
            original = entry['file']
            if original not in assets:
                assets[original] = f'{len(assets):03d}.png'
                shutil.copyfile(design / original, target / assets[original])
            frames.append({'file': assets[original], 'duration_ms': entry['duration_ms']})
        summary = {'label': '抽签 · ' + name, 'frame_count': len(frames),
                   'duration_ms': sum(frame['duration_ms'] for frame in frames),
                   'loop': approved[phase]['loop']}
        save(target / f'{phase}.json', {**summary, 'canvas': approved['canvas'], 'frames': frames})
        manifest['actions'][f'fortune_{phase}'] = {'path': f'anime/fortune/{phase}.json', **summary}
    save(manifest_path, manifest)


if __name__ == '__main__':
    main()
