const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const script = fs.readFileSync(path.join(__dirname, '../../src/CampusFlow.Web/Pages/Admin/StudentBilling/CostTypes.js'), 'utf8');
const fixture = `<div id="cost-types"><input name="__RequestVerificationToken" value="test-token" type="hidden" />
<p id="cost-auto-status"></p><p id="cost-auto-last"></p><div id="cost-auto-review-section" hidden><ul id="cost-auto-reviews"></ul><input id="cost-auto-reviewed" type="checkbox" /></div><button id="cost-auto-toggle" disabled></button><button id="cost-auto-refresh"></button>
<button id="cost-save">Save rules</button><button id="cost-add">Add</button><input id="cost-reason" /><input id="cost-search" />
<input id="cost-student" type="number" min="1" max="2147483647" /><button id="cost-preview">Preview</button><button id="cost-run" disabled>Run now</button>
<p id="cost-message"></p><p id="cost-run-note"></p><p id="cost-summary"></p><p id="cost-limit"></p>
<select id="cost-filter">${['Would change','Unmapped','Invalid lookup','Duplicate record','Already correct','All'].map(x=>`<option>${x}</option>`).join('')}</select>
<table><tbody id="cost-rules"></tbody></table><table><tbody id="cost-preview-rows"></tbody></table><ul id="cost-results"></ul><div id="cost-history"></div></div>`;
(async()=>{
    const browser=await chromium.launch({headless:true,...(process.env.CHAPEL_TEST_BROWSER?{executablePath:process.env.CHAPEL_TEST_BROWSER}:{})});let passed=0;
    async function scenario(name,options,run){
        const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
        const state={calls:[],setup:{automatic:{enabled:!!options.autoEnabled,reviewRuns:options.needsReview?[{id:'review-run',state:'Needs review',created:new Date().toISOString()}]:[],lastOutcome:'Automatic updates have not run.',nextRunAtUtc:options.autoEnabled?new Date(Date.now()+900000).toISOString():null},revision:options.unsaved?'':'v1',canRun:!options.disabled,rules:[{priority:1,campus:'MAIN',attendance:'Distance Education',degreeMatch:'ANY',degree:null,costType:'DE',costTypeId:4,enabled:true}],choices:[{id:4,label:'DE',description:'Distance Education'}]},rows:[{name:'Test account',studentId:'100',studentUid:8313,term:'Spring 2027',campus:'MAIN',attendance:'Distance Education',degree:null,currentLabel:'AC',currentId:26,proposedLabel:'DE',proposedId:4,action:options.correct?'Already correct':'Would change'}]};
        await page.route('http://cost.test/**',route=>{
            const req=route.request(),url=new URL(req.url()),handler=url.searchParams.get('handler');
            if(req.isNavigationRequest())return route.fulfill({contentType:'text/html',body:fixture});
            state.calls.push({handler,method:req.method(),uid:url.searchParams.get('studentUid'),body:req.postData()});
            assert.equal(req.headers().requestverificationtoken,'test-token');
            if(handler==='Setup')return options.failedSetup?route.fulfill({status:503,json:{message:'Elements unavailable'}}):route.fulfill({json:state.setup});
            if(handler==='Automation'){
                if(req.method()==='POST'){
                    const input=JSON.parse(req.postData());state.setup.automatic={...state.setup.automatic,enabled:input.enabled,reviewRuns:[],nextRunAtUtc:input.enabled?new Date(Date.now()+900000).toISOString():null};
                }
                return route.fulfill({json:state.setup.automatic});
            }
            if(handler==='History')return route.fulfill({json:[]});
            if(handler==='Save'&&options.forbiddenSave)return route.fulfill({status:403,contentType:'text/plain',body:'Student impersonation is read-only. Exit student view before making changes.'});
            if(handler==='Save'){state.setup={...JSON.parse(req.postData()),revision:'v2'};return route.fulfill({json:state.setup});}
            if(handler==='Preview')return route.fulfill({json:{runId:options.correct||options.disabled?null:'reviewed-preview',rows:state.rows,message:options.correct?'No changes are ready.':options.disabled?'Execution disabled.':'1 change ready. Review before running.'}});
            if(handler==='Run')return route.fulfill({json:[{studentUid:8313,termId:269,outcome:'Verified updated'}]});
            return route.fulfill({status:404,body:''});
        });
        await page.goto('http://cost.test/admin');if(options.readOnly)await page.locator('#cost-types').evaluate(x=>x.dataset.readOnly='true');await page.addScriptTag({content:script});
        if(options.failedSetup)await page.waitForFunction(()=>document.querySelector('#cost-message').textContent==='Elements unavailable');
        else await page.waitForFunction(()=>document.querySelector('#cost-history')&&document.querySelector('#cost-rules tr')&&document.querySelector('#cost-run-note').textContent!=='Working…');
        try{await run(page,state);assert.deepEqual(errors,[]);passed++;console.log(`PASS ${name}`);}finally{await page.close();}
    }
    try{
        await scenario('automation can be turned off even when Elements prevents loading the rules',{autoEnabled:true,failedSetup:true},async(page,s)=>{
            assert.equal(await page.locator('#cost-auto-toggle').isDisabled(),false);
            await page.locator('#cost-auto-toggle').click();await page.waitForFunction(()=>document.querySelector('#cost-auto-status').textContent.startsWith('Off'));
            assert.deepEqual(JSON.parse(s.calls.find(x=>x.handler==='Automation'&&x.method==='POST').body),{enabled:false,revision:'',reviewedRunIds:[]});
            assert.equal(s.calls.filter(x=>x.handler==='Run'||x.handler==='Preview'||x.handler==='Save').length,0);
        });
        await scenario('automatic enable is explicit, saved, unscoped by the manual UID and does not immediately apply',{},async(page,s)=>{
            assert.match(await page.locator('#cost-auto-status').innerText(),/Off/);
            await page.locator('#cost-student').fill('8313');
            page.once('dialog',d=>d.dismiss());await page.locator('#cost-auto-toggle').click();assert.equal(s.calls.filter(x=>x.handler==='Automation'&&x.method==='POST').length,0);
            page.once('dialog',d=>d.accept());await page.locator('#cost-auto-toggle').click();
            await page.waitForFunction(()=>document.querySelector('#cost-auto-status').textContent.startsWith('On'));
            const call=s.calls.find(x=>x.handler==='Automation'&&x.method==='POST');assert.equal(call.uid,null);assert.deepEqual(JSON.parse(call.body),{enabled:true,revision:'v1',reviewedRunIds:[]});
            assert.equal(s.calls.filter(x=>x.handler==='Run'||x.handler==='Preview').length,0);
        });
        await scenario('turning automation off works with unsaved rule edits and preserves them',{autoEnabled:true},async(page,s)=>{
            const attendance=page.locator('[aria-label="attendance, rule 1"]');await attendance.fill('Changed attendance');await attendance.blur();
            assert.equal(await page.locator('#cost-auto-toggle').isDisabled(),false);
            await page.locator('#cost-auto-toggle').click();await page.waitForFunction(()=>document.querySelector('#cost-auto-status').textContent.startsWith('Off'));
            assert.equal(await attendance.inputValue(),'Changed attendance');assert.equal(await page.locator('#cost-preview').isDisabled(),true);
            assert.equal(JSON.parse(s.calls.find(x=>x.handler==='Automation'&&x.method==='POST').body).enabled,false);
        });
        await scenario('unconfirmed history requires explicit review before automatic enable',{needsReview:true},async(page,s)=>{
            assert.equal(await page.locator('#cost-auto-toggle').isDisabled(),true);await page.locator('#cost-auto-reviewed').check();
            page.once('dialog',d=>d.accept());await page.locator('#cost-auto-toggle').click();await page.waitForFunction(()=>document.querySelector('#cost-auto-status').textContent.startsWith('On'));
            assert.deepEqual(JSON.parse(s.calls.find(x=>x.handler==='Automation'&&x.method==='POST').body).reviewedRunIds,['review-run']);
        });
        await scenario('read-only views cannot toggle automation',{readOnly:true,autoEnabled:true},async(page,s)=>{
            assert.equal(await page.locator('#cost-auto-toggle').isDisabled(),true);assert.equal(s.calls.filter(x=>x.handler==='Automation'&&x.method==='POST').length,0);
        });
        await scenario('unsaved rules explain the disabled run; saving and previewing never auto-apply', {unsaved:true},async(page,s)=>{
            assert.match(await page.locator('#cost-run-note').innerText(),/Step 1/);assert.equal(await page.locator('#cost-preview').isDisabled(),true);
            await page.locator('#cost-reason').fill('Review nonprod rules');await page.locator('#cost-save').click();
            await page.waitForFunction(()=>document.querySelector('#cost-run-note').textContent.includes('Step 2'));
            assert.equal(await page.locator('#cost-run').isDisabled(),true);await page.locator('#cost-preview').click();
            await page.waitForFunction(()=>!document.querySelector('#cost-run').disabled);
            assert.equal(s.calls.filter(x=>x.handler==='Run').length,0);
            page.once('dialog',d=>d.dismiss());await page.locator('#cost-run').click();assert.equal(s.calls.filter(x=>x.handler==='Run').length,0);
            page.once('dialog',d=>d.accept());await page.locator('#cost-run').click();
            await page.waitForFunction(()=>document.querySelector('#cost-message').textContent.includes('Run finished'));
            assert.equal(s.calls.filter(x=>x.handler==='Run').length,1);assert.equal(await page.locator('#cost-run').isDisabled(),true);
        });
        await scenario('saved rules explain that Preview finds accounts first',{},async(page,s)=>{
            assert.match(await page.locator('#cost-run-note').innerText(),/Click Preview to find accounts/);
            assert.equal(await page.locator('#cost-run').isDisabled(),true);assert.equal(s.calls.filter(x=>x.handler==='Run').length,0);
        });
        await scenario('an already-correct account is shown instead of an empty Would change filter',{correct:true},async(page,s)=>{
            await page.locator('#cost-preview').click();await page.waitForFunction(()=>document.querySelector('#cost-filter').value==='All');
            assert.match(await page.locator('#cost-preview-rows').innerText(),/Test account/);assert.match(await page.locator('#cost-preview-rows').innerText(),/Already correct/);
            assert.equal(await page.locator('#cost-run').isDisabled(),true);assert.equal(s.calls.filter(x=>x.handler==='Run').length,0);
        });
        await scenario('a long Student ID is rejected without broadening preview scope',{},async(page,s)=>{
            await page.locator('#cost-student').fill('10000000001');assert.equal(await page.locator('#cost-preview').isDisabled(),true);
            assert.match(await page.locator('#cost-run-note').innerText(),/not the longer Student ID/);assert.equal(s.calls.filter(x=>x.handler==='Preview').length,0);
            await page.locator('#cost-student').fill('8313');await page.locator('#cost-preview').click();
            await page.waitForFunction(()=>!document.querySelector('#cost-run').disabled);
            assert.equal(s.calls.find(x=>x.handler==='Preview').uid,'8313');
        });
        await scenario('changing the student clears the old preview and prevents reusing it',{},async(page,s)=>{
            await page.locator('#cost-preview').click();await page.waitForFunction(()=>!document.querySelector('#cost-run').disabled);
            await page.locator('#cost-student').fill('8313');assert.equal(await page.locator('#cost-run').isDisabled(),true);
            assert.equal(await page.locator('#cost-preview-rows tr').count(),0);assert.match(await page.locator('#cost-run-note').innerText(),/Step 2/);
        });
        await scenario('unsaved rule edits require another save and preview',{},async(page,s)=>{
            await page.locator('#cost-preview').click();await page.waitForFunction(()=>!document.querySelector('#cost-run').disabled);
            const campus=page.locator('#cost-rules input[type="text"]').first();await campus.fill('OTHER');await campus.press('Tab');
            assert.equal(await page.locator('#cost-run').isDisabled(),true);assert.equal(await page.locator('#cost-preview').isDisabled(),true);
            assert.match(await page.locator('#cost-run-note').innerText(),/Save your rule changes/);
        });
        await scenario('environment-disabled execution stays blocked while readonly preview remains available',{disabled:true},async(page,s)=>{
            assert.match(await page.locator('#cost-run-note').innerText(),/disabled for this environment/);
            await page.locator('#cost-preview').click();await page.waitForFunction(()=>document.querySelector('#cost-preview-rows tr'));
            assert.equal(await page.locator('#cost-run').isDisabled(),true);assert.equal(s.calls.filter(x=>x.handler==='Run').length,0);
        });
        await scenario('read-only student view clearly blocks save, preview and run',{readOnly:true},async(page,s)=>{
            for(const name of ['save','add','preview','run'])assert.equal(await page.locator('#cost-'+name).isDisabled(),true);
            assert.match(await page.locator('#cost-run-note').innerText(),/Exit student or faculty view/);
            assert.equal(s.calls.filter(x=>['Save','Preview','Run'].includes(x.handler)).length,0);
        });
        await scenario('a read-only 403 after switching views explains how to recover',{forbiddenSave:true},async(page,s)=>{
            await page.locator('#cost-reason').fill('Review');await page.locator('#cost-save').click();
            await page.waitForFunction(()=>document.querySelector('#cost-message').textContent.includes('Exit student or faculty view'));
            assert.equal(s.calls.filter(x=>x.handler==='Run').length,0);
            for(const name of ['save','preview','run'])assert.equal(await page.locator('#cost-'+name).isDisabled(),true);
        });
        console.log(`${passed} cost-type browser scenarios passed.`);
    }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
