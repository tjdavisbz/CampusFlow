(() => {
    'use strict';
    const root = document.getElementById('housing-term-copy');
    const el = name => document.getElementById('housing-copy-' + name);
    let preview = null, busy = false;
    const message = (text, error = false) => {
        el('message').textContent = text;
        el('message').className = error ? 'alert alert-danger' : (text ? 'alert alert-success' : '');
    };
    const controls = () => {
        el('source').disabled = busy; el('destination').disabled = busy;
        el('preview').disabled = busy || !el('source').value || !el('destination').value;
        el('apply').disabled = busy || !preview;
    };
    async function request(handler, body, parameters = {}) {
        const url = new URL(location.href);
        url.search = new URLSearchParams({handler, ...parameters});
        const response = await fetch(url, {
            method: body === undefined ? 'GET' : 'POST',
            headers: {'Content-Type': 'application/json', RequestVerificationToken: root.querySelector('[name="__RequestVerificationToken"]').value},
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        const text = await response.text(); let data;
        try { data = text ? JSON.parse(text) : null; }
        catch { throw new Error('Could not read the housing copy response. Reload and try again.'); }
        if (!response.ok) throw new Error(data?.error?.message || data?.message || 'The housing copy request failed.');
        return data;
    }
    async function run(action) {
        if (busy) return; busy = true; controls();
        try { await action(); } catch (error) { message(error.message, true); }
        finally { busy = false; controls(); }
    }
    function clearPreview() { preview = null; el('review').hidden = true; controls(); }
    el('source').onchange = clearPreview;
    el('destination').onchange = clearPreview;
    el('preview').onclick = () => run(async () => {
        clearPreview(); message('');
        preview = await request('Preview', undefined, {
            sourcePeriodId: el('source').value, destinationPeriodId: el('destination').value
        });
        el('summary').textContent = `${preview.studentsToCopy} students from ${preview.sourcePeriodName} will be placed in a draft for ${preview.destinationPeriodName} (${preview.destinationRooms} destination rooms). ${preview.intentionalSinglesToReview} intentional-single choices and ${preview.feesToCopy.length} fee amounts will be retained for review.`;
        el('warning').textContent = preview.warning;
        el('fees').replaceChildren(...preview.feesToCopy.map(fee => {
            const row = document.createElement('tr');
            for (const value of [fee.building, fee.rateChoice, fee.baseAmount, fee.singleSurchargeAmount]) {
                const cell = document.createElement('td'); cell.textContent = value; row.append(cell);
            }
            return row;
        }));
        el('review').hidden = false;
    });
    el('apply').onclick = () => run(async () => {
        if (!preview || !confirm(`Copy ${preview.studentsToCopy} assignments and ${preview.feesToCopy.length} fee amounts into ${preview.destinationPeriodName}? This does not change Elements. The destination cannot be copied again.`)) return;
        await request('Copy', {sourcePeriodId: preview.sourcePeriodId,
            destinationPeriodId: preview.destinationPeriodId,
            expectedStudents: preview.studentsToCopy, expectedFeeRevision: preview.feeRevision,
            expectedCopyRevision: preview.copyRevision, confirmed: true});
        clearPreview(); message('Destination assignments and fees copied. Review both Housing Assignments and Fee Setup before applying anything to Elements.');
    });
    run(async () => {
        const periods = await request('Periods');
        for (const period of periods) {
            el('source').add(new Option(period.name, period.id));
            el('destination').add(new Option(period.name, period.id));
        }
        if (periods.length < 2) message('A second housing period must be set up in Elements before assignments can be copied.', true);
    });
})();
