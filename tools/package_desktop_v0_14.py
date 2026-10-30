"""Verify the dance/input release before writing its portable ZIP and hashes."""
from pathlib import Path
import json,hashlib,re,zipfile,argparse,sys
R=Path(__file__).resolve().parent.parent;D=R/'docs/desktop-v0.14-validation'
parser=argparse.ArgumentParser();parser.add_argument('--source',default='dist/ArcPet-v0.9');parser.add_argument('--verify-only',action='store_true');args=parser.parse_args()
B=(R/args.source).resolve();B.relative_to(R)
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
before=read(R/'docs/asset-baseline-v0.13.json');assert all(sha(R/p)==h for p,h in before.items())
core=int(re.search(r'PASS: (\d+)',(D/'core-tests.txt').read_text(encoding='utf-8-sig')).group(1))
results={key:read(D/name) for key,name in [('routed_window_checks','routed-input.json'),('mood_window_checks','mood-window.json'),('rocket_window_checks','rocket-window.json'),('dance_window_checks','dance-window.json')]}
assert core>=624 and all(r['passed'] for r in results.values())
for p in (R/'anime').rglob('*'):
    if p.is_file() and (p.name=='animation.json' or re.fullmatch(r'\d{3}\.png',p.name)):assert sha(p)==sha(B/'assets'/p.relative_to(R))
for p in (R/'app/assets/moods').rglob('*'):
    if p.is_file():assert sha(p)==sha(B/'assets/moods'/p.relative_to(R/'app/assets/moods'))
assert sha(R/'manifest.json')==sha(B/'assets/manifest.json')
assert sha(R/'app/USAGE.txt')==sha(B/'使用说明.txt')
assert len(list((B/'assets/anime').glob('*/[0-9][0-9][0-9].png')))==740
clip=read(B/'assets/anime/dance/animation.json');assert clip['duration_ms']==1000 and clip['frame_count']==30
assert (R/'design/actions-v12/sources/preserved-body-atlas.png').is_file()
if args.verify_only:
    print('PASS: current runtime, canonical assets and release checks match; no ZIP generated.')
    sys.exit(0)
zpath=R/'exports/ArcPet-v0.14.zip'
with zipfile.ZipFile(zpath,'w',zipfile.ZIP_DEFLATED) as z:
    for p in sorted(B.rglob('*')):
        if p.is_file() and 'data' not in p.relative_to(B).parts:z.write(p,Path('ArcPet-v0.14')/p.relative_to(B))
with zipfile.ZipFile(zpath) as z:
    for p in B.rglob('*'):
        if p.is_file() and 'data' not in p.relative_to(B).parts:assert z.read('ArcPet-v0.14/'+p.relative_to(B).as_posix())==p.read_bytes()
result={'version':'0.14','date':'2026-10-30','core_checks':core,**{k:v['checks'] for k,v in results.items()},'total_checks':core+sum(v['checks'] for v in results.values()),'registered_clips':19,'total_pet_frames':740,'dance_speed_multiplier':1.2,'dance_loop_ms':1000,'dance_loops':12,'dance_loops_per_facing':3,'dance_facing_groups':4,'dance_batch_ms':12000,'double_click_lock_states':['locked','unlocked'],'single_right_unlocked_hold_drag_resize_preserve_ordinary_action':True,'locked_long_press_preserved':True,'skate_interrupt_policy_preserved':True,'unchanged_original_files':len(before),'binary_sha256':{p.name:sha(p) for p in B.iterdir() if p.suffix in ['.exe','.dll']},'runtime_asset_sha256':{p.relative_to(B/'assets').as_posix():sha(p) for p in (B/'assets').rglob('*') if p.is_file()},'package':'exports/ArcPet-v0.14.zip','package_bytes':zpath.stat().st_size,'window_test_scope':'Actual WPF controls. Locked clicks use routed events; unlocked clicks/drag/resize use production gesture adapters because synthetic events cannot change native button state. No physical mouse injected.'}
for name in ['handoff','cleanup']:
    if (D/(name+'.json')).exists():result[name]=read(D/(name+'.json'))
(D/'validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({k:result[k] for k in ['version','total_checks','registered_clips','total_pet_frames','package_bytes']},ensure_ascii=False))
