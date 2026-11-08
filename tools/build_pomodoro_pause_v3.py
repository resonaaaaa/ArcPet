"""Pause art matched to APPROVED reading, complete scene drawings only.

Static props may be replaced under the user's explicit permission. Character
registration uses one uniform scale per atlas and a rigid full-scene offset;
color changes affect RGB only. Never rescale or assemble character parts.
"""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import numpy as np
from PIL import Image, ImageFilter
import pomodoro_fixed_scene as fixed
from build_pomodoro_start_v3 import contact
from pomodoro_scene_helpers import digest, desk_anchor, measurement, opaque, encode
from pomodoro_color_calibration import calibrate, material_stats
from animation_geometry import connected_regions

ROOT=Path(__file__).resolve().parent.parent
D=ROOT/'design/pomodoro-pause-v3'
READ=ROOT/'design/pomodoro-reading-v3'
BASE_BOOK_DEPTH=fixed.moving_book_depth

def dump(p,obj):p.write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf8')

def full_book_depth(scene):
    """Reveal the DRAWN book's ink fringe, instead of cutting it at green fill."""
    original=np.asarray(scene).copy()
    core=np.asarray(BASE_BOOK_DEPTH(scene))[:,:,3]>0
    support=np.asarray(Image.fromarray((core*255).astype('uint8')).filter(ImageFilter.MaxFilter(7)))>0
    r,g,b,al=original.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    wood=(r>90)&(r<210)&(r-g>23)&(g-b>12)&(yy>=316)
    support&=~wood
    original[~support]=0
    return Image.fromarray(original)

def clean_white_material(frame):
    """Authorized RGB-only clean cel palette; leave ink and alpha intact."""
    a=np.asarray(frame).copy();r,g,b,al=a.astype(float).transpose(2,0,1);yy,xx=np.indices(al.shape)
    flesh_area=(yy<264)|((xx<205)&(yy<331))|((xx>255)&(yy<331))
    skin=(al>160)&(r>225)&(r-g>18)&(g-b>8)&(g-b<30)&flesh_area
    guard=np.asarray(Image.fromarray((skin*255).astype('uint8')).filter(ImageFilter.MaxFilter(5)))>0
    fabric=(xx>=148)&(xx<320)&(yy>=253)&(yy<327)&(al>100)&~guard
    fabric&=(np.minimum.reduce([r,g,b])>150)&(np.maximum.reduce([r,g,b])-np.minimum.reduce([r,g,b])<55)&(g-b<22)&(r-g<35)
    ink=(al>100)&(np.minimum.reduce([r,g,b])<140)
    ink_guard=np.asarray(Image.fromarray((ink*255).astype('uint8')).filter(ImageFilter.MaxFilter(3)))>0
    # One clean base fill avoids converting small stochastic luminance noise
    # into disconnected cel-shadow islands. Preserve original fold/edge ink
    # and its antialias fringe, which carry the drawn sleeve movement.
    a[fabric&~ink_guard,:3]=[245,242,240]
    assert np.array_equal(a[:,:,3],np.asarray(frame)[:,:,3])
    return Image.fromarray(a)

def export_scene_atlas(name,reference):
    """Keep every major region in its grid cell, including occluded legs."""
    path=D/f'sources/{name}.png';sheet=Image.open(path).convert('RGBA')
    assert abs(sheet.width/sheet.height-2)<.025,(name,sheet.size)
    cells=[];anchors=[];crop_audit=[]
    for i in range(8):
        box=(round(i%4*sheet.width/4),round(i//4*sheet.height/2),
             round((i%4+1)*sheet.width/4),round((i//4+1)*sheet.height/2))
        cell=sheet.crop(box);a=np.asarray(cell).copy();keep=np.zeros(a.shape[:2],np.uint8)
        regions=[r for r in connected_regions(a[:,:,3]>=128) if r['area']>=60]
        assert regions,(name,i,'empty cell')
        bounds=[]
        for region in regions:
            bounds.append(region['bbox'])
            for y,x0,x1 in region['runs']:keep[y,x0:x1]=255
        keep=np.asarray(Image.fromarray(keep).filter(ImageFilter.MaxFilter(3)))>0
        a[~keep]=0;cell=Image.fromarray(a)
        bb=cell.getbbox();assert min(bb[0],bb[1],cell.width-bb[2],cell.height-bb[3])>=2,(name,i,'crop risk',bb)
        cells.append(cell);anchors.append(desk_anchor(cell));crop_audit.append({'cell':box,'regions':bounds})
    target=desk_anchor(reference);factor=target['width']/np.median([v['width'] for v in anchors])
    frames=[];offsets=[]
    for cell,anchor in zip(cells,anchors):
        scaled=cell.resize((round(cell.width*factor),round(cell.height*factor)),Image.Resampling.LANCZOS)
        xy=(round(target['center']-anchor['center']*factor),round(target['row']-anchor['row']*factor))
        f=Image.new('RGBA',(448,448));f.alpha_composite(scaled,xy);frames.append(f);offsets.append(xy)
    return frames,{'source':str(path.relative_to(ROOT)),'sha256':digest(path),'uniform_scale':float(factor),
                   'whole_scene_offsets':offsets,'cell_audits':crop_audit}

def props_from_reading():
    fixed.SOURCE=READ/'anime/reading/000.png';fixed.DEST=D/'layers'
    props,audit=fixed.extract_props()
    # Book is copied from its user-confirmed ORIGINAL resting place, not the
    # open reading book. No generated/resized prop is used as the fixed book.
    props['book']=Image.open(D/'references/approved-resting-book.png').convert('RGBA')
    props['book'].save(D/'layers/book.png')
    # Keep the yellow daisy centers; exclude only the tan tail behind them.
    a=np.asarray(Image.open(fixed.SOURCE).convert('RGBA')).copy()
    r,g,b,al=a.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    mask=(xx>=310)&(xx<370)&(yy>=263)&(yy<327)
    tail=(r>150)&(r-g>18)&(g-b>15)&(g-b<70)&(((xx<323)&(yy<280))|((xx<314)&(yy<295)))
    a[~mask|tail]=0;a[:,:,3]=np.where(a[:,:,3]>=180,255,a[:,:,3])
    props['flower']=Image.fromarray(a);props['flower'].save(D/'layers/flower.png')
    # Static chair: include original dark ink/wood streaks beside the tan core.
    # The earlier color-only cutout made thin transparent holes in the legs.
    original=Image.open(fixed.SOURCE).convert('RGBA');a=np.asarray(original).copy()
    r,g,b,al=a.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    core=(al>128)&(r>130)&(r-g>23)&(g-b>10)&(yy>=354)&(yy<429)&(xx>=169)&(xx<298)
    core&=~((yy>=375)&(xx>=193)&(xx<=274))
    expanded=np.asarray(Image.fromarray((core*255).astype('uint8')).filter(ImageFilter.MaxFilter(5)))>0
    expanded&=(yy>=354)&(yy<429)&(xx>=169)&(xx<298)
    expanded&=~((yy>=375)&(xx>=193)&(xx<=274))
    a[~expanded]=0;a[:,:,3]=np.where(a[:,:,3]>=128,255,a[:,:,3])
    props['chair']=Image.fromarray(a);props['chair'].save(D/'layers/chair.png')
    audit['book_source']='references/approved-resting-book.png (approved start original resting book)'
    audit['layer_sha256']={k:digest(D/f'layers/{k}.png') for k in props}
    dump(D/'docs/static-scene.json',audit)
    return props

def remove_drawn_props(scene,moving):
    """Erase generated PROP regions; retain the complete cat in one image.

    Visibility changes are used only to replace authorized static furniture.
    No character component is independently moved, resized, or pasted.
    """
    a=np.asarray(scene).copy();r,g,b,al=a.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    keep=(yy<316)&(xx>=129)&(xx<380)
    keep&=~((xx<158)&(yy>=240))
    keep&=~((xx>=321)&(yy>=264))
    skin=(r>175)&(r-g>14)&(r-g<90)&(g-b>-5)&(g-b<35)&(al>100)
    hands=skin&(yy>=275)&(yy<342)&(xx>=145)&(xx<316)
    hands=np.asarray(Image.fromarray((hands*255).astype('uint8')).filter(ImageFilter.MaxFilter(3)))>0
    keep|=hands
    if moving:keep|=np.asarray(fixed.moving_book_depth(scene))[:,:,3]>0
    # Selected final source atlases have the CHAIR removed by the drawing
    # service. Keep ALL original skirt, leg and boot pixels as one scene;
    # color-segmenting dark wooden ink from boots proved unreliable and was
    # rejected. The approved fixed chair is placed behind this whole cat.
    keep|=(yy>=354)&(yy<430)&(xx>=169)&(xx<298)
    a[~keep]=0
    return Image.fromarray(a)

def save_clip(name,frames,times,ids,loop):
    out=D/'anime'/name;out.mkdir(parents=True,exist_ok=True)
    unique=[];lookup={};entries=[]
    for f,t,k in zip(frames,times,ids):
        key=hashlib.sha256(f.tobytes()).hexdigest()
        if key not in lookup:lookup[key]=len(unique);unique.append(f)
        entries.append({'file':f'{lookup[key]:03}.png','duration_ms':t,'source':k})
    for i,f in enumerate(unique):f.save(out/f'{i:03}.png')
    for p in out.glob('[0-9][0-9][0-9].png'):
        if int(p.stem)>=len(unique):p.unlink()
    meta={'label':name,'canvas':[448,448],'loop':loop,'unique_frame_count':len(unique),
          'frame_count':len(frames),'duration_ms':sum(times),'frames':[
              e for e in entries]}
    dump(out/'animation.json',meta);return meta

def main():
    fixed.moving_book_depth=full_book_depth
    protected=sorted((ROOT/'design/pomodoro-v3/anime/start').glob('*'))+sorted((READ/'anime/reading').glob('*'))
    protected+=sorted((ROOT/'anime').rglob('*.png'))
    before={str(p.relative_to(ROOT)):digest(p) for p in protected}
    ref=Image.open(READ/'anime/reading/000.png').convert('RGBA')
    batches={};audits={}
    config=json.loads((D/'docs/selection.json').read_text(encoding='utf8'))
    for name in sorted({k[0] for k in config['pause']+config['paused']}):
        batches[name],audits[name]=export_scene_atlas(name,ref)
    keys=[(n,i) for n in batches for i in range(8)]
    raw=[batches[n][i] for n,i in keys]
    # Reuse ONLY approved reading color reference. Never the lighter external
    # model illustration nor rejected pause-v2 as palette/geometry authority.
    colored=calibrate(raw,ref,D/'docs/rgb-calibration.json',reference_first=False,
                      blink_indices=(),method='translation',skin_bottom=342)
    # Recheck changed material membership: warm white shadows sometimes enter
    # the sample only after the first RGB shift. A second bounded RGB pass
    # removes that residual clothing flicker; alpha/poses remain unchanged.
    colored=calibrate(colored,ref,D/'docs/rgb-residual.json',reference_first=False,
                      blink_indices=(),method='translation',skin_bottom=342)
    colored=[clean_white_material(f) for f in colored]
    art=dict(zip(keys,colored));props=props_from_reading()
    # Selected sequential, entire poses; exclusions are set after visual review.
    pause_ids=[tuple(k) for k in config['pause']]
    rest_ids=[tuple(k) for k in config['paused']]
    pause=[fixed.compose(remove_drawn_props(art[k],i<config['fixed_book_from']),props,
                         book=i>=config['fixed_book_from'],moving=i<config['fixed_book_from'])
           for i,k in enumerate(pause_ids)]
    rest=[fixed.compose(remove_drawn_props(art[k],False),props,book=True) for k in rest_ids]
    pt=config['pause_times'];rt=config['paused_times']
    # Whole boundary frame, accompanied by multi-frame comparisons below.
    pause[-1]=rest[0].copy()
    pm=save_clip('pause',pause,pt,pause_ids,False);rm=save_clip('paused',rest,rt,rest_ids,True)
    allframes=pause+rest
    # Furniture/book interiors are fixed, and safety margins cover the whole
    # final scene, not only the bounding box of an isolated head or boot.
    for roi in [(130,345,315,353),(210,307,246,320)]:
        assert len({f.crop(roi).tobytes() for f in rest})==1,('Static prop drift',roi)
    for i,f in enumerate(allframes):
        bb=f.getbbox();assert min(bb[0],bb[1],448-bb[2],448-bb[3])>=6,('Final crop',i,bb)
    contact(pause,D/'all-pause-frames.png',6,280)
    contact(rest,D/'all-paused-frames.png',8,260)
    for label,bg in [('light','#f7f4ee'),('dark','#242832')]:
        flat=[]
        for f in allframes:
            q=Image.new('RGBA',f.size,bg);q.alpha_composite(f);flat.append(q)
        contact(flat,D/f'docs/all-{label}.png',6,260)
    contact([ref]+pause[:5]+[pause[i] for i in range(max(0,len(pause)-5),len(pause))]+rest[:5],
            D/'docs/seams.png',4,336)
    contact([ref]+[rest[i] for i in range(0,len(rest),max(1,len(rest)//7))],D/'docs/reading-pause-comparison.png',4,448)
    contact([f.crop((140,238,318,344)) for f in pause],D/'docs/hands-book.png',6,260)
    contact([f.crop((128,70,337,270)) for f in allframes],D/'docs/heads.png',6,240)
    pal=Image.new('RGB',(448*8,448*((len(allframes)+8)//8)),'#f7f4ee')
    for i,f in enumerate([ref]+allframes):pal.paste(opaque(f),(i%8*448,i//8*448))
    pal=pal.quantize(colors=256)
    readmeta=json.loads((READ/'anime/reading/animation.json').read_text(encoding='utf8'))
    readframes=[Image.open(READ/'anime/reading'/e['file']).convert('RGBA') for e in readmeta['frames']]
    clips=[('pause',pause,pt,False),('paused',rest,rt,True),('pause-preview',pause+rest*2,pt+rt*2,False),
           ('reading-pause',readframes+pause+rest*2,[e['duration_ms'] for e in readmeta['frames']]+pt+rt*2,False)]
    for name,frames,times,loop in clips:
        encode(D/f'{name}.gif',frames,times,pal,loop)
        expected=[opaque(f).quantize(palette=pal,dither=Image.Dither.NONE).convert('RGB') for f in frames]
        expected=[f for i,f in enumerate(expected) if i==0 or f.tobytes()!=expected[i-1].tobytes()]
        with Image.open(D/f'{name}.gif') as gif:
            assert gif.n_frames==len(expected)
            for i,f in enumerate(expected):gif.seek(i);assert gif.convert('RGB').tobytes()==f.tobytes()
    clipmeta={name:[{'src':f'anime/{name}/'+e['file'],'ms':e['duration_ms'],'label':f'{name} {i:03}'} for i,e in enumerate(meta['frames'])]
              for name,meta in [('pause',pm),('paused',rm)]}
    clipmeta['reading']=[{'src':'../pomodoro-reading-v3/anime/reading/'+e['file'],'ms':e['duration_ms'],'label':f'reading {i:03}'} for i,e in enumerate(readmeta['frames'])]
    template=(D/'docs/review-template.html').read_text(encoding='utf8').replace('__CLIPS__',json.dumps(clipmeta))
    (D/'review.html').write_text(template,encoding='utf8')
    script=re.search(r'<script>(.*?)</script>',template,re.S).group(1)
    check=subprocess.run(['node','--check','-'],input=script,text=True,capture_output=True);assert check.returncode==0,check.stderr
    assert before=={str(p.relative_to(ROOT)):digest(p) for p in protected}
    dump(D/'docs/validation.json',{'scope':'Art only, pending user visual review','protected_unchanged':True,
          'reference':{'path':str(READ/'anime/reading/000.png'),'materials':material_stats(ref),'geometry':measurement(ref)},
          'sources':audits,'pause_ms':sum(pt),'loop_ms':sum(rt),'pause_count':len(pause),'loop_count':len(rest),
          'frame_diagnostics':[{'source':k,'geometry':measurement(f),'materials':material_stats(f)} for k,f in zip(pause_ids+rest_ids,allframes)],
          'GIF_decoded_pixels_match':True,'JS_syntax':True,
          'limits':'Numerical diagnostics do not replace visual acceptance. Browser playback not verified.'})
    print({'pause':len(pause),'loop':len(rest),'pause_ms':sum(pt),'loop_ms':sum(rt)})

if __name__=='__main__':main()
