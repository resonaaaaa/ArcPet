"""Publish v0.20.1 using existing GCM credentials in memory only."""
from pathlib import Path
import argparse,hashlib,json,os,subprocess,urllib.request,urllib.error

ROOT=Path(__file__).resolve().parent.parent
BASE='/repos/resonaaaaa/ArcPet';TAG='v0.20.1';STATE=ROOT/'docs/github-release-v0.20.1.json'
parser=argparse.ArgumentParser();parser.add_argument('action',choices=['inspect','draft','publish','verify']);args=parser.parse_args()
env=dict(os.environ,GIT_TERMINAL_PROMPT='0',GCM_INTERACTIVE='never')
credential=subprocess.run(['git','credential','fill'],input='protocol=https\nhost=github.com\n\n',text=True,capture_output=True,env=env)
if credential.returncode:raise SystemExit('Existing GitHub credentials unavailable')
token=dict(line.split('=',1) for line in credential.stdout.splitlines() if '=' in line).get('password')
if not token:raise SystemExit('Existing GitHub credentials unavailable')
def api(path,method='GET',data=None,raw=None,content_type=None):
    url=path if path.startswith('https://') else 'https://api.github.com'+path
    headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','User-Agent':'ArcPet-release','X-GitHub-Api-Version':'2022-11-28'}
    body=raw if raw is not None else json.dumps(data).encode('utf8') if data is not None else None
    if body is not None:headers['Content-Type']=content_type or 'application/json'
    try:
        with urllib.request.urlopen(urllib.request.Request(url,data=body,headers=headers,method=method),timeout=180) as response:return json.load(response)
    except urllib.error.HTTPError as e:raise RuntimeError('GitHub HTTP '+str(e.code)+' '+method+' '+url) from None
def save(v):STATE.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf8')
repo=api(BASE);assert repo['id']==1439618787 and repo['private'] is False
if args.action=='inspect':
    print(json.dumps({'repository':repo['html_url'],'visibility':repo['visibility'],
        'recent_releases':[{'tag':r['tag_name'],'draft':r['draft']} for r in api(BASE+'/releases?per_page=5')]}));raise SystemExit()
commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
assert api(BASE+'/git/ref/heads/main')['object']['sha']==commit,'Push main first'
assert api(BASE+'/git/ref/tags/'+TAG)['object']['sha']==commit,'Push the lightweight release tag first'
if args.action=='draft':
    try:release=api(BASE+'/releases/tags/'+TAG)
    except RuntimeError as e:
        if 'HTTP 404 ' not in str(e):raise
        release=api(BASE+'/releases','POST',{'tag_name':TAG,'target_commitish':commit,
            'name':'ArcPet v0.20.1 — 番茄钟与像素时间气泡','body':(ROOT/'docs/RELEASE-v0.20.1.md').read_text('utf8'),
            'draft':True,'prerelease':False})
    assert release['draft'],'Existing public release must not be overwritten'
    state={'repository':repo['html_url'],'visibility':'public','commit':commit,'tag':TAG,'release_id':release['id'],'assets':[]}
    existing={a['name']:a for a in release['assets']}
    for name,kind in [('ArcPet-v0.20.1.zip','application/zip'),('ArcPet-v0.20.1-SHA256.txt','text/plain')]:
        p=ROOT/'exports'/name;digest=hashlib.sha256(p.read_bytes()).hexdigest()
        asset=existing.get(name)
        if asset is None:asset=api(release['upload_url'].split('{')[0]+'?name='+name,'POST',raw=p.read_bytes(),content_type=kind)
        assert asset['size']==p.stat().st_size and asset['state']=='uploaded'
        assert asset.get('digest')=='sha256:'+digest,'Uploaded content digest must match'
        state['assets'].append({'name':name,'id':asset['id'],'bytes':asset['size'],'sha256':digest,'url':asset['browser_download_url']})
    save(state);print(json.dumps({'draft_verified':True,'assets':len(state['assets'])}));raise SystemExit()
state=json.loads(STATE.read_text('utf8'));assert state['commit']==commit
if args.action=='publish':
    release=api(BASE+'/releases/'+str(state['release_id']),'PATCH',{'draft':False,'make_latest':'true'})
else:release=api(BASE+'/releases/tags/'+TAG)
assert release['id']==state['release_id'] and not release['draft'] and not release['prerelease']
assets={a['id']:a for a in release['assets']}
for item in state['assets']:
    asset=assets[item['id']];assert asset['size']==item['bytes'] and asset['digest']=='sha256:'+item['sha256'] and asset['state']=='uploaded'
state.update(published=True,verified=True,release_url=release['html_url']);save(state)
print(json.dumps({'repository':repo['html_url'],'visibility':'public','release':release['html_url'],'verified':True,'assets':len(assets)}))
