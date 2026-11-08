"""Package only the verified portable runtime; never include local preferences."""
from pathlib import Path
import hashlib,json,zipfile

ROOT=Path(__file__).resolve().parent.parent
BUILD=ROOT/'dist/ArcPet-v0.9'
REPORT=ROOT/'docs/desktop-v0.20.1-validation'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def read(p):return json.loads(p.read_text('utf-8-sig'))
proof=read(REPORT/'validation.json')
assert proof['version']=='0.20.1' and proof['total_checks']==1376
for name,h in proof['binary_sha256'].items():assert sha(BUILD/name)==h,name
for name,h in proof['runtime_asset_sha256'].items():assert sha(BUILD/'assets'/name)==h,name
assert sha(ROOT/'manifest.json')==sha(BUILD/'assets/manifest.json')
assert sha(ROOT/'app/USAGE.txt')==sha(BUILD/'使用说明.txt')
files=[BUILD/name for name in proof['binary_sha256']]
files+=sorted(p for p in (BUILD/'assets').rglob('*') if p.is_file())
assert len(files)==len(proof['binary_sha256'])+len(proof['runtime_asset_sha256'])
dest=ROOT/'exports/ArcPet-v0.20.1.zip';dest.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(dest,'w',zipfile.ZIP_DEFLATED) as archive:
    for p in files:archive.write(p,'ArcPet-v0.20.1/'+p.relative_to(BUILD).as_posix())
with zipfile.ZipFile(dest) as archive:
    assert archive.testzip() is None
    for p in files:assert archive.read('ArcPet-v0.20.1/'+p.relative_to(BUILD).as_posix())==p.read_bytes()
    assert all('/data/' not in name and 'InputPreview' not in name for name in archive.namelist())
result={'version':'0.20.1','files':len(files),'bytes':dest.stat().st_size,'sha256':sha(dest),
        'package':'exports/'+dest.name,'settings_excluded':True,'verified_against_tested_build':True}
(REPORT/'package.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
(dest.parent/'ArcPet-v0.20.1-SHA256.txt').write_text(result['sha256']+'  '+dest.name+'\n',encoding='utf8')
print(json.dumps(result,ensure_ascii=False))
