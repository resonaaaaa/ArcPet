"""Import the accepted whole-character walk with one fixed layout transform."""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image,ImageOps,ImageDraw

R=Path(__file__).resolve().parent.parent
D=R/'design/walk-v4'
V=R/'docs/desktop-v0.17-validation'
V.mkdir(parents=True,exist_ok=True)
def save(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')
def bounds(im):return im.getchannel('A').point(lambda v:255 if v>=64 else 0).getbbox()
source=json.loads((D/'anime/animation.json').read_text(encoding='utf8'))
original=[Image.open(D/'anime'/f['file']).convert('RGBA') for f in source['frames']]
idle=Image.open(R/'anime/idle/000.png').convert('RGBA')
idle_bounds=bounds(idle)
# Standing side-view has a narrower projected head than the front view. Match
# overall standing height, preserving the approved head/body proportions.
requested_scale=(idle_bounds[3]-idle_bounds[1])/float(np.median([bounds(f)[3]-bounds(f)[1] for f in original]))
size=round(320*requested_scale);scale=size/320
scaled=[f.resize((size,size),Image.Resampling.LANCZOS) for f in original]
offset=(round(135-154*scale),idle_bounds[3]-round(float(np.median([bounds(f)[3] for f in scaled]))))
right=[];left=[]
for im in scaled:
    f=Image.new('RGBA',(320,320));f.alpha_composite(im,offset);right.append(f)
    mirrored=Image.new('RGBA',(320,320));mirrored.alpha_composite(ImageOps.mirror(f),(-50,0));left.append(mirrored)
for direction,frames in [('right',right),('left',left)]:
    out=R/f'anime/walk_{direction}';out.mkdir(exist_ok=True)
    for i,im in enumerate(frames):
        b=im.getbbox();assert min(b[0],b[1],320-b[2],320-b[3])>=4,(direction,i,b)
        im.save(out/f'{i:03}.png')
    save(out/'animation.json',{'label':'散步向'+('右' if direction=='right' else '左'),'frame_count':len(frames),
        'loop':True,'duration_ms':source['duration_ms'],'handoff_frames':[0,15],
        'frames':[{'file':f'{i:03}.png','duration_ms':f['duration_ms']} for i,f in enumerate(source['frames'])]})
manifest=json.loads((R/'manifest.json').read_text(encoding='utf8'));manifest['version']='v17-walking-travel'
for direction in ['left','right']:
    name='walk_'+direction;clip=json.loads((R/'anime'/name/'animation.json').read_text(encoding='utf8'))
    manifest['actions'][name]={'path':f'anime/{name}/animation.json',**{k:clip[k] for k in ['label','frame_count','loop','duration_ms']}}
save(R/'manifest.json',manifest)
ground=sorted({bounds(f)[3] for f in right+left});assert max(ground)-min(ground)<=2
comparison=Image.new('RGB',(960,355),'#f7f4ee');dr=ImageDraw.Draw(comparison)
for col,(label,im) in enumerate([('Idle',idle),('Walk right',right[0]),('Walk left',left[0])]):
    comparison.paste(im,(col*320,0),im);dr.line((col*320,292,col*320+319,292),fill='#7caa87')
    dr.text((col*320+10,330),label,fill='#4a4033')
comparison.save(V/'walk-size-comparison.png')
save(V/'walk-import.json',{'accepted_design':'design/walk-v4, including approved seam correction','frames_per_direction':len(right),
    'duration_ms':source['duration_ms'],'calibration':'Median whole-character standing height, fixed body x135, shared ground292. Side-view head width is preserved.',
    'idle_visible_height':idle_bounds[3]-idle_bounds[1],'design_median_visible_height':float(np.median([bounds(f)[3]-bounds(f)[1] for f in original])),
    'whole_canvas_size':size,'uniform_scale':scale,'translation':offset,'single_transform_for_all_frames':True,
    'reflection_axis_x':135,'reflection_translation':[-50,0],'runtime_ground_range':ground,'handoff_frames':[0,15],
    'pose_warps':False,'source_sha256':{f['file']:hashlib.sha256((D/'anime'/f['file']).read_bytes()).hexdigest() for f in source['frames']}})
report=json.loads((D/'docs/verification.json').read_text(encoding='utf8'))
report.update(runtime_integrated=True,runtime_actions=['walk_left','walk_right'],runtime_calibration='docs/desktop-v0.17-validation/walk-import.json')
save(D/'docs/verification.json',report)
print(json.dumps({'clips':2,'frames':len(right)*2,'duration_ms':source['duration_ms'],'scale':scale,'offset':offset,'ground':ground}))
