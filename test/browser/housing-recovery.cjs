const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.CAMPUSFLOW_PLAYWRIGHT_PATH||'playwright');
const root=path.resolve(__dirname,'../..');
let markup=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.cshtml'),'utf8');
markup=markup.replace(/@if \(Model.CanApplyToElements\)\s*\{([\s\S]*?)\}\s*else\s*\{[\s\S]*?\}/g,'$1').replace(/@if \(Model.CanApplyToElements\)\s*\{([\s\S]*?)\}/g,'$1').replace('@Html.AntiForgeryToken()','<input name="__RequestVerificationToken" value="test" type="hidden">').replace('@Model.EnvironmentName','NON-PRODUCTION').split('\n').filter(x=>!x.trimStart().startsWith('@')).join('\n');
const script=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.js'),'utf8');
const bootstrap=fs.readFileSync(path.join(root,'src/CampusFlow.Web/wwwroot/libs/bootstrap/css/bootstrap.min.css'),'utf8');
const css=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.css'),'utf8');
const rooms=Array.from({length:20},(_,i)=>({id:i+1,building:'Commuter',name:String(i+1),capacity:10,occupants:[]}));
for(let id=1;id<=113;id++)rooms[(id-1)%20].occupants.push({studentUid:id,name:'Test '+id,rate:'Standard',intentionalSingle:false,notes:''});
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.CHAPEL_TEST_BROWSER||'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'});let passed=0;
try{for(const scenario of [{width:320,mode:'success'},{width:390,mode:'success'},{width:768,mode:'success'},{width:1440,mode:'success'},{width:390,mode:'failure'},{width:390,mode:'stale'},{width:390,mode:'unsupported'}]){
 const {width,mode}=scenario,page=await browser.newPage({viewport:{width,height:1000}}),calls=[],errors=[];
 let state={id:'draft',periodId:2,revision:'original',refreshedAt:'2026-11-10T12:00:00Z',rooms:structuredClone(rooms),applyStatus:'Needs review',applyRunId:'run',applyFailure:'The last operation could not be confirmed.'};
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://housing.test/**',route=>{const req=route.request(),handler=new URL(req.url()).searchParams.get('handler');
  if(req.isNavigationRequest())return route.fulfill({contentType:'text/html',body:'<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><style>'+bootstrap+'body{font:16px system-ui;margin:16px;color:#23212c;--bs-primary:#4d287d}button,textarea,select,input{font:inherit}textarea{display:block;max-width:100%;box-sizing:border-box}.d-block{display:block;margin:16px 0}section{margin:20px 0}'+css+'</style>'+markup});
  calls.push({handler,method:req.method()});
  if(handler==='Periods')return route.fulfill({json:[{id:2,name:'Spring 2027'}]});
  if(handler==='Setup')return route.fulfill({json:{periodId:2,masterCommuterRoomCount:20,periodCommuterRoomCount:20,missingCommuterRooms:[]}});
  if(handler==='Draft')return route.fulfill({json:state});
  if(handler==='Recovery'){if(mode==='unsupported')return route.fulfill({status:409,json:{error:{message:'Elements contains an unexpected assignment. The saved plan was preserved.'}}});return route.fulfill({json:{periodId:2,runId:'run',revision:'original',recoveryRevision:'reviewed',savedAssignments:113,alreadyAssigned:1,remainingAssignments:112}});}
  if(handler==='Resume'){const data=req.postDataJSON();assert.equal(data.confirmed,true);assert.equal(data.reason,'Reviewed rooms and batch');assert.equal(data.recoveryRevision,'reviewed');assert.equal(data.runId,'run');
   if(mode==='success'){state={...state,applyStatus:'Completed',revision:'done'};return route.fulfill({json:state});}
   state={...state,revision:'new-review',applyRunId:'new-run'};return route.fulfill({status:409,json:{error:{message:mode==='stale'?'The recovery preview changed. Review again.':'The apply did not finish. The saved plan is retained.'}}});}
  throw new Error('Unexpected request '+handler);
 });
 await page.goto('http://housing.test/');await page.addScriptTag({content:script});await page.waitForFunction(()=>document.querySelector('#housing-period').options.length===2);await page.selectOption('#housing-period','2');await page.waitForFunction(()=>document.querySelector('#housing-summary').textContent.startsWith('113 assigned'));
 for(const button of ['apply','refresh','save'])assert.equal(await page.locator('#housing-'+button).isDisabled(),true);
 await page.click('#housing-review-recovery');
 if(mode==='unsupported'){await page.waitForFunction(()=>document.querySelector('#housing-message').textContent.includes('unexpected'));assert.equal(await page.locator('#housing-resume').isDisabled(),true);}
 else{
  await page.waitForFunction(()=>document.querySelector('#housing-recovery-counts').textContent.includes('112 remaining'));
  assert.equal(calls.filter(x=>x.method==='POST').length,0);assert.equal(await page.locator('#housing-resume').isDisabled(),true);
  await page.fill('#housing-recovery-reason','Reviewed rooms and batch');assert.equal(await page.locator('#housing-resume').isDisabled(),true);await page.check('#housing-recovery-confirmed');assert.equal(await page.locator('#housing-resume').isDisabled(),false);
  if(process.env.CAMPUSFLOW_SCREENSHOT_DIR&&mode==='success'&&[390,1440].includes(width)){fs.mkdirSync(process.env.CAMPUSFLOW_SCREENSHOT_DIR,{recursive:true});await page.locator('#housing-recovery').screenshot({path:path.join(process.env.CAMPUSFLOW_SCREENSHOT_DIR,`recovery-${width}.png`)});}
  page.once('dialog',d=>d.accept());await page.click('#housing-resume');await page.waitForFunction(()=>{const t=document.querySelector('#housing-message').textContent;return t.includes('applied and verified')||t.includes('did not finish')||t.includes('preview changed');});
  assert.equal(calls.filter(x=>x.handler==='Resume').length,1);
  if(mode==='success')assert.match(await page.locator('#housing-summary').innerText(),/Completed/);
  else {assert.equal(await page.locator('#housing-resume').isDisabled(),true);assert.equal(await page.locator('#housing-recovery-preview').isHidden(),true);}
 }
 assert.equal(await page.locator('#housing-rows input[aria-label^="Select Test "]').count(),113);assert.match(await page.locator('#housing-summary').innerText(),/^113 assigned/);
 assert.equal(calls.filter(x=>['Refresh','Save','Apply'].includes(x.handler)).length,0);assert.deepEqual(errors,[]);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
 console.log(`PASS ${mode} at ${width}px: reviewed recovery preserves 113 assignments`);passed++;await page.close();
}console.log(`${passed} recovery browser scenarios passed.`);}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
