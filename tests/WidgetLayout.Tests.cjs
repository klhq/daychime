const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const { chromium } = require(process.env.DAYCHIME_PLAYWRIGHT_MODULE || path.resolve(__dirname, '../artifacts/widget-layout-check/node_modules/playwright'));
const root = path.resolve(__dirname, '..');
const deps = path.resolve(process.env.DAYCHIME_LAYOUT_DEPS || path.join(root, 'artifacts/widget-layout-check'));
const template = JSON.parse(fs.readFileSync(path.join(root, 'src/DaychimeWidget/Templates/DaychimeTemplate.json'), 'utf8'));
const locales = ['Resources/Strings.en.json', 'Locales/TraditionalChinese.json', 'Locales/SimplifiedChinese.json'];
const modes = ['idle', 'idle-manual', 'missing', 'working', 'working-tomorrow', 'working-timezone', 'overtime', 'clocked-out',
  'edit', 'edit-error', 'edit-future', 'edit-dst', 'confirm-clear', 'customize', 'customize-error'];
const dependencies = path.join(deps, 'adaptivecards-designer/package');
const galleryLanguage = Number(process.argv[2] || 0);
// Microsoft's widget guidance: at most 1, 3 and 4 touch targets on the main card.
const maxTouchTargets = { small: 1, medium: 3, large: 4 };

// Same shape as ShiftPresentation.BuildProgressBar: a 536x12 PNG drawn at 2x for 268px.
function progressBar(rgba) {
  const width = 536, height = 12, rows = Buffer.alloc(height * (1 + width * 4));
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) rows.set(rgba, y * (1 + width * 4) + 1 + x * 4);
  const crcTable = [...Array(256)].map((_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = buffer => { let c = 0xFFFFFFFF; for (const b of buffer) c = crcTable[(c ^ b) & 0xFF] ^ (c >>> 8); return (c ^ 0xFFFFFFFF) >>> 0; };
  const chunk = (type, data) => {
    const length = Buffer.alloc(4); length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const sum = Buffer.alloc(4); sum.writeUInt32BE(crc(body));
    return Buffer.concat([length, body, sum]);
  };
  const header = Buffer.alloc(13); header.writeUInt32BE(width, 0); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const png = Buffer.concat([Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), chunk('IHDR', header), chunk('IDAT', zlib.deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]);
  return 'data:image/png;base64,' + png.toString('base64');
}
const bars = { barLight: progressBar([0x00, 0x5F, 0xB8, 0xFF]), barDark: progressBar([0x60, 0xCD, 0xFF, 0xFF]) };

// Mirrors Daychime.GetDataForWidget with worst-case lengths (12-hour times, double-digit hours).
function buildData(s, mode, language) {
  // .NET composite format: {0}, plus zero-padding specs such as {1:00}.
  const f = (text, ...values) => text.replace(/\{(\d+)(?::(0+))?\}/g, (_, index, pad) => String(values[index]).padStart(pad ? pad.length : 0, '0'));
  const clock = (h, m) => f(s.time12, h, m, s.pm);
  const time = clock(12, '59');
  const date = language === 0 ? 'Wed, Sep 30' : '9月30日 週三';
  const data = {
    view: 'main', showShift: false, isOvertime: false, date, now: f(s.nowLine, time),
    editShort: s.editShort, editTitle: s.editTime, previousCaption: s.previousCaption, settingsCaption: s.settingsCaption,
    settingsLine: f(s.settingsLine, '9.5', s.timeFormat12, f(s.autoOnSummary, f(s.time12, 6, '00', s.am))),
    timezoneAdjusted: false, timezoneAdjustedNote: s.timezoneAdjustedNote, hasPrevious: true,
    previousLine: f(s.previousRange, `9/29 ${time}`, `9/30 ${time}`, f(s.elapsedHoursMinutes, 10, 59)),
    previousShort: f(s.previousCompact, time, time)
  };
  const primary = (verb, shortKey, longKey) => Object.assign(data, { primaryVerb: verb, primaryShort: s[shortKey], primaryLong: s[longKey] });
  const shiftMode = mode.startsWith('working') || mode === 'overtime' || mode.startsWith('edit') || mode === 'confirm-clear';
  if (shiftMode) {
    const over = mode === 'overtime';
    Object.assign(data, bars, {
      showShift: true, isOvertime: over, finish: time,
      finishCaption: over ? s.finishCaptionReached : mode === 'working-tomorrow' ? s.finishCaptionTomorrow : s.finishCaption,
      remaining: f(over ? s.overtimeLong : s.remainingLong, f(s.durationHoursMinutes, 10, 59)),
      statusShort: f(over ? s.overtimeShort : s.remainingShort, '10:59'),
      progressAlt: f(s.progressAlt, 61), startCaption: s.startCaption, startShort: time, workedCaption: s.workedCaption,
      workedShort: '10:59', leftCaption: over ? s.overCaption : s.leftCaption, leftShort: over ? '+10:59' : '10:59',
      startedLine: f(s.startedLine, time, '9.5'), timezoneAdjusted: mode === 'working-timezone'
    });
    primary('clockOut', 'clockOutShort', 'clockOutLong');
  } else if (mode === 'missing') {
    Object.assign(data, { headline: s.headlineMissing, statusShort: s.statusMissingShort, statusLong: s.statusMissingLong });
    primary('clockIn', 'clockInShort', 'clockInNowLong');
  } else if (mode === 'clocked-out') {
    Object.assign(data, { headline: s.headlineClockedOut, hasPrevious: false, statusShort: f(s.statusClockedOutShort, time, '10:59'),
      statusLong: f(s.statusClockedOutLong, time, time, f(s.durationHoursMinutes, 10, 59)) });
    primary('undoClockOut', 'undoShort', 'undoClockOut');
  } else if (mode.startsWith('idle') || mode.startsWith('customize')) {
    const auto = mode !== 'idle-manual';
    Object.assign(data, { headline: s.headlineIdle, statusShort: auto ? s.statusIdleAutoShort : s.statusIdleManualShort,
      statusLong: auto ? f(s.statusIdleAutoLong, f(s.time12, 6, '00', s.am)) : s.statusIdleManualLong, hasPrevious: mode !== 'idle' });
    primary('clockIn', 'clockInShort', 'clockInLong');
  }
  if (mode.startsWith('edit')) {
    const error = mode === 'edit-future' ? s.futureClockInShort : mode === 'edit-dst' ? s.invalidLocalTimeShort : f(s.invalidTimeShort, f(s.time12, 9, '30', s.am));
    Object.assign(data, { view: 'edit', editHint: f(s.editHint, date, f(s.time12, 9, '30', s.am)), timePlaceholder: f(s.time12, 9, '30', s.am), editLabel: s.clockInTimeLabel, editLabelShort: f(s.editLabelShort, '9/30'),
      clockInValue: { 'edit-error': '25:99', 'edit-future': clock(11, '59'), 'edit-dst': f(s.time12, 2, '30', s.am) }[mode] || time,
      save: s.saveShort, cancel: s.cancel, hasTimeError: mode !== 'edit', clockInTimeError: error, clearTitle: s.clearLink });
  } else if (mode === 'confirm-clear') {
    Object.assign(data, { view: 'confirmClear', confirmClear: s.confirmClear, confirmClearHint: s.confirmClearHint, clear: s.clear, cancel: s.cancel });
  } else if (mode.startsWith('customize')) {
    Object.assign(data, {
      view: 'customize', workHoursLabel: s.workHoursLabel, workHoursValue: '9.5',
      workHoursChoices: ['8', '8.5', '9', '9.5', '10'].map(value => ({ title: f(s.hoursChoice, value), value })),
      timeFormatLabel: s.timeFormatLabel, timeFormatValue: 'system',
      timeFormatChoices: [['system', s.timeFormatSystem], ['12', s.timeFormat12], ['24', s.timeFormat24]].map(([value, title]) => ({ title, value })),
      autoToggleTitle: s.autoToggleTitle, autoValue: 'true', autoAfterLabel: s.autoAfterLabel,
      autoAfterValue: mode === 'customize-error' ? '25:99' : f(s.time12, 6, '00', s.am), autoAfterPlaceholder: f(s.time12, 6, '00', s.am),
      hasAutoTimeError: mode === 'customize-error',
      invalidTimeShort: f(s.invalidTimeShort, f(s.time12, 6, '00', s.am)), autoClockInExplanation: f(s.autoClockInExplanation, clock(6, '00')), customizeSmallHint: s.customizeSmallHint,
      done: s.done, cancel: s.cancel });
  }
  return data;
}

let layoutBrowser;
(async () => {
  const browser = layoutBrowser = await chromium.launch({headless:true,...(process.env.DAYCHIME_BROWSER_EXECUTABLE ? {executablePath:process.env.DAYCHIME_BROWSER_EXECUTABLE} : {channel:'msedge'})});
  const page = await browser.newPage({viewport:{width:1100,height:900}});
  let total = 0;
  const failures = [];
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
        const data = buildData(s, mode, language);
        const id = `${theme}-${language}-${size}-${mode}`;
        const result = await page.evaluate(({template,data,size,host,id,theme,maxTargets}) => {
          const payload = new ACData.Template(template).expand({$root:data,$host:{widgetSize:size,hostTheme:theme}});
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
          for(const el of content.querySelectorAll('button,input,select,label,img,[role="button"],.ac-textBlock')) {
            const r=el.getBoundingClientRect();
            if(!r.width||!r.height) continue;
            const outer=frame.getBoundingClientRect();
            if(r.bottom>outer.top+expected-16+0.5) clipped.push(`${el.tagName}:${el.textContent||el.value||el.alt}:vertical`);
            if(r.right>outer.right-16+0.5 || r.left<outer.left+16-0.5) clipped.push(`${el.tagName}:${el.textContent||el.value||el.alt}:horizontal`);
            if(el.classList.contains('ac-textBlock') && el.scrollWidth>el.clientWidth+1) clipped.push(`text:${el.textContent}:truncated`);
          }
          if(height>expected+0.5) clipped.push(`card:${height}>${expected}`);
          const mode=id.split('-').slice(3).join('-');
          if(data.view==='main') {
            const targets=content.querySelectorAll('.ac-pushButton, .ac-selectable:not(.ac-pushButton)').length;
            if(targets>maxTargets[size]) errors.push(`${targets} touch targets exceed the ${size} limit of ${maxTargets[size]}`);
            if(!content.textContent.includes(data.primaryShort) && !content.textContent.includes(data.primaryLong)) errors.push('Primary action is missing');
          }
          if(data.showShift && data.view==='main') {
            const bar=content.querySelector('img');
            if(!bar) errors.push('Progress bar is missing');
            else if(Math.abs(bar.getBoundingClientRect().height-6)>1) errors.push(`Progress bar height ${bar.getBoundingClientRect().height}px, expected 6px`);
          }
          if(mode==='working-timezone' && size!=='small' && !content.textContent.includes(data.timezoneAdjustedNote)) errors.push('Timezone adjustment note is not displayed');
          if(mode.startsWith('edit') && !content.textContent.includes(size==='small' ? (data.hasTimeError ? data.clockInTimeError : data.editLabelShort) : data.editHint)) errors.push('Edit form does not identify the shift date');
          if(mode.startsWith('edit-') && !content.textContent.includes(data.clockInTimeError)) errors.push('Edit form does not display the specific time error');
          const form = mode==='edit' ? {save:'saveTime', cancel:'cancelEdit', input:'clockIn'}
            : mode==='customize' && size!=='small' ? {save:'saveCustomization', cancel:'cancelCustomization', input:'autoClockInAfter'} : null;
          if(form) {
            const input=content.querySelector('input[type="text"]');
            if(input.maxLength>0) errors.push('A character limit can silently truncate pasted times');
            const press=title=>[...content.querySelectorAll('button')].find(b=>b.textContent===title)?.click();
            input.value='07:45';
            window.lastLayoutAction=null;
            press(form.save==='saveTime' ? data.save : data.done);
            if(window.lastLayoutAction?.verb!==form.save || window.lastLayoutAction.data?.[form.input]!=='07:45')
              errors.push('Save did not submit the edited time: '+JSON.stringify(window.lastLayoutAction));
            input.value=' ０９３０ ';
            press(form.save==='saveTime' ? data.save : data.done);
            if(window.lastLayoutAction?.data?.[form.input]!==' ０９３０ ') errors.push('Save did not preserve the full pasted value for server-side parsing');
            if(form.save==='saveCustomization') {
              const sent=window.lastLayoutAction?.data||{};
              if(sent.workHours!=='9.5' || sent.timeFormat!=='system' || sent.autoClockIn!=='true') errors.push('Customization did not submit every setting: '+JSON.stringify(sent));
            }
            input.value='25:99';
            press(data.cancel);
            if(window.lastLayoutAction?.verb!==form.cancel) errors.push('Cancel did not invoke its action');
          }
          return {height,errors,clipped};
        }, {template,data,size,host,id,theme,maxTargets:maxTouchTargets});
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
