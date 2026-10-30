"""Extract one intact character and reject separate neighboring frame fragments."""
from PIL import Image, ImageFilter
import numpy as np

def connected_regions(mask):
    parent=[]; runs=[]; previous=[]
    def find(i):
        while parent[i]!=i:
            parent[i]=parent[parent[i]]
            i=parent[i]
        return i
    def join(a,b):
        a,b=find(a),find(b)
        if a!=b:parent[b]=a
    for y,row in enumerate(mask):
        changes=np.flatnonzero(np.diff(np.r_[False,row,False]))
        current=[]
        for j in range(0,len(changes),2):
            x0,x1=int(changes[j]),int(changes[j+1])
            k=len(parent);parent.append(k)
            for a,b,p in previous:
                if b<x0:continue
                if a>x1:break
                join(k,p)
            current.append((x0,x1,k))
            runs.append((y,x0,x1,k))
        previous=current
    regions={}
    for y,x0,x1,k in runs:
        root=find(k)
        if root not in regions:regions[root]={'area':0,'bbox':[x0,y,x1,y+1],'runs':[]}
        r=regions[root];r['area']+=x1-x0
        b=r['bbox'];b[:]=[min(b[0],x0),min(b[1],y),max(b[2],x1),max(b[3],y+1)]
        r['runs'].append((y,x0,x1))
    return sorted(regions.values(),key=lambda r:r['area'],reverse=True)

def isolate_character(image,threshold=64,padding=2,with_report=False):
    image=image.convert('RGBA')
    rgba=np.array(image)
    regions=connected_regions(rgba[:,:,3]>=threshold)
    if not regions:raise ValueError('Empty character image')
    main=regions[0]
    total=sum(r['area'] for r in regions)
    if main['area']/total<.80:
        raise ValueError('Ambiguous disconnected artwork; requires visual inspection')
    keep=np.zeros(rgba.shape[:2],dtype=np.uint8)
    for y,x0,x1 in main['runs']:keep[y,x0:x1]=255
    # Include the source's antialiased edge; never include a second opaque object.
    support=np.array(Image.fromarray(keep,'L').filter(ImageFilter.MaxFilter(3)))>0
    for other in regions[1:]:
        for y,x0,x1 in other['runs']:support[y,x0:x1]=False
    rgba[~support]=0
    b=main['bbox'];x0=max(0,b[0]-padding);y0=max(0,b[1]-padding)
    x1=min(image.width,b[2]+padding);y1=min(image.height,b[3]+padding)
    result=Image.fromarray(rgba,'RGBA').crop((x0,y0,x1,y1))
    report={'original_size':list(image.size),'crop_box':[x0,y0,x1,y1],
            'main_area':main['area'],'main_bbox':main['bbox'],
            'removed_opaque_pixels':sum(r['area'] for r in regions[1:]),
            'removed_regions':[{'area':r['area'],'bbox':r['bbox']} for r in regions[1:]]}
    return (result,report) if with_report else result

