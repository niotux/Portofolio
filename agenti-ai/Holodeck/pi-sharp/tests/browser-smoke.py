#!/usr/bin/env python3
"""Real Chromium/MCP integration fixture. Usage: python tests/browser-smoke.py path/to/PiSharp.Browser.dll
No fleet messaging or model calls. Keeps screenshot evidence in the reported private temp directory.
"""
import base64, http.server, json, os, pathlib, subprocess, sys, tempfile, threading

class Fixture(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/missing': self.send_error(500); return
        self.send_response(200); self.send_header('Content-Type','text/html'); self.end_headers()
        self.wfile.write(b'''<!doctype html><html><body style="font:24px sans-serif"><h1>Visual fixture</h1>
<div style="background:#d00000;color:white;width:800px;height:180px">RED PANEL</div>
<label>Your name <input aria-label="Your name"></label><button onclick="document.querySelector('#result').textContent='Hello '+document.querySelector('input').value">Greet</button>
<p id="result"></p>
<button id="tip" onpointerenter="document.querySelector('#hovered').textContent='hovered by pointer'">Hover target</button><p id="hovered"></p>
<button id="handle">Drag handle</button><p id="dragged"></p><p id="buttons"></p>
<button onpointerenter="indexed('near hover')" onclick="indexed('near click')">Indexed target extra</button>
<button onpointerenter="indexed('first hover')" onclick="indexed('first click')">Indexed target</button>
<button onpointerenter="indexed('second hover')" onclick="indexed('second click')">Indexed target</button>
<p id="indexed">index events 0: none</p>
<script>let events=0;function indexed(value){document.querySelector('#indexed').textContent='index events '+(++events)+': '+value;}</script>
<script>let start=null;const h=document.querySelector('#handle');
h.addEventListener('pointerdown',e=>{start=[e.clientX,e.clientY];h.setPointerCapture(e.pointerId);});
document.addEventListener('pointermove',e=>{document.querySelector('#buttons').textContent='buttons '+e.buttons;});
h.addEventListener('pointerup',e=>{if(start)document.querySelector('#dragged').textContent='dragged '+Math.round(e.clientX-start[0])+','+Math.round(e.clientY-start[1]);start=null;});</script><script>console.error('fixture-console-error');fetch('/missing');</script></body></html>''')
    def log_message(self, *args): pass

server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Fixture)
threading.Thread(target=server.serve_forever,daemon=True).start()
state=tempfile.mkdtemp(prefix='pi-browser-smoke-')
env=dict(os.environ,PI_BROWSER_ORIGIN=f'http://127.0.0.1:{server.server_port}',PI_BROWSER_STATE=state)
proc=subprocess.Popen(['dotnet',sys.argv[1]],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,env=env)
counter=0
def rpc(method,params):
    global counter
    counter+=1
    proc.stdin.write(json.dumps(dict(jsonrpc='2.0',id=counter,method=method,params=params))+'\n');proc.stdin.flush()
    line=proc.stdout.readline()
    if not line: raise RuntimeError('Companion exited: '+proc.stderr.read()[-2000:])
    reply=json.loads(line)
    assert 'error' not in reply,reply.get('error')
    return reply['result']
def call(**args): return rpc('tools/call',dict(name='browser',arguments=args))
def text(reply): return '\n'.join(c.get('text','') for c in reply['content'])
try:
    rpc('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'fixture','version':'1'}})
    opened=call(op='open');assert not opened['isError'],text(opened)
    snapshot=call(op='inspect');assert 'Greet' in text(snapshot),text(snapshot)
    revision=int(text(snapshot).split('revision=')[-1].split(';')[0])
    fill=call(op='act',role='textbox',name='Your name',action='fill',value='operator',revision=revision);assert not fill['isError'],text(fill)
    stale=call(op='act',role='button',name='Greet',action='click',revision=revision);assert stale['isError']
    click=call(op='act',role='button',name='Greet',action='click',revision=revision+1);assert not click['isError'],text(click)
    snapshot=call(op='inspect');assert 'Hello operator' in text(snapshot),text(snapshot)
    assert 'fixture-console-error' in text(snapshot)
    assert 'HTTP 500' in text(snapshot)
    revision=int(text(snapshot).split('revision=')[-1].split(';')[0])
    hover=call(op='act',role='button',name='Hover target',action='hover',revision=revision);assert not hover['isError'],text(hover)
    snapshot=call(op='inspect');assert 'hovered by pointer' in text(snapshot),text(snapshot)
    drag=call(op='act',role='button',name='Drag handle',action='drag',value='60,-25',revision=revision+1);assert not drag['isError'],text(drag)
    snapshot=call(op='inspect');assert 'dragged 60,-25' in text(snapshot),text(snapshot)
    for bad in ('60','x,1','2001,0'):
        refused=call(op='act',role='button',name='Drag handle',action='drag',value=bad,revision=revision+2);assert refused['isError'],bad
    missing=call(op='act',role='button',name='No such handle',action='drag',value='10,10',revision=revision+2);assert missing['isError']
    probe=call(op='act',role='button',name='Hover target',action='hover',revision=revision+2);assert not probe['isError'],text(probe)
    snapshot=call(op='inspect');assert 'buttons 0' in text(snapshot),'a refused or failed drag left a button held: '+text(snapshot)
    # Native actions, not schema-only checks: exact matching excludes the earlier near-name button.
    def current():
        snapshot=call(op='inspect');assert not snapshot['isError'],text(snapshot)
        return int(text(snapshot).split('revision=')[-1].split(';')[0]),text(snapshot)
    revision,_=current()
    for index,label in ((0,'first'),(1,'second')):
        hovered=call(op='act',role='button',name='Indexed target',action='hover',index=index,revision=revision)
        assert not hovered['isError'],text(hovered)
        revision,snapshot=current();assert f'{label} hover' in snapshot,snapshot
        clicked=call(op='act',role='button',name='Indexed target',action='click',index=index,revision=revision)
        assert not clicked['isError'],text(clicked)
        revision,snapshot=current();assert f'{label} click' in snapshot,snapshot
    assert 'index events 4: second click' in snapshot,snapshot
    # Each refusal must leave both native event effects and revision unchanged.
    for extra in ({}, *({'index':bad} for bad in (None,-1,2,2147483647,2147483648,1.5,1.0,'0',True,False,[],{}))):
        refused=call(op='act',role='button',name='Indexed target',action='click',revision=revision,**extra)
        assert refused['isError'],(extra,text(refused))
        after,snapshot=current()
        assert after==revision and 'index events 4: second click' in snapshot,(extra,snapshot)
        if not extra: assert 'strict mode violation' in text(refused),text(refused)
    stale=call(op='act',role='button',name='Indexed target',action='click',index=0,revision=revision-1)
    assert stale['isError'] and 'Stale page revision' in text(stale),text(stale)
    missing=call(op='act',role='button',name='No such target',action='hover',index=0,revision=revision)
    assert missing['isError'],text(missing)
    after,snapshot=current();assert after==revision and 'index events 4: second click' in snapshot,snapshot
    unique=call(op='act',role='button',name='Greet',action='click',index=0,revision=revision)
    assert not unique['isError'],text(unique)
    _,snapshot=current();assert 'Hello operator' in snapshot,snapshot
    schema=rpc('tools/list',{})['tools'][0]['inputSchema']
    assert schema['properties']['index']=={'type':'integer','minimum':0},schema
    assert 'index' not in schema['required'],schema
    capture=call(op='capture',task='browser-smoke',testedBuild='fixture-v1');assert not capture['isError'],text(capture)
    image=next(c for c in capture['content'] if c['type']=='image');assert len(base64.b64decode(image['data']))>1000
    metadata=json.loads(capture['content'][1]['text']);assert pathlib.Path(metadata['path']).exists()
    outside=call(op='open',url='http://127.0.0.1:1');assert outside['isError']
    call(op='close')
    print(json.dumps({'passed':True,'checks':['open','semantic inspect','fill/click','pointer hover','pointer drag by offset','drag bounds refusal','no held button after refused/failed drag','stale reference refusal','duplicate exact-name native hover/click index 0/1','strict omitted-index refusal','invalid/out-of-range index no action or revision advance','stale indexed revision refusal','missing indexed target refusal','exact name excludes near match','unique indexed click','optional index schema','console and HTTP diagnostics','image bytes','private original','origin restriction','close'],'evidence':metadata['path']}))
finally:
    proc.stdin.close()
    try: proc.wait(timeout=15)
    except subprocess.TimeoutExpired: proc.kill();proc.wait()
    server.shutdown()
