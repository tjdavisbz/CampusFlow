const fs = require('fs'), path = require('path'), assert = require('node:assert/strict');
const { chromium } = require(process.env.CAMPUSFLOW_PLAYWRIGHT_PATH || 'playwright');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'src/CampusFlow.Web/Pages/Housing.cshtml'), 'utf8');
const script = source.match(/<script>([\s\S]*?)<\/script>/)[1];
const css = fs.readFileSync(path.join(root, 'src/CampusFlow.Web/Pages/Housing.css'), 'utf8');
const card = source.match(/<label class="plan-card">[\s\S]*?<\/label>/)[0];
const button = source.match(/<button type="submit" class="housing-submit"[\s\S]*?<\/button>/)[0];
const overlay = source.match(/<div id="housing-processing"[\s\S]*?<script>/)[0].replace(/<script>$/, '');
const plans = [
  { value: '20', name: '20 meals per semester', description: 'Commuter dining option.', price: '$340.99' },
  { value: '40', name: '40 meals per semester', description: 'Commuter dining option.', price: '$557.49' },
  { value: 'none', name: 'No Meal Plan', description: 'Record that I do not need a meal plan.', price: 'No charge' }
];
const cards = plans.map((p, i) => card
  .replace('asp-for="MealPlanChoice"', `name="MealPlanChoice" id="choice-${i}"`)
  .replace('@value', p.value).replace('@plan.Name', p.name).replace('@plan.Description', p.description)
  .replace(/@\(plan.Price[\s\S]*?<\/span><\/label>$/, `${p.price}</span></label>`)).join('');
const html = `<!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
<style>body{margin:0;font-family:Arial;--tenant-primary:#4e2b7b;--tenant-primary-dark:#2b1744;--tenant-accent:#e2c761;--tenant-surface:#f7f8fa;--tenant-text:#172033;--tenant-heading-font:Arial;--tenant-body-font:Arial}${css}</style></head>
<body><main class="housing-page"><header class="housing-hero"><div><span>Campus living</span><h1>Housing & Meal Plan</h1></div></header>
<form method="post" id="housing-form"><input type="hidden" name="Input.ExternalTermId" value="269">
<input type="hidden" name="Input.HousingChoice" value="Commuter"><section class="housing-section">
<div class="section-heading"><span>Dining</span><h2>Choose your meal plan</h2></div>
<div class="plan-grid">${cards}</div>${button}</section></form></main>${overlay}<script>${script}</script></body></html>`;

(async () => {
  const browser = await chromium.launch({ headless: true,
    ...(process.env.CHAPEL_TEST_BROWSER ? { executablePath: process.env.CHAPEL_TEST_BROWSER } : {}) });
  try {
    for (const width of [320, 390, 768, 1440]) {
      const page = await browser.newPage({ viewport: { width, height: 1000 } });
      const errors = []; page.on('pageerror', e => errors.push(e.message));
      await page.route('http://meal.test/**', r => r.fulfill({ contentType: 'text/html', body: html }));
      await page.goto('http://meal.test/Housing?term=269');
      await page.evaluate(() => document.getElementById('housing-form').addEventListener('submit', e => e.preventDefault()));
      assert.equal(await page.locator('input[name="MealPlanChoice"]:checked').count(), 0);
      assert.equal(await page.locator('.housing-submit').isDisabled(), true);
      await page.evaluate(() => document.getElementById('housing-form').requestSubmit());
      assert.equal(await page.locator('#housing-processing').getAttribute('aria-hidden'), 'true');
      await page.locator('.plan-card').filter({ hasText: 'No Meal Plan' }).click();
      assert.equal(await page.locator('.housing-submit').isDisabled(), false);
      assert.equal(await page.locator('input[name="MealPlanChoice"]:checked').inputValue(), 'none');
      const data = await page.evaluate(() => Object.fromEntries(new FormData(document.getElementById('housing-form'))));
      assert.equal(data.MealPlanChoice, 'none'); assert.equal(data['Input.ExternalTermId'], '269');
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
      if (process.env.CAMPUSFLOW_SCREENSHOT_DIR) {
        fs.mkdirSync(process.env.CAMPUSFLOW_SCREENSHOT_DIR, { recursive: true });
        await page.screenshot({ path: path.join(process.env.CAMPUSFLOW_SCREENSHOT_DIR, `meal-${width}.png`), fullPage: true });
      }
      await page.locator('.housing-submit').click();
      assert.equal(await page.locator('.housing-submit').isDisabled(), true);
      assert.equal(await page.locator('#housing-processing').getAttribute('aria-hidden'), 'false');
      assert.deepEqual(errors, []);
      await page.reload();
      await page.locator('.plan-card').filter({ hasText: '20 meals per semester' }).click();
      assert.equal(await page.locator('.housing-submit').isDisabled(), false);
      assert.equal(await page.locator('input[name="MealPlanChoice"]:checked').inputValue(), '20');
      console.log(`PASS explicit None and real-plan choice, guarded submission, preserved term and layout at ${width}px`);
      await page.close();
    }
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
