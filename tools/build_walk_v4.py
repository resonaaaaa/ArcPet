"""Export whole-character drawings; no body-part compositing or pose warps."""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image,ImageDraw
from dance_asset_helpers import extract,contact

R=Path(__file__).resolve().parent.parent
D=R/'design/walk-v4'
def save(path,value):
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf8')
def measure(im):
    a=np.array(im);rgb=a[:,:,:3].astype(int);opaque=a[:,:,3]>=128
    yy,xx=np.where(opaque);top,bottom=int(yy.min()),int(yy.max())+1
    head=opaque.copy();head[top+round((bottom-top)*.60):]=False
    hy,hx=np.where(head)
    navy=opaque&(rgb[:,:,2]>rgb[:,:,0]+3)&(rgb[:,:,2]>rgb[:,:,1]+3)&(rgb[:,:,0]<130)&(rgb[:,:,1]<140)
    navy[:top+round((bottom-top)*.52)]=False
    navy[top+round((bottom-top)*.84):]=False
    # Include both light and dark navy panels; one shading island biases x.
    rows=[]
    for y in np.where(navy.any(axis=1))[0]:
        xs=np.where(navy[y])[0]
        if len(xs)>=8:rows.append((y,float(xs.min()+xs.max())/2))
    assert len(rows)>5,'Missing skirt anchor'
    hip_x=float(np.median([x for y,x in rows]));hip_y=float(np.mean([y for y,x in rows]))
    white=opaque&(rgb[:,:,0]>195)&(rgb[:,:,1]>190)&(rgb[:,:,2]>190)
    white[:top+round((bottom-top)*.48)]=False
    white[top+round((bottom-top)*.70):]=False
    white_rows=np.where(white.sum(axis=1)>=5)[0]
    assert len(white_rows)>3,'Missing white collar'
    neck_y=int(white_rows[0]);neck_x=float(np.median(np.where(white[neck_y:neck_y+5])[1]))
    return {'height':bottom-top,'ground_y':bottom,'hip':[hip_x,hip_y],'neck':[neck_x,neck_y],
            'head_width':int(hx.max()-hx.min()+1),'bounds':[int(xx.min()),top,int(xx.max()+1),bottom]}

sheets={};audits={};measurements={}
unified=False
for name,count,cols,excluded in [('first-half',16,4,()),('second-half',16,4,())]:
    sheets[name],audits[name]=extract(D/f'sources/{name}.png',count,cols,exclude=excluded)
    assert len(sheets[name])==count-len(excluded),(name,audits[name])
    assert all(a['accepted'] or a['key'] in excluded for a in audits[name]),(name,audits[name])
    measurements[name]=[measure(im) for im in sheets[name].values()]
base='first-half'
key_scale=240/float(np.median([m['height'] for m in measurements[base]]))
target_head=float(np.median([m['head_width'] for m in measurements[base]]))*key_scale
# One UNIFORM scale per full sheet. No per-frame or per-part resizing.
scales={name:target_head/float(np.median([m['head_width'] for m in ms])) for name,ms in measurements.items()}
torso_lengths={name:float(np.median([m['hip'][1]-m['neck'][1] for m in ms]))*scales[name] for name,ms in measurements.items()}
assert max(torso_lengths.values())/min(torso_lengths.values())<1.08,('Cross-sheet torso drift',torso_lengths)
# Reassign next-step drawings to their correct phase. First-half pose2 and
# second-half pose1 reverse the rear-foot path here and are omitted.
# No duplicate, warped, or off-model gap drawings are used.
order=([('first-half',i) for i in [0,1]]+[('second-half',i) for i in [13,14,15]]+
       [('first-half',i) for i in range(3,13)]+[('second-half',0)]+
       [('first-half',i) for i in [13,14,15]]+[('second-half',2)]+
       [('second-half',i) for i in range(3,13)])
COUNT=len(order)
assert COUNT==len(set(order))==30
# Two whole-character drawing edits fix the high head at the cycle start.
# Other 28 poses, their placement and their durations remain byte-identical.
seam_edits={0:('seam-000',145),1:('seam-001',143)}
for index,(name,head_width) in seam_edits.items():
    sheets[name],audits[name]=extract(D/f'sources/{name}.png',1,1)
    assert audits[name][0]['accepted']
    scales[name]=head_width/measure(sheets[name][0])['head_width']
for old_frame in (D/'anime').glob('*.png'):
    if old_frame.stem.isdigit() and int(old_frame.stem)>=COUNT:
        old_frame.unlink()
frames=[];trace=[];bounds=[]
for index in range(COUNT):
    name,pose=order[index]
    if index in seam_edits:
        name=seam_edits[index][0];pose=0
    im=sheets[name][pose];scale=scales[name]
    im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.LANCZOS)
    m=measure(im);x=round(154-m['hip'][0]);y=292-m['ground_y']
    frame=Image.new('RGBA',(320,320));frame.alpha_composite(im,(x,y))
    b=frame.getbbox();assert min(b[0],b[1],320-b[2],320-b[3])>=6,(name,pose,b)
    frame.save(D/f'anime/{index:03}.png');frames.append(frame);bounds.append(b)
    trace.append({'file':f'{index:03}.png','duration_ms':40,'source_sheet':name,'source_pose':pose,
                  'scale':scale,'translation':[x,y],'head_width_px':m['head_width'],
                  'hip_px':[x+m['hip'][0],y+m['hip'][1]],'neck_px':[x+m['neck'][0],y+m['neck'][1]],
                  'neck_to_hip_x_px':m['neck'][0]-m['hip'][0],'ground_y':292})
assert len(frames)==COUNT
arrays=[np.array(f) for f in frames]
assert len({hashlib.sha256(a.tobytes()).hexdigest() for a in arrays})==COUNT,'Duplicate exported poses'
widths=[t['head_width_px'] for t in trace]
assert max(widths)/min(widths)<1.08,('Head size drift',widths)
times=[40]*COUNT
frames[0].save(D/'preview.webp',save_all=True,append_images=frames[1:],duration=times,loop=0,lossless=True,method=0,kmin=1,kmax=1)
opaque=[]
for f in frames:
    bg=Image.new('RGBA',f.size,'#f7f4ee');bg.alpha_composite(f);opaque.append(bg.convert('RGB'))
opaque[0].save(D/'preview.gif',save_all=True,append_images=opaque[1:],duration=times,loop=0,disposal=2,optimize=False)
contact(frames,D/'frames-contact.png',8)
proof=Image.new('RGB',(960,290),'#f7f4ee');draw=ImageDraw.Draw(proof)
for col,(label,index) in enumerate([('Near contact',0),('Far passing',10),('Far contact',15),('Near passing',25)]):
    proof.paste(opaque[index].resize((240,240),Image.Resampling.LANCZOS),(col*240,0))
    draw.text((col*240+8,251),label,fill='#514437')
    draw.text((col*240+8,269),f'frame {index:02}',fill='#514437')
proof.save(D/'gait-proof.png')
# All leg crossings are shown enlarged, not just four selected good poses.
leg_detail=Image.new('RGB',(8*260,4*230),'#f7f4ee');ld=ImageDraw.Draw(leg_detail)
for i,f in enumerate(frames):
    crop=f.crop((80,195,235,300)).resize((248,168),Image.Resampling.LANCZOS)
    x=i%8*260;y=i//8*230;leg_detail.paste(crop,(x+6,y+8),crop)
    ld.text((x+8,y+190),f'{i:02}',fill='#514437')
leg_detail.save(D/'legs-detail.png')
comparison=Image.new('RGB',(640,350),'#f7f4ee');draw=ImageDraw.Draw(comparison)
for col,(label,im) in enumerate([('Idle reference',Image.open(R/'anime/idle/000.png').convert('RGBA')),('Whole-character walk',frames[0])]):
    bg=Image.new('RGBA',(320,320),'#f7f4ee');bg.alpha_composite(im);comparison.paste(bg.convert('RGB'),(col*320,0))
    draw.line((col*320,292,col*320+319,292),fill='#6c9c80');draw.text((col*320+8,328),label,fill='#514437')
comparison.save(D/'size-comparison.png')
previous=R/'design/walk-v3/anime/000.png'
if previous.exists():
    posture=Image.new('RGB',(640,355),'#f7f4ee');pd=ImageDraw.Draw(posture)
    for col,(label,im) in enumerate([('Previous v3',Image.open(previous).convert('RGBA')),('Upright revision',frames[0])]):
        m=measure(im);bg=Image.new('RGBA',(320,320),'#f7f4ee');bg.alpha_composite(im)
        posture.paste(bg.convert('RGB'),(col*320,0));hip=m['hip'][0]+col*320
        for yy in range(145,240,7):pd.line((hip,yy,hip,yy+3),fill='#79a48c')
        pd.text((col*320+8,331),label,fill='#514437')
    posture.save(D/'posture-comparison.png')
steps=[float(np.abs(arrays[(i+1)%COUNT].astype(float)-a.astype(float)).mean()) for i,a in enumerate(arrays)]
report={'frames':COUNT,'duration_ms':sum(times),'whole_character_drawings':COUNT,'composited_body_parts':False,
        'warps':False,'rotations':False,'blended_inbetweens':False,'per_frame_scaling':False,
        'sheet_uniform_scales':scales,'unified_single_sheet':unified,'head_width_range_px':[min(widths),max(widths)],
        'seam_whole_character_replacements':list(seam_edits),
        'median_collar_to_skirt_length_px':torso_lengths,
        'neck_to_hip_x_px':[t['neck_to_hip_x_px'] for t in trace],
        'collar_check_note':'White-collar color centroid is a posture inspection aid, not a spine model. Anatomy and limb occlusion still need visual review.',
        'pose_bounds':bounds,'source_crops':audits,'frame_sources':trace,
        'seam_mean_rgba_difference':steps[-1],'max_internal_mean_rgba_difference':max(steps[:-1]),
        'step_mean_rgba_differences':steps,'runtime_integrated':False,
        'source_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (D/'sources').glob('*.png')},
        'limitations':'Numerical crop, scale and pixel-change checks do not certify anatomical depth or visual smoothness. See gait-proof and full preview; user approval pending.'}
for ext in ['gif','webp']:
    im=Image.open(D/f'preview.{ext}');duration=0
    for i in range(im.n_frames):
        im.seek(i);im.load();duration+=im.info['duration']
    assert im.n_frames==COUNT and duration==sum(times),(ext,im.n_frames,duration)
    report[ext]={'frames':im.n_frames,'duration_ms':duration}
save(D/'docs/verification.json',report)
save(D/'anime/animation.json',{'label':'45度侧身直立散步设计','loop':True,'frame_count':COUNT,'duration_ms':sum(times),'frames':trace})
(D/'review.html').write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>ArcPet 散步 · 直立与步态修订</title>
<style>body{font:16px system-ui;background:#ede8de;color:#3e352b;margin:28px}main{display:flex;gap:20px;flex-wrap:wrap}article{background:white;padding:18px;border-radius:12px}img.pet{width:320px;height:320px;background:#f7f4ee}button{padding:8px 12px;margin:4px;border:1px solid #b6a58c;border-radius:7px;background:#faf7ef}.proof{max-width:960px;width:100%}</style>
<h1>散步 · 直立与步态修订</h1><p>重新绘制直立关键姿势与完整循环：缩短跨步、保持支撑重心，复查近远腿经过和遮挡。衣发保持轻微动态。首尾高度已微调；30帧，1.20秒循环，仅设计预览。</p>
<main><article><h2>当前设计</h2><img class="pet" id="walk" src="anime/000.png?revision=seam1"><p id="status">加载…</p><button id="toggle">暂停</button><button id="speed">慢放 ×0.5</button><button id="step">下一帧</button><p><a href="preview.gif">GIF</a> · <a href="preview.webp">WebP</a> · <a href="frames-contact.png">全部帧</a></p></article><article><h2>动作参考</h2><img src="../walk-v1/reference/walk.gif" style="height:270px"><p>仅参考表情和步态，服装沿用现有猫姬。</p></article></main>
<h2>步态关键姿势</h2><img class="proof" src="gait-proof.png"><h2>全部30帧腿部放大</h2><a href="legs-detail.png"><img class="proof" src="legs-detail.png"></a><h2>比例与落地线</h2><img class="proof" src="size-comparison.png"><p><a href="posture-comparison.png">前倾修订对照</a> · <a href="README.md">设计记录及限制</a></p>
<script>let index=0,playing=true,slow=false,last=0,elapsed=0;const images=[],target=document.getElementById('walk'),status=document.getElementById('status');function render(){target.src=images[index].src;status.textContent=`第 ${index+1}/30 帧`};document.getElementById('toggle').onclick=()=>{playing=!playing;elapsed=0;document.getElementById('toggle').textContent=playing?'暂停':'播放'};document.getElementById('speed').onclick=()=>{slow=!slow;elapsed=0;document.getElementById('speed').textContent=slow?'正常 ×1':'慢放 ×0.5'};document.getElementById('step').onclick=()=>{playing=false;elapsed=0;document.getElementById('toggle').textContent='播放';index=(index+1)%30;render()};Promise.all(Array.from({length:30},(_,i)=>new Promise((resolve,reject)=>{const im=new Image();images.push(im);im.onload=resolve;im.onerror=reject;im.src=`anime/${String(i).padStart(3,'0')}.png?revision=seam1`}))).then(()=>{render();requestAnimationFrame(function tick(t){if(last&&playing){elapsed+=Math.min(t-last,200);let duration=slow?80:40;if(elapsed>=duration){const n=Math.floor(elapsed/duration);index=(index+n)%30;elapsed-=n*duration;render()}}last=t;requestAnimationFrame(tick)})}).catch(()=>status.textContent='加载失败，请查看GIF');</script></html>''',encoding='utf8')
print(json.dumps({k:report[k] for k in ['frames','duration_ms','head_width_range_px','seam_mean_rgba_difference','max_internal_mean_rgba_difference','gif','webp']}))
