const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const root=path.resolve(__dirname,'../..'),script=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.js'),'utf8');
let markup=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.cshtml'),'utf8');
markup=markup.replace(/@if \(Model.CanApplyToElements\)\s*\{([\s\S]*?)\}\s*else\s*\{[\s\S]*?\}/g,'$1').replace(/@if \(Model.CanApplyToElements\)\s*\{([\s\S]*?)\}/g,'$1').replace('@Html.AntiForgeryToken()','<input name="__RequestVerificationToken" value="test" type="hidden">').replace('@Model.EnvironmentName','NON-PRODUCTION').split('\n').filter(x=>!x.trimStart().startsWith('@')).join('\n');
const rooms=Array.from({length:20},(_,i)=>({id:i+1,building:'Commuter',name:'Room '+(i+1),capacity:10,occupants:[]}));
for(let id=1;id<=113;id++)rooms[(id-1)%20].occupants.push({studentUid:id,name:'Test '+id,rate:'Standard',intentionalSingle:false,notes:''});
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.CHAPEL_TEST_BROWSER||'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'});let passed=0;
try{for(const mode of ['preflight-failure','partial-failure','uncertain-response','success','changed-server-draft','unavailable-server-status']){
 const page=await browser.newPage(),calls=[],errors=[];let state={id:'draft',periodId:2,revision:'original',refreshedAt:'2026-11-09T12:00:00Z',rooms:structuredClone(rooms),applyStatus:null},submitted=false;
 page.on('pageerror',e=>errors.push(e.message));await page.route('http://housing.test/**',route=>{const request=route.request();if(request.isNavigationRequest())return route.fulfill({contentType:'text/html',body:'<!doctype html><meta charset="utf-8">'+markup});const handler=new URL(request.url()).searchParams.get('handler');calls.push({handler,method:request.method()});
  if(handler==='Setup')return route.fulfill({json:{periodId:2,periodName:'Spring 2027',masterCommuterRoomCount:20,periodCommuterRoomCount:20,missingCommuterRooms:[]}});
  if(handler==='Periods')return route.fulfill({json:[{id:2,name:'Spring 2027'}]});
  if(handler==='Draft'){if(submitted&&mode==='unavailable-server-status')return route.fulfill({status:503,json:{error:{message:'Unavailable'}}});return route.fulfill({json:state});}
  if(handler==='RateRevision')return route.fulfill({json:''});
  if(handler==='Preview')return route.fulfill({json:Array.from({length:113},(_,i)=>({action:'Assign',detail:'Test '+(i+1)}))});
  if(handler==='Apply'){submitted=true;if(mode==='success'){state={...state,applyStatus:'Completed',revision:'completed'};return route.fulfill({json:state});}if(mode==='partial-failure'||mode==='uncertain-response')state={...state,applyStatus:mode==='partial-failure'?'Needs review':'Running',revision:'claimed'};if(mode==='changed-server-draft')state={...state,rooms:[]};return route.fulfill({status:mode==='uncertain-response'?502:409,json:{error:{message:mode==='preflight-failure'?'The live assignment changed. Review before applying.':'The apply needs review.'}}});}
  throw new Error('Unexpected mutation: '+handler);
 });
 await page.goto('http://housing.test/');await page.addScriptTag({content:script});await page.waitForFunction(()=>document.querySelector('#housing-period').options.length===2);await page.selectOption('#housing-period','2');await page.waitForFunction(()=>document.querySelector('#housing-summary').textContent.startsWith('113 assigned'));
 page.once('dialog',d=>d.accept());await page.click('#housing-apply');await page.waitForFunction(()=>document.querySelector('#housing-message').textContent.includes('applied')||document.querySelector('#housing-message').textContent.includes('review')||document.querySelector('#housing-message').textContent.includes('Review'));
 assert.equal(await page.locator('#housing-rows input[aria-label^="Select Test "]').count(),113);assert.match(await page.locator('#housing-summary').innerText(),/^113 assigned/);assert.equal(calls.filter(x=>x.handler==='Apply').length,1);assert.equal(calls.filter(x=>['Refresh','Save'].includes(x.handler)).length,0);
 if(['partial-failure','uncertain-response','changed-server-draft','unavailable-server-status'].includes(mode))assert.equal(await page.locator('#housing-apply').isDisabled(),true);
 if(mode==='success')assert.match(await page.locator('#housing-summary').innerText(),/Completed/);
 assert.deepEqual(errors,[]);console.log('PASS '+mode+' retains 113 saved assignments without Refresh or Save');passed++;await page.close();
 }console.log(`${passed} housing preservation browser scenarios passed.`);}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
