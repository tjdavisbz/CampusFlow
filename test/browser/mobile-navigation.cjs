const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.CAMPUSFLOW_PLAYWRIGHT_PATH||'playwright');
const root=path.resolve(__dirname,'../..');
const globals=fs.readFileSync(path.join(root,'src/CampusFlow.Web/wwwroot/global-scripts.js'),'utf8');
// Run the installed LeptonX toggle, rather than imitating the vendor's click handler.
const theme=fs.readFileSync(process.env.CAMPUSFLOW_THEME_SCRIPT,'utf8');
const jquery=fs.readFileSync(path.join(root,'src/CampusFlow.Web/wwwroot/libs/jquery/jquery.js'),'utf8');
const bootstrap=fs.readFileSync(path.join(root,'src/CampusFlow.Web/wwwroot/libs/bootstrap/js/bootstrap.bundle.min.js'),'utf8');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'});let passed=0;
 try{
  for(const {width,student,dashboard} of [...[320,390,767,768,1440].map(width=>({width,student:true,dashboard:true})),{width:390,student:false,dashboard:true},{width:390,student:true,dashboard:false}]){
   const page=await browser.newPage({viewport:{width,height:900}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
   await page.route('http://navigation.test/**',r=>r.fulfill({contentType:'text/html',body:`<!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1"><style>.d-none{display:none!important}.lpx-mobile-navbar-container{display:none}@media(max-width:767px){.lpx-mobile-navbar-container{display:block}}</style></head><body>
    <div class="lpx-mobile-navbar-container"><div class="user-menu"><i class="lpx-toggle" data-lpx-toggle="mobile-navbar" aria-hidden="true">Menu</i></div><nav class="d-none" id="mobile-navbar"><a href="/Schedule">Schedule</a></nav></div>
    <main ${dashboard?`data-auto-open-mobile-menu="${student}"`:''}>Dashboard content</main>
    <script>${jquery}</script><script>${bootstrap}</script><script>${theme}</script><script>${globals}</script><script>document.addEventListener('DOMContentLoaded',()=>leptonx.init.run());</script></body></html>`}));
   await page.goto('http://navigation.test/');
   const shouldOpen=width<=767&&student&&dashboard;
   assert.equal(await page.locator('#mobile-navbar').evaluate(e=>!e.classList.contains('d-none')),shouldOpen);
   if(shouldOpen){
    const toggle=page.locator('[data-lpx-toggle="mobile-navbar"]');assert.equal(await toggle.getAttribute('aria-expanded'),'true');
    await toggle.click();assert.equal(await toggle.getAttribute('aria-expanded'),'false');
    await page.setViewportSize({width:width===390?400:390,height:900});
    assert.equal(await toggle.getAttribute('aria-expanded'),'false');
    await toggle.focus();await page.keyboard.press('Enter');assert.equal(await toggle.getAttribute('aria-expanded'),'true');
    await page.keyboard.press('Escape');assert.equal(await toggle.getAttribute('aria-expanded'),'false');
    await page.keyboard.press('Space');assert.equal(await toggle.getAttribute('aria-expanded'),'true');
    assert.equal(await toggle.getAttribute('aria-hidden'),null);
   }
   assert.deepEqual(errors,[]);passed++;await page.close();
  }
  console.log(JSON.stringify({passed,actualTheme:true,keyboardAndEscape:true,desktopAndStaffUnchanged:true}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
