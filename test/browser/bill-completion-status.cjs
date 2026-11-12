const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.CAMPUSFLOW_PLAYWRIGHT_PATH || 'playwright');
const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'src/CampusFlow.Web/Pages/BillApproval.js'), 'utf8');
const source = fs.readFileSync(path.join(root, 'src/CampusFlow.Web/Pages/BillApproval.cshtml'), 'utf8');
const pending = source.match(/<p class="completion-status"[\s\S]*?<\/p>/)[0]
    .replace(/data-completion-url="[^\n]+/, 'data-completion-url="/BillApproval?handler=CompletionStatus&amp;term=B27S"')
    .replace(/data-registration-complete="[^\n]+/, 'data-registration-complete="false"')
    .replace(/data-pdf-available="[^\n]+?>/, 'data-pdf-available="false">');

(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.CHAPEL_TEST_BROWSER || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome' });
    try {
        for (const [name, response, text, expectedCalls] of [
            ['review', {registrationComplete:false,pdfAvailable:false,needsReview:true}, /needs to review/, 1],
            ['reset', null, /status has changed/, 1],
            ['network', 'error', /taking longer/, 3],
            ['pending', {registrationComplete:false,pdfAvailable:false,needsReview:false}, /taking longer/, 24]
        ]) {
            const page = await browser.newPage();
            await page.clock.install();
            await page.route('http://completion.test/**', route => route.fulfill({contentType:'text/html',body:pending}));
            await page.goto('http://completion.test/BillApproval?term=B27S');
            await page.evaluate(value => {
                window.calls = [];
                window.fetch = async (url, options) => {
                    window.calls.push({url, options});
                    if (value === 'error') throw new Error('Offline');
                    return {status:value === null ? 404 : 200,ok:value !== null,headers:new Headers({'content-type':'application/json'}),json:async()=>value};
                };
            }, response);
            await page.addScriptTag({content:script});
            await page.evaluate(() => document.dispatchEvent(new Event('DOMContentLoaded')));
            await page.clock.runFor(130001);
            assert.match(await page.locator('[data-completion-url]').textContent(), text);
            const calls = await page.evaluate(() => window.calls);
            assert.equal(calls.length, expectedCalls);
            assert.ok(calls.every(x => x.url.endsWith('term=B27S') && x.options.cache === 'no-store' && !x.options.method));
            await page.clock.runFor(60000);
            assert.equal(await page.evaluate(() => window.calls.length), expectedCalls);
            console.log(`PASS completion ${name}: bounded read-only polling, selected term and truthful status`);
            await page.close();
        }
        const page = await browser.newPage();
        await page.clock.install();
        let navigations = 0;
        await page.route('http://completion.test/**', route => { navigations++; return route.fulfill({contentType:'text/html',body:pending}); });
        await page.goto('http://completion.test/BillApproval?term=B27S');
        await page.evaluate(() => {window.fetch=async()=>({status:200,ok:true,headers:new Headers({'content-type':'application/json'}),json:async()=>({registrationComplete:true,pdfAvailable:true,needsReview:false})});});
        await page.addScriptTag({content:script});
        await page.evaluate(() => document.dispatchEvent(new Event('DOMContentLoaded')));
        const reload = page.waitForEvent('framenavigated');
        await page.clock.runFor(3001);
        await reload;
        assert.equal(navigations,2);
        console.log('PASS verified completion refreshes the page without submitting approval or retrying completion work');
        await page.close();

    } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exit(1);});
