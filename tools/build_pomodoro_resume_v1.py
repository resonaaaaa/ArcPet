"""Resume artwork: approved whole-pose keys + genuinely drawn inbetweens.

No runtime integration; no character-part assembly or local geometry changes.
"""
from pathlib import Path
import hashlib,json,re,subprocess
import numpy as np
from PIL import Image,ImageDraw
import build_pomodoro_pause_v3 as art
from pomodoro_color_calibration import calibrate,material_stats
from pomodoro_scene_helpers import digest,measurement,opaque,encode
from build_pomodoro_start_v3 import contact
from animation_geometry import connected_regions

ROOT=Path(__file__).resolve().parent.parent
D=ROOT/'design/pomodoro-resume-v1'
PAUSE=ROOT/'design/pomodoro-pause-v3'
READ=ROOT/'design/pomodoro-reading-v3'

def dump(p,obj):p.write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf8')

def clip(directory):
    m=json.loads((directory/'animation.json').read_text(encoding='utf8'))
    return [Image.open(directory/e['file']).convert('RGBA') for e in m['frames']],[e['duration_ms'] for e in m['frames']],m

def clean_palm_and_paper(frame):
    """RGB-only color fill in existing palms/paper; never change alpha/ink."""
    a=np.asarray(frame).copy();r,g,b,al=a.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    skin=(al>128)&(r>175)&(r-g>13)&(r-g<70)&(g-b>5)&(g-b<35)
    skin&=(yy>=275)&(yy<331)&(xx>=145)&(xx<307)
    paw=np.zeros(al.shape,bool)
    def hull(points):
        points=sorted(set(points))
        if len(points)<3:return points
        cross=lambda o,u,v:(u[0]-o[0])*(v[1]-o[1])-(u[1]-o[1])*(v[0]-o[0])
        lo=[];hi=[]
        for p in points:
            while len(lo)>=2 and cross(lo[-2],lo[-1],p)<=0:lo.pop()
            lo.append(p)
        for p in reversed(points):
            while len(hi)>=2 and cross(hi[-2],hi[-1],p)<=0:hi.pop()
            hi.append(p)
        return lo[:-1]+hi[:-1]
    for reg in connected_regions(skin):
        l,t,rr,bb=reg['bbox']
        if reg['area']<120 or (l+rr)/2>=300:continue
        points=[p for y,x0,x1 in reg['runs'] for p in [(x0,y),(x1-1,y)]]
        mask=Image.new('L',frame.size);ImageDraw.Draw(mask).polygon(hull(points),fill=255)
        paw|=np.asarray(mask)>0
    # Convex color supports are inside the already painted round paws; only
    # near-white erroneous fill is changed. Dark outline and alpha stay exact.
    white=(r>=240)&(g>=232)&(b>=220)&(r-g<22)&(g-b<18)&(np.maximum.reduce([r,g,b])-np.minimum.reduce([r,g,b])<30)
    a[paw&white,:3]=[246,222,209]
    depth=np.asarray(art.full_book_depth(Image.fromarray(a)))[:,:,3]>0
    light=depth&~paw&(al>128)&(r>190)&(g>175)&(b>140)&(r>=g)&(g>=b)
    for reg in connected_regions(light):
        if reg['area']<30:continue
        count=sum(int(((r[y,x0:x1]-g[y,x0:x1]>3)&(g[y,x0:x1]-b[y,x0:x1]>8)).sum()) for y,x0,x1 in reg['runs'])
        if count<3:continue
        for y,x0,x1 in reg['runs']:a[y,x0:x1,:3]=[235,226,203]
    assert np.array_equal(a[:,:,3],np.asarray(frame)[:,:,3])
    return Image.fromarray(a)

def main():
    protected=[]
    for directory in [ROOT/'anime',ROOT/'design/pomodoro-v3/anime',PAUSE/'anime',READ/'anime']:
        protected+=sorted(p for p in directory.rglob('*') if p.is_file())
    before={str(p.relative_to(ROOT)):digest(p) for p in protected}
    keyinfo=json.loads((D/'docs/key-sequence.json').read_text(encoding='utf8'))['keys']
    keys=[Image.open(D/k['source']/k['file']).convert('RGBA') for k in keyinfo]
    ref=Image.open(READ/'anime/reading/000.png').convert('RGBA')
    art.D=D;art.fixed.moving_book_depth=art.full_book_depth
    batches={};audits={}
    for n in ['reach-clean','between-b']:batches[n],audits[n]=art.export_scene_atlas(n,ref)
    # The reach atlas's COMPLETE drawing is about 3% larger than the approved
    # scene (both head and boots expand). Register the entire scene with ONE
    # fixed uniform scale around its scene anchor, shared by all eight poses.
    # No head/body/limb is transformed independently; this corrects camera
    # scale, not motion. Original fixed furniture is composited afterward.
    registered=[]
    for f in batches['reach-clean']:
        small=f.resize((435,435),Image.Resampling.LANCZOS)
        out=Image.new('RGBA',(448,448));out.alpha_composite(small,(7,8));registered.append(out)
    batches['reach-clean']=registered
    audits['reach-clean']['additional_whole_scene_registration']={'uniform_scale':435/448,'offset':[7,8],
        'basis':'Visual comparison of complete character head-to-boots bounds against approved paused scene; same transform for all eight scenes.'}
    ids=[(n,i) for n in batches for i in range(8)]
    raw=[batches[n][i] for n,i in ids]
    colored=calibrate(raw,ref,D/'docs/rgb-calibration.json',reference_first=False,blink_indices=(),method='translation',skin_bottom=342)
    colored=calibrate(colored,ref,D/'docs/rgb-residual.json',reference_first=False,blink_indices=(),method='translation',skin_bottom=342)
    colored=[art.clean_white_material(f) for f in colored]
    props={n:Image.open(PAUSE/f'layers/{n}.png').convert('RGBA') for n in ['table','chair','lamp','flower','book']}
    inbetweens=[]
    for i,f in enumerate(colored):
        moving=i>=8
        inbetweens.append(art.fixed.compose(art.remove_drawn_props(f,moving),props,book=not moving,moving=moving))
    sequence=[keys[0]];times=[120];sources=[{'kind':'approved paused entry','source':keyinfo[0]}]
    # The reach sheet was drawn as a continuous gesture, not as its requested
    # paired-cell midpoints. Use it chronologically; interleaving it with tiny
    # chin-rest keys would make the hand move backward.
    for i in range(8):
        sequence.append(inbetweens[i]);times.append(90)
        sources.append({'kind':'new whole-character reach','source':ids[i]})
    for i in range(8,16):
        sequence.extend([keys[i],inbetweens[i]]);times.extend([90,70])
        sources.extend([{'kind':'approved complete pickup/open key','source':keyinfo[i]}, {'kind':'new whole-character inbetween','source':ids[i]}])
    sequence.append(keys[-1]);times.append(120);sources.append({'kind':'approved reading entry','source':keyinfo[-1]})
    # Actual approved breathing/blink frames settle the newly opened book;
    # never use one frozen whole-cat frame as a breathing substitute.
    read_assets=READ/'anime/reading'
    for i,t in zip(range(1,8),[250,70,70,70,250,250,250]):
        sequence.append(Image.open(read_assets/f'{i:03}.png').convert('RGBA'));times.append(t)
        sources.append({'kind':'approved breathing/blink pose','source':f'../pomodoro-reading-v3/anime/reading/{i:03}.png'})
    sequence.append(ref.copy());times.append(120);sources.append({'kind':'approved reading loop seam','source':keyinfo[-1]})
    # Correct exported copies only. Approved source files and the actual
    # original reading breath/loop entries remain byte-for-byte unchanged.
    sequence[1:25]=[clean_palm_and_paper(f) for f in sequence[1:25]]
    assert len(sequence)==34 and sequence[0].tobytes()==keys[0].tobytes() and sequence[-1].tobytes()==ref.tobytes()
    dest=D/'anime/resume';dest.mkdir(parents=True,exist_ok=True)
    for i,f in enumerate(sequence):f.save(dest/f'{i:03}.png')
    meta={'label':'继续 · 拿书并恢复阅读','loop':False,'canvas':[448,448],'unique_frame_count':len({f.tobytes() for f in sequence}),
          'frame_count':len(sequence),'duration_ms':sum(times),'new_inbetweens':16,'approved_whole_keys':17,
          'frames':[{'file':f'{i:03}.png','duration_ms':t,**s} for i,(t,s) in enumerate(zip(times,sources))]}
    dump(dest/'animation.json',meta)
    contact(sequence,D/'all-frames.png',6,300)
    contact([f.crop((142,240,315,343)) for f in sequence],D/'docs/hands-book.png',6,260)
    contact([f.crop((124,65,338,270)) for f in sequence],D/'docs/heads.png',6,240)
    for label,bg in [('light','#f7f4ee'),('dark','#242832')]:
        flat=[]
        for f in sequence:
            q=Image.new('RGBA',f.size,bg);q.alpha_composite(f);flat.append(q)
        contact(flat,D/f'docs/all-{label}.png',6,260)
    contact([keys[0]]+inbetweens[:8]+[keys[8]],D/'docs/reach-gesture.png',5,336)
    for group in [1]:
        comparison=[];labels=[]
        for j in range(8):
            i=group*8+j;comparison.extend([keys[i],inbetweens[i],keys[i+1]])
            labels.extend([f'{i} key before',f'{i} NEW',f'{i} key after'])
        contact(comparison,D/f'docs/paired-{group}.png',3,336,labels)
    paused,paused_times,_=clip(PAUSE/'anime/paused')
    reading,reading_times,read_meta=clip(READ/'anime/reading')
    contact(paused[-3:]+sequence[:4]+sequence[-4:]+reading[1:5],D/'docs/seams.png',5,336)
    palette_image=Image.new('RGB',(448*8,448*((len(sequence)+len(paused)+len(reading)+7)//8)),'#f7f4ee')
    for i,f in enumerate(sequence+paused+reading):palette_image.paste(opaque(f),(i%8*448,i//8*448))
    palette=palette_image.quantize(colors=256)
    output_clips=[('resume',sequence,times,False),('resume-reading',sequence+reading*2,times+reading_times*2,False),
                  ('paused-resume-reading',paused+sequence+reading,paused_times+times+reading_times,False)]
    for name,fs,ts,loop in output_clips:
        encode(D/f'{name}.gif',fs,ts,palette,loop)
        expected=[opaque(f).quantize(palette=palette,dither=Image.Dither.NONE).convert('RGB') for f in fs]
        expected=[f for i,f in enumerate(expected) if i==0 or f.tobytes()!=expected[i-1].tobytes()]
        with Image.open(D/f'{name}.gif') as gif:
            assert gif.n_frames==len(expected)
            for i,f in enumerate(expected):gif.seek(i);assert gif.convert('RGB').tobytes()==f.tobytes(),(name,i)
    # The finite transition is distinct from the existing reading loop. The
    # review page references approved loop files, without copying/modifying it.
    review_clips={'resume':[{'src':'anime/resume/'+e['file'],'ms':e['duration_ms'],'label':f'继续 {i:02} · '+e['kind']} for i,e in enumerate(meta['frames'])],
                  'reading':[{'src':'../pomodoro-reading-v3/anime/reading/'+e['file'],'ms':e['duration_ms'],'label':f'已确认阅读 {i:02}'} for i,e in enumerate(read_meta['frames'])]}
    _,_,paused_meta=clip(PAUSE/'anime/paused')
    review_clips['paused']=[{'src':'../pomodoro-pause-v3/anime/paused/'+e['file'],'ms':e['duration_ms'],'label':f'已确认托腮 {i:02}'} for i,e in enumerate(paused_meta['frames'])]
    template=(D/'docs/review-template.html').read_text(encoding='utf8').replace('__CLIPS__',json.dumps(review_clips))
    (D/'review.html').write_text(template,encoding='utf8')
    script=re.search(r'<script>(.*?)</script>',template,re.S).group(1)
    test=subprocess.run(['node','--check','-'],input=script,text=True,capture_output=True);assert test.returncode==0,test.stderr
    assert before=={str(p.relative_to(ROOT)):digest(p) for p in protected}
    for i,f in enumerate(sequence):
        bb=f.getbbox();assert min(bb[0],bb[1],448-bb[2],448-bb[3])>=6,(i,'crop',bb)
    dump(D/'docs/validation.json',{'scope':'Artwork only, pending user approval','frames':34,'unique_frames':meta['unique_frame_count'],'new_drawings':16,'approved_whole_keys':17,'duration_ms':sum(times),
         'approved_assets_unchanged':True,'sources':audits,'GIF_decoded_pixels_match':True,'JS_syntax':True,
         'frames_diagnostic':[{'index':i,'materials':material_stats(f),'geometry':measurement(f)} for i,f in enumerate(sequence)],
         'limits':'Coordinates and hashes are diagnostics, not visual acceptance. Browser playback not tested.'})
    print({'frames':34,'new_drawings':16,'duration_ms':sum(times),'approved_unchanged':True})

if __name__=='__main__':main()
