"""Verify frame counts, alpha bounds, accidental extra objects and shared poses."""
from pathlib import Path
import json,hashlib
from PIL import Image,ImageOps
import numpy as np
from animation_geometry import connected_regions
ROOT=Path(__file__).resolve().parent.parent
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
report={}
referenced={}
for action,item in manifest['actions'].items():
    clip_path=ROOT/item['path']
    folder=clip_path.parent
    expected={'idle':30,'sleep':30,'think':60,'icecream':30,'click_down':30,'click_hold':30,'click_release':30,'music':32,'exercise':224,'skateboard_left':34,'skateboard_right':34,'calling':40,'rocket_charge':9,'rocket_hold':9,'rocket_launch':6,'rocket_flight':7,'rocket_land':45,'dance':30,'dance_mirrored':30}
    expected['lying']=36
    expected.update(walk_left=30,walk_right=30)
    expected.update(drag_lift=18,drag_hold=16,drag_land=21)
    expected.update(pomodoro_start=32,pomodoro_read=5,pomodoro_read_blink=8,pomodoro_read_page=23,
                    pomodoro_pause=16,pomodoro_paused=38,pomodoro_resume=34,pomodoro_stretch=42,pomodoro_end=65)
    metadata=json.loads(clip_path.read_text(encoding='utf-8'))
    files=[folder/f['file'] for f in metadata['frames']]
    referenced.setdefault(folder,set()).update(files)
    assert len(files)==item['frame_count']==expected[action],(action,len(files))
    assert metadata['frame_count']==len(files) and sum(f['duration_ms'] for f in metadata['frames'])==metadata['duration_ms']==item['duration_ms']
    heights=[];foot=[];tops=[];extra=[]
    for i,p in enumerate(files):
        im=Image.open(p)
        expected_size=(384,384) if action.startswith(('drag_','pomodoro_')) else (320,440) if action.startswith('rocket_') else (320,320)
        assert im.size==expected_size and im.mode=='RGBA'
        rgba=np.array(im)
        regions=connected_regions(rgba[:,:,3]>64)
        b=regions[0]['bbox']
        if action!='pomodoro_end':
            assert b[0]>0 and b[1]>0 and b[2]<im.width and b[3]<im.height,(action,i,b)
        visible_extra=[{'area':r['area'],'bbox':r['bbox']} for r in regions[1:] if r['area']>8]
        if not action.startswith('pomodoro_'):
            assert not visible_extra,(action,i,visible_extra)
        heights.append(b[3]-b[1]);foot.append(b[3]);tops.append(b[1])
    if action in ['idle','sleep','think','icecream','click_hold']:
        assert max(heights)-min(heights)<=1,(action,heights)
        assert max(foot)-min(foot)<=1,(action,foot)
    if action in ['music','exercise','skateboard_left','skateboard_right']:
        assert max(foot)-min(foot)<=1
        assert max(abs(a-b) for a,b in zip(heights,heights[1:]))<=5
        assert np.array_equal(np.array(Image.open(files[0])),np.array(Image.open(files[-1])))
        for index in metadata.get('handoff_frames',[]):
            assert np.array_equal(np.array(Image.open(files[index])),np.array(Image.open(files[0])))
    if action in ['calling','rocket_hold','rocket_flight']:
        assert np.array_equal(np.array(Image.open(files[0])),np.array(Image.open(files[-1])))
    report[action]={'frame_count':len(files),'height_range':[min(heights),max(heights)],
                    'top_range':[min(tops),max(tops)],'ground_range':[min(foot),max(foot)],
                    'foreign_objects':0 if not action.startswith('pomodoro_') else 'scene contains authorized separate props/effects'}
    if action=='lying':
        assert max(foot)==min(foot)==292
        assert metadata['duration_ms']==1600
    if action in ['walk_left','walk_right']:
        assert metadata['duration_ms']==1200 and metadata['handoff_frames']==[0,15]
        assert max(foot)-min(foot)<=2
def same(a,b):
    return np.array_equal(np.array(Image.open(ROOT/a)),np.array(Image.open(ROOT/b)))
assert same('anime/drag_lift/017.png','anime/drag_hold/000.png')
assert same('anime/drag_hold/000.png','anime/drag_land/000.png')
assert same('anime/drag_lift/000.png','anime/drag_land/020.png')
idle=Image.open(ROOT/'anime/idle/000.png').convert('RGBA')
assert np.array_equal(np.asarray(idle.convert('RGBa')),
                      np.asarray(Image.open(ROOT/'anime/drag_lift/000.png').crop((32,64,352,384)).convert('RGBa')))
assert same('anime/idle/000.png','anime/click_down/000.png')
assert same('anime/click_down/029.png','anime/click_hold/000.png')
assert same('anime/click_hold/000.png','anime/click_release/000.png')
assert same('anime/click_release/029.png','anime/idle/000.png')
report['shared_endpoints_match']=True
report['total_frames']=sum(item['frame_count'] for item in manifest['actions'].values())
assert report['total_frames']==sum(expected[action] for action in manifest['actions'])
for folder,files in referenced.items():
    assert set(folder.glob('[0-9][0-9][0-9].png'))==files,folder
report['unique_png_files']=sum(map(len,referenced.values()))
if 'skateboard_left' in manifest['actions']:
    for i in range(34):
        mirrored=ImageOps.mirror(Image.open(ROOT/f'anime/skateboard_left/{i:03}.png').convert('RGBA'))
        test=Image.new('RGBA',(320,320));test.alpha_composite(mirrored,(-40,0))
        assert np.array_equal(np.array(test),np.array(Image.open(ROOT/f'anime/skateboard_right/{i:03}.png')))
    report['right_skate_exact_reflection']=True
if 'walk_right' in manifest['actions']:
    for i in range(30):
        mirrored=ImageOps.mirror(Image.open(ROOT/f'anime/walk_right/{i:03}.png').convert('RGBA'))
        positioned=Image.new('RGBA',(320,320));positioned.alpha_composite(mirrored,(-50,0))
        assert np.array_equal(np.array(positioned),np.array(Image.open(ROOT/f'anime/walk_left/{i:03}.png')))
    report['walk_exact_reflection']=True
for a,b in [('rocket_charge','rocket_hold'),('rocket_hold','rocket_launch'),('rocket_launch','rocket_flight'),('rocket_flight','rocket_land')]:
    aa=manifest['actions'][a]['frame_count']-1
    assert same(f'anime/{a}/{aa:03}.png',f'anime/{b}/000.png')
assert np.array_equal(np.array(Image.open(ROOT/'anime/rocket_charge/000.png'))[:320],np.array(Image.open(ROOT/'anime/click_hold/000.png')))
assert np.array_equal(np.array(Image.open(ROOT/'anime/rocket_land/044.png'))[:320],np.array(Image.open(ROOT/'anime/idle/000.png')))
report['rocket_exact_cross_clip_endpoints']=True
think=[np.array(Image.open(ROOT/f'anime/think/{i:03}.png')) for i in range(60)]
assert all(np.array_equal(think[i],think[59-i]) for i in range(30))
assert not np.array_equal(think[29],think[0]),'The forward endpoint must not be overwritten with the start'
assert np.array_equal(think[-1],think[0])
d0=float(np.mean(np.abs(think[1].astype(float)-think[0].astype(float))))
d1=float(np.mean(np.abs(think[-1].astype(float)-think[-2].astype(float))))
assert abs(d0-d1)<1e-9
clip=json.loads((ROOT/'anime/think/animation.json').read_text(encoding='utf-8'))
assert clip['duration_ms']==6000 and all(f['duration_ms']==100 for f in clip['frames'])
report['thinking_loop']={'playback_frames':60,'duration_ms':6000,'seam_pixel_difference':0,'first_and_final_step_difference':abs(d0-d1),'return_path_complete':True}
ice=[np.array(Image.open(ROOT/f'anime/icecream/{i:03}.png'),dtype=float) for i in range(30)]
assert all(np.array_equal(ice[24+j],ice[5-j]) for j in range(6))
assert np.array_equal(ice[-1],ice[0])
steps=[float(np.abs(a-b).mean()) for a,b in zip(ice,ice[1:])]
normal_max=max(steps[:23])
assert max(steps[23:])<=normal_max,('icecream return jump',steps[23:],normal_max)
assert abs(steps[-1]-steps[0])<1e-9
clip=json.loads((ROOT/'anime/icecream/animation.json').read_text(encoding='utf-8'))
assert clip['duration_ms']==2700 and all(f['duration_ms']==90 for f in clip['frames'])
report['icecream_loop']={'playback_frames':30,'duration_ms':2700,'seam_pixel_difference':0,'return_transition_mean_rgba_difference':steps[23],'normal_forward_max_difference':normal_max,'return_path_complete':True,'first_and_final_step_difference':abs(steps[-1]-steps[0])}
(ROOT/'docs/crop-repair-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'verified_clips':len(manifest['actions']),'verified_frames':report['unique_png_files'],'shared_endpoints_match':True},ensure_ascii=False))


