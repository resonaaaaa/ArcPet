"""Inspect the supplied walking GIF; copy reference and build a review contact sheet."""
from pathlib import Path
import json,shutil
from PIL import Image,ImageDraw
R=Path(__file__).resolve().parent.parent;D=R/'design/walk-v1'
for name in ['reference','sources','anime','docs']:(D/name).mkdir(parents=True,exist_ok=True)
src=Path('C:/Users/24847/Downloads/d1518d2762d0f703b98bd7f54efa513d2697c5a5.gif')
dest=D/'reference/walk.gif'
if not dest.exists():shutil.copyfile(src,dest)
im=Image.open(dest);frames=[];durations=[]
for i in range(im.n_frames):
    im.seek(i);frames.append(im.convert('RGBA').copy());durations.append(im.info.get('duration',100))
cols=min(8,len(frames));rows=(len(frames)+cols-1)//cols
contact=Image.new('RGB',(cols*160,rows*215),'#f7f4ee');draw=ImageDraw.Draw(contact)
for i,f in enumerate(frames):
    f.thumbnail((150,180));bg=Image.new('RGBA',f.size,'white');bg.alpha_composite(f)
    x=i%cols*160;y=i//cols*215;contact.paste(bg.convert('RGB'),(x,y));draw.text((x+5,y+185),f'{i}: {durations[i]}ms',fill='#4b4135')
contact.save(D/'reference/contact.png')
report={'size':im.size,'frames':len(frames),'durations_ms':durations,'loop_ms':sum(durations),
    'role':'motion, expression, rhythm only; existing ArcPet defines outfit and drawing style'}
(D/'docs/reference.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report))
