"""Export drawn drag poses and review timing without changing the runtime catalog."""
from pathlib import Path
import hashlib
import json

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from animation_geometry import connected_regions

ROOT = Path(__file__).resolve().parent.parent
D = ROOT / "design/drag-lift-v1"
SIZE = 384
GROUND = 356
LAYOUT = {"keys": (2, 3), "hold": (4, 4), "land": (4, 4)}


def read_atlas(name):
    im = Image.open(D / "sources" / (name + ".png")).convert("RGBA")
    a = np.asarray(im)
    rows, cols = LAYOUT[name]
    regs = [r for r in connected_regions(a[:, :, 3] >= 128) if r["area"] > 2400]
    if len(regs) != rows * cols:
        raise ValueError((name, im.size, [(r["area"], r["bbox"]) for r in regs]))
    regs.sort(key=lambda r: (r["bbox"][1] + r["bbox"][3]) / 2)
    ordered = []
    for row in range(rows):
        ordered.extend(sorted(regs[row * cols:(row + 1) * cols], key=lambda r: r["bbox"][0]))
    result = []
    for reg in ordered:
        x0, y0, x1, y1 = reg["bbox"]
        assert min(x0, y0, im.width-x1, im.height-y1) >= 3, (name, reg["bbox"])
        box = (x0 - 3, y0 - 3, x1 + 3, y1 + 3)
        cropped = np.array(im.crop(box))
        mask = np.zeros(cropped.shape[:2], dtype=np.uint8)
        for y, l, r in reg["runs"]:
            mask[y-box[1], l-box[0]:r-box[0]] = 255
        support = np.array(Image.fromarray(mask).filter(ImageFilter.MaxFilter(3))) > 0
        cropped[~support] = 0
        result.append(Image.fromarray(cropped))
    return result


def head(im):
    a = np.asarray(im).astype(np.int16)
    r, g, b, alpha = a.transpose(2, 0, 1)
    opaque = alpha >= 128
    yy, _ = np.where(opaque)
    # Warm hair and ear edges; restrict to the upper part to exclude the tail.
    mask = opaque & (r >= 166) & (g >= 111) & (r-g >= 19) & (g-b >= 29)
    mask[round(yy.min() + .56 * (yy.max()+1-yy.min())):] = False
    y, x = np.where(mask)
    lo, hi = np.percentile(x, [1, 99])
    return (lo+hi)/2, float(np.median(y)), hi-lo+1


def opaque_bounds(im):
    return im.getchannel("A").point(lambda a: 255 if a >= 64 else 0).getbbox()


def place(im, x, y):
    frame = Image.new("RGBA", (SIZE, SIZE))
    b = im.getbbox()
    assert min(b[0]+x, b[1]+y, SIZE-b[2]-x, SIZE-b[3]-y) >= 3, (b,x,y)
    frame.alpha_composite(im, (x,y))
    return frame


def contact(frames, path, cols=4, thumb=192):
    out = Image.new("RGB", (cols*thumb, ((len(frames)+cols-1)//cols)*(thumb+22)), "#f7f4ee")
    dr = ImageDraw.Draw(out)
    for i, im in enumerate(frames):
        x, y = i%cols*thumb, i//cols*(thumb+22)
        im = im.resize((thumb,thumb), Image.Resampling.LANCZOS)
        out.paste(im,(x,y),im)
        dr.text((x+8,y+thumb+3),f"{i+1:02}",fill="#504439")
    out.save(path)


def gif(path, frames, times, loop=False):
    filled = []
    for f in frames:
        bg = Image.new("RGBA",f.size,"#f7f4ee")
        bg.alpha_composite(f)
        filled.append(bg.convert("RGB"))
    filled[0].save(path,save_all=True,append_images=filled[1:],duration=times,
                   disposal=2,optimize=False,**({"loop":0} if loop else {}))
    with Image.open(path) as result:
        total = 0
        for n in range(result.n_frames):
            result.seek(n)
            total += result.info["duration"]
        assert total == sum(times), (path,total)
        return result.n_frames


def save(path, data):
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding="utf8")


def review_page():
    page = """<!doctype html>
<html lang="zh-CN"><meta charset="utf-8"><title>ArcPet · 拎起、悬挂与落地</title>
<style>body{background:#ede8de;color:#3e352b;font:16px system-ui;margin:24px}main{display:flex;gap:20px;flex-wrap:wrap}article{background:white;padding:16px;border-radius:12px}button,select{font:inherit;margin:4px;padding:6px}img{background:#f7f4ee;max-width:100%}#stage{position:relative;width:384px;max-width:80vw}#stage img{display:block;width:100%}#stage:after{content:'';position:absolute;pointer-events:none;top:92.708%;width:100%;border-bottom:1px dashed #b1a691}</style>
<h1>拖动动作 · 三段预览</h1><p>从站立到拎起，悬挂时摆尾和眨眼，松开后抬手找地、屈膝缓冲、站直。虚线为落地线。</p>
<main><article><div id="stage"><img id="frame" src="anime/lift/000.png" alt="动作帧"></div>
<p><select id="clip"><option value="all">完整流程（悬挂两轮）</option><option value="lift">拎起</option><option value="hold">悬挂循环</option><option value="land">落地</option></select>
<button id="play">暂停</button><button id="step">下一帧</button><label><input type="checkbox" id="slow">半速</label></p><p id="info" aria-live="polite"></p>
<p><a href="preview.gif">完整 GIF</a> · <a href="lift.gif">拎起</a> · <a href="hold.gif">悬挂</a> · <a href="land.gif">落地</a></p></article>
<article><h2>关键姿势</h2><img src="key-poses.png" width="576" alt="关键姿势表"><p><a href="lift-frames.png">拎起逐帧</a> · <a href="hold-frames.png">悬挂逐帧</a> · <a href="land-frames.png">落地逐帧</a> · <a href="README.md">设计记录</a></p></article></main>
<script>
const meta=__METADATA__;
const el=id=>document.getElementById(id);
const seq={};
for(const [name,clip] of Object.entries(meta)){
  seq[name]=clip.frames.map((f,i)=>({path:'anime/'+name+'/'+f.file,ms:f.duration_ms,name,index:i+1,total:clip.frame_count}));
}
seq.all=[...seq.lift,...seq.hold,...seq.hold,...seq.land];
const imgs=Object.values(seq).flat().map(f=>f.path);
let t=0,prev=0,n=0,playing=true,ready=false;
const names={lift:'拎起',hold:'悬挂',land:'落地'};
const current=()=>seq[el('clip').value];
const draw=()=>{const f=current()[n];el('frame').src=f.path;el('info').textContent=names[f.name]+' · '+f.index+'/'+f.total+' 帧';};
function state(x){playing=x;el('play').textContent=x?'暂停':'播放';}
el('clip').onchange=()=>{n=0;t=0;state(true);draw();};
el('play').onclick=()=>{if(!playing && n===current().length-1 && ['lift','land'].includes(el('clip').value))n=0;state(!playing);t=0;draw();};
el('step').onclick=()=>{state(false);n=(n+1)%current().length;t=0;draw();};
function tick(now){const a=current();
 if(ready && playing && prev){t+=Math.min(100,now-prev)*(el('slow').checked ? 0.5 : 1);
 while(t>=a[n].ms){t-=a[n].ms;if(n===a.length-1 && ['lift','land'].includes(el('clip').value)){state(false);t=0;break;}n=(n+1)%a.length;draw();}}
 prev=now;requestAnimationFrame(tick);}
Promise.all([...new Set(imgs)].map(src=>new Promise(resolve=>{const i=new Image();i.onload=resolve;i.onerror=resolve;i.src=src;}))).then(()=>{ready=true;draw();requestAnimationFrame(tick);});
</script></html>"""
    metadata = {n: json.loads((D/"anime"/n/"animation.json").read_text(encoding="utf8")) for n in ("lift","hold","land")}
    (D/"review.html").write_text(page.replace("__METADATA__",json.dumps(metadata,ensure_ascii=False)),encoding="utf8")


def main():
    idle = Image.open(ROOT/"anime/idle/000.png").convert("RGBA")
    ix, _, width = head(idle)
    center = round(ix+32)
    standing = place(idle,32,64)
    cfg = json.loads((D/"docs/timing.json").read_text(encoding="utf8"))
    LAYOUT.update({name: (2,2) for name in cfg.get("bridges",[])})
    LAYOUT.update(cfg.get("atlas_layouts", {}))
    atlases = {name: read_atlas(name) for name in LAYOUT}
    scales, art = {}, {}
    for name, poses in atlases.items():
        scale = cfg.get("scale",{}).get(name,width/float(np.median([head(im)[2] for im in poses])))
        scales[name] = scale
        art[name] = [im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.LANCZOS) for im in poses]

    def pose(name, index, ground, shift=0):
        im = art[name][index]
        x, _, _ = head(im)
        return place(im,round(center-x+shift),ground-opaque_bounds(im)[3])

    contact([pose("keys",i,g) for i,g in enumerate([GROUND,346,318,335,GROUND,GROUND])],D/"key-poses.png",3)
    clips = {}
    report = {"canvas":[SIZE,SIZE],"ground_y":GROUND,"whole_atlas_scale":scales,"clips":{}}
    held = []
    start = cfg["hold"][0][0]
    first = art["hold"][start]
    hanging_head = 318-opaque_bounds(first)[3]+head(first)[1]
    for index, _, shift in cfg["hold"]:
        im = art["hold"][index]
        hx, hy, _ = head(im)
        held.append(place(im,round(center-hx+shift),round(hanging_head-hy)))
    anchor = held[0]
    for name in ("lift","hold","land"):
        frames,times,provenance = [],[],[]
        for entry in cfg[name]:
            index,ms,placement = entry
            if index=="idle":
                f = standing
            elif index=="anchor":
                f = anchor
            elif name=="hold":
                f = held[len(frames)]
            elif isinstance(index,list):
                f = pose(index[0],index[1],placement)
            else:
                f = pose(name,index,placement)
            frames.append(f)
            times.append(ms)
            provenance.append(index)
        out = D/"anime"/name
        out.mkdir(parents=True,exist_ok=True)
        paths = [f"{i:03}.png" for i in range(len(frames))]
        if any(p.name not in paths for p in out.glob("*.png")):
            raise ValueError("Remove retired frame files before rebuilding "+name)
        for path, frame in zip(paths,frames):
            frame.save(out/path)
        meta = {"loop":name=="hold","frame_count":len(frames),"duration_ms":sum(times),
                "frames":[{"file":p,"duration_ms":t,"source_pose":i} for p,t,i in zip(paths,times,provenance)]}
        save(out/"animation.json",meta)
        contact(frames,D/f"{name}-frames.png")
        decoded = gif(D/f"{name}.gif",frames,times,name=="hold")
        clips[name] = (frames,times)
        report["clips"][name] = {"frames":len(frames),"gif_frames":decoded,"duration_ms":sum(times),
                                  "distinct_frames":len({hashlib.sha256(f.tobytes()).digest() for f in frames}),
                                  "bounds":[opaque_bounds(f) for f in frames]}
    assert clips["lift"][0][-1].tobytes()==clips["hold"][0][0].tobytes()==clips["land"][0][0].tobytes()
    assert clips["lift"][0][0].tobytes()==clips["land"][0][-1].tobytes()
    report["exact_stage_endpoints"] = True
    hold_frames = [np.asarray(f,dtype=np.int16) for f in clips["hold"][0]]
    diffs = [round(float(np.abs(a-b).mean()),3) for a,b in zip(hold_frames,hold_frames[1:]+hold_frames[:1])]
    report["hold_seam_pixel_difference"] = diffs[-1]
    report["hold_max_internal_pixel_difference"] = max(diffs[:-1])
    report["hold_hair_vertical_range"] = [min(head(f)[1] for f in clips["hold"][0]),max(head(f)[1] for f in clips["hold"][0])]
    report["source_sha256"] = {
        name + ".png": hashlib.sha256((D/"sources"/(name + ".png")).read_bytes()).hexdigest()
        for name in sorted(atlases)
    }
    report["runtime_integrated"] = False
    frames = clips["lift"][0]+clips["hold"][0]*2+clips["land"][0]
    times = clips["lift"][1]+clips["hold"][1]*2+clips["land"][1]
    gif(D/"preview.gif",frames,times,True)
    save(D/"docs/verification.json",report)
    review_page()
    print({k:(len(f),sum(t)) for k,(f,t) in clips.items()})


if __name__=="__main__":
    main()
