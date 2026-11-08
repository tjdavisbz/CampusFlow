(() => {
    'use strict';
    const root=document.getElementById('clearance-resets'),el=n=>document.getElementById('clearance-'+n);
    const readOnly=root.dataset.readOnly==='true';let state=null,busy=false,reviewKey='',preview=null;
    const date=x=>x?new Date(x).toLocaleString():'—';
    function validScope(){const v=el('student').value.trim();return !el('student').validity.badInput&&(v===''||(/^\d+$/.test(v)&&Number(v)>0&&Number(v)<=2147483647));}
    function controls(){
        const reviews=state?.reviewRuns??[],running=reviews.some(x=>x.state==='Running');
        el('toggle').disabled=readOnly||busy||!state||(!state.enabled&&(!state.canEnable||!!state.runningRunId||running||(reviews.length>0&&!el('reviewed').checked)));
        el('student').disabled=readOnly||busy;
        el('preview').disabled=readOnly||busy||!state||!validScope();
        el('run').disabled=readOnly||busy||!preview?.runId||!state?.canEnable||!!state?.runningRunId||running||(reviews.length>0&&!el('reviewed').checked)||!validScope();
        el('refresh').disabled=busy;el('reviewed').disabled=readOnly||busy||running;
    }
    async function request(handler,body,params={}){
        const url=new URL(location.href);url.search=new URLSearchParams({handler,...params});
        const r=await fetch(url,{method:handler==='Status'?'GET':'POST',headers:{'Content-Type':'application/json',RequestVerificationToken:root.querySelector('[name="__RequestVerificationToken"]').value},body:body===undefined?undefined:JSON.stringify(body)});
        let data;try{data=await r.json();}catch{throw new Error('Could not read the response. Refresh the schedule status before retrying.');}
        if(!r.ok)throw new Error(data?.error?.message||data?.message||'The schedule could not be changed. Refresh before retrying.');return data;
    }
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
        controls();
    }
    async function work(action){if(busy)return;busy=true;controls();try{await action();}catch(e){el('message').textContent=e.message;}finally{busy=false;controls();}}
    async function refresh(){state=await request('Status');render();}
    el('reviewed').onchange=controls;el('refresh').onclick=()=>work(refresh);
    el('toggle').onclick=()=>{
        if(!state)return;const enabled=!state.enabled;
        if(enabled&&!confirm('Enable 15-minute clearance resets for Spring 2027 and later? Eligible Cleared student-term records with no Nelson courses will become Not Cleared. Transfer courses are excluded.'))return;
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
        if(!confirm(`Change ${preview.rows.length} reviewed student-term records from Cleared to Not Cleared now? Live course and term checks run again before each update.`))return;
        work(async()=>{
            preview.runId=null;el('preview-message').textContent='This preview has been used. Create a fresh preview before another run.';
            const results=await request('Run',{runId:id,reviewedRunIds:el('reviewed').checked?(state.reviewRuns??[]).map(x=>x.id):[]});
            el('results').replaceChildren();for(const r of results){const li=document.createElement('li');li.textContent=`Student UID ${r.studentUid}, ${r.term}: ${r.outcome}`;el('results').append(li);}
            await refresh();el('message').textContent='Run finished. Review the results before creating another preview.';
        });
    };
    work(refresh);setInterval(()=>{if(!busy&&!document.hidden)work(refresh);},30000);
})();
