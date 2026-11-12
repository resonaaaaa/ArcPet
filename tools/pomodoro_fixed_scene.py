"""User-authorized extraction of STATIC props only. Never cuts character parts."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from pomodoro_scene_helpers import digest

ROOT=Path(__file__).resolve().parent.parent
SOURCE=ROOT/'design/pomodoro-v3/anime/start/000.png'
DEST=ROOT/'design/pomodoro-pause-v3/layers'


def layer(source, mask):
    a=np.asarray(source).copy(); a[np.asarray(mask)==0]=0
    # Opaque painted interiors stop the layer behind showing through the
    # source's almost-opaque AI alpha. Keep the antialiased silhouette edge.
    a[:,:,3]=np.where(a[:,:,3]>=180,255,a[:,:,3])
    return Image.fromarray(a)


def extract_props():
    DEST.mkdir(parents=True,exist_ok=True)
    source=Image.open(SOURCE).convert('RGBA');a=np.asarray(source).copy()
    # Only the thin horizontal DESKTOP strip was occluded by the original
    # book/skirt. Continue the same row's unobstructed wood samples across it.
    # This repairs a static prop surface, never the cat or a moving body part.
    for y in range(316,327):
        left=np.asarray(source)[y,140].astype(float)
        right=np.asarray(source)[y,305].astype(float)
        for x in range(156,313):
            t=(x-140)/(305-140);a[y,x]=np.rint(left*(1-t)+right*t).astype('uint8')
    repaired=Image.fromarray(a)
    mask=Image.new('L',(448,448));draw=ImageDraw.Draw(mask)
    draw.rectangle((76,316,371,354),fill=255)
    draw.rectangle((76,355,128,447),fill=255)
    draw.rectangle((324,355,371,447),fill=255)
    table=layer(repaired,mask)
    # Lamp is fully isolated in this rectangle of the approved reference.
    mask=Image.new('L',(448,448));ImageDraw.Draw(mask).rectangle((92,243,157,325),fill=255)
    lamp=layer(source,mask)
    # Trace the flower/pot exterior; exclude the gold tail behind its left edge.
    mask=Image.new('L',(448,448));ImageDraw.Draw(mask).rectangle((310,263,369,326),fill=255)
    fm=np.asarray(mask).copy()
    rr,gg,bb,aa=np.asarray(source).astype(int).transpose(2,0,1)
    fy,fx=np.indices(aa.shape)
    tail_edge=(rr>150)&(rr-gg>18)&(gg-bb>15)&(((fx<323)&(fy<280))|((fx<314)&(fy<295)))
    fm[tail_edge]=0
    flower=layer(source,Image.fromarray(fm))
    # Chair wood only, as one stationary background prop. Central boots and
    # skirt are explicitly excluded; no character pixels become prop layers.
    r,g,b,al=np.asarray(source).astype(int).transpose(2,0,1)
    yy,xx=np.indices(al.shape)
    chair_mask=(al>0)&(r>135)&(g>85)&(r-g>23)&(g-b>10)&(yy>=354)&(yy<429)&(xx>=165)&(xx<300)
    chair_mask&=~((yy>=375)&(xx>=191)&(xx<=275))
    chair=layer(source,Image.fromarray((chair_mask*255).astype('uint8')))
    mask=Image.new('L',(448,448));ImageDraw.Draw(mask).polygon(
        [(193,301),(266,301),(268,323),(267,325),(191,325),(192,310)],fill=255)
    book=layer(source,mask)
    props={'chair':chair,'table':table,'lamp':lamp,'flower':flower,'book':book}
    for name,im in props.items():im.save(DEST/f'{name}.png')
    return props,{'source':str(SOURCE.relative_to(ROOT)),'source_sha256':digest(SOURCE),
                  'permission':'User approved static-prop extraction and reuse; full-cat drawings remain intact.',
                  'desktop_repair_rect':[156,316,313,327],
                  'layer_sha256':{n:digest(DEST/f'{n}.png') for n in props}}


def moving_book_depth(cat):
    """A book PROP visibility mask, never repositions/scales character parts."""
    a=np.asarray(cat).astype(int);r,g,b,al=a.transpose(2,0,1)
    yy,xx=np.indices(al.shape)
    roi=(xx>=150)&(xx<312)&(yy>=245)&(yy<347)&(al>128)
    green=roi&(g>r+2)&(g>b+5)&(r<170)
    if green.sum()<150:return Image.new('RGBA',cat.size)
    gy,gx=np.where(green);left,right=int(gx.min()),int(gx.max())
    pages=roi&(xx>=left)&(xx<=right)&(r>180)&(r-g>5)&(r-g<25)&(g-b>6)
    candidates=green|pages
    top=int(gy.min());gaps=0
    for y in range(top-1,max(244,top-29),-1):
        if candidates[y].sum()>=4:top=y;gaps=0
        else:
            gaps+=1
            if gaps>=3:break
    visible=np.zeros(al.shape,bool)
    bounds=[]
    for y in range(top,int(gy.max())+1):
        xs=np.where(candidates[y])[0]
        if len(xs)>=2:bounds.append((y,max(0,int(xs.min())-1),int(xs.max())+2))
    if not bounds:return Image.new('RGBA',cat.size)
    by=np.array([v[0] for v in bounds]);bl=np.array([v[1] for v in bounds]);br=np.array([v[2] for v in bounds])
    for y in range(top,int(gy.max())+1):
        l=round(float(np.interp(y,by,bl)));rr=round(float(np.interp(y,by,br)))
        visible[y,l:rr]=True
    out=np.asarray(cat).copy();out[~visible]=0
    return Image.fromarray(out)


def compose(cat,props,book=False,moving=False):
    out=Image.new('RGBA',(448,448));out.alpha_composite(props['chair'])
    out.alpha_composite(cat)
    # A depth stencil changes the PROP's occlusion, not the whole-cat artwork.
    # No limb is separately positioned, scaled, rotated or pasted into a pose.
    a=np.asarray(cat).astype(int);r,g,b,al=a.transpose(2,0,1);yy,xx=np.indices(al.shape)
    roi=(yy>=275)&(yy<342)&(xx>=145)&(xx<316)&(al>160)
    skin=roi&(r>210)&(r-g>16)&(r-g<65)&(g-b>7)&(g-b<30)
    flesh=roi&(r>175)&(r-g>14)&(r-g<90)&(g-b>-5)&(g-b<35)
    # Do not treat isolated warm tail-shadow flecks as a foreground paw.
    from animation_geometry import connected_regions
    paw_seed=np.zeros(skin.shape,np.uint8)
    for reg in connected_regions(flesh):
        anchored=sum(int(skin[y,x0:x1].sum()) for y,x0,x1 in reg['runs'])
        if reg['area']>=150 and anchored>=100:
            for y,x0,x1 in reg['runs']:paw_seed[y,x0:x1]=255
    paw=np.asarray(Image.fromarray(paw_seed).filter(ImageFilter.MaxFilter(5))).astype(float)/255
    paw*=al/255
    depth=paw.copy()
    if moving:depth=np.maximum(depth,np.asarray(moving_book_depth(cat))[:,:,3]/255)
    table=np.asarray(props['table']).copy();table[:,:,3]=np.rint(table[:,:,3]*(1-depth)).astype('uint8')
    out.alpha_composite(Image.fromarray(table))
    for name in ('lamp','flower'):out.alpha_composite(props[name])
    if book:
        fixed=np.asarray(props['book']).copy();fixed[:,:,3]=np.rint(fixed[:,:,3]*(1-paw)).astype('uint8')
        out.alpha_composite(Image.fromarray(fixed))
    return out


if __name__=='__main__':
    props,report=extract_props()
    import json
    (DEST.parent/'docs/static-scene.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    plate=Image.new('RGBA',(448,448))
    for im in props.values():plate.alpha_composite(im)
    plate.save(DEST.parent/'docs/props-only.png')
    print(report)
