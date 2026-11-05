"""Import accepted drawings with one global scale/anchor adjustment, no pose edits."""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image,ImageDraw
R=Path(__file__).resolve().parent.parent;D=R/'design/lying-v1';V=R/'docs/desktop-v0.16-validation'
V.mkdir(parents=True,exist_ok=True)
def save(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')
def warm_bounds(im,roi):
    x0,y0,x1,y1=roi;c=np.array(im)[y0:y1,x0:x1].astype(int)
    mask=(c[:,:,0]>180)&(c[:,:,1]>125)&(c[:,:,0]>c[:,:,1])&(c[:,:,1]>c[:,:,2]+15)&(c[:,:,3]>128)
    yy,xx=np.where(mask)
    return [int(xx.min()+x0),int(yy.min()+y0),int(xx.max()+x0+1),int(yy.max()+y0+1)]
def visible_bounds(im):
    return im.getchannel('A').point(lambda value:255 if value>=64 else 0).getbbox()
idle=Image.open(R/'anime/idle/000.png').convert('RGBA')
sleep=Image.open(R/'anime/sleep/000.png').convert('RGBA')
body=Image.new('RGBA',(320,320));body.alpha_composite(Image.open(D/'layers/body.png').convert('RGBA'),(60,157))
target_head=warm_bounds(idle,(40,40,225,190));source_head=warm_bounds(body,(80,150,250,265))
target_width=target_head[2]-target_head[0];source_width=source_head[2]-source_head[0]
scale=target_width/source_width
target_x=(target_head[0]+target_head[2])/2;source_x=(source_head[0]+source_head[2])/2
ground=visible_bounds(idle)[3];source_ground=visible_bounds(body)[3]
size=round(320*scale);actual_scale=size/320
offset=(round(target_x-source_x*actual_scale),ground-visible_bounds(body.resize((size,size),Image.Resampling.LANCZOS))[3])
print(json.dumps({'source_head':source_head,'idle_head':target_head,'scale':actual_scale,'offset':offset,'ground':ground}))
source=json.loads((D/'anime/animation.json').read_text(encoding='utf8'))
out=R/'anime/lying';out.mkdir(exist_ok=True)
frames=[];bounds=[];source_hashes={}
for i,entry in enumerate(source['frames']):
    path=D/'anime'/entry['file'];im=Image.open(path).convert('RGBA')
    source_hashes[entry['file']]=hashlib.sha256(path.read_bytes()).hexdigest()
    scaled=im.resize((size,size),Image.Resampling.LANCZOS)
    b=scaled.getbbox();positioned=[b[0]+offset[0],b[1]+offset[1],b[2]+offset[0],b[3]+offset[1]]
    assert min(positioned[0],positioned[1],320-positioned[2],320-positioned[3])>=5,positioned
    frame=Image.new('RGBA',(320,320));frame.alpha_composite(scaled,offset)
    frame.save(out/f'{i:03}.png');frames.append(frame);bounds.append(visible_bounds(frame))
metadata={'label':'趴下摇尾巴','frame_count':len(frames),'loop':True,'duration_ms':source['duration_ms'],
    'frames':[{'file':f'{i:03}.png','duration_ms':f['duration_ms']} for i,f in enumerate(source['frames'])]}
save(out/'animation.json',metadata)
manifest=json.loads((R/'manifest.json').read_text(encoding='utf8'))
if 'walk_left' not in manifest['actions']:manifest['version']='v16-lying-tail-sway'
manifest['actions']['lying']={'path':'anime/lying/animation.json',**{k:metadata[k] for k in ['label','frame_count','loop','duration_ms']}}
save(R/'manifest.json',manifest)
calibrated_body=Image.new('RGBA',(320,320));calibrated_body.alpha_composite(body.resize((size,size),Image.Resampling.LANCZOS),offset)
actual_head=warm_bounds(calibrated_body,(20,130,250,275))
comparison=Image.new('RGB',(4*320,360),'#f7f4ee');draw=ImageDraw.Draw(comparison)
for index,(label,im,head) in enumerate([
    ('Idle reference',idle,target_head),('Sleep reference',sleep,None),
    ('Accepted design',Image.open(D/'anime/000.png').convert('RGBA'),source_head),
    ('Runtime calibrated',frames[0],actual_head)]):
    bg=Image.new('RGBA',(320,320),'#f7f4ee');bg.alpha_composite(im);comparison.paste(bg.convert('RGB'),(index*320,0))
    draw.line((index*320,ground,index*320+319,ground),fill='#7caa87')
    if head:draw.rectangle((head[0]+index*320,head[1],head[2]+index*320,head[3]),outline='#4791aa')
    draw.text((index*320+8,328),label,fill='#4a4033')
comparison.save(V/'size-comparison.png')
report={'accepted_design':'design/lying-v1','source_head_warm_bounds':source_head,'idle_head_warm_bounds':target_head,
    'measurement':'Warm hair/ear pixel bounding box in recorded head ROI; visual comparison required.',
    'source_head_width_px':source_width,'target_head_width_px':target_width,'runtime_head_warm_bounds':actual_head,
    'runtime_head_width_px':actual_head[2]-actual_head[0],'requested_scale':scale,'actual_uniform_scale':actual_scale,
    'whole_canvas_resize':[size,size],'translation':offset,'source_ground':source_ground,'target_ground':ground,
    'runtime_ground_values':sorted({b[3] for b in bounds}),
    'head_center_target_x':target_x,'head_center_runtime_x':(actual_head[0]+actual_head[2])/2,
    'single_transform_for_all_frames':True,'frame_count':len(frames),'duration_ms':source['duration_ms'],
    'output_bounds':bounds,'accepted_source_sha256':source_hashes,
    'accepted_artwork_modified':False,'pose_rotation':False,'pose_warp':False}
assert abs(report['runtime_head_width_px']-target_width)<=2
assert abs(report['head_center_runtime_x']-target_x)<=1
assert max(abs(b[3]-ground) for b in bounds)<=2
save(V/'lying-size.json',report)
design_report=D/'docs/verification.json'
if design_report.exists():
    verification=json.loads(design_report.read_text(encoding='utf8'))
    verification['runtime_integrated']=True
    verification['runtime_action']='lying'
    verification['runtime_calibration']='docs/desktop-v0.16-validation/lying-size.json'
    save(design_report,verification)
print(json.dumps({k:v for k,v in report.items() if k not in ['output_bounds','accepted_source_sha256']}))
