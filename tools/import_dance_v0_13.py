"""Export the redrawn dance and its exact reflection with one shared scale/pivot."""
from pathlib import Path
import sys,json,hashlib,shutil
import numpy as np
from PIL import Image,ImageOps,ImageDraw
R=Path(__file__).resolve().parent.parent;D=R/'design/actions-v12'
from dance_asset_helpers import extract,feet_center,contact,locate
def write(p,v):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
evidence=R/'docs/desktop-v0.13-validation';evidence.mkdir(exist_ok=True)
baseline=R/'docs/asset-baseline-v0.13.json'
assert baseline.exists(),'Missing current asset baseline'
keys,audit=extract(D/'sources/preserved-body-atlas.png',16,4)
assert len(keys)==16
motion=[locate(keys[i]) for i in range(16)]
# Reject the earlier failure: a moving tail with a nearly static torso.
assert motion[0]['lean'] < -8 and motion[-1]['lean'] > 18
assert motion[-1]['lean']-motion[0]['lean'] > 30
assert max(abs(m['eye_angle']) for m in motion)<3
# One uniform scale for the entire atlas. No per-frame aspect/size changes.
scale=240/float(np.median([im.height-6 for im in keys.values()]))
poses=[];bounds=[]
for i in range(16):
    im=keys[i];im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.LANCZOS)
    cx,ground=feet_center(im);f=Image.new('RGBA',(320,320));f.alpha_composite(im,(round(160-cx),292-ground))
    a=np.array(f);b=f.getbbox();assert min(b[0],b[1],320-b[2],320-b[3])>=8
    assert np.count_nonzero(a[:,:,3]>=128)==np.count_nonzero(np.array(im)[:,:,3]>=128)
    poses.append(f);bounds.append(b)
order=list(range(16))+list(range(14,0,-1))
source_times=[300]+[20]*12+[30]*2+[300]+[30]*2+[20]*12
elapsed=previous=0;times=[]
for t in source_times:
    elapsed+=t;end=(elapsed*5+3)//6;times.append(end-previous);previous=end
assert len(times)==30 and sum(times)==1000 and times[0]==times[15]==250
manifest=json.loads((R/'manifest.json').read_text(encoding='utf8'));records=[]
for name,label,mirrored in [('dance','跳舞',False),('dance_mirrored','跳舞（镜像）',True)]:
    frames=[ImageOps.mirror(poses[i]) if mirrored else poses[i] for i in order]
    folder=R/'anime'/name;folder.mkdir(exist_ok=True,parents=True)
    dest=R/'anime'/name;dest.mkdir(exist_ok=True)
    for i,f in enumerate(frames):
        p=folder/f'{i:03}.png';f.save(p);records.append({'file':(dest/p.name).relative_to(R).as_posix(),'sha256':sha(p)})
    clip={'label':label,'frame_count':30,'drawn_pose_count':16,'loop':True,'duration_ms':1000,'speed_multiplier':1.2,'source_duration_ms':1200,'mirror_of':'dance' if mirrored else None,'frames':[{'file':f'{i:03}.png','duration_ms':t} for i,t in enumerate(times)]}
    write(folder/'animation.json',clip);write(dest/'animation.json',clip)
    manifest['actions'][name]={'path':f'anime/{name}/animation.json',**{k:clip[k] for k in ['label','frame_count','loop','duration_ms']}}
    frames[0].save(R/f'previews/{name}.webp',save_all=True,append_images=frames[1:],duration=times,loop=0,lossless=False,quality=90,method=0,kmin=1,kmax=1,exact=True)
    # GIF uses centiseconds: sample at 50Hz; runtime/WebP retain exact 16/17ms boundaries.
    ends=np.cumsum(times);samples=[]
    for t in range(0,1000,20):
        f=frames[int(np.searchsorted(ends,t,side='right'))];bg=Image.new('RGBA',(320,320),'#f7f4ee');bg.alpha_composite(f);samples.append(bg.convert('RGB'))
    samples[0].save(R/f'previews/{name}.gif',save_all=True,append_images=samples[1:],duration=20,loop=0,disposal=2,optimize=False)
    if not mirrored:contact(frames,R/f'previews/{name}-frames.png')
manifest.update(version='v12-dance-body-sway-mirror',method='Built-in imagegen redraw, uniform scale, exact reflection and independent facing selection')
write(R/'manifest.json',manifest)
html='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>ArcPet 跳舞重绘</title><style>body{font:16px system-ui;background:#ece7de;margin:28px}main{display:flex;flex-wrap:wrap;gap:20px}article{background:white;padding:18px;border-radius:12px}img{width:320px;display:block;background:#f7f4ee}</style><h1>跳舞重绘 · 两个朝向</h1><p>暖金色头发、修长头型与尾巴摆动；每轮1秒，随机起始朝向，每3轮镜像，共12轮。</p><main>'
review=html
for name in ['dance','dance_mirrored']:review+=f'<article><h2>{manifest["actions"][name]["label"]}</h2><img src="previews/{name}.webp"><a href="previews/{name}.webp">GIF</a></article>'
(D/'review.html').write_text((review+'</main>').replace('src="previews/','src="../../previews/').replace('href="previews/','href="../../previews/'),encoding='utf8')
for name,a in manifest['actions'].items():html+=f'<article><h2>{a["label"]}</h2><img src="previews/{name}.webp"><p>{a["frame_count"]}帧 · {a["duration_ms"]/1000:.2f}秒</p></article>'
(R/'preview.html').write_text(html+'</main>',encoding='utf8')
for i in range(30):assert np.array_equal(np.array(ImageOps.mirror(Image.open(R/f'anime/dance/{i:03}.png'))),np.array(Image.open(R/f'anime/dance_mirrored/{i:03}.png')))
assert order[1]==order[-1] and order[14]==order[16]
before=json.loads(baseline.read_text(encoding='utf8'));assert all(sha(R/p)==h for p,h in before.items())
report={'date':'2026-10-28','uniform_scale':scale,'drawn_poses':16,'frames_per_direction':30,'loop_ms':1000,'endpoint_hold_ms':250,'loops_per_trigger':12,'exact_reflection':True,'shared_ground_y':292,'source_extraction':audit,'pose_bounds':bounds,'original_files_unchanged':len(before),'files':records,'registered_clips':len(manifest['actions']),'total_pet_frames':sum(a['frame_count'] for a in manifest['actions'].values()),'limitations':'Technical checks verify cropping, alpha, timing and reflection; perceived art quality still requires user review.'}
report['body_motion']={'back_lean_degrees':motion[0]['lean'],'front_lean_degrees':motion[-1]['lean'],'sway_degrees':motion[-1]['lean']-motion[0]['lean'],'head_eyeline_max_degrees':max(abs(m['eye_angle']) for m in motion),'note':'Image landmark estimate, not anatomical measurement; rejects nearly static torso.'}
write(evidence/'animation-import.json',report);write(D/'docs/verification.json',report)
print('Exported two 30-frame directions, 1000ms each; exact mirror and protected assets verified.')
