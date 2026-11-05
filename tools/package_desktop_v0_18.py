"""Verify the drag-lift runtime and optionally create a portable ZIP."""
from pathlib import Path
import argparse,hashlib,json,re,zipfile

R=Path(__file__).resolve().parent.parent
D=R/'docs/desktop-v0.18-validation'
parser=argparse.ArgumentParser()
parser.add_argument('--source',default='dist/ArcPet-v0.9')
parser.add_argument('--verify-only',action='store_true')
args=parser.parse_args();B=(R/args.source).resolve();B.relative_to(R)
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
before=read(R/'docs/asset-baseline-v0.16.json')
assert all(sha(R/p)==h for p,h in before.items()),'An existing animation asset changed'
core=int(re.search(r'PASS: (\d+)',(D/'core-tests.txt').read_text(encoding='utf-8-sig')).group(1))
names={'routed_window_checks':'routed-input','mood_window_checks':'mood-window','rocket_window_checks':'rocket-window',
    'dance_window_checks':'dance-window','sleep_variant_window_checks':'sleep-variant-window','travel_window_checks':'travel-window'}
results={key:read(D/(name+'.json')) for key,name in names.items()}
assert core>=823 and all(x['passed'] for x in results.values()) and results['travel_window_checks']['checks']>=136
manifest=read(R/'manifest.json');assert len(manifest['actions'])==25
assert sum(a['frame_count'] for a in manifest['actions'].values())==891
assert sha(R/'manifest.json')==sha(B/'assets/manifest.json')
assert sha(R/'app/USAGE.txt')==sha(B/'使用说明.txt')
for p in (R/'anime').rglob('*'):
    if p.is_file() and (p.name=='animation.json' or re.fullmatch(r'\d{3}\.png',p.name)):
        assert sha(p)==sha(B/'assets'/p.relative_to(R)),str(p)
for p in (R/'app/assets/moods').rglob('*'):
    if p.is_file():assert sha(p)==sha(B/'assets/moods'/p.relative_to(R/'app/assets/moods'))
assert len(list((B/'assets/anime').glob('*/[0-9][0-9][0-9].png')))==891
for direction in ['left','right']:
    clip=read(B/f'assets/anime/walk_{direction}/animation.json')
    assert clip['duration_ms']==1200 and clip['frame_count']==30 and clip['handoff_frames']==[0,15]
import_record=read(R/'docs/desktop-v0.17-validation/walk-import.json')
for name,h in import_record['source_sha256'].items():assert sha(R/'design/walk-v4/anime'/name)==h
for phase in ('lift','hold','land'):
    for frame in (R/'design/drag-lift-v1/anime'/phase).glob('*.png'):
        assert sha(frame)==sha(B/'assets/anime'/('drag_'+phase)/frame.name)
zpath=R/'exports/ArcPet-v0.18.zip'
if not args.verify_only:
    zpath.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(zpath,'w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(B.rglob('*')):
            if p.is_file() and 'data' not in p.relative_to(B).parts:z.write(p,Path('ArcPet-v0.18')/p.relative_to(B))
    with zipfile.ZipFile(zpath) as z:
        for p in B.rglob('*'):
            if p.is_file() and 'data' not in p.relative_to(B).parts:assert z.read('ArcPet-v0.18/'+p.relative_to(B).as_posix())==p.read_bytes()
result={'version':'0.18','date':'2026-11-05','core_checks':core,**{key:x['checks'] for key,x in results.items()},
    'total_checks':core+sum(x['checks'] for x in results.values()),'registered_clips':25,'total_pet_frames':891,
    'travel_style_probability':{'skateboard':'1/2','walk':'1/2'},'movement_pool_slots':1,'walk_loop_ms':1200,
    'drag_hold_threshold_ms':500,
    'travel_any_input_stop':True,'ordinary_action_gesture_policy_preserved':True,'sleep_waits_for_complete_trip':True,
    'unchanged_previous_asset_files':len(before),'binary_sha256':{p.name:sha(p) for p in B.iterdir() if p.suffix in ['.exe','.dll']},
    'runtime_asset_sha256':{p.relative_to(B/'assets').as_posix():sha(p) for p in (B/'assets').rglob('*') if p.is_file()},
    'package':None if args.verify_only else 'exports/ArcPet-v0.18.zip',
    'package_sha256':None if args.verify_only else sha(zpath),
    'window_test_scope':'Actual WPF controls; walking-specific trips use the test clock. Synthetic routed clicks and existing production drag adapters. No physical mouse injected.'}
for name in ['handoff','cleanup']:
    if (D/(name+'.json')).exists():result[name]=read(D/(name+'.json'))
(D/'validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({key:result[key] for key in ['version','total_checks','registered_clips','total_pet_frames','package']},ensure_ascii=False))
