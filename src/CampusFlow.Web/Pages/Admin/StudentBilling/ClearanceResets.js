(() => {
    'use strict';
    const root=document.getElementById('clearance-resets'),el=n=>document.getElementById('clearance-'+n);
    const readOnly=root.dataset.readOnly==='true';let state=null,busy=false,reviewKey='',preview=null,exceptionTarget=null;
    const date=x=>x?new Date(x).toLocaleString():'—';
    function validScope(){const v=el('student').value.trim();return !el('student').validity.badInput&&(v===''||(/^\d+$/.test(v)&&Number(v)>0&&Number(v)<=2147483647));}
    function controls(){
        const reviews=state?.reviewRuns??[],running=reviews.some(x=>x.state==='Running');
        el('toggle').disabled=readOnly||busy||!state||(!state.enabled&&(!state.canEnable||!!state.runningRunId||running||(reviews.length>0&&!el('reviewed').checked)));
        el('student').disabled=readOnly||busy;
        el('preview').disabled=readOnly||busy||!state||!validScope();
        el('run').disabled=readOnly||busy||!preview?.runId||!state?.canEnable||!!state?.runningRunId||running||(reviews.length>0&&!el('reviewed').checked)||!validScope();
        el('exception-student').disabled=readOnly||busy;el('exception-term').disabled=readOnly||busy;el('exception-reason').disabled=readOnly||busy;
        el('exception-preview').disabled=readOnly||busy||!positive(el('exception-student').value)||!positive(el('exception-term').value);
        el('exception-add').disabled=readOnly||busy||!exceptionTarget||el('exception-reason').value.trim().length<10||!!state?.runningRunId;
        el('exceptions').querySelectorAll('button').forEach(x=>x.disabled=readOnly||busy||!!state?.runningRunId);
        el('refresh').disabled=busy;el('reviewed').disabled=readOnly||busy||running;
    }
    async function request(handler,body,params={}){
        const url=new URL(location.href);url.search=new URLSearchParams({handler,...params});
        const r=await fetch(url,{method:handler==='Status'?'GET':'POST',headers:{'Content-Type':'application/json',RequestVerificationToken:root.querySelector('[name="__RequestVerificationToken"]').value},body:body===undefined?undefined:JSON.stringify(body)});
        let data;try{data=await r.json();}catch{throw new Error('Could not read the response. Refresh the schedule status before retrying.');}
        if(!r.ok)throw new Error(data?.error?.message||data?.message||'The schedule could not be changed. Refresh before retrying.');return data;
    }
    function positive(v){return /^\d+$/.test(v)&&Number(v)>0&&Number(v)<=2147483647;}
    function render(){
        el('status').textContent=state.enabled?`On — every 15 minutes. Next check: ${date(state.nextRunAtUtc)}.`:'Off — automatic resets will not start.';
        el('toggle').textContent=state.enabled?'Turn off automatic resets':'Turn on automatic resets';
        el('last').textContent=`${state.lastOutcome} Last started: ${date(state.lastStartedAtUtc)}. Last finished: ${date(state.lastFinishedAtUtc)}.`;
        el('note').textContent=state.runningRunId?'A run is in progress or interrupted. Another run cannot overlap it.':!state.canEnable?'Cleared-status updates are disabled or the Elements environment settings need review. Turning off remains available.':'';
        const reviews=state.reviewRuns??[],key=reviews.map(x=>x.id+':'+x.state).join(',');if(key!==reviewKey){el('reviewed').checked=false;reviewKey=key;}
        el('review').hidden=state.enabled||reviews.length===0;el('review-runs').replaceChildren();
        for(const r of reviews){const li=document.createElement('li');li.textContent=`${date(r.created)} — ${r.automatic?'Automatic':'Manual'} — ${r.state} (${r.id})`;el('review-runs').append(li);}
        el('history').replaceChildren();
        if(!state.recentRuns?.length)el('history').textContent='No automatic clearance resets have run.';
        for(const r of state.recentRuns??[]){const d=document.createElement('details'),s=document.createElement('summary');s.textContent=`${date(r.created)} — ${r.automatic?'Automatic':'Manual'} — ${r.state}${r.reviewed?' (reviewed)':''} (${r.id})`;d.append(s);const p=document.createElement('p');p.textContent=r.summary;d.append(p);for(const x of r.results??[]){const item=document.createElement('p');item.textContent=`Student UID ${x.studentUid}, ${x.term}: ${x.outcome}`;d.append(item);}el('history').append(d);}
        renderExceptions();
        controls();
    }
    function renderExceptions(){
        el('exceptions').replaceChildren();
        for(const item of state.manualExceptions??[]){const li=document.createElement('li');li.textContent=`Student UID ${item.studentUid}, ${item.term}: ${item.reason} (added ${date(item.createdAtUtc)}) `;
            const button=document.createElement('button');button.type='button';button.className='btn btn-sm btn-outline-danger';button.textContent='Remove exception';
            button.onclick=()=>{const reason=prompt('Enter a reason (10–1000 characters) for removing this protection. The student may be reset on the next run.');if(reason===null)return;if(reason.trim().length<10||reason.trim().length>1000){el('message').textContent='Enter a reason of 10–1000 characters.';return;}if(!confirm(`Remove the exception for Student UID ${item.studentUid}, ${item.term}? This does not change Elements now.`))return;
                work(async()=>{state=await request('Exception',{studentUid:item.studentUid,termId:item.termId,reason,confirmed:true,remove:true});resetPreview();render();el('message').textContent='Exception removed and audited. No Elements status or bill was changed.';});};li.append(button);el('exceptions').append(li);}
    }
    async function work(action){if(busy)return;busy=true;controls();try{await action();}catch(e){el('message').textContent=e.message;}finally{busy=false;controls();}}
    async function refresh(){state=await request('Status');render();}
    el('reviewed').onchange=controls;el('refresh').onclick=()=>work(refresh);
    el('toggle').onclick=()=>{
        if(!state)return;const enabled=!state.enabled;
        if(enabled&&!confirm('Enable 15-minute resets? For eligible Cleared records without an approved bill or manual exception, clear Cleared Status, Disable Blackboard, Payment Plan?, Non-Returning and Non-Returning Reason. Checkboxes will become unchecked.'))return;
        work(async()=>{state=await request('Schedule',{enabled,reviewedRunIds:el('reviewed').checked?(state.reviewRuns??[]).map(x=>x.id):[]});render();el('message').textContent=enabled?'Automatic resets enabled. The first check is scheduled in about 15 minutes.':'Automatic resets turned off. Any update already in progress will finish verification.';});
    };
    function resetPreview(){preview=null;el('preview-rows').replaceChildren();el('preview-message').textContent='Preview the affected records before running.';controls();}
    el('student').oninput=resetPreview;
    el('preview').onclick=()=>work(async()=>{
        resetPreview();el('results').replaceChildren();const uid=el('student').value;
        preview=await request('Preview',undefined,uid?{studentUid:uid}:{});el('preview-message').textContent=preview.message;
        for(const row of preview.rows.slice(0,200)){const tr=document.createElement('tr');for(const value of [row.name,row.studentId,row.studentUid,row.term,row.action]){const td=document.createElement('td');td.textContent=value;tr.append(td);}el('preview-rows').append(tr);}
        if(preview.rows.length>200)el('preview-message').textContent+=` Showing 200 of ${preview.rows.length}; narrow by Student UID.`;
    });
    el('run').onclick=()=>{
        if(!preview?.runId)return;const id=preview.runId;
        if(!confirm(`Reset the five listed fields for ${preview.rows.length} reviewed student-term records now? Cleared Status, Disable Blackboard, Payment Plan?, Non-Returning and Non-Returning Reason will be cleared; checkboxes will become unchecked. Live approval and term checks run again before each update.`))return;
        work(async()=>{
            preview.runId=null;el('preview-message').textContent='This preview has been used. Create a fresh preview before another run.';
            const results=await request('Run',{runId:id,reviewedRunIds:el('reviewed').checked?(state.reviewRuns??[]).map(x=>x.id):[]});
            el('results').replaceChildren();for(const r of results){const li=document.createElement('li');li.textContent=`Student UID ${r.studentUid}, ${r.term}: ${r.outcome}`;el('results').append(li);}
            await refresh();el('message').textContent='Run finished. Review the results before creating another preview.';
        });
    };
    function clearExceptionTarget(){exceptionTarget=null;el('exception-target').textContent='';controls();}
    el('exception-student').oninput=clearExceptionTarget;el('exception-term').oninput=clearExceptionTarget;el('exception-reason').oninput=controls;
    el('exception-preview').onclick=()=>work(async()=>{exceptionTarget=null;exceptionTarget=await request('ExceptionPreview',undefined,{studentUid:el('exception-student').value,termId:el('exception-term').value});el('exception-target').textContent=`${exceptionTarget.name} — Student ID ${exceptionTarget.studentId}, ${exceptionTarget.term}. Verify before adding protection.`;});
    el('exception-add').onclick=()=>{if(!exceptionTarget||!confirm(`Protect ${exceptionTarget.name}, ${exceptionTarget.term}, from resets? This does not approve a bill or change Elements.`))return;
        work(async()=>{state=await request('Exception',{...exceptionTarget,reason:el('exception-reason').value.trim(),confirmed:true,remove:false});exceptionTarget=null;el('exception-reason').value='';el('exception-target').textContent='';resetPreview();render();el('message').textContent='Manual exception saved and audited. No bill approval or Elements status was changed.';});};
    work(async()=>{await refresh();try{const terms=await request('ExceptionTerms',undefined);for(const term of terms)el('exception-term').add(new Option(term.term,term.termId));}catch{el('exception-target').textContent='Terms could not be loaded. Refresh the page before adding an exception.';}});setInterval(()=>{if(!busy&&!document.hidden)work(refresh);},30000);
})();
