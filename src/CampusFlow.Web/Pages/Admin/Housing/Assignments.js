(() => {
    'use strict';
    const root = document.getElementById('housing-workspace');
    const el = name => document.getElementById('housing-' + name);
    let state = null, dirty = false, busy = false, selectedRoom = null, loadedPeriod = '';
    const selected = new Set();
    const rates = ['Unreviewed','Standard','AIC Triple Room','Savell Triple Occupancy'];
    function message(text, error = false) { el('message').textContent = text; el('message').dataset.error = error; }
    function controls() {
        el('period').disabled = busy;
        el('refresh').disabled = busy || !el('period').value;
        ['save','preview'].forEach(x => el(x).disabled = busy || !state);
        el('clear').disabled = busy || !state || !selected.size;
        el('rows').querySelectorAll('input,select,button').forEach(x => x.disabled = busy);
        updateSelectionUi();
    }
    async function request(handler, body, parameters = {}) {
        const url = new URL(location.href); url.search = new URLSearchParams({ handler, ...parameters });
        const response = await fetch(url, { method: body === undefined ? 'GET' : 'POST',
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value },
            body: body === undefined ? undefined : JSON.stringify(body) });
        const text = await response.text(); let data;
        try { data = text ? JSON.parse(text) : null; } catch { throw new Error('The request could not be completed. Check the application log, then retry. Your unsaved draft is still here.'); }
        if (!response.ok) throw new Error(data?.error?.message || data?.message || 'The request failed. Your unsaved draft is still here.');
        return data;
    }
    async function run(action) { if (busy) return; busy = true; controls(); try { await action(); } catch (e) { message(e.message, true); } finally { busy = false; controls(); } }
    function changed() { dirty = true; el('changes').hidden = true; summary(); }
    function summary() {
        if (!state) { el('summary').textContent = 'No saved draft. Refresh to load rooms and assignments from Elements.'; return; }
        const assigned = state.rooms.reduce((n,r) => n + r.occupants.length, 0);
        const beds = state.rooms.reduce((n,r) => n + r.capacity, 0);
        el('summary').textContent = `${assigned} assigned · ${beds - assigned} vacant · ${state.rooms.length} rooms · ${dirty ? 'Unsaved changes' : 'Draft saved'} · Last refresh ${new Date(state.refreshedAt).toLocaleString()}`;
    }
    function cell(row, text) { const td = document.createElement('td'); if (text !== undefined) td.textContent = text; row.append(td); return td; }
    function input(type, label, value, change) {
        const i = document.createElement('input'); i.type = type; i.setAttribute('aria-label', label);
        if (type === 'checkbox') i.checked = value; else { i.value = value; i.className = 'form-control'; }
        i.addEventListener('change', () => change(type === 'checkbox' ? i.checked : i.value)); return i;
    }
    function matchesFilter(room, occupant, filter) {
        if (!filter) return true;
        return [room.building, room.name, occupant?.name, occupant?.rate, occupant?.notes,
            occupant ? (occupant.intentionalSingle ? 'intentional single yes' : 'intentional single no') : 'vacant']
            .join(' ').toLowerCase().includes(filter);
    }
    function filteredStudentIds() {
        if (!state) return [];
        const filter = el('filter').value.trim().toLowerCase();
        return state.rooms.flatMap(room => room.occupants
            .filter(occupant => matchesFilter(room, occupant, filter))
            .map(occupant => occupant.studentUid));
    }
    function selectedOccupants() {
        if (!state) return [];
        return state.rooms.flatMap(room => room.occupants).filter(occupant => selected.has(occupant.studentUid));
    }
    function updateSelectionUi() {
        const existing = new Set(state ? state.rooms.flatMap(room => room.occupants.map(o => o.studentUid)) : []);
        [...selected].forEach(id => { if (!existing.has(id)) selected.delete(id); });
        const filtered = filteredStudentIds(), selectedFiltered = filtered.filter(id => selected.has(id));
        el('select-all').checked = filtered.length > 0 && selectedFiltered.length === filtered.length;
        el('select-all').indeterminate = selectedFiltered.length > 0 && selectedFiltered.length < filtered.length;
        el('select-all').disabled = busy || !filtered.length;
        el('selected-count').textContent = selected.size ? `${selected.size} student${selected.size === 1 ? '' : 's'} selected.` : 'No students selected.';
        el('bulk-rate-enabled').disabled = busy || !selected.size;
        el('bulk-notes-enabled').disabled = busy || !selected.size;
        el('bulk-single-enabled').disabled = busy || !selected.size;
        el('bulk-rate').disabled = busy || !selected.size || !el('bulk-rate-enabled').checked;
        el('bulk-notes').disabled = busy || !selected.size || !el('bulk-notes-enabled').checked;
        el('bulk-single').disabled = busy || !selected.size || !el('bulk-single-enabled').checked;
        el('bulk-apply').disabled = busy || !selected.size ||
            (!el('bulk-rate-enabled').checked && !el('bulk-notes-enabled').checked && !el('bulk-single-enabled').checked);
    }
    function render() {
        const rows = el('rows'); rows.replaceChildren(); summary();
        if (!state) return;
        const filter = el('filter').value.toLowerCase();
        state.rooms.forEach(room => {
            const slots = Math.max(room.capacity, room.occupants.length, 1);
            for (let slot = 0; slot < slots; slot++) {
                const occupant = room.occupants[slot];
                if (!matchesFilter(room, occupant, filter)) continue;
                const row = document.createElement('tr'); rows.append(row);
                const select = cell(row);
                if (occupant) select.append(input('checkbox', 'Select ' + occupant.name, selected.has(occupant.studentUid), checked => { checked ? selected.add(occupant.studentUid) : selected.delete(occupant.studentUid); updateSelectionUi(); }));
                cell(row, `${room.building} / ${room.name}`);
                const capacity = cell(row);
                if (slot === 0) {
                    const count = input('number', `Capacity for ${room.building} ${room.name}`, room.capacity, value => {
                        const n = Number(value);
                        if (!Number.isInteger(n) || n < room.occupants.length || n < 0 || n > 1000) { message('Capacity must accommodate the assigned students (maximum 1,000).', true); render(); return; }
                        room.capacity = n; changed(); render();
                    }); count.min = room.occupants.length; count.max = 1000; capacity.append(count);
                }
                const student = cell(row);
                if (occupant) student.textContent = occupant.name;
                else if (slot < room.capacity) {
                    const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Assign student';
                    button.onclick = () => { selectedRoom = room; el('search-results').replaceChildren(); el('students').showModal(); el('search').focus(); }; student.append(button);
                } else student.textContent = 'No available beds';
                const single = cell(row), rate = cell(row), notes = cell(row);
                if (!occupant) continue;
                single.append(input('checkbox', 'Intentional single for ' + occupant.name, occupant.intentionalSingle, v => { occupant.intentionalSingle = v; changed(); }));
                const choice = document.createElement('select'); choice.className = 'form-select'; choice.setAttribute('aria-label', 'Rate for ' + occupant.name);
                rates.forEach(x => choice.add(new Option(x,x)));
                choice.value = occupant.rate; choice.onchange = () => { occupant.rate = choice.value; changed(); }; rate.append(choice);
                const note = input('text', 'Notes for ' + occupant.name, occupant.notes, v => { occupant.notes = v; changed(); }); note.maxLength = 2000; notes.append(note);
            }
        });
        controls();
    }
    el('period').onchange = () => run(async () => {
        if (dirty && !confirm('Discard unsaved edits and open the selected period?')) { el('period').value = loadedPeriod; return; }
        const next = el('period').value;
        try { const draft = next ? await request('Draft', undefined, {periodId:next}) : null; state = draft; loadedPeriod = next; dirty = false; selected.clear(); render(); message(''); }
        catch (e) { el('period').value = loadedPeriod; throw e; }
    });
    el('refresh').onclick = () => run(async () => {
        if (!confirm('Replace this period’s saved draft and unsaved edits with Elements assignments? Draft-only rate choices and notes will also be cleared. This does not change Elements.')) return;
        state = await request('Refresh', { periodId:Number(el('period').value), revision:state?.revision, confirmed:true }); dirty = false; selected.clear(); render(); message('Refreshed from Elements. No Elements data was changed.');
    });
    async function save() { state = await request('Save', state); dirty = false; render(); message('Draft saved in CampusFlow. Elements is unchanged.'); }
    el('save').onclick = () => run(save);
    el('preview').onclick = () => run(async () => {
        if (dirty) await save();
        const changes = await request('Preview', undefined, {periodId:state.periodId});
        const list = el('changes').querySelector('ul'); list.replaceChildren();
        (changes.length ? changes : [{action:'No assignment changes',detail:'The saved assignments and capacities match the last refresh.'}]).forEach(c => { const li = document.createElement('li'); li.textContent = `${c.action}: ${c.detail}`; list.append(li); });
        el('changes').hidden = false; el('changes').scrollIntoView({behavior:'smooth'});
    });
    el('clear').onclick = () => {
        if (!selected.size || !confirm(`Clear ${selected.size} selected assignments from the draft? Students remain assigned in Elements until sync is implemented and run.`)) return;
        state.rooms.forEach(r => r.occupants = r.occupants.filter(o => !selected.has(o.studentUid))); selected.clear(); changed(); render();
    };
    el('filter').oninput = render;
    el('select-all').onchange = () => {
        const ids = filteredStudentIds();
        ids.forEach(id => el('select-all').checked ? selected.add(id) : selected.delete(id));
        render();
    };
    ['bulk-rate-enabled','bulk-notes-enabled','bulk-single-enabled'].forEach(name => el(name).onchange = updateSelectionUi);
    el('bulk-apply').onclick = () => {
        const occupants = selectedOccupants();
        if (!occupants.length) return;
        const updateRate = el('bulk-rate-enabled').checked, updateNotes = el('bulk-notes-enabled').checked,
            updateSingle = el('bulk-single-enabled').checked;
        if (!updateRate && !updateNotes && !updateSingle) return;
        if (!confirm(`Apply the selected changes to ${occupants.length} student${occupants.length === 1 ? '' : 's'} in this draft?`)) return;
        occupants.forEach(occupant => {
            if (updateRate) occupant.rate = el('bulk-rate').value;
            if (updateNotes) occupant.notes = el('bulk-notes').value;
            if (updateSingle) occupant.intentionalSingle = el('bulk-single').value === 'true';
        });
        changed(); render(); message(`Updated ${occupants.length} selected student${occupants.length === 1 ? '' : 's'}. Save the draft when ready.`);
    };
    el('close-search').onclick = () => el('students').close();
    el('search-form').onsubmit = event => { event.preventDefault(); run(async () => {
        const students = await request('Students', undefined, {query:el('search').value}); const results = el('search-results'); results.replaceChildren();
        if (!students.length) results.textContent = 'No matching students.';
        students.forEach(s => { const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-primary'; button.textContent = `${s.name} · ${s.studentId}`;
            button.onclick = () => {
                const uid = Number(s.externalStudentId);
                if (state.rooms.some(r => r.occupants.some(o => o.studentUid === uid))) { results.textContent = 'This student is already assigned. Clear the existing assignment before moving them.'; return; }
                if (selectedRoom.occupants.length >= selectedRoom.capacity) { results.textContent = 'This room is full. Increase capacity first.'; return; }
                selectedRoom.occupants.push({studentUid:uid,name:s.name,intentionalSingle:false,rate:'Standard',notes:''}); changed(); el('students').close(); render();
            }; results.append(button); });
    }); };
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    run(async () => { const periods = await request('Periods'); periods.forEach(p => el('period').add(new Option(p.name,p.id))); summary(); });
})();
