"""Ending part2 art only: complete actor drawings + separately drawn VFX."""
from pathlib import Path
import json,re,subprocess
import numpy as np
from PIL import Image,ImageFilter
import build_pomodoro_pause_v3 as art
from build_pomodoro_end_stretch_v1 import character_scene
from animation_geometry import connected_regions
from pomodoro_color_calibration import calibrate
from pomodoro_scene_helpers import digest,opaque,encode
from build_pomodoro_start_v3 import contact

ROOT=Path(__file__).resolve().parent.parent;D=ROOT/'design/pomodoro-end-tomato-v1'
PART1=ROOT/'design/pomodoro-end-stretch-v1';PAUSE=ROOT/'design/pomodoro-pause-v3'
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')

def grid_effect(name,rows,cols,connected):
    sheet=Image.open(D/f'sources/{name}.png').convert('RGBA');frames=[]
    for i in range(rows*cols):
        box=(round(i%cols*sheet.width/cols),round(i//cols*sheet.height/rows),round((i%cols+1)*sheet.width/cols),round((i//cols+1)*sheet.height/rows))
        cell=sheet.crop(box)
        if connected:
            a=np.asarray(cell).copy();regs=connected_regions(a[:,:,3]>=180);reg=max(regs,key=lambda r:r['area'])
            mask=np.zeros(a.shape[:2],np.uint8)
            for y,x0,x1 in reg['runs']:mask[y,x0:x1]=255
            mask=np.asarray(Image.fromarray(mask).filter(ImageFilter.MaxFilter(3)))>0;a[~mask]=0;cell=Image.fromarray(a)
        else:
            # Atlas divider gutters are not part of the fluid/lens artwork.
            cell=cell.crop((8,8,cell.width-8,cell.height-8))
        frames.append(cell.resize((448,448),Image.Resampling.LANCZOS))
    return frames

def save_frames(name,fs,ts,labels):
    out=D/'anime'/name;out.mkdir(parents=True,exist_ok=True)
    for i,f in enumerate(fs):f.save(out/f'{i:03}.png')
    m={'label':name,'canvas':[448,448],'loop':False,'frame_count':len(fs),'duration_ms':sum(ts),
       'frames':[{'file':f'{i:03}.png','duration_ms':t,'phase':label} for i,(t,label) in enumerate(zip(ts,labels))]}
    dump(out/'animation.json',m);return m

def main():
    protect=[]
    for folder in [ROOT/'anime',ROOT/'design/pomodoro-v3/anime',ROOT/'design/pomodoro-reading-v3/anime',PAUSE/'anime',PART1/'anime',ROOT/'design/pomodoro-resume-v1/anime']:
        protect+=sorted(p for p in folder.rglob('*') if p.is_file())
    before={str(p.relative_to(ROOT)):digest(p) for p in protect}
    entry=Image.open(PART1/'anime/end-stretch/041.png').convert('RGBA')
    ref=Image.open(ROOT/'design/pomodoro-reading-v3/anime/reading/000.png').convert('RGBA')
    art.D=D;raw=[];audits={}
    for n in ['fetch','windup','throw']:
        fs,audits[n]=art.export_scene_atlas(n,ref);raw+=fs
    fs=calibrate(raw,ref,D/'docs/rgb-calibration.json',False,(),method='translation',skin_bottom=342)
    fs=calibrate(fs,ref,D/'docs/rgb-residual.json',False,(),method='translation',skin_bottom=342)
    fs=[art.clean_white_material(f) for f in fs]
    props={n:Image.open(PAUSE/f'layers/{n}.png').convert('RGBA') for n in ['chair','table','lamp','flower','book']}
    actors=[]
    for f in fs:
        cat=np.asarray(character_scene(f)).copy();a=np.asarray(f);r,g,b,al=a.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
        red=(al>100)&(r>140)&(r>g*1.8)&(r>b*1.7)
        preserve=np.asarray(Image.fromarray((red*255).astype('uint8')).filter(ImageFilter.MaxFilter(9)))>0
        preserve&=(xx<158)&(yy>=240)&(yy<316)
        cat[preserve]=a[preserve]
        lower=(yy>=354)&(xx>=169)&(xx<298)
        cat[lower]=a[lower] # retain every drawn boot edge, including low antialias fringe
        actors.append(art.fixed.compose(Image.fromarray(cat),props,book=True))
    # Reorder COMPLETE drawn poses at the atlas boundary: the first two
    # wind-up poses are lower than the final fetch pose. This removes a
    # backward dip of the held tomato without deforming any character part.
    actor_order=list(range(7))+[8,9,7]+list(range(10,24))
    actors=[actors[i] for i in actor_order]
    for i,f in enumerate(actors):
        bb=f.getbbox();assert min(bb[0],bb[1],448-bb[2],448-bb[3])>=6,('Actor crop margin',i,bb)
    save_frames('actor',actors,[50]*24,['fetch' if i<8 else 'windup' if i<16 else 'throw' for i in actor_order])
    flight=grid_effect('flight',2,3,True);splash=grid_effect('splash',2,4,False)
    # Opaque food-sauce matte under the final settled smear guarantees the
    # lens is fully covered before the scene changes; no character is warped.
    sample=np.asarray(splash[-1]);rgb=np.median(sample[100:350,100:350,:3].reshape(-1,3),axis=0).astype('uint8')
    for i in range(5,8):
        cover=Image.new('RGBA',(448,448),tuple(rgb)+(255,));cover.alpha_composite(splash[i]);splash[i]=cover
    save_frames('flight',flight,[30]*6,['tomato toward center']*6)
    save_frames('splash',splash,[40]*8,['lens impact']*8)
    idlemeta=json.loads((ROOT/'anime/idle/animation.json').read_text(encoding='utf8'));idle=[]
    # Uniform whole-frame viewport fit only. Official idle files remain exact.
    scale=200/164;offset=(64,68)
    for e in idlemeta['frames']:
        im=Image.open(ROOT/'anime/idle'/e['file']).convert('RGBA');im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.LANCZOS)
        out=Image.new('RGBA',(448,448));out.alpha_composite(im,offset);idle.append(out)
    frames=[entry];times=[120];labels=['approved part1 seam'];idle_at=0;idle_clock=0
    def advance_idle(ms):
        nonlocal idle_at,idle_clock
        idle_clock+=ms
        while idle_clock>=idlemeta['frames'][idle_at]['duration_ms']:
            idle_clock-=idlemeta['frames'][idle_at]['duration_ms'];idle_at=(idle_at+1)%len(idle)
    for i in range(19):frames.append(actors[i]);times.append(60 if i<8 else 50);labels.append('fetch' if i<8 else 'windup/forward swing')
    for i,vfx in enumerate(flight):
        out=actors[min(19+i,23)].copy();out.alpha_composite(vfx);frames.append(out);times.append(30);labels.append('release / tomato flight')
    # The approaching tomato already fills most of the view. Start impact
    # from the third drawn splash so it expands rather than shrinking first.
    for i in range(2,len(splash)):
        vfx=splash[i]
        base=actors[-1] if i<5 else idle[idle_at];out=base.copy();out.alpha_composite(vfx);frames.append(out);times.append(40);labels.append('impact / lens coverage')
        if i>=5:advance_idle(40)
    # Hold the settled sauce for 1.05s before the existing 750ms fade.
    # Only the opaque effect is held; the hidden idle clock keeps advancing.
    for i in range(15):
        out=idle[idle_at].copy();out.alpha_composite(splash[-1]);frames.append(out);times.append(70);labels.append('fully occluded idle handoff');advance_idle(70)
    fade=[];fade_values=[]
    for i in range(15):
        alpha=1-(i+1)/15;a=np.asarray(splash[-1]).copy();a[:,:,3]=np.rint(a[:,:,3]*alpha).astype('uint8')
        overlay=Image.fromarray(a);fade.append(overlay);fade_values.append(alpha)
        out=idle[idle_at].copy();out.alpha_composite(overlay);frames.append(out);times.append(50);labels.append('sauce fade / real idle breathing');advance_idle(50)
    save_frames('fade',fade,[50]*15,['overlay opacity fade']*15)
    if idle_clock:
        remaining=idlemeta['frames'][idle_at]['duration_ms']-idle_clock
        frames.append(idle[idle_at].copy());times.append(remaining);labels.append('native idle frame remainder');advance_idle(remaining)
    meta=save_frames('end-tomato',frames,times,labels)
    # Idle sequence starts at the next native pose after the fade, preserving
    # its breath/blink rhythm across the boundary.
    idle_order=list(range(idle_at,len(idle)))+list(range(idle_at))
    ordered_idle=[idle[i] for i in idle_order];it=[idlemeta['frames'][i]['duration_ms'] for i in idle_order]
    imeta=save_frames('idle-preview',ordered_idle,it,['official idle whole-frame viewport']*len(idle));imeta['loop']=True;dump(D/'anime/idle-preview/animation.json',imeta)
    for name,seq in [('all-actor-frames',actors),('all-frames',frames),('all-flight',flight),('all-splash',splash)]:contact(seq,D/f'{name}.png',6,240)
    for name,bg in [('light','#f7f4ee'),('dark','#242832')]:
        seq=[]
        for f in frames:q=Image.new('RGBA',f.size,bg);q.alpha_composite(f);seq.append(q)
        contact(seq,D/f'docs/all-{name}.png',6,220)
    contact([entry]+actors[:3]+actors[6:10]+actors[16:24],D/'docs/actor-seams.png',4,300)
    contact(frames[17:28],D/'docs/release-flight.png',4,300)
    pm=json.loads((PART1/'anime/end-stretch/animation.json').read_text(encoding='utf8'))
    pf=[Image.open(PART1/'anime/end-stretch'/e['file']).convert('RGBA') for e in pm['frames']];pt=[e['duration_ms'] for e in pm['frames']]
    allfs=pf+frames+ordered_idle;pal=Image.new('RGB',(448*8,448*((len(allfs)+7)//8)),'#f7f4ee')
    for i,f in enumerate(allfs):pal.paste(opaque(f),(i%8*448,i//8*448))
    pal=pal.quantize(colors=256)
    for name,seq,ts in [('end-tomato',frames+ordered_idle,times+it),('ending-complete',pf+frames+ordered_idle,pt+times+it)]:
        encode(D/f'{name}.gif',seq,ts,pal,False)
        expected=[opaque(f).quantize(palette=pal,dither=Image.Dither.NONE).convert('RGB') for f in seq]
        expected=[f for i,f in enumerate(expected) if i==0 or f.tobytes()!=expected[i-1].tobytes()]
        with Image.open(D/f'{name}.gif') as gif:
            assert gif.n_frames==len(expected)
            for i,f in enumerate(expected):gif.seek(i);assert gif.convert('RGB').tobytes()==f.tobytes()
    clips={n:[{'src':f'anime/{folder}/'+e['file'],'ms':e['duration_ms'],'label':e['phase']} for e in m['frames']]
           for n,folder,m in [('end','end-tomato',meta),('idle','idle-preview',imeta)]}
    clips['part1']=[{'src':'../pomodoro-end-stretch-v1/anime/end-stretch/'+e['file'],'ms':e['duration_ms'],'label':'approved part1'} for e in pm['frames']]
    template=(D/'docs/review-template.html').read_text(encoding='utf8').replace('__CLIPS__',json.dumps(clips));(D/'review.html').write_text(template,encoding='utf8')
    js=re.search(r'<script>(.*?)</script>',template,re.S).group(1);check=subprocess.run(['node','--check','-'],input=js,text=True,capture_output=True);assert check.returncode==0,check.stderr
    assert before=={str(p.relative_to(ROOT)):digest(p) for p in protect}
    assert frames[0].tobytes()==entry.tobytes()
    assert all(np.asarray(f)[:,:,3].min()==255 for f in splash[5:])
    assert all(a>=b for a,b in zip(fade_values,fade_values[1:])) and fade_values[-1]==0
    # A fully covered lens hides either background identically.
    a=actors[-1].copy();a.alpha_composite(splash[-1]);b=idle[0].copy();b.alpha_composite(splash[-1]);assert a.tobytes()==b.tobytes()
    dump(D/'docs/validation.json',{'scope':'Art only, pending visual approval','actor_drawings':24,'flight_drawings':6,'splash_drawings':8,
      'composite_frames':len(frames),'duration_ms':sum(times),'source_audits':audits,'actor_whole_pose_order':actor_order,'impact_start_asset':2,'approved_assets_unchanged':True,
      'opaque_handoff_verified':True,'sauce_hold_frames':15,'sauce_hold_ms':1050,'fade_alpha':fade_values,'GIF_decode_match':True,'JS_syntax':True,
      'idle_viewport':{'uniform_scale':scale,'offset':offset,'source':'official anime/idle unchanged'},
      'limits':'Diagnostics do not constitute visual approval. Browser playback untested. No runtime integration.'})
    print({'frames':len(frames),'ms':sum(times),'opaque_handoff':True,'approved_unchanged':True})
if __name__=='__main__':main()
