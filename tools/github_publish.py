"""GitHub repository/release operations using GCM credentials only in memory."""
from pathlib import Path
import argparse,json,os,subprocess,urllib.request,urllib.error,hashlib
R=Path(__file__).resolve().parent.parent;STATE=R/'docs/github-publish.json'
p=argparse.ArgumentParser();p.add_argument('action',choices=['create','release','publish','verify']);args=p.parse_args()
env=os.environ.copy();env.update(GIT_TERMINAL_PROMPT='0',GCM_INTERACTIVE='never')
c=subprocess.run(['git','credential','fill'],input='protocol=https\nhost=github.com\n\n',text=True,capture_output=True,env=env)
if c.returncode:raise SystemExit('GitHub credentials unavailable')
token=dict(x.split('=',1) for x in c.stdout.splitlines() if '=' in x).get('password')
if not token:raise SystemExit('GitHub credentials unavailable')
def api(path,method='GET',data=None,raw=None):
    url=path if path.startswith('https://') else 'https://api.github.com'+path
    headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','User-Agent':'ArcPet-release','X-GitHub-Api-Version':'2022-11-28'}
    body=raw if raw is not None else json.dumps(data).encode() if data is not None else None
    if body is not None:headers['Content-Type']='application/zip' if raw is not None else 'application/json'
    request=urllib.request.Request(url,data=body,headers=headers,method=method)
    try:
        with urllib.request.urlopen(request,timeout=180) as response:return json.load(response)
    except urllib.error.HTTPError as e:raise RuntimeError('GitHub HTTP '+str(e.code)+' for '+method+' '+url) from None
def save(state):STATE.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf8')
if args.action=='create':
    account=api('/user')['login']
    if account!='resonaaaaa':raise SystemExit('Authenticated account does not match selected account')
    if STATE.exists():
        state=json.loads(STATE.read_text(encoding='utf8'));repo=api('/repos/'+state['full_name']);assert repo['id']==state['repository_id'] and repo['private']
    else:
        # Never overwrite or adopt an unrelated existing repository.
        name='ArcPet'
        try:
            api('/repos/'+account+'/'+name)
        except RuntimeError as e:
            if 'HTTP 404 ' not in str(e):raise
        else:raise SystemExit('ArcPet already exists; select a new repository name')
        repo=api('/user/repos','POST',{'name':name,'private':True,'description':'Interactive Windows Neco-Arc desktop pet built with C# and WPF','auto_init':False})
        state={'full_name':repo['full_name'],'repository_id':repo['id'],'private':repo['private'],'repository_url':repo['html_url'],'clone_url':repo['clone_url']};save(state)
    print(json.dumps(state,ensure_ascii=False));raise SystemExit()
state=json.loads(STATE.read_text(encoding='utf8'));base='/repos/'+state['full_name']
if args.action=='release':
    commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();assert api(base+'/git/ref/tags/v0.14')['object']['sha']==commit
    release=api(base+'/releases','POST',{'tag_name':'v0.14','target_commitish':commit,'name':'ArcPet v0.14 — 每三轮镜像跳舞','body':(R/'docs/RELEASE-v0.14.md').read_text(encoding='utf8'),'draft':True,'prerelease':False})
    state.update(release_id=release['id'],release_url=state['repository_url']+'/releases/tag/v0.14',commit=commit);save(state)
    f=R/'exports/ArcPet-v0.14.zip';asset=api(release['upload_url'].split('{')[0]+'?name='+f.name,'POST',raw=f.read_bytes())
    state.update(asset_id=asset['id'],asset_bytes=asset['size'],asset_sha256=hashlib.sha256(f.read_bytes()).hexdigest());save(state)
    assert asset['size']==f.stat().st_size
    if asset.get('digest'):assert asset['digest']=='sha256:'+state['asset_sha256']
    print(json.dumps({'draft_uploaded':True,'bytes':asset['size'],'release_url':state['release_url']}))
elif args.action=='publish':
    release=api(base+'/releases/'+str(state['release_id']),'PATCH',{'draft':False,'make_latest':'true'})
    state.update(release_url=release['html_url'],published=not release['draft']);save(state);print(json.dumps({'published':state['published'],'release_url':state['release_url']}))
else:
    repo=api(base);release=api(base+'/releases/tags/v0.14');head=api(base+'/git/ref/heads/main')['object']['sha']
    assert repo['private'] and not release['draft'] and head==state['commit']
    assets=release['assets'];asset=next(a for a in assets if a['id']==state['asset_id']);assert asset['size']==state['asset_bytes'] and asset['state']=='uploaded'
    if asset.get('digest'):assert asset['digest']=='sha256:'+state['asset_sha256']
    state.update(verified=True,download_url=asset['browser_download_url']);save(state);print(json.dumps({'repository':repo['html_url'],'private':repo['private'],'release':release['html_url'],'asset_bytes':asset['size'],'verified':True}))
