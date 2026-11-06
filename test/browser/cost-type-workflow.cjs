const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const script = fs.readFileSync(path.join(__dirname, '../../src/CampusFlow.Web/Pages/Admin/StudentBilling/CostTypes.js'), 'utf8');
const fixture = `<div id="cost-types"><input name="__RequestVerificationToken" value="test-token" type="hidden" />
<button id="cost-save">Save rules</button><button id="cost-add">Add</button><input id="cost-reason" /><input id="cost-search" />
<input id="cost-student" type="number" min="1" max="2147483647" /><button id="cost-preview">Preview</button><button id="cost-run" disabled>Run now</button>
<p id="cost-message"></p><p id="cost-run-note"></p><p id="cost-summary"></p><p id="cost-limit"></p>
<select id="cost-filter">${['Would change','Unmapped','Invalid lookup','Duplicate record','Already correct','All'].map(x=>`<option>${x}</option>`).join('')}</select>
<table><tbody id="cost-rules"></tbody></table><table><tbody id="cost-preview-rows"></tbody></table><ul id="cost-results"></ul><div id="cost-history"></div></div>`;
(async()=>{
    const browser=await chromium.launch({headless:true,...(process.env.CHAPEL_TEST_BROWSER?{executablePath:process.env.CHAPEL_TEST_BROWSER}:{})});let passed=0;
    async function scenario(name,options,run){
        const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
        const state={calls:[],setup:{revision:options.unsaved?'':'v1',canRun:!options.disabled,rules:[{priority:1,campus:'MAIN',attendance:'Distance Education',degreeMatch:'ANY',degree:null,costType:'DE',costTypeId:4,enabled:true}],choices:[{id:4,label:'DE',description:'Distance Education'}]},rows:[{name:'Test account',studentId:'100',studentUid:8313,term:'Spring 2027',campus:'MAIN',attendance:'Distance Education',degree:null,currentLabel:'AC',currentId:26,proposedLabel:'DE',proposedId:4,action:options.correct?'Already correct':'Would change'}]};
        await page.route('http://cost.test/**',route=>{
            const req=route.request(),url=new URL(req.url()),handler=url.searchParams.get('handler');
            if(req.isNavigationRequest())return route.fulfill({contentType:'text/html',body:fixture});
            state.calls.push({handler,uid:url.searchParams.get('studentUid'),body:req.postData()});
            assert.equal(req.headers().requestverificationtoken,'test-token');
            if(handler==='Setup')return route.fulfill({json:state.setup});
            if(handler==='History')return route.fulfill({json:[]});
            if(handler==='Save'&&options.forbiddenSave)return route.fulfill({status:403,contentType:'text/plain',body:'Student impersonation is read-only. Exit student view before making changes.'});
            if(handler==='Save'){state.setup={...JSON.parse(req.postData()),revision:'v2'};return route.fulfill({json:state.setup});}
            if(handler==='Preview')return route.fulfill({json:{runId:options.correct||options.disabled?null:'reviewed-preview',rows:state.rows,message:options.correct?'No changes are ready.':options.disabled?'Execution disabled.':'1 change ready. Review before running.'}});
            if(handler==='Run')return route.fulfill({json:[{studentUid:8313,termId:269,outcome:'Verified updated'}]});
            return route.fulfill({status:404,body:''});
        });
        await page.goto('http://cost.test/admin');if(options.readOnly)await page.locator('#cost-types').evaluate(x=>x.dataset.readOnly='true');await page.addScriptTag({content:script});
        await page.waitForFunction(()=>document.querySelector('#cost-history')&&document.querySelector('#cost-rules tr')&&document.querySelector('#cost-run-note').textContent!=='Working…');
        try{await run(page,state);assert.deepEqual(errors,[]);passed++;console.log(`PASS ${name}`);}finally{await page.close();}
    }
    try{
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
