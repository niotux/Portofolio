#!/usr/bin/env python3
"""Small 1/4/8 concurrent completion probe, not a full coding-fleet throughput benchmark.
Run under a seat with its service env loaded. Does not alter server queue/slot settings.
"""
import argparse, concurrent.futures, json, os, pathlib, statistics, time, urllib.request
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--model',required=True);a=p.parse_args()
models=json.loads((pathlib.Path.home()/'.config/pi-sharp/models.json').read_text())['models']
model=next(m for m in models if m['id']==a.model)
token=os.environ.get(model.get('apiKeyEnv',''),model.get('apiKey',''))
def complete(index):
    payload={'model':a.model,'messages':[{'role':'user','content':f'Probe {index}. Reply only READY.'}],
        'max_tokens':32,'temperature':0,'stream':False,'chat_template_kwargs':{'enable_thinking':False}}
    start=time.monotonic()
    request=urllib.request.Request(model['baseUrl'].rstrip('/')+'/chat/completions',data=json.dumps(payload).encode(),
        headers={'Authorization':'Bearer '+token,'Content-Type':'application/json'})
    with urllib.request.urlopen(request,timeout=180) as response:result=json.load(response)
    text=result['choices'][0]['message'].get('content','')
    return {'seconds':time.monotonic()-start,'ready':'READY' in text,'usage':result.get('usage')}
for count in [1,4,8]:
    start=time.monotonic()
    with concurrent.futures.ThreadPoolExecutor(max_workers=count) as pool:results=list(pool.map(complete,range(count)))
    elapsed=time.monotonic()-start;latencies=sorted(r['seconds'] for r in results)
    print(json.dumps({'model':a.model,'concurrency':count,'passed':all(r['ready'] for r in results),
        'wallSeconds':round(elapsed,3),'p50Seconds':round(statistics.median(latencies),3),
        'maxSeconds':round(max(latencies),3),'samples':results}),flush=True)
