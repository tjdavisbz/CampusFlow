const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.CAMPUSFLOW_PLAYWRIGHT_PATH || 'playwright');
const root = path.resolve(__dirname, '../..');
const housing = fs.readFileSync(path.join(root, 'src/CampusFlow.Web/Pages/Housing.cshtml'), 'utf8');
const success = housing.match(/<section class="housing-success">[\s\S]*?<\/section>/)[0]
    .replace('asp-page="/Index" asp-route-term="@Model.Selection.ExternalTermId"', 'href="/Index?term=269"');
const css = fs.readFileSync(path.join(root, 'src/CampusFlow.Web/Pages/Housing.css'), 'utf8');

(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.CHAPEL_TEST_BROWSER||'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'});try{
        for (const width of [320,390,768,1440]) {
            const p = await browser.newPage({viewport:{width,height:900}});
            await p.setContent(`<meta name="viewport" content="width=device-width,initial-scale=1"><style>*{box-sizing:border-box}body{margin:0;--tenant-primary:#502d7f;--tenant-primary-dark:#38205a;--tenant-text:#111;--tenant-body-font:Arial}${css}</style><main class="housing-page">${success}</main>`);
            assert.equal(await p.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
            const link = p.getByRole('link',{name:/Continue with bill approval/});
            assert.equal(await link.getAttribute('href'),'/Index?term=269');
            assert.ok((await link.boundingBox()).height>=44);
            if ([390,1440].includes(width)) await p.screenshot({path:`/private/tmp/campusflow-cf034-036-tests/meal-continue-${width}.png`,fullPage:true});
            console.log(`PASS meal-plan success action: selected term, touch target and layout at ${width}px`);
            await p.close();
        }
}finally{await browser.close();}})().catch(error=>{console.error(error);process.exit(1);});
