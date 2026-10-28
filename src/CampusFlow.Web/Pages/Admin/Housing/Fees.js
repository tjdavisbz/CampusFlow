(() => {
    'use strict';
    const root = document.getElementById('housing-fees');
    const el = name => document.getElementById('fee-' + name);
    const canEdit = root.dataset.canEdit === 'true';
    let state = null, dirty = false, busy = false, loadedPeriod = '';
    const message = (text, error = false) => { el('message').textContent = text; el('message').dataset.error = error; };
    function controls() {
        el('period').disabled = busy;
        if (el('save')) el('save').disabled = busy || !state || !dirty;
        el('rows').querySelectorAll('input').forEach(input => input.disabled = busy || !canEdit);
    }
    async function request(handler, body, parameters = {}) {
        const url = new URL(location.href);
        url.search = new URLSearchParams({handler, ...parameters});
        const response = await fetch(url, {method: body === undefined ? 'GET' : 'POST',
            headers: {'Content-Type':'application/json', RequestVerificationToken:root.querySelector('[name="__RequestVerificationToken"]').value},
            body:body === undefined ? undefined : JSON.stringify(body)});
        const text = await response.text(); let data;
        try { data = text ? JSON.parse(text) : null; }
        catch { throw new Error('Could not read the fee setup response. Please reload the page.'); }
        if (!response.ok) throw new Error(data?.error?.message || data?.message || 'The fee setup request failed.');
        return data;
    }
    async function run(action) {
        if (busy) return;
        busy = true; controls();
        try { await action(); } catch (error) { message(error.message, true); }
        finally { busy = false; controls(); }
    }
    function render() {
        const rows = el('rows'); rows.replaceChildren();
        el('status').textContent = !state ? 'Choose a housing period.' :
            `${state.periodName} · Term ${state.termCalendarId} · ${state.isSaved ? 'Saved CampusFlow fee schedule' : 'Suggested amounts — not saved or active'}${dirty ? ' · Unsaved edits' : ''}`;
        if (!state) { controls(); return; }
        state.rates.forEach(rate => {
            const tr = document.createElement('tr'); rows.append(tr);
            for (const value of [rate.building, rate.rateChoice]) {
                const td = document.createElement('td'); td.textContent = value; tr.append(td);
            }
            for (const [property, label] of [['baseAmount','Base charge'],['singleSurchargeAmount','Additional single charge']]) {
                const td = document.createElement('td'); tr.append(td);
                const input = document.createElement('input'); input.type = 'number'; input.className = 'form-control';
                input.min = '0.01'; input.max = '100000'; input.step = '0.01'; input.required = true;
                input.value = Number(rate[property]).toFixed(2);
                input.setAttribute('aria-label', `${label} for ${rate.building}, ${rate.rateChoice}`);
                input.oninput = () => { rate[property] = input.value === '' ? null : Number(input.value); dirty = true; controls();
                    el('status').textContent = `${state.periodName} · Term ${state.termCalendarId} · Unsaved edits`; };
                td.append(input);
            }
        });
        controls();
    }
    el('period').onchange = () => run(async () => {
        if (dirty && !confirm('Discard unsaved housing fee edits?')) { el('period').value = loadedPeriod; return; }
        const next = el('period').value;
        try { state = next ? await request('Rates', undefined, {periodId:next}) : null;
            loadedPeriod = next; dirty = false; render(); message(''); }
        catch (error) { el('period').value = loadedPeriod; throw error; }
    });
    if (el('save')) el('save').onclick = () => run(async () => {
        if (!state || state.rates.some(rate => !Number.isFinite(rate.baseAmount) || rate.baseAmount <= 0 ||
            !Number.isFinite(rate.singleSurchargeAmount) || rate.singleSurchargeAmount <= 0))
            throw new Error('Enter positive base and single-room amounts for every listed rate.');
        if (!confirm(`Save ${state.rates.length} CampusFlow housing rates for ${state.periodName}? These prices will be used for future non-production assignment applies, but existing posted charges will not be changed.`)) return;
        state = await request('Save', state); dirty = false; render(); message('Housing fee amounts saved in CampusFlow. Elements was not changed.');
    });
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    run(async () => { const periods = await request('Periods'); periods.forEach(p => el('period').add(new Option(p.name,p.id))); render(); });
})();
