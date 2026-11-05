"""Inspect the isolated rear boot while support boot is on the right."""
from pathlib import Path
import json,numpy as np
from PIL import Image
from animation_geometry import connected_regions
R=Path(__file__).resolve().parent.parent;D=R/'design/walk-v4'
trace=json.loads((D/'anime/animation.json').read_text(encoding='utf8'))['frames']
out=[]
for i in [*range(7),*range(len(trace)//2,len(trace)//2+7)]:
    a=np.array(Image.open(D/f'anime/{i:03}.png'));rgb=a[:,:,:3].astype(int)
    mask=(a[:,:,3]>128)&(rgb[:,:,0]>rgb[:,:,1]+5)&(rgb[:,:,1]>rgb[:,:,2]+2)&(rgb[:,:,0]<160)
    mask[:230]=False
    regions=[r for r in connected_regions(mask) if r['area']>30]
    centers=[]
    for r in regions:
        x=sum((x0+x1-1)/2*(x1-x0) for y,x0,x1 in r['runs'])/r['area']
        y=sum(y*(x1-x0) for y,x0,x1 in r['runs'])/r['area']
        if x<150:centers.append((r['area'],round(x,2),round(y,2),r['bbox']))
    out.append({'index':i,'source':[trace[i]['source_sheet'],trace[i]['source_pose']],
                'rear_boot_regions':sorted(centers,reverse=True)[:2]})
(D/'docs/early-gait-diagnostic.json').write_text(json.dumps(out,indent=2),encoding='utf8')
print(json.dumps(out))
