#!/usr/bin/env python3
"""Configure existing fleet seats without changing their actor credentials or starting them.
Run as root with the deploying actor's PI_FACTORY_TOKEN (or --token-stdin).
The private notebook is outside work/config and survives the ordinary pi-reset script.
"""
import argparse, json, os, pathlib, pwd, sys, urllib.request

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--seats',default='1,2,3,4,5,6,7,8')
p.add_argument('--kb',default='http://192.168.8.81:8081')
p.add_argument('--trigger',type=int,default=50000)
p.add_argument('--keep',type=int,default=20000)
p.add_argument('--target',type=int,default=25000,help='whole context a compaction leaves behind')
p.add_argument('--token-stdin',action='store_true')
a=p.parse_args()
if os.geteuid()!=0: raise SystemExit('Root deployment access required')
if not 8000<=a.keep<a.trigger<=90000: raise SystemExit('Expected 8000 <= keep < trigger <= 90000')
if not 8000<=a.target<a.trigger: raise SystemExit('Expected 8000 <= target < trigger')
token=sys.stdin.read().strip().lstrip('\ufeff') if a.token_stdin else os.environ.get('PI_FACTORY_TOKEN')
if not token: raise SystemExit('Deploying actor credential required')
def get(path):
    with urllib.request.urlopen(urllib.request.Request(a.kb+path,headers={'Authorization':'Bearer '+token}),timeout=20) as r: return json.load(r)
actor=get('/whoami')['slug']; roles=get('/roles')
# Who may start a coder's task with /fresh-task: the team lead's current holders, the merge owners and the owner.
fresh_from=sorted(set(roles.get('team-lead',[]))|set(roles.get('agent-maker',[]))|{'digital-scribe'})
if not any(actor in roles.get(r,[]) for r in ['agent-maker','admin','factory-admin']): raise SystemExit('Deployment role required')
for n in [int(v) for v in a.seats.split(',')]:
    if not 1<=n<=8: raise SystemExit('Seat must be 1..8')
    user=pwd.getpwnam(f'coder-06-pi{n}'); home=pathlib.Path(user.pw_dir); config=home/'.config/pi-sharp'
    def write(path,data):
        if path.exists() and not path.with_suffix(path.suffix+'.pre-notebook').exists():
            backup=path.with_suffix(path.suffix+'.pre-notebook'); backup.write_bytes(path.read_bytes()); backup.chmod(0o600)
        temporary=path.with_suffix('.new'); temporary.write_text(json.dumps(data,indent=2)+'\n'); temporary.chmod(0o600)
        os.chown(temporary,user.pw_uid,user.pw_gid); os.replace(temporary,path)
    state=home/'.local/state/pi-sharp'; state.mkdir(parents=True,exist_ok=True); state.chmod(0o700); os.chown(state,user.pw_uid,user.pw_gid)
    settings=json.loads((config/'settings.json').read_text()); settings['notebookDirectory']=str(state/'notebook')
    settings['compaction']={'enabled':True,'reserveTokens':8192,'keepRecentTokens':a.keep,'triggerTokens':a.trigger,'targetTokens':a.target}
    factory=settings.setdefault('factory',{})
    # Coders clear their context only on the team lead's /fresh-task, after their own delivery checks;
    # the role seats keep the per-arrival reset.
    if str(factory.get('actor','')).startswith('coder-'): factory.update(freshContext='assignment',freshTaskFrom=fresh_from)
    else: factory.pop('freshContext',None); factory.pop('freshTaskFrom',None)
    settings['maxTokens']=8192
    settings.update(taskMaxTurns=200,taskMaxOutputTokens=400000,taskMaxToolCalls=1000)
    write(config/'settings.json',settings)
    models=json.loads((config/'models.json').read_text())
    for model in models['models']: model['contextWindow']=min(model.get('contextWindow',100000),100000); model['maxOutputTokens']=min(model.get('maxOutputTokens',8192),8192)
    write(config/'models.json',models)
    mcp=json.loads((config/'mcp.json').read_text())
    mcp.setdefault('mcpServers',{})['browser']={'command':'/opt/pi-sharp-attached/bin/pi-browser','args':[],
        'cwd':str(home/'work'),'env':{'PI_BROWSER_ORIGIN':f'http://127.0.0.1:{4300+n}',
        'PI_BROWSER_STATE':str(state/'browser'),'PI_BROWSER_WORKSPACE':str(home/'work')}}
    write(config/'mcp.json',mcp)
    print(json.dumps({'deployer':actor,'seat':n,'owner':settings.get('factory',{}).get('actor'),
        'notebook':str(state/'notebook'),'browserOrigin':f'http://127.0.0.1:{4300+n}','trigger':a.trigger,'keep':a.keep,'target':a.target,'freshContext':factory.get('freshContext','arrival'),'hardWindow':100000}))
