const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const script = fs.readFileSync(path.join(__dirname, '../../src/CampusFlow.Web/Pages/Admin/Chapel/Attendance.js'), 'utf8');
function fixture() {
    const rows = [1, 2].map(id => `<tr data-attendance-row><td><span data-attendance-status class="chapel-status absent">Absent</span>
        <select name="status" form="r${id}">${['Pending','Present','Absent','Excused'].map(x=>`<option ${x==='Absent'?'selected':''}>${x}</option>`).join('')}</select>
        <input name="comment" form="r${id}" value="" /></td><td data-attendance-source>LegacyImport</td><td>
        <form id="r${id}" data-chapel-autosave method="post" action="/attendance?handler=Update">
        <input type="hidden" name="id" value="${id}" /><input type="hidden" name="revision" value="v1" />
        <input type="hidden" name="__RequestVerificationToken" value="test-antiforgery" />
        <span data-save-message>Up to date</span><button data-save-now>Save change</button><button type="button" data-reload-record hidden>Reload saved record</button></form></td></tr>`).join('');
    return `<main id="chapel-attendance-workspace" data-summary-url="http://chapel.test/attendance?handler=DaySummary"><p id="chapel-save-summary"></p><p id="chapel-totals-message"></p>
        <div data-session="2026-10-01T09:30:00"><span data-session-counts>2 students · 0 present · 2 absent</span><form data-bulk-credit><input name="expectedCount" value="1" /><button>Credit</button></form></div>
        <table><tbody>${rows}</tbody></table></main>`;
}
function field(body, name) { const escaped=name.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');return (body.match(new RegExp(`name="${escaped}"\\r\\n\\r\\n([^\\r]*)`))||[])[1]; }
(async()=>{
    const browser=await chromium.launch({headless:true, ...(process.env.CHAPEL_TEST_BROWSER ? {executablePath:process.env.CHAPEL_TEST_BROWSER} : {})});
    let passed=0;
    async function scenario(name, run) {
        const page=await browser.newPage();
        const server={ rows:{'1':{id:'1',revision:'v1',status:2,comment:'',source:0},'2':{id:'2',revision:'v1',status:2,comment:'',source:0}},calls:[],summaries:0,abort:false,hold:null,holdReload:null,holdSummary:null,scanInOnly:0 };
        await page.route('http://chapel.test/**',async route=>{
            const request=route.request(),url=new URL(request.url()),handler=url.searchParams.get('handler');
            if(request.isNavigationRequest())return route.fulfill({contentType:'text/html',body:fixture()});
            if(handler==='AutoSave'){
                const body=request.postData(),id=field(body,'id'),revision=field(body,'revision');
                assert.equal(field(body,'__RequestVerificationToken'),'test-antiforgery');
                const call={id,revision,status:field(body,'status'),comment:field(body,'comment')};server.calls.push(call);
                if(server.abort)return route.abort('internetdisconnected');
                if(server.hold){const gate=server.hold;server.hold=null;await gate;}
                if(revision!==server.rows[id].revision)return route.fulfill({status:409,json:{message:'This record changed. Reload it before saving.'}});
                server.rows[id]={...server.rows[id],revision:`v${server.calls.length+1}`,status:['Pending','Present','Absent','Excused'].indexOf(call.status),comment:call.comment,source:2};
                return route.fulfill({json:server.rows[id]});
            }
            if(handler==='AttendanceRow'){
                if(server.holdReload)await server.holdReload;
                return route.fulfill({json:server.rows[url.searchParams.get('id')]});
            }
            if(handler==='DaySummary'){
                server.summaries++;const rows=Object.values(server.rows).map(x=>({...x}));
                if(server.holdSummary){const gate=server.holdSummary;server.holdSummary=null;await gate;}
                return route.fulfill({json:[{session:'2026-10-01T09:30:00',total:2,present:rows.filter(x=>x.status===1).length,absent:rows.filter(x=>x.status===2).length,excused:rows.filter(x=>x.status===3).length,scanInOnly:server.scanInOnly,confirmation:`Credit all ${server.scanInOnly} scan-in-only records?`}]});
            }
            return route.fulfill({status:404,body:''});
        });
        page.on('dialog',dialog=>dialog.accept());
        await page.goto('http://chapel.test/attendance');await page.addScriptTag({content:script});
        try {await run(page,server);passed++;process.stdout.write(`PASS ${name}\n`);}finally{await page.close();}
    }
    try {
        await scenario('edits during a save are queued and use the returned revision',async(page,s)=>{
            let release;s.hold=new Promise(r=>release=r);
            await page.locator('input[form="r1"]').fill('First note');
            await page.waitForFunction(()=>document.querySelector('#r1').dataset.saveState==='saving');
            await page.locator('input[form="r1"]').fill('Latest note');release();
            await page.waitForFunction(()=>document.querySelector('#r1').dataset.saveState==='saved');
            assert.equal(s.calls.length,2);assert.equal(s.calls[1].revision,'v2');assert.equal(s.rows['1'].comment,'Latest note');
            assert.equal(await page.locator('input[form="r1"]').inputValue(),'Latest note');
        });
        await scenario('separate rows save independently and daily totals refresh',async(page,s)=>{
            await page.locator('select[form="r1"]').selectOption('Present');await page.locator('select[form="r2"]').selectOption('Excused');
            await page.waitForFunction(()=>Array.from(document.querySelectorAll('[data-chapel-autosave]')).every(x=>x.dataset.saveState==='saved'));
            await page.waitForFunction(()=>document.querySelector('[data-session-counts]').textContent.includes('1 present')&&document.querySelector('[data-session-counts]').textContent.includes('1 excused'));
            assert.equal(s.calls.length,2);assert.equal(s.rows['1'].status,1);assert.equal(s.rows['2'].status,3);assert.ok(s.summaries>0);
            assert.equal(await page.locator('[data-bulk-credit] button').isDisabled(),true);
        });
        await scenario('conflict retains edits and requires a confirmed reload',async(page,s)=>{
            s.rows['1']={...s.rows['1'],revision:'external',comment:'Another staff note',status:3,source:2};
            await page.locator('input[form="r1"]').fill('My unsaved note');await page.waitForFunction(()=>document.querySelector('#r1').dataset.saveState==='conflict');
            assert.equal(await page.locator('input[form="r1"]').inputValue(),'My unsaved note');assert.equal(await page.locator('#r1 [data-save-now]').isDisabled(),true);
            assert.equal(await page.evaluate(()=>{const e=new Event('beforeunload',{cancelable:true});window.dispatchEvent(e);return e.defaultPrevented;}),true);
            let release;s.holdReload=new Promise(r=>release=r);
            await page.locator('#r1 [data-reload-record]').click();await page.waitForFunction(()=>document.querySelector('input[form="r1"]').disabled);
            release();await page.waitForFunction(()=>document.querySelector('#r1').dataset.saveState==='saved');
            assert.equal(await page.locator('input[form="r1"]').inputValue(),'Another staff note');assert.equal(s.calls.length,1);
        });
        await scenario('network failure keeps text and allows explicit retry',async(page,s)=>{
            s.abort=true;await page.locator('input[form="r1"]').fill('Keep this note');await page.waitForFunction(()=>document.querySelector('#r1').dataset.saveState==='error');
            assert.equal(await page.locator('input[form="r1"]').inputValue(),'Keep this note');s.abort=false;await page.locator('#r1 [data-save-now]').click();
            await page.waitForFunction(()=>document.querySelector('#r1').dataset.saveState==='saved');assert.equal(s.rows['1'].comment,'Keep this note');assert.equal(s.calls.length,2);
        });
        await scenario('a stale totals response cannot enable bulk correction after a later save',async(page,s)=>{
            let release;s.scanInOnly=1;s.holdSummary=new Promise(r=>release=r);
            await page.locator('select[form="r1"]').selectOption('Present');
            await page.waitForFunction(()=>document.querySelector('#r1').dataset.saveState==='saved');
            while(s.summaries===0)await page.waitForTimeout(10);
            await page.locator('select[form="r2"]').selectOption('Excused');
            await page.waitForFunction(()=>document.querySelector('#r2').dataset.saveState==='saved');
            release();await page.waitForTimeout(100);
            assert.equal(await page.locator('[data-bulk-credit] button').isDisabled(),true);
            await page.waitForFunction(()=>document.querySelector('[data-session-counts]').textContent.includes('1 excused')&&!document.querySelector('[data-bulk-credit] button').disabled);
            assert.ok(s.summaries>=2);
        });
        await scenario('reverting an edit before debounce does not write or change source',async(page,s)=>{
            await page.locator('input[form="r1"]').fill('Temporary');await page.locator('input[form="r1"]').fill('');await page.waitForTimeout(850);
            assert.equal(s.calls.length,0);assert.equal(s.rows['1'].source,0);
        });
        console.log(`${passed} browser scenarios passed.`);
    } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
