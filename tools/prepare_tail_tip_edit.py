"""Lay out existing selected drawings as an image-edit input, without posing them."""
import json
from pathlib import Path
import numpy as np
from PIL import Image,ImageFilter
from animation_geometry import connected_regions
D=Path(__file__).resolve().parent.parent/'design/lying-v1'
m=json.loads((D/'docs/pre-hook-layout.json').read_text(encoding='utf8'))
poses=m['frames'][:m['distinct_drawn_tail_poses']]
sheet=Image.new('RGBA',(1500,1000))
for index,frame in enumerate(poses+[poses[0]]):
    im=Image.open(D/f"sources/tail-lateral-{frame['source_sheet']}.png").convert('RGBA');a=np.array(im)
    x0,y0,x1,y1=frame['source_bounds']
    crop=a[y0-3:y1+3,x0-3:x1+3].copy()
    rs=[r for r in connected_regions(crop[:,:,3]>=128) if r['area']>1000]
    r=max(rs,key=lambda r:r['area']);mask=np.zeros(crop.shape[:2],dtype='uint8')
    for y,l,h in r['runs']:mask[y,l:h]=255
    support=np.array(Image.fromarray(mask).filter(ImageFilter.MaxFilter(3)))>0
    crop[~support]=0;t=Image.fromarray(crop)
    factor=frame['uniform_scale']*1.5
    t=t.resize((round(t.width*factor),round(t.height*factor)),Image.Resampling.LANCZOS)
    mm=np.array(t)[:,:,3]>=128;yy,xx=np.where(mm);bottom=int(yy.max())
    root_x=float(np.median(np.where(mm[bottom-4:bottom+1])[1]))
    sheet.alpha_composite(t,(index%5*300+round(150-root_x),index//5*250+220-bottom))
sheet.save(D/'docs/tail-tip-edit-input.png')
