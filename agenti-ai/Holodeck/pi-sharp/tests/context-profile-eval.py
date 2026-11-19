#!/usr/bin/env python3
"""Isolated real coding fixtures comparing working triggers on the same augmented binary.
No factory/chat/MCP connections. Runs as a production pool user in private temporary workspaces.
All failures and summary calls count toward cost per successful task. This is a synthetic workload,
not proof of general model quality. --fixtures 12 --repetitions 3 is the full paired set.
"""
import argparse, json, os, pathlib, random, signal, subprocess, time, uuid
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--binary',default='/opt/pi-sharp/bin/pi-sharp.dll')
p.add_argument('--model',default='maxiclaude')
p.add_argument('--root',required=True);p.add_argument('--fixtures',type=int,default=12)
p.add_argument('--repetitions',type=int,default=3);p.add_argument('--profiles',default='90:20,24:16,28:20,32:24')
a=p.parse_args();root=pathlib.Path(a.root);root.mkdir(parents=True,exist_ok=True);root.chmod(0o700)
models=json.loads((pathlib.Path.home()/'.config/pi-sharp/models.json').read_text())
for m in models['models']:m['contextWindow']=100000;m['maxOutputTokens']=4096
profiles=[tuple(map(int,v.split(':'))) for v in a.profiles.split(',')]
results=root/'results.jsonl'
completed=set()
if results.exists(): completed={tuple(json.loads(v)['case']) for v in results.read_text().splitlines()}
for repetition in range(a.repetitions):
 for fixture in range(a.fixtures):
  order=profiles[:];random.Random(repetition*100+fixture).shuffle(order)
  for trigger,keep in order:
   case=(fixture,repetition,trigger,keep)
   if case in completed:continue
   run=root/('-'.join(map(str,case)));run.mkdir(exist_ok=True);work=run/'work';work.mkdir(exist_ok=True);config=run/'config';config.mkdir(exist_ok=True)
   floor=fixture-5;remainder=fixture%3;scale=fixture%4+1;offset=fixture*2;marker=f'TASK_{fixture}'
   request=f'''Implement solution.py with function select_values(values). Requirements:
1. Accept a list or tuple. 2. Return a new list. 3. Include only strict Python int objects, excluding bool.
4. Keep only integers >= {floor}. 5. Keep only integers whose modulo 3 is {remainder}.
6. Multiply each retained integer by {scale}. 7. Then add {offset}.
8. Remove repeated input integers. 9. Preserve the original first-occurrence order.
10. Never mutate the input. 11. Never print or use third-party dependencies.
12. The function docstring must include the exact marker {marker}.
The late conditions are mandatory. Implement the file, run a useful verification, then finish.'''
   (config/'models.json').write_text(json.dumps(models));(config/'mcp.json').write_text('{"mcpServers":{}}')
   (config/'settings.json').write_text(json.dumps({'defaultModel':a.model,'temperature':0,'maxTokens':4096,
       'notebookDirectory':str(run/'notebook'),'taskMaxTurns':24,'taskMaxOutputTokens':40000,'taskMaxToolCalls':100,
       'compaction':{'enabled':True,'reserveTokens':8192,'triggerTokens':trigger*1000,'keepRecentTokens':keep*1000}}))
   session=run/'session.jsonl';stamp='2026-09-22T00:00:00Z';sid=uuid.uuid4().hex
   entries=[{'type':'session','version':3,'id':sid,'timestamp':stamp,'cwd':str(work)}]
   parent=None
   def add(role,text,index):
    global parent
    message={'role':role,'content':[{'type':'text','text':text}],'timestamp':stamp}
    if role=='assistant':message.update(model=a.model,stopReason='stop',usage={})
    key=f'seed-{index}';entries.append({'type':'message','id':key,'parentId':parent,'timestamp':stamp,'message':message});parent=key
   add('user',request,0)
   for i in range(24):
    log=('Archived unrelated test log. No new requirements. Module build passed; cache warmed; worker idle; old fixture discarded.\n'*70)
    add('assistant',f'Archived diagnostic block {i}.\n'+log,i+1)
   session.write_text(''.join(json.dumps(e)+'\n' for e in entries))
   command=['/usr/bin/time','-f','%M','-o',str(run/'rss-kib.txt'),'dotnet',a.binary,'--json','--config-dir',str(config),'--cwd',str(work),'--session',str(session),'-ns','-np','-nc','-p','Now implement the original select_values request. Archived logs add no requirements. Verify the actual file before finishing.']
   started=time.monotonic();exitcode=None;failure=None
   try:
    with (run/'events.jsonl').open('w') as stdout,(run/'stderr.txt').open('w') as stderr:
     process=subprocess.Popen(command,stdout=stdout,stderr=stderr,start_new_session=True)
     try:exitcode=process.wait(timeout=600)
     except subprocess.TimeoutExpired:
      os.killpg(process.pid,signal.SIGKILL);process.wait();raise
   except subprocess.TimeoutExpired:failure='timeout'
   elapsed=time.monotonic()-started
   # Evaluate in a separate interpreter; the model never sees these held-out inputs.
   inputs=[[-9,-5,-1,0,1,2,3,4,5,9,12,12,True,False,None,'3',3.0],(20,3,9,6,0,-3,6),[]]
   expected=[]
   for values in inputs:
    seen=set();out=[]
    for v in values:
     if type(v) is int and v>=floor and v%3==remainder and v not in seen:seen.add(v);out.append(v*scale+offset)
    expected.append(out)
   verify=f"import solution,copy,io,contextlib; values={inputs!r}; expected={expected!r}; assert {marker!r} in (solution.select_values.__doc__ or ''); capture=io.StringIO();\nwith contextlib.redirect_stdout(capture):\n for v,e in zip(values,expected):\n  before=copy.deepcopy(v); result=solution.select_values(v); assert type(result) is list and result==e and v==before\nassert capture.getvalue()==''"
   checked=subprocess.run(['python3','-c',verify],cwd=work,capture_output=True,text=True,timeout=10)
   success=exitcode==0 and checked.returncode==0
   totals={'input':0,'output':0,'cacheRead':0,'cacheWrite':0};compactions=0;retained=True
   for line in session.read_text().splitlines():
    row=json.loads(line);usage=None
    if row.get('type')=='compaction':compactions+=1;usage=row.get('usage',{})
    elif row.get('type')=='message' and row['message'].get('role')=='assistant':usage=row['message'].get('usage',{})
    if usage:
     for key in totals:totals[key]+=usage.get(key,0)
   journal=run/'notebook/notebook.jsonl'
   retained=journal.exists() and any(json.loads(line).get('record',{}).get('text')==request for line in journal.read_text().splitlines())
   result={'case':case,'model':a.model,'success':success,'sourceRetained':retained,'elapsedSeconds':round(elapsed,3),'usage':totals,
       'totalTokens':sum(totals.values()),'compactions':compactions,'rssKiB':(run/'rss-kib.txt').read_text().strip() if (run/'rss-kib.txt').exists() else None,
       'exitCode':exitcode,'usageComplete':failure!='timeout','failure':failure or (checked.stderr[-1200:] if not success else None)}
   with results.open('a') as f:f.write(json.dumps(result)+'\n');f.flush();os.fsync(f.fileno())
   print(json.dumps(result),flush=True)
