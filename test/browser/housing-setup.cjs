const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.CAMPUSFLOW_PLAYWRIGHT_PATH||'playwright');
const root=path.resolve(__dirname,'../..');
// Exercise the real workspace markup/script with read-only fixture responses.
const html=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.cshtml'),'utf8')
 .split('\n').filter(x=>!x.trim().startsWith('@')&&!['{','}'].includes(x.trim())).join('\n')
 .replace('@Model.EnvironmentName','NON-PRODUCTION')
 .replace('<div id="housing-workspace"', '<input name="__RequestVerificationToken" value="test"><div id="housing-workspace"');
const script=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.js'),'utf8');
const css=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/Admin/Housing/Assignments.css'),'utf8');
(async()=>{const browser=await chromium.launch({headless:true,...(process.env.CHAPEL_TEST_BROWSER?{executablePath:process.env.CHAPEL_TEST_BROWSER}:{})});
try{for(const width of [320,390,768,1440]){
 const page=await browser.newPage({viewport:{width,height:900}}),calls=[],errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://housing.test/**',async route=>{
  const req=route.request(),url=new URL(req.url()),handler=url.searchParams.get('handler');
  if(!handler)return route.fulfill({contentType:'text/html',body:'<!doctype html><meta name="viewport" content="width=device-width, initial-scale=1"><style>'+css+'</style>'+html});
  calls.push({handler,method:req.method()});let body;
  if(handler==='Periods')body=[{id:1,name:'Spring 2027'},{id:2,name:'Summer 2027'},{id:3,name:'Unavailable'}];
  else if(handler==='Draft')body=null;
  else if(handler==='Setup'){
   const period=Number(url.searchParams.get('periodId'));
   if(period===3)return route.fulfill({status:503,contentType:'application/json',body:'{"error":{"message":"Unavailable"}}'});
   body={periodId:period,periodName:period===1?'Spring 2027':'Summer 2027',masterCommuterRoomCount:20,periodCommuterRoomCount:period===1?1:20,missingCommuterRooms:period===1?['Commuter / 2','<img src=x onerror=alert(1)>']:[]};
  }else throw Error('Unexpected handler '+handler);
  return route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
 });
 await page.goto('http://housing.test/Admin/Housing/Assignments');
 // Keep the token inside the same root as the production form.
 await page.evaluate(()=>document.getElementById('housing-workspace').append(document.querySelector('[name="__RequestVerificationToken"]')));
 await page.addScriptTag({content:script});await page.waitForFunction(()=>document.getElementById('housing-period').options.length===4&&!document.getElementById('housing-period').disabled);
 await page.selectOption('#housing-period','1');await page.waitForFunction(()=>document.getElementById('housing-setup-rooms').children.length===2);
 assert.match(await page.locator('#housing-setup-counts').textContent(),/1 commuter rooms.*20 active/);
 assert.equal(await page.locator('#housing-setup-missing').isVisible(),true);
 assert.equal(await page.locator('#housing-setup-rooms img').count(),0);
 assert.match(await page.locator('#housing-setup-missing').textContent(),/does not add master rooms/);
 await page.selectOption('#housing-period','2');await page.waitForFunction(()=>document.getElementById('housing-setup-counts').textContent.includes('Summer'));
 assert.equal(await page.locator('#housing-setup-missing').isVisible(),false);
 assert.equal(await page.locator('#housing-setup-rooms li').count(),0);
 await page.selectOption('#housing-period','3');await page.waitForFunction(()=>document.getElementById('housing-setup-counts').textContent.includes('could not be checked'));
 assert.equal(await page.locator('#housing-refresh').isDisabled(),false);
 await page.selectOption('#housing-period','');assert.equal(await page.locator('#housing-setup').isVisible(),false);
 assert.ok(calls.length>0&&calls.every(x=>x.method==='GET'));assert.deepEqual(errors,[]);
 console.log('PASS read-only setup gap, period switching, safe text, retry guidance and no writes at '+width+'px');await page.close();
}}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1)});
