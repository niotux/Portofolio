#!/usr/bin/env python3
"""Live isolated Pi/Chromium/vision repair task; run under a seat with its service env loaded.
No factory/chat connection. Independent DOM assertions verify the result at two viewports.
"""
import functools, http.server, json, os, pathlib, signal, subprocess, tempfile, threading, time

root=pathlib.Path(tempfile.mkdtemp(prefix='pi-visual-repair-')); root.chmod(0o700)
work=root/'work'; work.mkdir(); config=root/'config'; config.mkdir()
(work/'index.html').write_text('''<!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1"><style>
body{font-family:sans-serif;margin:16px} .panel{background:rgb(208,0,0);color:white;width:1200px;padding:20px;box-sizing:border-box}
</style></head><body><h1>Repair fixture</h1><div class="panel">Status panel</div>
<label>Your name <input aria-label="Your name"></label><button onclick="document.querySelector('#result').textContent='Hello '+document.querySelector('input').value">Greet</button><p id="result"></p></body></html>''')
class Quiet(http.server.SimpleHTTPRequestHandler):
 def log_message(self,*args):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(work)))
threading.Thread(target=server.serve_forever,daemon=True).start()
origin=f'http://127.0.0.1:{server.server_port}'
models=json.loads((pathlib.Path.home()/'.config/pi-sharp/models.json').read_text())
for model in models['models']:model['contextWindow']=100000;model['maxOutputTokens']=4096
(config/'models.json').write_text(json.dumps(models))
(config/'settings.json').write_text(json.dumps({'defaultModel':'maxiclaude','temperature':0,'maxTokens':4096,
 'notebookDirectory':str(root/'notebook'),'taskMaxTurns':30,'taskMaxOutputTokens':50000,'taskMaxToolCalls':100,
 'compaction':{'enabled':True,'reserveTokens':8192,'triggerTokens':28000,'keepRecentTokens':20000}}))
(config/'mcp.json').write_text(json.dumps({'mcpServers':{'browser':{'command':'/opt/pi-sharp-attached/bin/pi-browser',
 'args':[],'cwd':str(work),'env':{'PI_BROWSER_ORIGIN':origin,'PI_BROWSER_WORKSPACE':str(work),'PI_BROWSER_STATE':str(root/'browser')}}}}))
prompt=f'''Repair index.html served at {origin}. First use the browser tool to inspect an actual screenshot.
The status panel must be green (CSS rgb(0,128,0)), and the page must have no horizontal overflow at desktop
or a 390px mobile viewport. Keep the greeting form functional. Edit the file and verify with fresh screenshots
and the Greet interaction after entering operator. Do not install packages. Finish when verified.'''
started=time.monotonic()
command=['dotnet','/opt/pi-sharp/bin/pi-sharp.dll','--json','--config-dir',str(config),'--cwd',str(work),'-ns','-np','-nc','-p',prompt]
try:
 with (root/'events.jsonl').open('w') as out,(root/'stderr.txt').open('w') as err:
  process=subprocess.Popen(command,stdout=out,stderr=err,start_new_session=True)
  try:code=process.wait(timeout=900)
  except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL);process.wait();raise
 # Separate browser/assertions are outside model control.
 checker=root/'verify.cjs'
 checker.write_text('''const {chromium}=require(process.argv[2]); (async()=>{const b=await chromium.launch({headless:true,chromiumSandbox:true});
 try {for(const width of [1280,390]){const page=await b.newPage({viewport:{width,height:800}});await page.goto(process.argv[3]);
 const state=await page.evaluate(()=>({color:getComputedStyle(document.querySelector('.panel')).backgroundColor,overflow:document.documentElement.scrollWidth>innerWidth}));
 if(state.color!=='rgb(0, 128, 0)'||state.overflow)throw new Error(JSON.stringify({width,...state}));
 await page.getByRole('textbox',{name:'Your name'}).fill('operator');await page.getByRole('button',{name:'Greet',exact:true}).click();
 if(await page.locator('#result').textContent()!=='Hello operator')throw new Error('Greeting broken');
 await page.screenshot({path:process.argv[4]+'/'+width+'.png'});await page.close();}console.log('DOM and interaction assertions passed');}finally{await b.close();}
 })().catch(e=>{console.error(e);process.exitCode=1;});''')
 env=dict(os.environ,PLAYWRIGHT_BROWSERS_PATH='/opt/pi-sharp-browser/0.5.8/browsers')
 checked=subprocess.run(['/opt/pi-sharp-browser/0.5.8/bin/.playwright/node/linux-x64/node',str(checker),
  '/opt/pi-sharp-browser/0.5.8/bin/.playwright/package',origin,str(root)],capture_output=True,text=True,timeout=60,env=env)
 result={'passed':code==0 and checked.returncode==0,'exitCode':code,'elapsedSeconds':round(time.monotonic()-started,2),
  'root':str(root),'independentAssertions':checked.stdout,'error':checked.stderr[-2000:]}
 (root/'result.json').write_text(json.dumps(result));print(json.dumps(result),flush=True)
finally:server.shutdown()
