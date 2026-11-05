"""Small display-only previews; canonical PNG pixels/timing stay untouched."""
from pathlib import Path
import json
from PIL import Image
R=Path(__file__).resolve().parent.parent
def main(only=None):
    manifest=json.loads((R/'manifest.json').read_text(encoding='utf8'));records=[]
    for name,action in manifest['actions'].items():
        if only and name not in only:continue
        clip_path=R/action['path'];clip=json.loads(clip_path.read_text(encoding='utf8'))
        frames=[Image.open(clip_path.parent/f['file']).convert('RGBA') for f in clip['frames']]
        times=[f['duration_ms'] for f in clip['frames']];dest=R/f'previews/{name}.webp'
        options={'loop':0} if clip['loop'] else {'loop':1}
        temporary=dest.with_suffix('.pending.webp')
        frames[0].save(temporary,save_all=True,append_images=frames[1:],duration=times,lossless=False,quality=90,method=0,kmin=1,kmax=1,**options)
        im=Image.open(temporary);decoded=0
        for i in range(im.n_frames):im.seek(i);im.load();decoded+=im.info['duration']
        assert decoded==clip['duration_ms'] and im.size==frames[0].size,(name,decoded)
        im.close();temporary.replace(dest)
        records.append({'action':name,'duration_ms':decoded,'bytes':dest.stat().st_size,'quality':90,'source_png_modified':False})
    html='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>ArcPet 当前动画</title><style>body{font:16px system-ui;background:#ede8de;margin:28px}main{display:flex;flex-wrap:wrap;gap:16px}article{background:white;padding:16px;border-radius:12px}img{width:320px;height:auto;background:#f7f4ee}</style><h1>ArcPet 当前动画</h1><p>网页为压缩预览，程序使用原始透明PNG。跳舞每轮1秒，随机起始朝向、每3轮镜像、共12轮。</p><main>'
    for name,a in manifest['actions'].items():html+=f'<article><h2>{a["label"]}</h2><img src="previews/{name}.webp"><p>{a["frame_count"]}帧 · {a["duration_ms"]/1000:.2f}秒</p></article>'
    (R/'preview.html').write_text(html+'</main>',encoding='utf8')
    (R/'docs/preview-encoding.json').write_text(json.dumps({'preview_only':True,'quality':90,'records':records},ensure_ascii=False,indent=2),encoding='utf8')
    print('Verified'+str(len(records))+' lightweight previews; all source PNGs unchanged.')
if __name__=='__main__':
    import sys
    main(set(sys.argv[1:]) or None)
