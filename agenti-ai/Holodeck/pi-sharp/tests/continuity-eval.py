#!/usr/bin/env python3
"""Five real compactions with process restart between cycles and held-out exact-source checks.
Run under a deployed seat with service environment loaded. No chat/factory connection.
"""
import datetime,json,os,pathlib,signal,subprocess,tempfile,time,uuid
root=pathlib.Path(tempfile.mkdtemp(prefix='pi-continuity-'));root.chmod(0o700)
work=root/'work';work.mkdir();config=root/'config';config.mkdir();session=root/'session.jsonl'
models=json.loads((pathlib.Path.home()/'.config/pi-sharp/models.json').read_text())
for model in models['models']:model['contextWindow']=100000;model['maxOutputTokens']=2048
(config/'models.json').write_text(json.dumps(models));(config/'mcp.json').write_text('{"mcpServers":{}}')
(config/'settings.json').write_text(json.dumps({'defaultModel':'maxiclaude','temperature':0,'maxTokens':2048,
 'notebookDirectory':str(root/'notebook'),'taskMaxTurns':80,'taskMaxOutputTokens':80000,'taskMaxToolCalls':200,
 'compaction':{'enabled':True,'reserveTokens':8192,'triggerTokens':28000,'keepRecentTokens':20000}}))
source='''Persistent task across five cycles: write continuity.json in this workspace. Preserve these exact values on every cycle:
{"project":"cedar","port":4317,"pending":["verify screenshot","retain rollback tag","operator approval"],"forbidden":"publish without evidence","late_marker":"TAIL-83719"}.
Add a numeric cycle field matching the requested cycle. Do not remove or reorder pending entries.
No external services or dependencies. Verify the file contents, then reply briefly. The original source remains open until cycle 5 completes.'''
stamp=lambda:datetime.datetime.now(datetime.timezone.utc).isoformat()
session.write_text(json.dumps({'type':'session','version':3,'id':uuid.uuid4().hex,'timestamp':stamp(),'cwd':str(work)})+'\n')
parent=None
def append(role,text):
 global parent
 key=uuid.uuid4().hex;message={'role':role,'content':[{'type':'text','text':text}],'timestamp':stamp()}
 if role=='assistant':message.update(model='maxiclaude',stopReason='stop',usage={})
 with session.open('a') as f:f.write(json.dumps({'type':'message','id':key,'parentId':parent,'timestamp':stamp(),'message':message})+'\n')
 parent=key
append('user',source)
expected={'project':'cedar','port':4317,'pending':['verify screenshot','retain rollback tag','operator approval'],
 'forbidden':'publish without evidence','late_marker':'TAIL-83719'}
previous_compactions=0
for cycle in range(1,6):
 entries=[json.loads(line) for line in session.read_text().splitlines()]
 parent=next((e['id'] for e in reversed(entries) if 'id' in e and e.get('type')!='session'),parent)
 for i in range(20):append('assistant',f'Obsolete diagnostic cycle {cycle} block {i}.\n'+('Old build log; no task requirements; all diagnostic output is disposable.\n'*140))
 command=['dotnet','/opt/pi-sharp/bin/pi-sharp.dll','--json','--config-dir',str(config),'--cwd',str(work),'--session',str(session),'-ns','-np','-nc','-p',f'Continue the original persistent task, cycle {cycle}. Use its exact values. Read the notebook source if needed. Write and verify continuity.json, then finish briefly.']
 started=time.monotonic()
 with (root/f'events-{cycle}.jsonl').open('w') as out,(root/f'stderr-{cycle}.txt').open('w') as err:
  process=subprocess.Popen(command,stdout=out,stderr=err,start_new_session=True)
  try:code=process.wait(timeout=600)
  except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL);process.wait();raise
 result=json.loads((work/'continuity.json').read_text()) if (work/'continuity.json').exists() else {}
 rows=[json.loads(line) for line in session.read_text().splitlines()]
 compactions=sum(e.get('type')=='compaction' for e in rows)
 # The complete original text must exist in the durable journal, not just a summary marker.
 notebook=[json.loads(line) for line in (root/'notebook/notebook.jsonl').read_text().splitlines()]
 retained=any(e.get('record',{}).get('text')==source for e in notebook)
 passed=code==0 and result==dict(expected,cycle=cycle) and retained and compactions>previous_compactions
 record={'cycle':cycle,'passed':passed,'exitCode':code,'compactions':compactions,'newCompactions':compactions-previous_compactions,
 'exactSourceRetained':retained,'elapsedSeconds':round(time.monotonic()-started,2),'root':str(root)}
 with (root/'results.jsonl').open('a') as f:f.write(json.dumps(record)+'\n')
 print(json.dumps(record),flush=True)
 if not passed:raise SystemExit(1)
 previous_compactions=compactions
