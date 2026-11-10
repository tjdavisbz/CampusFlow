const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.CAMPUSFLOW_PLAYWRIGHT_PATH||'playwright');
const root=path.resolve(__dirname,'../..');
const css=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/BillApproval.css'),'utf8');
const pageScript=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/BillApproval.js'),'utf8');
const view=fs.readFileSync(path.join(root,'src/CampusFlow.Web/Pages/BillApproval.cshtml'),'utf8');
// Use the installed theme's actual menu implementation, rather than a mock toggle.
const dll=fs.readFileSync(process.env.CAMPUSFLOW_LEPTON_DLL||path.join(root,'src/CampusFlow.Web/bin/Debug/net10.0/Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.dll'));
const start=dll.indexOf('var leptonx=function('),ending='}({},bootstrap);',end=dll.indexOf(ending,start);
assert.ok(start>=0&&end>start,'Build the web project before running this test.');
const themeScript=dll.subarray(start,end+Buffer.byteLength(ending)).toString('utf8');
const escape=value=>value.replace(/[&<>"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
const noticeStart=view.indexOf('<aside class="pending-loan-notice"');
let notice=view.slice(noticeStart,view.indexOf('</aside>',noticeStart)+8)
 .replace(/<ul>[\s\S]*?<\/ul>/,'<ul><li><span>'+escape('Pending private loan & '+ 'extended loan description '.repeat(5))+'</span><strong>$1,250.00</strong></li></ul>')
 .replace('asp-page="/FinancialAid" asp-route-term="@Model.ExternalTermId" asp-route-openTerm="@Model.TermCode"','href="/FinancialAid?term=269&amp;openTerm=B27C"');
const fixture=`<!doctype html><meta name="viewport" content="width=device-width, initial-scale=1"><style>
*{box-sizing:border-box}body{margin:0;padding:12px;background:#f4f4f7;--tenant-primary:#502d7f;--tenant-primary-dark:#38205a;--tenant-accent:#ffd987;--tenant-text:#111;--tenant-surface:#f8f8fb;--tenant-body-font:Arial;--tenant-heading-font:Arial}
#lpx-wrapper{display:grid;grid-template-columns:minmax(0,1fr);gap:20px}#lpx-sidebar{padding:12px;background:#502d7f;border-radius:10px;align-self:start}#lpx-sidebar a{display:block;padding:10px;color:white;text-decoration:none}#lpx-sidebar ul{list-style:none;padding-left:12px;margin:0}.lpx-inner-menu.collapsed{display:none}#lpx-sidebar .selected{font-weight:bold}.dd-icon{display:inline-block}
@media(min-width:1100px){#lpx-wrapper{grid-template-columns:220px minmax(0,1fr)}}${css}</style>
<div id="lpx-wrapper"><aside id="lpx-sidebar"><div id="desktop-sidebar"><div class="outer-menu-item"><a class="lpx-menu-item selected" href="javascript:void(0)" id="student-menu">Student<i class="dd-icon"></i></a><ul class="lpx-inner-menu"><li class="lpx-inner-menu-item"><a class="lpx-menu-item" href="/Schedule">My Schedule</a></li><li class="lpx-inner-menu-item"><a class="lpx-menu-item selected" href="/Billing" id="billing-menu">Billing</a></li></ul></div></div></aside>
<main class="approval-page" data-initial-step="review"><header><h1>Review and approve your bill</h1><p>Spring 2027</p></header><nav class="stepper no-payment" aria-label="Bill approval progress"><button data-step-target="review">Review bill</button><button data-step-target="agreement">Agreement</button></nav>
<section class="approval-step active" data-step="review"><h2>Review your semester</h2><details open><summary>Expected financial aid <strong>$500.00</strong></summary><p>Accepted aid only</p></details>${notice}<button class="primary next" data-next="agreement">Review agreement</button></section><section class="approval-step" data-step="agreement" hidden><h2>Agreement</h2><button data-next="review">Back to review</button></section></main></div>`;
(async()=>{const browser=await chromium.launch({headless:true,...(process.env.CHAPEL_TEST_BROWSER?{executablePath:process.env.CHAPEL_TEST_BROWSER}:{})});
try{for(const width of [320,390,768,1440]){
 const page=await browser.newPage({viewport:{width,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://bill.test/**',r=>r.fulfill({contentType:'text/html',body:fixture}));await page.goto('http://bill.test/BillApproval?term=B27C');
 await page.addScriptTag({content:'var bootstrap={};'+themeScript});await page.evaluate(()=>leptonx.init.initializers.get('initSideMenu')());
 await page.addScriptTag({content:pageScript});await page.evaluate(()=>document.dispatchEvent(new Event('DOMContentLoaded')));
 const menu=page.locator('#student-menu + .lpx-inner-menu');assert.equal(await menu.isVisible(),true);assert.equal(await page.locator('#billing-menu').evaluate(x=>x.classList.contains('selected')),true);
 assert.match(await page.locator('.pending-loan-notice').textContent(),/do not reduce the balance/);
 assert.equal(await page.locator('.pending-loan-notice li').count(),1);
 const url=new URL(await page.locator('.pending-loan-notice a').evaluate(x=>x.href));assert.equal(url.pathname,'/FinancialAid');assert.equal(url.searchParams.get('term'),'269');assert.equal(url.searchParams.get('openTerm'),'B27C');
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
 if(process.env.CAMPUSFLOW_SCREENSHOT_DIR&&[390,1440].includes(width)){fs.mkdirSync(process.env.CAMPUSFLOW_SCREENSHOT_DIR,{recursive:true});await page.screenshot({path:path.join(process.env.CAMPUSFLOW_SCREENSHOT_DIR,'bill-guidance-'+width+'.png'),fullPage:true});}
 await page.locator('[data-next="agreement"]').click();assert.equal(await menu.isVisible(),true);
 await page.locator('[data-next="review"]').click();assert.equal(await menu.isVisible(),true);
 await page.locator('#student-menu').click();assert.equal(await menu.isVisible(),false);
 await page.locator('#student-menu').click();assert.equal(await menu.isVisible(),true);
 assert.deepEqual(errors,[]);console.log('PASS actual theme keeps Billing/Student open, manual toggle, term-specific aid link, notice and overflow at '+width+'px');await page.close();
}}finally{await browser.close();}})().catch(e=>{console.error(e);process.exit(1)});
