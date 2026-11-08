"""Uniform grid registration of COMPLETE cats, never character-part transforms."""
from pathlib import Path
import numpy as np
from PIL import Image
from build_pomodoro_start_v3 import extract
from pomodoro_scene_helpers import digest


def export_cat_atlas(path,rows=2,cols=4):
    path=Path(path);sheet=Image.open(path).convert('RGBA')
    assert abs(sheet.width/sheet.height-cols/rows)<.03,('Unexpected grid ratio',sheet.size,rows,cols)
    drawings,crops=extract(path,rows,cols)
    cell=sheet.width/cols;factor=448/cell
    frames=[]
    for i,(drawing,info) in enumerate(zip(drawings,crops)):
        # Whole-image crop is restored to its original atlas-cell coordinates.
        # The same uniform scene scale applies to every frame in this sheet.
        xy=(round((info['box'][0]-i%cols*cell)*factor),round((info['box'][1]-i//cols*sheet.height/rows)*factor))
        scaled=drawing.resize((round(drawing.width*factor),round(drawing.height*factor)),Image.Resampling.LANCZOS)
        out=Image.new('RGBA',(448,448));out.alpha_composite(scaled,xy)
        assert out.getbbox()[3]<445,(path,i,'bottom crop risk')
        frames.append(out)
    bottoms=[int(np.where(np.asarray(f)[:,:,3]>128)[0].max()+1) for f in frames]
    offset=int(round(424-float(np.median(bottoms))))
    if offset:
        registered=[]
        for f in frames:
            out=Image.new('RGBA',(448,448));out.alpha_composite(f,(0,offset));registered.append(out)
        frames=registered
    return frames,{'source':str(path),'sha256':digest(path),'uniform_scene_scale':factor,'fixed_grid':list(sheet.size),
                   'whole_atlas_translation':[0,offset],'opaque_foot_bottom_before':bottoms,'crops':crops}


def repeated_layout(frame,path):
    out=Image.new('RGBA',(1792,896))
    for i in range(8):out.alpha_composite(frame,(i%4*448,i//4*448))
    out.save(path)
