"""Assemble individually drawn lateral tail poses; no rotation, warp or blending."""
from pathlib import Path
import hashlib,json,math
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
from animation_geometry import connected_regions
R=Path(__file__).resolve().parent.parent; D=R/'design/lying-v1'
def save(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')
body=Image.open(D/'layers/body.png').convert('RGBA')
body_hash=hashlib.sha256((D/'layers/body.png').read_bytes()).hexdigest()
assert body_hash==json.loads((D/'docs/body-before-sway.json').read_text())['sha256']
root=(120,230); body_position=(60,292-body.height); pool={}
for tag,count in [('arc',20)]:
    source=D/'sources/tail-arc-atlas.png';im=Image.open(source).convert('RGBA');a=np.array(im)
    regions=[r for r in connected_regions(a[:,:,3]>=128) if r['area']>1500]
    assert len(regions)==count,(tag,len(regions))
    regions.sort(key=lambda r:r['bbox'][1]); ordered=[]
    for row in range(count//5):ordered+=sorted(regions[row*5:(row+1)*5],key=lambda r:r['bbox'][0])
    for index,region in enumerate(ordered):
        bounds=region['bbox'];x0,y0,x1,y1=bounds
        assert min(x0,y0,im.width-x1,im.height-y1)>=5
        mask=np.zeros(a.shape[:2],dtype='uint8')
        for y,left,right in region['runs']:mask[y,left:right]=255
        support=np.array(Image.fromarray(mask).filter(ImageFilter.MaxFilter(3)))>0
        pixels=a.copy();pixels[~support]=0
        tail=Image.fromarray(pixels).crop((x0-3,y0-3,x1+3,y1+3));m=np.array(tail)[:,:,3]>=128
        yy,xx=np.where(m);bottom=int(yy.max());top=int(yy.min())
        root_x=float(np.median(np.where(m[bottom-4:bottom+1])[1]))
        tip_x=float(np.median(np.where(m[top:top+5])[1]))
        # Uniform whole-drawing scale stabilizes length, without synthesizing poses.
        rows=np.arange(top+5,bottom-4,4);centers=np.array([np.median(np.where(m[y])[0]) for y in rows])
        length=float(np.hypot(np.diff(centers),np.diff(rows)).sum()+10);scale=118/length
        tip=((tip_x-root_x)*scale,(top-bottom)*scale)
        pool[(tag,index)]={'image':tail,'scale':scale,'root':(root_x,bottom),'tip':tip,
            'angle':math.atan2(tip[0],-tip[1]),'bounds':bounds,
            'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest()}
outbound=[('arc',i) for i in range(19)]
assert len(outbound)==len(set(outbound))
order=outbound+outbound[-2:0:-1];assert len(order)>=30
# Keep the previous approved-for-revision timing and pose correspondence exactly.
previous=json.loads((D/'docs/pre-hook-layout.json').read_text(encoding='utf8'))
period=1600;times=[f['duration_ms'] for f in previous['frames']]
assert len(times)==len(order)
assert sum(times)==period and min(times)>=10
frames=[];trace=[];tips=[];tail_boxes=[]
for index,key in enumerate(order):
    pose=pool[key];scale=pose['scale'];tail=pose['image']
    tail=tail.resize((round(tail.width*scale),round(tail.height*scale)),Image.Resampling.LANCZOS)
    x=round(root[0]-pose['root'][0]*scale);y=round(root[1]-pose['root'][1]*scale)
    frame=Image.new('RGBA',(320,320));frame.alpha_composite(tail,(x,y));bounds=frame.getbbox()
    assert min(bounds[0],bounds[1],320-bounds[2],320-bounds[3])>=5
    tail_boxes.append(bounds);frame.alpha_composite(body,body_position);frames.append(frame)
    frame.save(D/f'anime/{index:03}.png');tips.append([root[0]+pose['tip'][0],root[1]+pose['tip'][1]])
    trace.append({'file':f'{index:03}.png','duration_ms':times[index],'source_sheet':key[0],
        'drawn_pose_index':key[1],'source_bounds':pose['bounds'],'source_sha256':pose['source_sha256'],
        'uniform_scale':scale,'translation':[x,y]})
metadata={'frame_count':len(frames),'distinct_drawn_tail_poses':len(outbound),'frame_size':[320,320],
    'loop':True,'duration_ms':period,'body_position':body_position,'tail_root':root,
    'rotations':False,'warping':False,'generated_inbetween_blends':False,
    'return_path':'same drawn poses in reverse','timing':'preserved from pre-hook-layout.json',
    'tail_shape_style':'gentle whole-tail arc; rounded tip without hook, individually drawn','frames':trace}
save(D/'anime/animation.json',metadata)
frames[0].save(D/'preview.webp',save_all=True,append_images=frames[1:],duration=times,
    loop=0,lossless=True,method=0,kmin=1,kmax=1)
opaque=[]
for frame in frames:
    bg=Image.new('RGBA',frame.size,'#f7f4ee');bg.alpha_composite(frame);opaque.append(bg.convert('RGB'))
opaque[0].save(D/'preview.gif',save_all=True,append_images=opaque[1:],duration=times,
    loop=0,disposal=2,optimize=False)
body_mask=Image.new('RGBA',(320,320));body_mask.alpha_composite(body,body_position)
body_mask=np.array(body_mask)[:,:,3]==255
assert all(np.array_equal(np.array(f)[body_mask],np.array(frames[0])[body_mask]) for f in frames)
tips=np.array(tips);steps=np.linalg.norm(np.roll(tips,-1,axis=0)-tips,axis=1)
assert steps[-1]<max(steps) and max(steps)<22
cols=6;contact=Image.new('RGB',(cols*200,math.ceil(len(frames)/cols)*160),'#f7f4ee');draw=ImageDraw.Draw(contact)
for i,frame in enumerate(frames):
    thumb=Image.new('RGBA',frame.size,'#f7f4ee');thumb.alpha_composite(frame)
    thumb=thumb.crop((10,95,270,305)).resize((180,145),Image.Resampling.LANCZOS)
    x=i%cols*200;y=i//cols*160;contact.paste(thumb.convert('RGB'),(x,y))
    draw.text((x+4,y+145),f'{i:02}: {times[i]}ms {order[i]}',fill='#554634')
contact.save(D/'frames-contact.png')
report={'frame_count':len(frames),'distinct_drawn_tail_poses':len(outbound),'duration_ms':period,
    'body_pixel_identical':True,'body_file_unchanged':True,'body_sha256':body_hash,'tail_root':root,
    'rotations':0,'shape_warps':0,'synthetic_pose_interpolation':0,
    'tip_horizontal_travel_px':float(np.ptp(tips[:,0])),'tip_vertical_travel_px':float(np.ptp(tips[:,1])),
    'max_tip_step_px':float(max(steps)),'loop_seam_tip_step_px':float(steps[-1]),
    'duration_range_ms':[min(times),max(times)],'tip_positions':tips.tolist(),'tail_bounds':tail_boxes,
    'frame_source_map':trace,'runtime_integrated':(R/'anime/lying/animation.json').is_file(),
    'tail_shape_style':'gentle whole-tail arc; rounded tip without hook, individually drawn',
    'timing_unchanged':times==[f['duration_ms'] for f in previous['frames']],
    'limitations':'Drawn contour variation remains possible; visual acceptance pending.'}
for ext in ['gif','webp']:
    im=Image.open(D/f'preview.{ext}');duration=0
    for j in range(im.n_frames):im.seek(j);im.load();duration+=im.info['duration']
    assert duration==period and im.info['loop']==0
    report[ext]={'decoded_frames':im.n_frames,'duration_ms':duration};im.close()
save(D/'docs/verification.json',report)
(D/'review.html').write_text('<!doctype html><meta charset="utf-8"><title>左右摆尾修订</title>'
    '<style>body{font:17px system-ui;background:#eee8df;margin:30px}img{width:384px;background:#f7f4ee}</style>'
    '<h1>趴下摇尾巴 · 左右扫动</h1>'
    f'<p>{len(frames)}帧 / 1.6秒；整根尾巴带平缓弧度，末端自然收圆；大幅左右摇摆，往返共用原画。主体静止。</p>'
    '<img src="preview.webp"><p><a href="preview.gif">GIF</a> · '
    '<a href="frames-contact.png">逐帧来源图</a></p>',encoding='utf8')
print(json.dumps({k:v for k,v in report.items() if k not in ['tip_positions','tail_bounds','frame_source_map']}))
