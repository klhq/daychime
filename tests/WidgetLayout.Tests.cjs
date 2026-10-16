const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DAYCHIME_PLAYWRIGHT_MODULE || path.resolve(__dirname, '../artifacts/widget-layout-check/node_modules/playwright'));
const root = path.resolve(__dirname, '..');
const deps = path.resolve(process.env.DAYCHIME_LAYOUT_DEPS || path.join(root, 'artifacts/widget-layout-check'));
const template = JSON.parse(fs.readFileSync(path.join(root, 'src/DaychimeWidget/Templates/DaychimeTemplate.json'), 'utf8'));
const locales = ['Resources/Strings.en.json', 'Locales/TraditionalChinese.json', 'Locales/SimplifiedChinese.json'];
const modes = ['idle', 'idle-previous', 'missing', 'missing-stale', 'working',  'finished','options', 'options-idle', 'options-finished', 'auto', 'auto-error', 'edit', 'edit-error', 'edit-future', 'edit-dst', 'clear'];
const dependencies = path.join(deps, 'adaptivecards-designer/package');
const galleryLanguage=Number(process.argv[2]||0);
let layoutBrowser;
(async () => {
  const browser = layoutBrowser = await chromium.launch({headless:true,...(process.env.DAYCHIME_BROWSER_EXECUTABLE ? {executablePath:process.env.DAYCHIME_BROWSER_EXECUTABLE} : {channel:'msedge'})});
  const page = await browser.newPage({viewport:{width:1100,height:900}});
  let total = 0;
  let failures = [];
  for (const theme of ['light', 'dark']) {
    await page.setContent('<html><head><meta charset="UTF-8"></head><body style="margin:0;display:flex;flex-wrap:wrap;gap:12px;align-items:flex-start"></body></html>');
    await page.addScriptTag({path:path.join(deps, 'adaptivecards/package/dist/adaptivecards.min.js')});
    await page.addScriptTag({path:path.join(deps, 'adaptive-expressions-4.11/package/lib/browser.js')});
    await page.addScriptTag({path:path.join(deps, 'adaptivecards-templating/package/dist/adaptivecards-templating.min.js')});
    await page.addStyleTag({path:path.join(deps, 'adaptivecards/package/dist/adaptivecards.css')});
    await page.addStyleTag({path:path.join(dependencies, `src/containers/widget/widget-container-${theme}.css`)});
    const host = JSON.parse(fs.readFileSync(path.join(dependencies, `lib/hostConfigs/widget-${theme}.json`), 'utf8'));
    for (const [language, relative] of locales.entries()) {
      const s = JSON.parse(fs.readFileSync(path.join(root, 'src/DaychimeWidget', relative), 'utf8'));
      for (const size of ['small','medium','large']) for (const mode of modes) {
        const normal = ['idle','idle-previous','missing','missing-stale','working','finished'].includes(mode);
        const working = mode.startsWith('edit') || ['missing-stale','working','finished','options','options-finished','auto','auto-error','clear'].includes(mode);
        const previous = !['idle','working'].includes(mode);
        const data = {...s, endsNextDay:true, clockedIn:working, showMain:normal, isAutoSettings:mode.startsWith('auto'), hasAutoTimeError:mode==='auto-error', isEditing:mode.startsWith('edit'), hasTimeError:mode==='edit-error', confirmingClear:mode==='clear', isOptions:mode.startsWith('options'), isWorkdayComplete:mode==='finished'||mode==='options-finished', hasPreviousShift:previous, hasUndoableShift:!working&&previous, needsAutoClockInTime:mode.startsWith('missing'), date:language===0?'Wednesday, September 30, 2026':'2026年10月15日 星期四', now:'12:59 PM', clockIn:'12:59 PM', finish:'12:59 PM', finishCompact:'12:59 PM +1', finishCaption:mode==='finished'?s.workdayCompleteShort:s.finishLabel, clockInValue:mode==='edit-error'?'25:99':'09:00', autoClockInAfter:mode==='auto-error'?'25:99':'06:00', autoClockInEnabled:true, workHoursCompact:'9.5h', workHoursSummary:s.workHoursSummary.replace('{0}','9.5'), formatBadge:'12h', formatToggleTitle:s.switchTo24Hour, workHoursToggleTitle:s.workHoursToggleTitle.replace('{0}','10'), autoClockInCompact:s.autoClockInOnCompact.replace('{0}','06:00'), autoClockInStatus:s.autoClockInAfterLabel.replace('{0}','06:00'), autoClockInToggleTitle:s.configureAutoClockIn, clockInActionTitle:s.startNewWorkday, clockOutActionTitle:s.clockOutNow, undoClockOutActionTitle:s.undoClockOut, previousShiftCompact:s.previousCompact.replace('{0}','12:59 PM').replace('{1}','12:59 PM'), previousShiftSummary:s.previousClockedOutSummary.replace('{0}','10/14 12:59 PM').replace('{1}','10/15 12:59 PM').replace('{2}',s.elapsedHoursMinutes.replace('{0}','9').replace('{1}','30'))};
        data.clockInTimeCompactLabel = s.clockInTimeCompactLabel.replace('{0}', '2026-10-14');
        data.hasTimeError = mode.startsWith('edit-');
        data.clockInTimeError = mode==='edit-future' ? s.futureClockInShort : mode==='edit-dst' ? s.invalidLocalTimeShort : s.invalidTimeShort;
        if (mode==='edit-future') data.clockInValue = '23:59';
        if (mode==='edit-dst') data.clockInValue = '02:30';
        const id = `${theme}-${language}-${size}-${mode}`;
        const result = await page.evaluate(({template,data,size,host,id}) => {
          const payload = new ACData.Template(template).expand({$root:data,$host:{widgetSize:size}});
          if (JSON.stringify(payload).includes('${')) return {error:'Unbound template expression'};
          const errors=[];
          const card = new AdaptiveCards.AdaptiveCard();
          card.hostConfig = new AdaptiveCards.HostConfig(host);
          card.onExecuteAction = a => { window.lastLayoutAction={verb:a.verb,data:a.data}; };
          card.parse(payload);
          const validation=card.validateProperties();
          for(const failure of validation.validationEvents||[]) errors.push(failure.message);
          const frame=document.createElement('div');
          frame.id=id;
          frame.className=`widget-outer-container widget-${size}-container-no-border`;
          const content=card.render();
          frame.append(content);document.body.append(frame);
          const header=document.createElement('div');
          header.textContent='Daychime · '+id.split('-').slice(3).join('-');
          header.style.cssText='position:absolute;left:16px;top:14px;font:12px Segoe UI;color:gray';
          frame.append(header);
          const expected={small:146,medium:304,large:462}[size];
          const height=content.getBoundingClientRect().height;
          const clipped=[];
          for(const el of content.querySelectorAll('button,input,label,[role="button"],.ac-textBlock')) {
            const r=el.getBoundingClientRect();
            if(!r.width||!r.height) continue;
            const outer=frame.getBoundingClientRect();
            if(r.bottom>outer.top+expected-16+0.5) clipped.push(`${el.tagName}:${el.textContent||el.value}:vertical`);
            if(r.right>outer.right-16+0.5 || r.left<outer.left+16-0.5) clipped.push(`${el.tagName}:${el.textContent||el.value}:horizontal`);
            if(el.classList.contains('ac-textBlock') && el.scrollWidth>el.clientWidth+1) clipped.push(`text:${el.textContent}:truncated`);
          }
          if(height>expected+0.5) clipped.push(`card:${height}>${expected}`);
          const mode=id.split('-').slice(3).join('-');
          if(mode.startsWith('edit') && !content.textContent.includes('2026-10-14')) errors.push('Edit form does not display the shift date');
          if(mode.startsWith('edit-') && !content.textContent.includes(data.clockInTimeError)) errors.push('Edit form does not display the specific time error');
          if(mode==='auto' || mode==='edit') {
            const input=content.querySelector('input');
            if(input.maxLength>0) errors.push('A character limit can silently truncate pasted times');
            input.value='07:45';
            window.lastLayoutAction=null;
            [...content.querySelectorAll('button')].find(b=>b.textContent===data.saveShort)?.click();
            const action=window.lastLayoutAction;
            if(action?.verb!==(mode==='auto'?'saveAutoClockIn':'saveTime')
                || action.data?.[mode==='auto'?'autoClockInAfter':'clockIn']!=='07:45')
              errors.push('Save did not submit the edited time: '+JSON.stringify(action));
            input.value=' ０９３０ ';
            [...content.querySelectorAll('button')].find(b=>b.textContent===data.saveShort)?.click();
            if(window.lastLayoutAction?.data?.[mode==='auto'?'autoClockInAfter':'clockIn']!==' ０９３０ ')
              errors.push('Save did not preserve the full pasted value for server-side parsing');
            input.value='25:99';
            [...content.querySelectorAll('button')].find(b=>b.textContent===data.cancel)?.click();
            if(window.lastLayoutAction?.verb!==(mode==='auto'?'cancelAutoClockIn':'cancelEdit'))
              errors.push('Cancel did not invoke its action');
          }
          return {height,errors,clipped};
        }, {template,data,size,host,id});
        total++;
        if(result.error || result.errors?.length || result.clipped?.length) failures.push({id,...result});
        // Keep a compact gallery of the small cards for visual inspection.
        if(size!=='small' || language!==galleryLanguage) await page.locator('#'+id).evaluate(el=>el.remove());
      }
    }
    await page.screenshot({path:path.join(deps, `small-${theme}-${galleryLanguage}.png`), fullPage:true});
  }
  console.log(JSON.stringify({cases:total,failures},null,2));
  await browser.close();
  process.exitCode=failures.length?1:0;
})().catch(async e=>{console.error(e);await layoutBrowser?.close();process.exitCode=1;});
