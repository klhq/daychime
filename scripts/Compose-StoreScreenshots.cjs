// Compose unmodified real-host captures on a Store-size background.
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.DAYCHIME_PLAYWRIGHT_MODULE || '../artifacts/widget-layout-check/node_modules/playwright');
const root = path.resolve(__dirname, '..');
const input = path.join(root, 'docs', 'store-images', 'captures');
const output = path.join(root, 'docs', 'store-images', 'listings');
(async () => {
  const browser = await chromium.launch({headless:true, channel:'msedge'});
  try {
    const page = await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1});
    await page.setContent('<!doctype html><html><head><style>*{box-sizing:border-box}body{margin:0;width:1920px;height:1080px;display:grid;place-items:center;background:linear-gradient(135deg,#e8f2fc 0%,#c4dcf4 52%,#e4effa 100%)}img{display:block;max-width:100%;max-height:100%;box-shadow:0 20px 60px #46698933;border-radius:12px}</style></head><body><img></body></html>');
    for (const locale of ['en-US','zh-TW','zh-CN']) {
      const destination = path.join(output,locale); fs.mkdirSync(destination,{recursive:true});
      for (const name of ['01-working.png','02-work-hours.png','03-edit.png','04-auto-clock-in.png']) {
        const file = path.join(input,locale,name);
        const data = fs.readFileSync(file).toString('base64');
        const dims = await page.evaluate(async uri => {const img=document.querySelector('img');img.src=uri;await img.decode();return {width:img.naturalWidth,height:img.naturalHeight,shownWidth:img.getBoundingClientRect().width,shownHeight:img.getBoundingClientRect().height};}, 'data:image/png;base64,'+data);
        if(dims.width!==dims.shownWidth || dims.height!==dims.shownHeight) throw new Error('Unexpected image scaling: '+file);
        await page.screenshot({path:path.join(destination,name)});
        console.log(locale, name, JSON.stringify(dims));
      }
    }
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1});
