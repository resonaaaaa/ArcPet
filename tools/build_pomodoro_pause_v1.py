"""Export whole-scene pause drawings. No character-part assembly or warping."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import numpy as np
from PIL import Image, ImageDraw
import build_pomodoro_reading_v2 as atlas
from build_pomodoro_start_v3 import contact
from pomodoro_scene_helpers import desk_anchor, digest, opaque, encode, measurement
from pomodoro_color_calibration import calibrate
from animation_geometry import connected_regions

ROOT = Path(__file__).resolve().parent.parent
DESIGN = ROOT / 'design/pomodoro-pause-v1'
START = ROOT / 'design/pomodoro-v3'
READ = ROOT / 'design/pomodoro-reading-v3'


def dump(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf8')


def book_box(im):
    r,g,b,a = np.asarray(im).astype(int).transpose(2,0,1)
    m = (a>128)&(g>r+4)&(g>b+8)
    m[:285]=False; m[330:]=False; m[:,:165]=False; m[:,290:]=False
    # Largest green-cover component excludes isolated brown/antialias pixels
    # that happen to pass the color test on sleeves or a paw edge.
    regions=[r for r in connected_regions(m) if r['area']>=12]
    if not regions:return None
    boxes=[r['bbox'] for r in regions]
    return [min(b[0] for b in boxes),min(b[1] for b in boxes),max(b[2] for b in boxes),max(b[3] for b in boxes)]


def transparent_review(frames):
    # Diagnostic contact only. Never edits playback frames.
    out=Image.new('RGB',(448*4,448*len(frames)), '#f7f4ee')
    for row,frame in enumerate(frames):
        for col,bg in enumerate(('#f7f4ee','#242832','#4f78a0','#cfbe9d')):
            base=Image.new('RGBA',(448,448),bg); base.alpha_composite(frame)
            out.paste(base.convert('RGB'),(col*448,row*448))
    out.save(DESIGN/'docs/background-check.png')


def main():
    protected = sorted((START/'anime/start').glob('*'))+sorted((READ/'anime/reading').glob('*'))
    protected += [START/'sources/start.png',START/'start.gif',START/'start.webp',READ/'reading.gif']
    before={str(p.relative_to(ROOT)):digest(p) for p in protected}
    reference=Image.open(START/'anime/start/031.png').convert('RGBA')
    read_entry=Image.open(READ/'anime/reading/000.png').convert('RGBA')
    original=Image.open(START/'anime/start/000.png').convert('RGBA')
    atlas.DESIGN=DESIGN
    batches={}; audits={}
    for name in ('close','tilt','place','prop','rest'):
        batches[name],audits[name]=atlas.export_atlas(name,2,4,desk_anchor(reference))
    assets=[(name,i) for name in ('close','tilt','place','prop') for i in range(8)]
    transition=[batches[name][i] for name,i in assets]
    transition[0]=read_entry.copy()
    transition=calibrate(transition,reference,DESIGN/'docs/pause-colors.json',reference_first=True,blink_indices=(),method='translation')
    rest=calibrate(batches['rest'],reference,DESIGN/'docs/paused-colors.json',reference_first=False,blink_indices=(),method='translation')
    rejected=calibrate(batches['rest'][:1],reference,DESIGN/'docs/diagnostic-rejected-method.json',reference_first=False,blink_indices=())
    contact([im.crop((175,253,283,302)).resize((432,196)) for im in (batches['rest'][0],rejected[0],rest[0])],
            DESIGN/'docs/noise-diagnosis.png',3,432,['painted source','rejected quantile grain','constant-shift RGB'])
    # Shared whole endpoint between finite pause and the native breathing loop.
    transition[-1]=rest[0].copy()
    durations=[90]*8+[80]*8+[90]*8+[100]*8
    rest_times=[220]*8
    for name,frames,times,loop in [('pause',transition,durations,False),('paused',rest,rest_times,True)]:
        dest=DESIGN/'anime'/name; dest.mkdir(parents=True,exist_ok=True)
        for i,f in enumerate(frames):f.save(dest/f'{i:03}.png')
        meta={'label':name,'canvas':[448,448],'loop':loop,'unique_frame_count':len(frames),
              'duration_ms':sum(times),'frames':[{'file':f'{i:03}.png','duration_ms':ms} for i,ms in enumerate(times)]}
        if name=='pause':meta['source_poses']=[{'source':n,'pose':i} for n,i in assets]
        dump(dest/'animation.json',meta)
    contact(transition,DESIGN/'all-pause-frames.png',8,224)
    contact(rest,DESIGN/'all-paused-frames.png',4,300)
    contact([read_entry]+transition[:4]+transition[7:10]+transition[15:18]+transition[23:26]+transition[-2:]+rest[:2]+rest[-2:],
            DESIGN/'docs/seams.png',5,260)
    crops=[original]+transition[19:]+rest
    contact([f.crop((145,280,310,334)) for f in crops],DESIGN/'docs/book-position.png',5,260,
            ['original start book']+[f'pause {i}' for i in range(19,32)]+[f'paused {i}' for i in range(8)])
    contact([f.crop((130,190,320,328)) for f in transition],DESIGN/'docs/hands-book.png',8,228)
    transparent_review([transition[i] for i in (1,8,16,24,30)]+[rest[i] for i in (0,3,7)])
    palette_source=Image.new('RGB',(448*8,448*6),'#f7f4ee')
    for i,f in enumerate([read_entry]+transition+rest):palette_source.paste(opaque(f),(i%8*448,i//8*448))
    palette=palette_source.quantize(colors=256)
    counts={}
    counts['pause']=encode(DESIGN/'pause.gif',transition,durations,palette,False)
    counts['paused']=encode(DESIGN/'paused.gif',rest,rest_times,palette,True)
    preview=transition+rest*3
    counts['pause-preview']=encode(DESIGN/'pause-preview.gif',preview,durations+rest_times*3,palette,False)
    read_meta=json.loads((READ/'anime/reading/animation.json').read_text(encoding='utf8'))
    rf=[Image.open(READ/'anime/reading'/e['file']).convert('RGBA') for e in read_meta['frames']]
    counts['reading-pause']=encode(DESIGN/'reading-pause.gif',rf+preview,[e['duration_ms'] for e in read_meta['frames']]+durations+rest_times*3,palette,False)
    # Decode each delivered GIF and compare to the expected shared-palette PNG.
    for filename,frames in [('pause',transition),('paused',rest),('pause-preview',preview),('reading-pause',rf+preview)]:
        expected=[opaque(f).quantize(palette=palette,dither=Image.Dither.NONE).convert('RGB') for f in frames]
        expected=[f for i,f in enumerate(expected) if i==0 or f.tobytes()!=expected[i-1].tobytes()]
        with Image.open(DESIGN/f'{filename}.gif') as gif:
            assert gif.n_frames==len(expected)
            for i,f in enumerate(expected):
                gif.seek(i); assert gif.convert('RGB').tobytes()==f.tobytes(),(filename,i)
    assert before=={str(p.relative_to(ROOT)):digest(p) for p in protected}
    assert transition[-1].tobytes()==rest[0].tobytes()
    assert len({hashlib.sha256(f.tobytes()).digest() for f in transition})==32
    assert len({hashlib.sha256(f.tobytes()).digest() for f in rest})==8
    assert len({hashlib.sha256(np.asarray(f)[253:315,150:315].tobytes()).digest() for f in rest})==8
    checks={'scope':'Art only, not integrated','approved_start_and_reading_unchanged':True,
            'protected_hashes':before,'source_atlases':audits,'transition_frames':32,'transition_ms':sum(durations),
            'paused_frames':8,'paused_loop_ms':sum(rest_times),'shared_whole_exit':True,
            'gif_frames':counts,'decoded_gif_matches_quantized_png':True,
            'original_book_box':book_box(original),'resting_book_boxes':{'pause':[(i,book_box(f)) for i,f in enumerate(transition) if i>=19],
                                                                      'paused':[book_box(f) for f in rest]},
            'measurements':[measurement(f) for f in transition+rest],
            'browser_playback':'Not tested: local file protocol previously blocked; no workaround used.',
            'visual_acceptance':'Pending user. Numeric diagnostics are not proof of art continuity.',
            'remaining_art_difference':'Book resting position/thickness retains roughly 3-6px hand-drawn variation across batches. Not pixel-identical to approved start.'}
    dump(DESIGN/'docs/validation.json',checks)
    payload={'pause':[{'src':f'anime/pause/{i:03}.png','ms':ms,'label':f'暂停 {i+1}/32'} for i,ms in enumerate(durations)],
             'paused':[{'src':f'anime/paused/{i:03}.png','ms':ms,'label':f'托腮呼吸 {i+1}/8'} for i,ms in enumerate(rest_times)],
             'reading':[{'src':'../pomodoro-reading-v3/anime/reading/'+e['file'],'ms':e['duration_ms'],'label':'已确认阅读'} for e in read_meta['frames']]}
    html='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>暂停 · 合书与托腮</title>
<style>body{font:16px system-ui;margin:30px auto;max-width:1050px;background:#f7f4ee;color:#44372c}button,select{font:inherit;padding:8px;margin:4px}canvas{max-width:100%;background:#f7f4ee;border:1px solid #ccbda9;border-radius:12px}.row{display:flex;flex-wrap:wrap;gap:20px}small{display:block}a{color:#77583c}input{width:90%}</style>
<h1>合书放回 · 托腮看向屏幕外</h1><p>暂停32帧／2.88秒；随后接8张实际绘制的托腮呼吸帧，1.76秒一轮。仅美术预览。</p>
<div><select id="mode"><option value="pause">暂停 → 托腮</option><option value="reading">阅读 → 暂停 → 托腮</option><option value="paused">仅托腮呼吸</option></select><button id="play">暂停播放</button><button id="reset">重新播放</button><button id="prev">上一帧</button><button id="next">下一帧</button><select id="speed"><option value="1">正常速度</option><option value="0.5">半速</option></select><select id="bg"><option value="#f7f4ee">浅底</option><option value="#242832">深底</option><option value="#4f78a0">蓝底</option></select></div>
<div class="row"><div><canvas id="canvas" width="448" height="448"></canvas><small id="label"></small></div><div><p>开始动画中的书本原位</p><img src="../pomodoro-v3/anime/start/000.png" width="448" alt="已确认开始原位"></div></div><input id="seek" type="range" min="0" value="0"><p><a href="docs/book-position.png">书本落点逐帧对照</a> · <a href="docs/hands-book.png">手、书和桌沿</a> · <a href="docs/background-check.png">深浅背景检查</a> · <a href="all-pause-frames.png">全部暂停帧</a> · <a href="docs/prompt-set.json">内置绘图提示词</a></p><p>开始及阅读原稿保持不变。书本仍有约3–6像素位置/厚度手绘差异，尚待视觉确认，未接入程序。</p><script>
const clips=PAYLOAD;const el=id=>document.getElementById(id);const ctx=el('canvas').getContext('2d');let sequence=[],loopAt=0,index=0,run=true,elapsed=0,last=0,ready=false;const cache={};
async function prepare(){await Promise.all(Object.values(clips).flat().map(e=>new Promise((resolve,reject)=>{if(cache[e.src])return resolve();let im=new Image();cache[e.src]=im;im.onload=resolve;im.onerror=reject;im.src=e.src;})));ready=true;reset();requestAnimationFrame(tick);}
function reset(){const mode=el('mode').value;sequence=mode==='paused'?[...clips.paused]:[...(mode==='reading'?clips.reading:[]),...clips.pause,...clips.paused];loopAt=sequence.length-clips.paused.length;index=0;elapsed=0;last=0;el('seek').max=sequence.length-1;draw();}
function draw(){if(!ready)return;const f=sequence[index];ctx.fillStyle=el('bg').value;ctx.fillRect(0,0,448,448);ctx.drawImage(cache[f.src],0,0);el('label').textContent=f.label+' · '+f.ms+'ms';el('seek').value=index;el('play').textContent=run?'暂停播放':'继续播放';}
function tick(t){if(last&&run){elapsed+=Math.min(t-last,150)*Number(el('speed').value);while(elapsed>=sequence[index].ms){elapsed-=sequence[index].ms;index++;if(index>=sequence.length)index=loopAt;}}last=t;draw();requestAnimationFrame(tick);}
el('mode').onchange=reset;el('reset').onclick=()=>{run=true;reset();};el('play').onclick=()=>{run=!run;draw();};el('prev').onclick=()=>{run=false;index=Math.max(0,index-1);elapsed=0;draw();};el('next').onclick=()=>{run=false;index=(index+1)%sequence.length;elapsed=0;draw();};el('seek').oninput=()=>{run=false;index=Number(el('seek').value);elapsed=0;draw();};el('bg').onchange=draw;prepare().catch(()=>el('label').textContent='图片加载失败，请保留完整目录后重新打开');
</script></html>'''.replace('PAYLOAD',json.dumps(payload,ensure_ascii=False))
    (DESIGN/'review.html').write_text(html,encoding='utf8')
    script=re.search(r'<script>(.*?)</script>',html,re.S).group(1)
    subprocess.run(['node','--check','-'],input=script,text=True,check=True,capture_output=True)
    print(json.dumps({k:v for k,v in checks.items() if k in ('transition_frames','transition_ms','paused_frames','paused_loop_ms','gif_frames','original_book_box','resting_book_boxes')},ensure_ascii=False))


if __name__=='__main__':main()
