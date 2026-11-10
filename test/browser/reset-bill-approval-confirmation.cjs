const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const checkboxFile=process.env.CF022_CHECKBOX_HTML;
assert.ok(checkboxFile&&fs.existsSync(checkboxFile),'Run BillApprovalResetRenderedConfirmationTests with CF022_HTML_DIR, then set CF022_CHECKBOX_HTML to confirmation-checkbox.html.');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.CHAPEL_TEST_BROWSER||'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'});
 try{
  const page=await browser.newPage();const errors=[];page.on('pageerror',x=>errors.push(x.message));
  await page.setContent(`<form id="reset"><input name="ResetInput.ApprovalId" value="11111111-1111-1111-1111-111111111111" type="hidden">${fs.readFileSync(checkboxFile,'utf8')}<span data-valmsg-for="ResetInput.Confirmed"></span><button type="submit">Reset Bill Approval</button><input name="ResetInput.Confirmed" value="false" type="hidden"></form>`);
  for(const name of ['jquery/jquery.js','jquery-validation/jquery.validate.js','jquery-validation-unobtrusive/jquery.validate.unobtrusive.js'])await page.addScriptTag({path:path.join(root,'src/CampusFlow.Web/wwwroot/libs',name)});
  await page.evaluate(()=>{$.validator.unobtrusive.parse(document);window.submissions=[];$('#reset').validate().settings.submitHandler=(form,event)=>{event.preventDefault();window.submissions.push([...new FormData(form)]);return false;};});
  const input=page.locator('input[type=checkbox]'),button=page.locator('button');
  assert.equal(await input.getAttribute('required'),'required');await button.click();assert.equal(await page.evaluate(()=>window.submissions.length),0);console.log('PASS unchecked native confirmation blocks submission');
  await input.check();assert.equal(await page.evaluate(()=>$('#reset').valid()),true);await button.click();assert.equal(await page.evaluate(()=>window.submissions.length),1);console.log('PASS checked confirmation passes actual unobtrusive validation');
  const posted=await page.evaluate(()=>window.submissions[0].filter(x=>x[0]==='ResetInput.Confirmed').map(x=>x[1]));assert.deepEqual(posted,['true','false']);console.log('PASS checked value precedes the hidden false fallback for server binding');
  await input.uncheck();assert.equal(await page.evaluate(()=>$('#reset').valid()),false);await button.click();assert.equal(await page.evaluate(()=>window.submissions.length),1);assert.deepEqual(errors,[]);console.log('PASS removing confirmation blocks another reset');
  console.log('4 reset-confirmation browser scenarios passed.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
