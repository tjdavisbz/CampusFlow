const {chromium}=require('playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const http=require('node:http');
const path=require('node:path');
(async()=>{
 const root=path.resolve(__dirname,'../..');
 const fixtures=process.env.CF028_UI_HTML_DIR;
 assert.ok(fixtures,'Set CF028_UI_HTML_DIR to the real Razor-rendered test output.');
 const output=process.env.CF028_UI_BROWSER_DIR;
 if(output)fs.mkdirSync(output,{recursive:true});
 const stages=['identity','question','password','complete','unavailable','unknown'];
 const server=http.createServer((request,response)=>{
  const route=new URL(request.url,'http://127.0.0.1').pathname;
  let file,type;
  if(stages.includes(route.slice(1))){file=path.join(fixtures,route.slice(1)+'.html');type='text/html';}
  else if(/^\/css\/account-recovery(?:\.[a-zA-Z0-9]+)?\.css$/.test(route)){file=path.join(root,'src/CampusFlow.Web/wwwroot/css/account-recovery.css');type='text/css';}
  else if(/^\/images\/tenants\/nelson\/logo-stacked(?:\.[a-zA-Z0-9]+)?\.png$/.test(route)){file=path.join(root,'src/CampusFlow.Web/wwwroot/images/tenants/nelson/logo-stacked.png');type='image/png';}
  else{response.writeHead(route==='/favicon.ico'?204:404);response.end();return;}
  response.writeHead(200,{'Content-Type':type,'Cache-Control':'no-store','Referrer-Policy':'no-referrer'});response.end(fs.readFileSync(file));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const browser=await chromium.launch({headless:true,executablePath:process.env.CF028_UI_BROWSER||'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'});
 const scenarios=[];
 try{
  for(const size of [{width:1440,height:1100,label:'desktop'},{width:320,height:1000,label:'mobile'}]){
   const page=await browser.newPage({viewport:{width:size.width,height:size.height}});
   for(const stage of stages){
    await page.goto(`http://127.0.0.1:${server.address().port}/${stage}`,{waitUntil:'networkidle'});
    assert.equal(await page.title(),'Forgot password / Activate account | Nelson University');
    assert.equal(await page.locator('link[rel="stylesheet"]').count(),1);
    assert.match(await page.locator('link[rel="stylesheet"]').getAttribute('href'),/^\/css\/account-recovery(?:\.[a-zA-Z0-9]+)?\.css(?:\?v=[a-zA-Z0-9_-]+)?$/);
    assert.equal(await page.evaluate(()=>getComputedStyle(document.body).backgroundColor),'rgb(245, 243, 248)','Standalone CSS must load');
    assert.equal(await page.locator('.recovery-brand img').evaluate(img=>img.complete&&img.naturalWidth>0),true,'Nelson logo must load');
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'Horizontal overflow '+stage+' '+size.label);
    assert.equal(await page.locator('a[href="/Account/MicrosoftLogin"]').count(),1);
    assert.equal(await page.locator('script').count(),0);
    assert.deepEqual(await page.evaluate(()=>[Object.keys(localStorage).length,Object.keys(sessionStorage).length]),[0,0]);
    if(['complete','unavailable','unknown'].includes(stage))assert.equal(await page.locator('form').count(),0);
    if(stage==='identity'){
     assert.equal(await page.locator('input[type="password"]').count(),3);
     assert.equal(await page.locator('input[name="Input.Password"]').count(),0);
     const ssnTops=await page.locator('.recovery-ssn-fields input').evaluateAll(inputs=>inputs.map(input=>input.getBoundingClientRect().top));
     assert.ok(Math.max(...ssnTops)-Math.min(...ssnTops)<=1,'SSN input tops must align');
     await page.locator('input[name="Input.FirstName"]').fill('Synthetic');
     await page.locator('input[name="Input.LastName"]').fill('Student');
     await page.locator('input[name="Input.SsnFirst"]').fill('000');
     await page.locator('input[name="Input.SsnMiddle"]').fill('00');
     await page.locator('input[name="Input.SsnLast"]').fill('1234');
     await page.locator('input[name="Input.BirthDate"]').fill('2000-01-02');
     assert.equal(await page.locator('form').evaluate(form=>form.checkValidity()),true);
     await page.locator('form').evaluate(form=>form.reset());
    }
    if(stage==='password'){
     assert.equal(await page.locator('input[type="password"]').count(),2);
     assert.equal(await page.locator('input[name="Input.Answer"]').count(),0);
     assert.ok((await page.locator('button[type="submit"]').boundingBox()).width<=size.width);
    }
    await page.locator('h1').click();
    if(output&&['identity','password','unavailable'].includes(stage))await page.screenshot({path:path.join(output,`${stage}-${size.label}.png`),fullPage:true});
    scenarios.push(stage+' '+size.label);
   }
   await page.close();
  }
 }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
 console.log(JSON.stringify({scenarioCount:scenarios.length,scenarios,realRazorFixtures:true,liveServiceCalled:false}));
})().catch(error=>{console.error(error);process.exitCode=1;});
