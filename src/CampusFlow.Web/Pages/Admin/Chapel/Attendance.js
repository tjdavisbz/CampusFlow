(() => {
    'use strict';
    const root = document.getElementById('chapel-attendance-workspace');
    if (!root || !window.fetch || !window.FormData) return;
    const states = [], summary = document.getElementById('chapel-save-summary');
    const statusNames = ['Pending', 'Present', 'Absent', 'Excused'];
    const sourceNames = ['LegacyImport', 'CbordImport', 'Staff', 'Scheduled'];
    const enumName = (value, names) => typeof value === 'number' ? names[value] : value;
    const normalized = value => (value || '').trim();
    const snapshot = state => ({ status: state.form.elements.status.value, comment: normalized(state.form.elements.comment.value) });
    const equal = (a, b) => a.status === b.status && a.comment === b.comment;
    const dirty = state => !equal(snapshot(state), state.saved);
    let totalsTimer, totalsRunning = false, totalsAgain = false, totalsCurrent = true;
    function notify(state, message, kind) {
        state.message.textContent = message;
        state.message.dataset.state = kind;
        state.form.dataset.saveState = kind;
        state.retry.hidden = !state.failed && !state.conflict;
        state.retry.disabled = state.inFlight || state.conflict;
        state.reload.hidden = !state.conflict && !state.failed;
        state.reload.disabled = state.inFlight;
        updateGlobal();
    }
    function updateGlobal() {
        const pending = states.filter(x => x.inFlight || dirty(x) || x.failed || x.conflict).length;
        if (summary) summary.textContent = pending ? `${pending} record(s) still saving or need attention. Keep this page open until saved.` : 'All changes saved.';
        root.querySelectorAll('[data-bulk-credit]').forEach(form => {
            const count = Number(form.elements.expectedCount.value);
            form.querySelector('button').disabled = pending > 0 || !totalsCurrent || count === 0;
        });
    }
    async function responseJson(response) {
        let data;
        try { data = await response.json(); } catch { throw new Error('The save could not be confirmed. Your edits are still here; retry or reload the saved record.'); }
        if (!response.ok) {
            const error = new Error(data?.message || data?.error?.message || 'The request failed. Your edits are still here.');
            error.conflict = response.status === 409;
            throw error;
        }
        return data;
    }
    function scheduleTotals() {
        if (!root.dataset.summaryUrl) return;
        totalsCurrent = false;
        if (totalsRunning) { totalsAgain = true; return; }
        clearTimeout(totalsTimer);
        totalsTimer = setTimeout(refreshTotals, 250);
    }
    async function refreshTotals() {
        if (totalsRunning) { totalsAgain = true; return; }
        totalsRunning = true;
        const note = document.getElementById('chapel-totals-message');
        try {
            const rows = await responseJson(await fetch(root.dataset.summaryUrl, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } }));
            if (!Array.isArray(rows)) throw new Error('Invalid totals response.');
            rows.forEach(row => {
                const card = Array.from(root.querySelectorAll('[data-session]')).find(x => x.dataset.session === row.session);
                if (!card) return;
                card.querySelector('[data-session-counts]').textContent = `${row.total} students · ${row.present} present · ${row.absent} absent · ${row.excused} excused · ${row.scanInOnly} scan-in-only`;
                const form = card.querySelector('[data-bulk-credit]');
                if (form) { form.elements.expectedCount.value = row.scanInOnly; form.dataset.confirmation = row.confirmation; form.querySelector('button').textContent = `Credit ${row.scanInOnly} scan-in-only records`; }
            });
            totalsCurrent = !totalsAgain;
            if (note) note.textContent = totalsCurrent ? '' : 'Updating daily totals…';
        } catch {
            totalsCurrent = false;
            if (note) note.textContent = 'Daily totals could not be refreshed. Reload this page after your edits are saved; bulk correction is disabled until then.';
        } finally {
            totalsRunning = false;
            updateGlobal();
            if (totalsAgain) { totalsAgain = false; scheduleTotals(); }
        }
    }
    async function save(state) {
        clearTimeout(state.timer);
        if (state.inFlight || state.conflict) return;
        if (!dirty(state) && !state.failed) { notify(state, 'Up to date', 'saved'); return; }
        const sent = snapshot(state), editVersion = state.edits;
        const body = new FormData(state.form);
        body.set('status', sent.status); body.set('comment', sent.comment);
        const url = new URL(state.form.action, location.href); url.searchParams.set('handler', 'AutoSave');
        state.inFlight = true; state.failed = false;
        notify(state, 'Saving…', 'saving');
        let succeeded = false;
        try {
            const row = await responseJson(await fetch(url, { method: 'POST', body, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } }));
            const name = enumName(row.status, statusNames), source = enumName(row.source, sourceNames);
            if (!row.revision || !statusNames.includes(name) || !sourceNames.includes(source)) throw new Error('The save response could not be confirmed. Reload the saved record before retrying.');
            state.form.elements.revision.value = row.revision;
            state.saved = { status: name, comment: normalized(row.comment) };
            if (state.edits === editVersion) { state.form.elements.status.value = name; state.form.elements.comment.value = row.comment || ''; }
            state.row.querySelector('[data-attendance-status]').textContent = name;
            state.row.querySelector('[data-attendance-status]').className = `chapel-status ${name.toLowerCase()}`;
            state.row.querySelector('[data-attendance-source]').textContent = source;
            succeeded = true; scheduleTotals();
        } catch (error) {
            state.failed = true; state.conflict = !!error.conflict;
            notify(state, error instanceof TypeError ? 'Connection lost. Your edits are still here; retry when connected.' : error.message, state.conflict ? 'conflict' : 'error');
        } finally {
            state.inFlight = false;
            if (succeeded) {
                notify(state, dirty(state) ? 'Waiting to save your latest edits…' : 'Saved', dirty(state) ? 'pending' : 'saved');
                if (dirty(state)) state.timer = setTimeout(() => save(state), 0);
            } else notify(state, state.message.textContent, state.conflict ? 'conflict' : 'error');
        }
    }
    async function reload(state) {
        if (state.inFlight || !confirm('Replace your unsaved edits for this record with its latest saved status and comment?')) return;
        const url = new URL(state.form.action, location.href); url.searchParams.set('handler', 'AttendanceRow'); url.searchParams.set('id', state.form.elements.id.value);
        clearTimeout(state.timer); state.inFlight = true;
        state.form.elements.status.disabled = true; state.form.elements.comment.disabled = true;
        notify(state, 'Loading saved record…', 'saving');
        try {
            const row = await responseJson(await fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } }));
            const name = enumName(row.status, statusNames), source = enumName(row.source, sourceNames);
            if (!row.revision || !statusNames.includes(name) || !sourceNames.includes(source)) throw new Error('The saved record could not be read. Your edits are still here.');
            state.form.elements.revision.value = row.revision; state.form.elements.status.value = name; state.form.elements.comment.value = row.comment || '';
            state.saved = snapshot(state); state.failed = false; state.conflict = false;
            state.row.querySelector('[data-attendance-status]').textContent = name; state.row.querySelector('[data-attendance-status]').className = `chapel-status ${name.toLowerCase()}`;
            state.row.querySelector('[data-attendance-source]').textContent = source; scheduleTotals();
        } catch (error) { state.failed = true; notify(state, error.message, 'error'); }
        finally { state.inFlight = false; state.form.elements.status.disabled = false; state.form.elements.comment.disabled = false; notify(state, state.failed ? state.message.textContent : 'Saved record reloaded', state.failed ? 'error' : 'saved'); }
    }
    root.querySelectorAll('form[data-chapel-autosave]').forEach(form => {
        const state = { form, row: form.closest('[data-attendance-row]'), message: form.querySelector('[data-save-message]'), retry: form.querySelector('[data-save-now]'), reload: form.querySelector('[data-reload-record]'), edits: 0, inFlight: false, failed: false, conflict: false };
        state.saved = snapshot(state); states.push(state); state.retry.textContent = 'Retry save'; state.retry.hidden = true;
        function changed(delay) {
            state.edits++; clearTimeout(state.timer);
            if (state.failed || state.conflict) { notify(state, 'Your edits are still here. Retry or reload the saved record before continuing.', state.conflict ? 'conflict' : 'error'); return; }
            notify(state, dirty(state) ? 'Waiting to save…' : 'Up to date', dirty(state) ? 'pending' : 'saved');
            if (!state.inFlight && dirty(state)) state.timer = setTimeout(() => save(state), delay);
        }
        form.elements.status.addEventListener('change', () => changed(0));
        form.elements.comment.addEventListener('input', () => changed(700));
        form.addEventListener('submit', event => { event.preventDefault(); save(state); });
        state.reload.addEventListener('click', () => reload(state));
    });
    window.addEventListener('beforeunload', event => {
        if (states.some(x => x.inFlight || dirty(x) || x.failed || x.conflict)) { event.preventDefault(); event.returnValue = ''; }
    });
})();
