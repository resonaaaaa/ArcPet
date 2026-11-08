"""Finite ending part 1 only: put book down, close eyes, stretch, settle."""
from pathlib import Path
import json,re,subprocess
import numpy as np
from PIL import Image,ImageFilter
import build_pomodoro_pause_v3 as art
from build_pomodoro_start_v3 import contact
from pomodoro_color_calibration import calibrate,material_stats
from pomodoro_scene_helpers import digest,opaque,encode,measurement

ROOT=Path(__file__).resolve().parent.parent
D=ROOT/'design/pomodoro-end-stretch-v1'
READ=ROOT/'design/pomodoro-reading-v3';PAUSE=ROOT/'design/pomodoro-pause-v3'
NAMES=['raise-a','raise-b','release-a','release-b']
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')

def character_scene(scene):
    """Remove authorized prop regions, retain complete raised-arm artwork."""
    a=np.asarray(scene).copy();r,g,b,al=a.astype(int).transpose(2,0,1);yy,xx=np.indices(al.shape)
    keep=(yy<316)&(xx>=70)&(xx<385)
    lamp=(xx<158)&(yy>=240);plant=(xx>=310)&(yy>=263)
    keep&=~(lamp|plant)
    # White sleeve/peach hand that projects in front of the lamp is retained;
    # original lamp is then composited with its actual silhouette, not a box.
    foreground=(r>190)&(g>165)&(b>145)&(g-b<30)&(r-g<55)&(al>100)
    keep|=lamp&foreground&(yy<316)&(xx>=125)
    hands=(r>175)&(r-g>14)&(r-g<75)&(g-b>5)&(g-b<35)&(al>100)&(yy>=275)&(yy<342)&(xx>=145)&(xx<310)
    keep|=np.asarray(Image.fromarray((hands*255).astype('uint8')).filter(ImageFilter.MaxFilter(3)))>0
    keep|=(yy>=354)&(yy<434)&(xx>=169)&(xx<298)
    a[~keep]=0;return Image.fromarray(a)

def main():
    protected=[]
    for folder in [ROOT/'anime',ROOT/'design/pomodoro-v3/anime',PAUSE/'anime',READ/'anime',ROOT/'design/pomodoro-resume-v1/anime']:
        protected+=sorted(p for p in folder.rglob('*') if p.is_file())
    before={str(p.relative_to(ROOT)):digest(p) for p in protected}
    ref=Image.open(READ/'anime/reading/000.png').convert('RGBA')
    art.D=D;batches={};audits={}
    for n in NAMES:batches[n],audits[n]=art.export_scene_atlas(n,ref)
    ids=[(n,i) for n in NAMES for i in range(8)];raw=[batches[n][i] for n,i in ids]
    colored=calibrate(raw,ref,D/'docs/rgb-calibration.json',False,(),method='translation',skin_bottom=342)
    colored=calibrate(colored,ref,D/'docs/rgb-residual.json',False,(),method='translation',skin_bottom=342)
    colored=[art.clean_white_material(f) for f in colored]
    props={n:Image.open(PAUSE/f'layers/{n}.png').convert('RGBA') for n in ['chair','table','lamp','flower','book']}
    stretch=[art.fixed.compose(character_scene(f),props,book=True) for f in colored]
    for roi in [(130,345,315,353),(210,307,246,320)]:
        assert len({f.crop(roi).tobytes() for f in stretch})==1,('Static prop drift',roi)
    # Reuse approved COMPLETE book-closing/placing drawings. No body parts
    # are extracted or repositioned, and the approved original files stay intact.
    book=[ref.copy()]+[Image.open(PAUSE/f'anime/pause/{i:03}.png').convert('RGBA') for i in range(9)]
    frames=book+stretch;times=[120]+[90]*9+[50]*32
    for i in range(4):times[len(book)+16+i]=100 # four distinct drawn breath poses
    sources=[{'kind':'approved reading entry','file':'../pomodoro-reading-v3/anime/reading/000.png'}]
    sources += [{'kind':'approved whole book gesture','file':f'../pomodoro-pause-v3/anime/pause/{i:03}.png'} for i in range(9)]
    sources += [{'kind':'new complete character','atlas':n,'pose':i} for n,i in ids]
    dest=D/'anime/end-stretch';dest.mkdir(parents=True,exist_ok=True)
    for i,f in enumerate(frames):f.save(dest/f'{i:03}.png')
    meta={'label':'结束第一段 · 放书闭眼伸懒腰','loop':False,'canvas':[448,448],
          'frame_count':len(frames),'unique_frame_count':len({f.tobytes() for f in frames}),'new_drawings':32,
          'duration_ms':sum(times),'frames':[{'file':f'{i:03}.png','duration_ms':t,'source':s} for i,(t,s) in enumerate(zip(times,sources))]}
    dump(dest/'animation.json',meta)
    contact(frames,D/'all-frames.png',7,260)
    contact([f.crop((85,65,362,319)) for f in stretch],D/'docs/arms-head.png',4,300)
    contact([f.crop((142,240,312,342)) for f in frames],D/'docs/book-hands.png',7,240)
    for name,bg in [('light','#f7f4ee'),('dark','#242832')]:
        flat=[]
        for f in frames:
            q=Image.new('RGBA',f.size,bg);q.alpha_composite(f);flat.append(q)
        contact(flat,D/f'docs/all-{name}.png',7,240)
    contact([ref]+book[1:4]+book[-2:]+stretch[:4]+stretch[14:18]+stretch[-3:],D/'docs/seams.png',4,336)
    rm=json.loads((READ/'anime/reading/animation.json').read_text(encoding='utf8'))
    rf=[Image.open(READ/'anime/reading'/e['file']).convert('RGBA') for e in rm['frames']]
    rt=[e['duration_ms'] for e in rm['frames']]
    pal=Image.new('RGB',(448*8,448*((len(frames)+len(rf)+7)//8)),'#f7f4ee')
    for i,f in enumerate(frames+rf):pal.paste(opaque(f),(i%8*448,i//8*448))
    pal=pal.quantize(colors=256)
    for name,fs,ts in [('end-stretch',frames,times),('reading-end-stretch',rf+frames,rt+times)]:
        encode(D/f'{name}.gif',fs,ts,pal,False)
        expected=[opaque(f).quantize(palette=pal,dither=Image.Dither.NONE).convert('RGB') for f in fs]
        expected=[f for i,f in enumerate(expected) if i==0 or f.tobytes()!=expected[i-1].tobytes()]
        with Image.open(D/f'{name}.gif') as gif:
            assert gif.n_frames==len(expected)
            for i,f in enumerate(expected):gif.seek(i);assert gif.convert('RGB').tobytes()==f.tobytes()
    clips={'end':[{'src':'anime/end-stretch/'+e['file'],'ms':e['duration_ms'],'label':f'结束(1) {i:02}'} for i,e in enumerate(meta['frames'])],
           'reading':[{'src':'../pomodoro-reading-v3/anime/reading/'+e['file'],'ms':e['duration_ms'],'label':f'已确认阅读 {i:02}'} for i,e in enumerate(rm['frames'])]}
    template=(D/'docs/review-template.html').read_text(encoding='utf8').replace('__CLIPS__',json.dumps(clips))
    (D/'review.html').write_text(template,encoding='utf8')
    script=re.search(r'<script>(.*?)</script>',template,re.S).group(1)
    test=subprocess.run(['node','--check','-'],input=script,text=True,capture_output=True);assert test.returncode==0,test.stderr
    for i,f in enumerate(frames):
        bb=f.getbbox();assert min(bb[0],bb[1],448-bb[2],448-bb[3])>=6,(i,'crop risk',bb)
    assert before=={str(p.relative_to(ROOT)):digest(p) for p in protected}
    assert frames[0].tobytes()==ref.tobytes()
    dump(D/'docs/validation.json',{'scope':'Ending part1 only; artwork pending visual approval','frames':len(frames),'duration_ms':sum(times),
          'new_drawings':32,'dynamic_frame_ms':50,'stretch_breath_hold':'Four distinct drawn poses,100ms each',
          'approved_assets_unchanged':True,'sources':audits,'GIF_decoded_match':True,'JS_syntax':True,
          'diagnostics':[{'i':i,'materials':material_stats(f),'geometry':measurement(f)} for i,f in enumerate(frames)],
          'limits':'Numerical verification does not imply visual approval. Browser playback untested. No tomato/idle return or runtime changes.'})
    print({'frames':len(frames),'new':32,'ms':sum(times),'approved_unchanged':True})
if __name__=='__main__':main()
