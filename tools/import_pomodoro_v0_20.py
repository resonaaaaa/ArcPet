"""Register approved whole-scene artwork with one uniform viewport transform."""
from pathlib import Path
import json, hashlib
from PIL import Image

ROOT=Path(__file__).resolve().parent.parent
def read(p):return json.loads(p.read_text('utf8'))
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def fit(im):
    # Inverse of the approved ending preview's native-idle viewport (200/164,
    # 64,68), with 32,64 padding for the passive 384px artwork window.
    return im.transform((384,384),Image.Transform.AFFINE,
        (200/164,0,(64-32/(164/200)),0,200/164,(68-64/(164/200))),
        resample=Image.Resampling.BICUBIC)
def native(index):
    out=Image.new('RGBA',(384,384));out.alpha_composite(Image.open(ROOT/f'anime/idle/{index:03}.png').convert('RGBA'),(32,64));return out
def register_clip(out,filename,name,frames,loop,manifest,report,source,label=None):
    clip={'label':label or '番茄钟 · '+name,'canvas':[384,384],'loop':loop,
          'frame_count':len(frames),'duration_ms':sum(f['duration_ms'] for f in frames),'frames':frames}
    dump(out/filename,clip)
    manifest['actions']['pomodoro_'+name]={'path':f'anime/{out.name}/{filename}',
        **{k:clip[k] for k in ['label','frame_count','duration_ms','loop']}}
    report[name]={'source':source,'frames':len(frames),'duration_ms':clip['duration_ms']}
def register_reading(out,frames,manifest,report,source):
    # The approved timeline repeats the same blink/breath drawings three times
    # before turning a page. Reuse those drawings in three independent sequences.
    blink=frames[:8]
    plain=[dict(blink[i],duration_ms=blink[i]['duration_ms']+extra)
           for i,extra in [(0,0),(1,105),(5,105),(6,0),(7,0)]]
    for filename,name,sequence,loop,label in [
        ('animation.json','read',plain,True,'番茄钟 · 阅读呼吸'),
        ('blink.json','read_blink',blink,False,'番茄钟 · 阅读眨眼'),
        ('page.json','read_page',frames[24:],False,'番茄钟 · 阅读翻页')]:
        register_clip(out,filename,name,sequence,loop,manifest,report,source,label)
    duplicates=[(out/f'{i:03}.png',out/f'{i%8:03}.png') for i in range(8,24)]
    for duplicate,original in duplicates:
        assert not duplicate.exists() or digest(duplicate)==digest(original),duplicate
    for duplicate,_ in duplicates:
        duplicate.unlink(missing_ok=True)
def main():
    before={str(p.relative_to(ROOT)):digest(p) for p in (ROOT/'anime').rglob('*') if p.is_file() and not p.parent.name.startswith('pomodoro_')}
    sources={
      'start':'pomodoro-v3/anime/start','read':'pomodoro-reading-v3/anime/reading',
      'pause':'pomodoro-pause-v3/anime/pause','paused':'pomodoro-pause-v3/anime/paused',
      'resume':'pomodoro-resume-v1/anime/resume','stretch':'pomodoro-end-stretch-v1/anime/end-stretch',
      'end':'pomodoro-end-tomato-v1/anime/end-tomato'}
    manifest=read(ROOT/'manifest.json');report={};fade=ROOT/'design/pomodoro-end-tomato-v1/anime/fade'
    for name,source in sources.items():
        folder=ROOT/'design'/source;meta=read(folder/'animation.json');out=ROOT/'anime'/('pomodoro_'+name);out.mkdir(exist_ok=True)
        frames=[];clock=0;impact=0;fade_at=0
        for i,f in enumerate(meta['frames']):
            if name=='read' and 8<=i<24:
                assert f==meta['frames'][i%8]
                frames.append({'file':f'{i:03}.png','duration_ms':f['duration_ms']})
                continue
            im=fit(Image.open(folder/f['file']).convert('RGBA'));phase=f.get('phase','')
            if name=='end':
                if phase=='impact / lens coverage':
                    if impact>=3:clock+=f['duration_ms']
                    impact+=1
                elif phase=='fully occluded idle handoff':clock+=f['duration_ms']
                elif phase=='sauce fade / real idle breathing':
                    im=native(clock//70%30);im.alpha_composite(fit(Image.open(fade/f'{fade_at:03}.png').convert('RGBA')))
                    fade_at+=1;clock+=f['duration_ms']
                elif phase=='native idle frame remainder':im=native(clock//70%30);clock+=f['duration_ms']
            im.save(out/f'{i:03}.png');frames.append({'file':f'{i:03}.png','duration_ms':f['duration_ms']})
        if name=='end':
            while clock%2100:
                i=len(frames);ms=70-clock%70;native(clock//70%30).save(out/f'{i:03}.png')
                frames.append({'file':f'{i:03}.png','duration_ms':ms});clock+=ms
        if name=='read':register_reading(out,frames,manifest,report,source)
        else:register_clip(out,'animation.json',name,frames,name=='paused',manifest,report,source)
    manifest['version']='v20-pomodoro';dump(ROOT/'manifest.json',manifest)
    assert all(digest(ROOT/p)==h for p,h in before.items())
    dest=ROOT/'docs/desktop-v0.20-validation';dest.mkdir(exist_ok=True)
    dump(dest/'artwork-import.json',{'uniform_scale':164/200,'artwork_offset':[-32,-64],
        'whole_scene_transform_only':True,'existing_files_unchanged':len(before),'clips':report,
        'ending_native_idle_alignment':True})
    # Full-scene size comparison for visual review; no local character edits.
    images=[native(0),Image.open(ROOT/'anime/pomodoro_read/000.png').convert('RGBA'),Image.open(ROOT/'anime/pomodoro_paused/000.png').convert('RGBA')]
    sheet=Image.new('RGBA',(1152,384),'#f4eee4')
    for i,im in enumerate(images):sheet.alpha_composite(im,(i*384,0))
    sheet.convert('RGB').save(dest/'size-comparison.png')
    print(report)
if __name__=='__main__':main()
