(() => {
    'use strict';
    const root=document.getElementById('cost-types'), el=n=>document.getElementById('cost-'+n);
    let readOnly=root.dataset.readOnly==='true';
    let setup=null,preview=null,dirty=false,busy=false,automatic=null,reviewKey="";
    function message(text){el('message').textContent=text;}
    function validStudentScope(){
        if(el('student').validity.badInput)return false;
        const value=el('student').value.trim();
        return value===''||(/^\d+$/.test(value)&&Number(value)>0&&Number(value)<=2147483647);
    }
    function resetPreview(){
        preview=null;el('preview-rows').replaceChildren();el('summary').textContent='';el('limit').textContent='';
    }
    function controls(){
        for(const n of ['save','add','preview']) el(n).disabled=readOnly||busy||!setup||(n==='preview'&&(dirty||!setup?.revision||!validStudentScope()));
        el('run').disabled=readOnly||busy||dirty||!preview?.runId||!setup?.canRun||!validStudentScope();
        let reason;
        if(readOnly) reason='Exit student or faculty view using the banner at the top, then return here to save, preview or run updates.';
        else if(busy) reason='Working…';
        else if(!setup) reason='Rules could not be loaded. Reload this page before continuing.';
        else if(!setup.canRun) reason='Applying cost types is disabled for this environment. You can preview saved rules.';
        else if(!setup.revision) reason='Step 1: Enter a change reason and choose Save rules. Then preview the accounts before running.';
        else if(dirty) reason='Save your rule changes, then create a new preview before running.';
        else if(!validStudentScope()) reason='Enter a valid Elements UID, not the longer Student ID, or leave it blank to preview all accounts.';
        else if(!preview) reason='Step 2: Click Preview to find accounts and review proposed changes. Run now becomes available when the preview has changes ready.';
        else reason=preview.message||'No changes are ready. Create a fresh preview before running.';
        el('run-note').textContent=reason;
        root.querySelectorAll('#cost-rules input,#cost-rules select').forEach(x=>x.disabled=readOnly||busy);
        el('student').disabled=readOnly||busy;
        const reviews=automatic?.reviewRuns??[],running=reviews.some(x=>x.state==='Running');
        el('auto-toggle').disabled=readOnly||busy||!automatic||(!automatic.enabled&&
            (!setup||dirty||!setup.revision||!setup.canRun||!!automatic.runningRunId||running||(reviews.length>0&&!el('auto-reviewed').checked)));
        el('auto-refresh').disabled=busy;
        el('auto-reviewed').disabled=readOnly||busy||running;

    }
    async function request(handler,body,params={}){
        const url=new URL(location.href);url.search=new URLSearchParams({handler,...params});
        const r=await fetch(url,{method:body===undefined&&['Setup','History','Automation'].includes(handler)?'GET':'POST',headers:{'Content-Type':'application/json',RequestVerificationToken:root.querySelector('[name="__RequestVerificationToken"]').value},body:body===undefined?undefined:JSON.stringify(body)});
        const text=await r.text();
        let data;try{data=JSON.parse(text);}catch{
            if(r.status===403&&text.includes('impersonation is read-only.')) {
                readOnly=true;
                throw new Error('Exit student or faculty view using the banner at the top, then return here to save, preview or run updates.');
            }
            throw new Error('Could not read the response. Reload and check the last run before retrying.');
        }
        if(!r.ok)throw new Error(data?.error?.message||data?.message||'Request failed. Reload before retrying.');return data;
    }
    async function work(action){if(busy)return;busy=true;controls();try{await action();}catch(e){message(e.message);}finally{busy=false;controls();}}
    function changed(){dirty=true;resetPreview();message('Unsaved edits. Save and preview before running.');controls();}
    function cell(tr,text){const td=document.createElement('td');td.textContent=text??'';tr.append(td);return td;}
    async function history(){
        const rows=await request('History');el('history').replaceChildren();
        for(const r of rows){const details=document.createElement('details');const summary=document.createElement('summary');summary.textContent=`${new Date(r.created).toLocaleString()} — ${r.automatic?'Automatic':'Manual'} — ${r.state}${r.reviewed?' (reviewed)':''} (${r.id})`;details.append(summary);if(r.summary){const note=document.createElement('p');note.textContent=r.summary;details.append(note);}
            for(const x of r.results){const p=document.createElement('p');p.textContent=`Student UID ${x.studentUid}, term ${x.termId}: ${x.outcome}`;details.append(p);}el('history').append(details);}
    }
    function renderAutomatic(){
        if(!automatic)return;
        const date=x=>x?new Date(x).toLocaleString():'—';
        el('auto-status').textContent=automatic.enabled?`On — every 15 minutes. Next check: ${date(automatic.nextRunAtUtc)}.`:'Off — no new automatic updates will start.';
        if(automatic.runningRunId)el('auto-status').textContent+=' A run is in progress or awaiting review; another run cannot overlap it.';
        el('auto-last').textContent=`${automatic.lastOutcome||'Automatic updates have not run.'}${automatic.lastStartedAtUtc?' Last started: '+date(automatic.lastStartedAtUtc)+'.':''}${automatic.lastFinishedAtUtc?' Last finished: '+date(automatic.lastFinishedAtUtc)+'.':''}`;
        el('auto-toggle').textContent=automatic.enabled?'Turn off automatic updates':'Turn on automatic updates';
        const reviews=automatic.reviewRuns??[],key=reviews.map(x=>`${x.id}:${x.state}`).join(',');
        if(key!==reviewKey){el('auto-reviewed').checked=false;reviewKey=key;}
        el('auto-review-section').hidden=automatic.enabled||reviews.length===0;
        el('auto-reviews').replaceChildren();
        for(const run of reviews){const li=document.createElement('li');li.textContent=`${date(run.created)} — ${run.state} (${run.id})`;el('auto-reviews').append(li);}
    }
    async function refreshAutomatic(){automatic=await request('Automation');renderAutomatic();controls();}
    el('auto-reviewed').onchange=controls;
    el('auto-refresh').onclick=()=>work(refreshAutomatic);
    el('auto-toggle').onclick=()=>{
        if(!automatic)return;const enabled=!automatic.enabled;
        if(enabled&&!confirm('Run the saved cost-type rules automatically every 15 minutes for all records in the current and future term scope? This may affect billing. The first check starts in about 15 minutes.'))return;
        work(async()=>{
            automatic=await request('Automation',{enabled,revision:setup?.revision??'',reviewedRunIds:el('auto-reviewed').checked?(automatic.reviewRuns??[]).map(x=>x.id):[]});
            renderAutomatic();message(enabled?'Automatic updates enabled. The first check is scheduled in about 15 minutes.':'Automatic updates turned off. Any update already sent to Elements will finish verification.');
            await history();
        });
    };
    function renderRules(){
        const target=el('rules');target.replaceChildren();const search=el('search').value.toLowerCase();
        for(const r of setup.rules){
            if(search&&!JSON.stringify(r).toLowerCase().includes(search))continue;
            const tr=document.createElement('tr');target.append(tr);
            for(const [key,type] of [['enabled','checkbox'],['priority','number'],['campus','text'],['attendance','text']]){
                const input=document.createElement('input');input.type=type;input.className=type==='checkbox'?'form-check-input':'form-control';input.setAttribute('aria-label',`${key}, rule ${r.priority}`);
                if(type==='checkbox')input.checked=r[key];else input.value=r[key]??'';
                if(type==='number')input.min='1';
                input.onchange=()=>{r[key]=type==='checkbox'?input.checked:type==='number'?Number(input.value):key==='campus'&&input.value===''?null:input.value;changed();};cell(tr,'').append(input);
            }
            const mode=document.createElement('select');mode.className='form-select';mode.setAttribute('aria-label',`Degree condition, rule ${r.priority}`);
            for(const [v,label] of [['ANY','Any'],['NULL','Missing'],['EXACT','Exact']])mode.add(new Option(label,v));mode.value=r.degreeMatch;
            mode.onchange=()=>{r.degreeMatch=mode.value;changed();};cell(tr,'').append(mode);
            const degree=document.createElement('input');degree.className='form-control';degree.value=r.degree??'';degree.setAttribute('aria-label',`Degree, rule ${r.priority}`);degree.onchange=()=>{r.degree=degree.value;changed();};cell(tr,'').append(degree);
            const cost=document.createElement('select');cost.className='form-select';cost.setAttribute('aria-label',`Cost type, rule ${r.priority}`);cost.add(new Option('Choose a cost type',''));
            for(const c of setup.choices)cost.add(new Option(`${c.label} — ${c.description} (#${c.id})`,String(c.id)));
            cost.value=r.costTypeId==null?'':String(r.costTypeId);if(r.costTypeId==null){cost.options[0].text=`Unmapped: ${r.costType||'(choose a cost type)'}`;}cost.onchange=()=>{const c=setup.choices.find(x=>String(x.id)===cost.value);r.costTypeId=c?.id??null;r.costType=c?.label??'';changed();};cell(tr,'').append(cost);
        }controls();
    }
    function renderPreview(){
        const target=el('preview-rows');target.replaceChildren();if(!preview)return;
        const counts={};preview.rows.forEach(r=>counts[r.action]=(counts[r.action]||0)+1);el('summary').textContent=Object.entries(counts).map(([k,v])=>`${k}: ${v}`).join(' · ');
        const rows=preview.rows.filter(r=>el('filter').value==='All'||r.action===el('filter').value);
        for(const r of rows.slice(0,200)){const tr=document.createElement('tr');target.append(tr);for(const v of [`${r.name} (${r.studentId})`,r.term,`${r.campus} / ${r.attendance} / ${r.degree??'(missing)'}`,`${r.currentLabel} (#${r.currentId})`,r.proposedLabel==null?'—':`${r.proposedLabel} (#${r.proposedId??'?'})`,r.action])cell(tr,v);}
        el('limit').textContent=rows.length>200?`Showing 200 of ${rows.length} records. Use a student UID to narrow the preview.`:'';
    }
    el('search').oninput=()=>setup&&renderRules();el('filter').onchange=renderPreview;
    el('student').oninput=()=>{resetPreview();message('Student scope changed. Click Preview to find accounts for this scope.');controls();};
    el('add').onclick=()=>{setup.rules.push({priority:Math.max(...setup.rules.map(x=>x.priority),0)+1,campus:null,attendance:'',degreeMatch:'ANY',degree:null,costType:'',costTypeId:null,enabled:true});el('search').value='';changed();renderRules();};
    el('save').onclick=()=>work(async()=>{setup.changeReason=el('reason').value;setup=await request('Save',setup);automatic=setup.automatic??automatic;dirty=false;resetPreview();renderRules();renderAutomatic();message('Rules saved. Preview before running. Unmapped rules are retained for review and cannot be applied.');});
    el('preview').onclick=()=>work(async()=>{resetPreview();el('results').replaceChildren();const uid=el('student').value;preview=await request('Preview',undefined,uid?{studentUid:uid}:{});const hasChanges=preview.rows.some(x=>x.action==='Would change');if(!hasChanges&&el('filter').value==='Would change')el('filter').value='All';renderPreview();message(hasChanges?'Preview complete. Review the proposed changes below.':'No changes are ready. The preview shows the accounts and explains whether they are already correct or need a rule mapping.');});
    el('run').onclick=()=>{if(!preview?.runId)return;const id=preview.runId;const count=preview.rows.filter(x=>x.action==='Would change').length;if(!confirm(`Apply ${count} reviewed cost-type changes in this environment?`))return;
        work(async()=>{preview.runId=null;preview.message='This preview has been used. Create a fresh preview before another run.';const results=await request('Run',undefined,{runId:id});el('results').replaceChildren();for(const r of results){const li=document.createElement('li');li.textContent=`Student UID ${r.studentUid}, term ${r.termId}: ${r.outcome}`;el('results').append(li);}await history();await refreshAutomatic();message('Run finished. Review the results; create a fresh preview before another run.');});};
    work(async()=>{try{await refreshAutomatic();}catch{el('auto-status').textContent='Schedule status could not be loaded. Use Refresh schedule status to try again.';}setup=await request('Setup');automatic=setup.automatic??automatic??{enabled:false,lastOutcome:'Automatic updates have not run.',reviewRuns:[]};renderRules();renderAutomatic();await history();message(readOnly?'Read-only view: exit student or faculty view before saving, previewing or running.':setup.revision?'Rules loaded. Preview before running.':'These baseline rules have not been saved. Enter a change reason and choose Save rules before previewing. Unmapped rules can be saved for review but will not run.');});
    setInterval(()=>{if(automatic&&!busy&&!document.hidden)refreshAutomatic().catch(()=>{el('auto-status').textContent='Schedule status could not be refreshed. Refresh or reload before changing it.';});},30000);
})();
