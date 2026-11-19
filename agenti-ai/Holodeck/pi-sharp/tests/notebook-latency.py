#!/usr/bin/env python3
"""Read-only latency probe; run as a configured seat with its service env loaded.
Use separate seat processes for concurrency. Never supply credentials in argv.
"""
import json, os, pathlib, statistics, time, urllib.request

settings=json.loads((pathlib.Path.home()/'.config/pi-sharp/settings.json').read_text())
factory=settings['factory'];url=factory['kbUrl'].split('/mcp')[0].rstrip('/')+'/notebook/records'
values=[]
for _ in range(20):
    started=time.monotonic()
    request=urllib.request.Request(url,headers={'Authorization':'Bearer '+os.environ['PI_FACTORY_TOKEN']})
    with urllib.request.urlopen(request,timeout=20) as response:json.load(response)
    values.append((time.monotonic()-started)*1000)
print(json.dumps({'actor':factory['actor'],'samples':len(values),'p50ms':round(statistics.median(values),2),
    'p95ms':round(sorted(values)[18],2),'maxms':round(max(values),2)}))
