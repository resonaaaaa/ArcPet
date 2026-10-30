from pathlib import Path
import numpy as np
from PIL import Image,ImageFilter,ImageDraw
from animation_geometry import connected_regions

def extract(source,count,cols,exclude=(), save_keys=False):
    im=Image.open(source).convert('RGBA');a=np.array(im);regs=[r for r in connected_regions(a[:,:,3]>=128) if r['area']>10000]
    assert len(regs)==count,(source,len(regs));regs.sort(key=lambda r:r['bbox'][1]);ordered=[]
    for row in range(count//cols):ordered+=sorted(regs[row*cols:(row+1)*cols],key=lambda r:r['bbox'][0])
    folder=source.parent/(source.stem.replace('-atlas','')+'-keys');keys={};audit=[]
    for i,r in enumerate(ordered):
        b=r['bbox'];edge=min(b[0],b[1],im.width-b[2],im.height-b[3])
        if i in exclude or edge<3:
            audit.append({'key':i,'source_bounds':b,'accepted':False,'reason':'excluded for pose consistency' if edge>=3 else 'source touches atlas boundary'});continue
        mask=np.zeros(a.shape[:2],dtype='uint8')
        for y,x0,x1 in r['runs']:mask[y,x0:x1]=255
        support=np.array(Image.fromarray(mask).filter(ImageFilter.MaxFilter(3)))>0;data=a.copy();data[~support]=0
        box=[b[0]-3,b[1]-3,b[2]+3,b[3]+3];key=Image.fromarray(data).crop(box);
        if save_keys:
            folder.mkdir(exist_ok=True);key.save(folder/f'{i:03}.png')
        keys[i]=key
        audit.append({'key':i,'source_bounds':b,'crop':box,'accepted':True})
    return keys,audit

def locate(image):
    a=np.array(image);rgb=a[:,:,:3].astype(float);m=a[:,:,3]>=128;ys,xs=np.where(m)
    left,top,right,bottom=int(xs.min()),int(ys.min()),int(xs.max())+1,int(ys.max())+1;h=bottom-top;w=right-left
    navy=m&(rgb[:,:,2]>rgb[:,:,0]*1.15)&(rgb[:,:,2]>rgb[:,:,1]*1.05)&(rgb[:,:,0]<125)&(rgb[:,:,1]<140)
    navy[:top+round(h*.5)]=False;navy[top+round(h*.86):]=False;r=connected_regions(navy)[0];area=r['area']
    cx=sum((x0+x1-1)/2*(x1-x0) for y,x0,x1 in r['runs'])/area;cy=sum(y*(x1-x0) for y,x0,x1 in r['runs'])/area
    dark=m&(rgb[:,:,0]<115)&(rgb[:,:,1]<90)&(rgb[:,:,2]<80)
    dark[:top+round(h*.25)]=False;dark[top+round(h*.64):]=False;dark[:,:left+round(w*.22)]=False;dark[:,left+round(w*.94):]=False
    # Fine eyelids can be thinner than three pixels: erosion deletes them.
    # Find two horizontal components with similar width/height instead.
    regions=[r for r in connected_regions(dark) if r['area']>15 and 10<r['bbox'][2]-r['bbox'][0]<w*.35 and r['bbox'][3]-r['bbox'][1]<(r['bbox'][2]-r['bbox'][0])*.3]
    pairs=[]
    for i,a0 in enumerate(regions):
        for a1 in regions[i+1:]:
            b0,b1=a0['bbox'],a1['bbox'];ww=[b0[2]-b0[0],b1[2]-b1[0]]
            c0=np.array([(b0[0]+b0[2])/2,(b0[1]+b0[3])/2]);c1=np.array([(b1[0]+b1[2])/2,(b1[1]+b1[3])/2])
            if max(ww)/min(ww)<1.6 and w*.15<abs(c0[0]-c1[0])<w*.42 and abs(c0[1]-c1[1])<h*.045:
                pairs.append((a0['area']+a1['area'],[c0.tolist(),c1.tolist()]))
    assert pairs,'No plausible eyelid pair'
    eyes=sorted(max(pairs,key=lambda p:p[0])[1])
    delta=np.array(eyes[1])-eyes[0];eye=np.mean(eyes,axis=0)
    boots=m&(rgb[:,:,0]<125)&(rgb[:,:,1]<110)&(rgb[:,:,2]<105);boots[:top+round(h*.76)]=False;by,bx=np.where(boots)
    return {'hip':[cx,cy],'eyes':eyes,'eye_distance':float(np.linalg.norm(delta)),
            'eye_angle':float(np.degrees(np.arctan2(delta[1],delta[0]))),
            'lean':float(np.degrees(np.arctan2(eye[0]-cx,cy-eye[1]))),
            'ground_y':int(by.max())+1,'bounds':[left,top,right,bottom],'height':h}

def feet_center(im):
    a=np.array(im);m=a[:,:,3]>=128;y,x=np.where(m);top,bottom=y.min(),y.max()+1
    brown=m&(a[:,:,0]<140)&(a[:,:,1]<125)&(a[:,:,2]<120)&(a[:,:,0].astype(int)+5>=a[:,:,2])
    brown[:round(top+(bottom-top)*.74)]=False
    yy,xx=np.where(brown)
    return (int(xx.min())+int(xx.max()))/2,int(yy.max())+1

def contact(frames,path,cols=6):
    out=Image.new('RGB',(cols*200,((len(frames)+cols-1)//cols)*222),'#f7f4ee');dr=ImageDraw.Draw(out)
    for i,f in enumerate(frames):
        f=f.resize((200,200),Image.Resampling.LANCZOS);x=i%cols*200;y=i//cols*222;out.paste(f,(x,y),f);dr.text((x+8,y+202),str(i),fill='#574734')
    out.save(path)

