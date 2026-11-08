"""Pause revision: whole-cat drawings, authorized static props, scene depth."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import numpy as np
from PIL import Image,ImageFilter
from animation_geometry import connected_regions
from pomodoro_cat_atlas import export_cat_atlas
from pomodoro_fixed_scene import extract_props,compose
from pomodoro_color_calibration import calibrate,material_stats,masks
from pomodoro_scene_helpers import digest,opaque,encode,measurement
from build_pomodoro_start_v3 import contact

ROOT=Path(__file__).resolve().parent.parent
D=ROOT/'design/pomodoro-pause-v2'
READ=ROOT/'design/pomodoro-reading-v3'
START=ROOT/'design/pomodoro-v3'


def dump(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf8')


def neck_color(frame):
    """RGB-only correction of a mispainted black neck fill; keep ink border."""
    a=np.asarray(frame).copy();r,g,b,al=a.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    dark=(np.maximum.reduce([r,g,b])<45)&(al>180)&(xx>=193)&(xx<268)&(yy>=235)&(yy<269)
    keep=np.zeros(al.shape,np.uint8)
    for reg in connected_regions(dark):
        l,t,rr,bb=reg['bbox']
        if reg['area']>=50 and rr-l>=30 and bb-t>=3:
            for y,x0,x1 in reg['runs']:keep[y,x0:x1]=255
    component=keep>0;core=np.zeros(component.shape,bool)
    for x in range(193,268):
        ys=np.where(component[:,x])[0]
        if len(ys)>=2:core[ys[1:],x]=True
    # Painted fill only: never change alpha or extend/rebuild the neck outline.
    a[core,:3]=[237,203,183]
    assert np.array_equal(a[:,:,3],np.asarray(frame)[:,:,3])
    return Image.fromarray(a),int(core.sum())


def tail_extent(im):
    r,g,b,al=np.asarray(im).astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    m=(al>200)&(r>150)&(r-g>15)&(r-g<80)&(g-b>20)&(xx>=300)&(yy>=255)&(yy<302)
    xs=np.where(m)[1]
    return float(np.percentile(xs,99)) if len(xs) else 300.


def palette_reference(frame):
    """Target material colors sampled from the user's latest model reference."""
    model=np.asarray(Image.open(D/'sources/model-reference.webp').convert('RGBA')).astype(int)
    r,g,b,al=model.transpose(2,0,1);yy,xx=np.indices(al.shape)
    hair=(yy>100)&(yy<350)&(xx>80)&(xx<600)&(r>190)&(g>145)&(r-g>15)&(r-g<75)&(g-b>25)&(g-b<80)
    skin=(yy>350)&(yy<620)&(xx>150)&(xx<420)&(r>230)&(r-g>15)&(r-g<50)&(g-b>8)&(g-b<27)
    target={'hair':np.median(model[hair,:3],axis=0),'skin':np.median(model[skin,:3],axis=0),'white':np.array([254.,254.,254.])}
    a=np.asarray(frame).astype(float);weights,samples=masks(frame,skin_bottom=342)
    for name,rgb in target.items():
        sample=samples[name]
        if sample.sum()<12:continue
        shift=rgb-np.median(a[sample,:3],axis=0)
        a[:,:,:3]+=weights[name][:,:,None]*shift
    out=Image.fromarray(np.rint(np.clip(a,0,255)).astype('uint8'))
    out.save(D/'docs/palette-reference.png')
    dump(D/'docs/palette.json',{'basis':'Latest user model-reference.webp','targets':{k:v.tolist() for k,v in target.items()},'boots_base_rgb':[95,80,76],
                              'method':'Constant RGB material translation; no quantile contrast amplification.'})
    return out


def clean_fabric_and_boots(frame):
    a=np.asarray(frame).copy();r,g,b,al=a.astype(float).transpose(2,0,1);yy,xx=np.indices(al.shape)
    fabric=(al>160)&(xx>=145)&(xx<320)&(yy>=248)&(yy<332)&(np.minimum.reduce([r,g,b])>100)
    fabric&=(np.maximum.reduce([r,g,b])-np.minimum.reduce([r,g,b])<45)&(g-b<12)&(r-g<40)
    skin_seed=(al>160)&(r>225)&(r-g>16)&(g-b>8)&(g-b<35)
    skin_guard=np.asarray(Image.fromarray((skin_seed*255).astype('uint8')).filter(ImageFilter.MaxFilter(15)))>0
    fabric&=~skin_guard
    lum=np.maximum(175,np.rint(r*.2126+g*.7152+b*.0722))
    a[fabric,:3]=np.repeat(lum[fabric,None],3,axis=1).astype('uint8')
    # Whole boots are a material-color support; no geometry or motion changes.
    boots=(al>160)&(xx>=178)&(xx<286)&(yy>=374)&(yy<429)&(r>70)&(r<170)&(g<145)&(b<145)&(r-g>2)&(g-b>=0)
    sample=boots&(xx>=198)&(xx<267)&(yy>=390)&(yy<418)&(r>90)
    if sample.sum()>=12:
        shift=np.array([95.,80.,76.])-np.median(a[sample,:3],axis=0)
        w=boots*np.clip((r-70)/20,0,1)
        a[:,:,:3]=np.rint(np.clip(a[:,:,:3].astype(float)+w[:,:,None]*shift,0,255)).astype('uint8')
    assert np.array_equal(a[:,:,3],np.asarray(frame)[:,:,3])
    return Image.fromarray(a)


def save_clip(name,frames,times,loop,poses):
    dest=D/'anime'/name;dest.mkdir(parents=True,exist_ok=True)
    unique=[];lookup={};entries=[]
    for f,ms,pose in zip(frames,times,poses):
        key=hashlib.sha256(f.tobytes()).hexdigest()
        if key not in lookup:lookup[key]=len(unique);unique.append(f)
        entries.append({'file':f'{lookup[key]:03}.png','duration_ms':ms,'source':pose})
    for stale in dest.glob('[0-9][0-9][0-9].png'):
        if int(stale.stem)>=len(unique):stale.unlink()
    for i,f in enumerate(unique):f.save(dest/f'{i:03}.png')
    meta={'label':name,'canvas':[448,448],'loop':loop,'frame_count':len(frames),'duration_ms':sum(times),
          'unique_frame_count':len(unique),'frames':entries}
    dump(dest/'animation.json',meta);return meta


def main():
    protected=sorted((START/'anime/start').glob('*'))+sorted((READ/'anime/reading').glob('*'))
    protected += [START/'sources/start.png',START/'start.gif',READ/'reading.gif']
    before={str(p.relative_to(ROOT)):digest(p) for p in protected}
    props,prop_audit=extract_props();batches={};audits={}
    dump(D/'docs/static-scene.json',prop_audit)
    plate=Image.new('RGBA',(448,448))
    for im in props.values():plate.alpha_composite(im)
    plate.save(D/'docs/props-only.png')
    names=['keys','close','close-repair','lower','lift-early','lift','loop-a','loop-b','loop-c','loop-d','loop-e','blink']
    for name in names:batches[name],audits[name]=export_cat_atlas(D/f'sources/{name}.png',1 if name=='blink' else 2,3 if name=='blink' else 4)
    all_ids=[(n,i) for n in names for i in range(len(batches[n]))]
    raw=[batches[n][i] for n,i in all_ids];neck=[];fixed=[]
    for f in raw:
        out,count=neck_color(f);fixed.append(out);neck.append(count)
    # The NEW clean cel drawing is the material reference. Do not reintroduce
    # the old scene's warm shirt texture/quantile grain into clean white fills.
    ref=palette_reference(neck_color(batches['loop-a'][0])[0])
    calibrated=calibrate(fixed,ref,D/'docs/rgb-calibration.json',reference_first=False,blink_indices=(),method='translation',skin_bottom=342)
    art=dict(zip(all_ids,[clean_fabric_and_boots(f) for f in calibrated]))
    # Reorder COMPLETE poses by actual tail outline for a continuous excursion.
    # Do not trust the model's atlas numbering at quarter-sheet boundaries.
    opens=[(n,i) for n in ('loop-a','loop-b','loop-c','loop-d','loop-e') for i in range(8)
           if (n!='loop-b' or i not in (1,2,3)) and (n!='loop-a' or i<4)]
    ranked=sorted(opens,key=lambda k:tail_extent(art[k]))
    route=ranked[::2]+ranked[1::2][::-1]
    # Share an actually drawn middle pose on both directions when needed.
    # It is never held as a frozen substitute for breathing or tail movement.
    smooth=[]
    for a,b in zip(route,route[1:]+route[:1]):
        smooth.append(a);current=tail_extent(art[a]);goal=tail_extent(art[b]);direction=1 if goal>current else -1
        while abs(goal-current)>6:
            choices=[k for k in opens if 0<direction*(tail_extent(art[k])-current)<=6 and direction*(goal-tail_extent(art[k]))>0]
            if not choices:break
            mid=max(choices,key=lambda k:direction*tail_extent(art[k]));smooth.append(mid);current=tail_extent(art[mid])
    route=smooth
    start=route.index(('loop-b',0));route=route[start:]+route[:start]
    loop_ids=route[:1]+[('blink',i) for i in range(3)]+route[1:]
    assert len(set(loop_ids))==36
    loop_cat=[art[k] for k in loop_ids]
    loop_times=[60 if n=='blink' and i!=1 else 80 if n=='blink' else 100 for n,i in loop_ids]
    # The rejected oversized opening pose is not used. The selected whole
    # book gestures close, roll forward, contact and release in that order.
    gesture=[('close-repair',0),('close',1),('close-repair',1),('close',2),('close-repair',2),
             ('close',3),('close',4),('close-repair',3),('close-repair',4),('close',5),('close',6),
             ('close-repair',5),('close-repair',6),('close-repair',7),('keys',5)]
    # Keep head/gaze progression continuous; discard the wrong-side arm sheet.
    transition_ids=gesture+[('lift-early',i) for i in range(4)]+[('lift',i) for i in range(1,6)]+[loop_ids[0]]
    assert len(transition_ids)==25
    trans_cat=[art[k] for k in transition_ids]
    trans_times=[90]*len(gesture)+[80]*4+[110]*5+[150]
    transition=[compose(f,props,book=i>=13,moving=i<13) for i,f in enumerate(trans_cat)]
    rest=[compose(f,props,book=True) for f in loop_cat]
    transition[-1]=rest[0].copy()
    pause_meta=save_clip('pause',transition,trans_times,False,transition_ids)
    loop_meta=save_clip('paused',rest,loop_times,True,loop_ids)
    contact(transition,D/'all-pause-frames.png',6,260)
    contact(rest,D/'all-paused-frames.png',8,240)
    contact([f.crop((145,236,311,343)) for f in transition],D/'docs/contact-hands-book.png',6,260)
    contact([f.crop((130,57,334,274)) for f in transition[13:]],D/'docs/head-raise.png',6,260)
    blink_i=[i for i,k in enumerate(loop_ids) if k[0]=='blink']
    contact([f.crop((134,155,330,268)) for f in rest[:5]],D/'docs/blink.png',5,300)
    contact([f.crop((287,230,383,327)) for f in rest],D/'docs/tail.png',8,168)
    # Composite every frame on light AND dark backgrounds before delivery.
    all_scene=transition+rest
    for label,bg in [('light','#f7f4ee'),('dark','#242832')]:
        flat=[]
        for f in all_scene:
            q=Image.new('RGBA',f.size,bg);q.alpha_composite(f);flat.append(q)
        contact(flat,D/f'docs/all-{label}.png',8,180)
    contact([Image.open(START/'anime/start/000.png').convert('RGBA')]+[rest[i] for i in (0,8,16,24,31)],D/'docs/props-comparison.png',3,300,
            ['approved original book']+[f'paused {i}' for i in (0,8,16,24,31)])
    palette_src=Image.new('RGB',(448*8,448*((len(all_scene)+7)//8)),'#f7f4ee')
    for i,f in enumerate(all_scene):palette_src.paste(opaque(f),(i%8*448,i//8*448))
    palette=palette_src.quantize(colors=256)
    preview=transition+rest*2;preview_times=trans_times+loop_times*2
    gif_counts={}
    for name,frames,times,loop in [('pause',transition,trans_times,False),('paused',rest,loop_times,True),('pause-preview',preview,preview_times,False)]:
        gif_counts[name]=encode(D/f'{name}.gif',frames,times,palette,loop)
        expected=[opaque(f).quantize(palette=palette,dither=Image.Dither.NONE).convert('RGB') for f in frames]
        expected=[f for i,f in enumerate(expected) if i==0 or f.tobytes()!=expected[i-1].tobytes()]
        with Image.open(D/f'{name}.gif') as gif:
            assert gif.n_frames==len(expected)
            for i,f in enumerate(expected):gif.seek(i);assert gif.convert('RGB').tobytes()==f.tobytes(),(name,i)
    read_meta=json.loads((READ/'anime/reading/animation.json').read_text(encoding='utf8'))
    rf=[Image.open(READ/'anime/reading'/e['file']).convert('RGBA') for e in read_meta['frames']]
    encode(D/'reading-pause.gif',rf+preview,[e['duration_ms'] for e in read_meta['frames']]+preview_times,palette,False)
    assert before=={str(p.relative_to(ROOT)):digest(p) for p in protected}
    assert transition[-1].tobytes()==rest[0].tobytes()
    # Static table front/legs and book core must be identical in all rest poses.
    table_roi=(130,345,315,353);book_roi=(210,307,246,320)
    for roi in (table_roi,book_roi):assert len({f.crop(roi).tobytes() for f in rest})==1,('Static prop changed',roi)
    assert len({f.tobytes() for f in rest})==36
    assert len({f.crop((202,265,261,300)).tobytes() for f in loop_cat})>8
    tail=[tail_extent(f) for f in loop_cat]
    report={'scope':'ART ONLY; no program integration','sources':audits,'static_props':prop_audit,
            'whole_cat_per_pose':True,'no_character_part_transforms':True,'prop_occlusion_stencils_only':True,
            'neck_fill_rgb_only':dict((f'{n}/{i}',c) for (n,i),c in zip(all_ids,neck) if c),
            'pause_frames':25,'pause_ms':sum(trans_times),'loop_frames':len(loop_ids),'loop_unique_poses':36,'loop_ms':sum(loop_times),
            'blink_positions':blink_i,'blink_ms':sum(loop_times[i] for i in blink_i),
            'loop_tail_extent':tail,'tail_excursion_px':max(tail)-min(tail),
            'max_tail_step_px':max(abs(a-b) for a,b in zip(tail,tail[1:]+tail[:1])),
            'static_prop_pixels_unchanged_in_unoccluded_rois':True,'decoded_gif_pixel_comparison':'pass','gif_counts':gif_counts,
            'approved_start_reading_unchanged':True,'protected_hashes':before,
            'measurements':[measurement(f) for f in trans_cat+loop_cat],
            'remaining':'Pending visual approval; hand-drawn pose differences remain. Browser file playback not tested.'}
    dump(D/'docs/validation.json',report)
    payload={'pause':[{'src':'anime/pause/'+e['file'],'ms':e['duration_ms'],'label':f'合书、抬头托腮 {i+1}/25'} for i,e in enumerate(pause_meta['frames'])],
             'paused':[{'src':'anime/paused/'+e['file'],'ms':e['duration_ms'],'label':f'眨眼、摆尾、呼吸 {i+1}/{len(loop_ids)}'} for i,e in enumerate(loop_meta['frames'])],
             'reading':[{'src':'../pomodoro-reading-v3/anime/reading/'+e['file'],'ms':e['duration_ms'],'label':'已确认阅读'} for e in read_meta['frames']]}
    old=(D/'docs/review-template.html').read_text(encoding='utf8')
    old=re.sub(r'const clips=.*?;const el=',lambda _: 'const clips='+json.dumps(payload,ensure_ascii=False)+';const el=',old,count=1)
    old=old.replace('暂停32帧／2.88秒；随后接8张实际绘制的托腮呼吸帧，1.76秒一轮。',f'暂停25帧／2.37秒；托腮循环36张独立整人姿势、{len(loop_ids)}播放位置，含一次200ms眨眼和自然摆尾，{sum(loop_times)/1000:.2f}秒一轮。')
    old=old.replace('书本仍有约3–6像素位置/厚度手绘差异，尚待视觉确认，未接入程序。','固定桌椅、闭灯、雏菊和落桌书本；整人逐帧绘制并组合检查。尚待视觉确认，未接入程序。')
    old=old.replace('docs/book-position.png','docs/props-comparison.png').replace('docs/hands-book.png','docs/contact-hands-book.png').replace('docs/background-check.png','docs/all-dark.png')
    (D/'review.html').write_text(old,encoding='utf8')
    script=re.search(r'<script>(.*?)</script>',old,re.S).group(1)
    subprocess.run(['node','--check','-'],input=script,text=True,check=True,capture_output=True)
    print(json.dumps({k:report[k] for k in ('pause_frames','pause_ms','loop_frames','loop_ms','blink_ms','tail_excursion_px','max_tail_step_px')},ensure_ascii=False))


if __name__=='__main__':main()
