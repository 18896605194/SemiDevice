import fs from 'node:fs';
import path from 'node:path';
import {createRequire} from 'node:module';
import {pathToFileURL} from 'node:url';

const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_CORE || 'C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright-core');
const root=path.resolve(process.argv[2]||'D:/Code');
const output=path.resolve(process.argv[3]||'D:/tools/ToolBuilder/qa/frontend-controls-guide');fs.mkdirSync(output,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROMIUM_PATH||'C:/Users/Administrator/AppData/Local/ms-playwright/chromium-1248/chrome-win64/chrome.exe',
 args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--allow-file-access-from-files']});
const page=await browser.newPage({viewport:{width:1500,height:980},deviceScaleFactor:1});
const errors=[],checks=[];page.on('pageerror',e=>errors.push(String(e)));page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
function check(ok,message){if(!ok)throw new Error(message);checks.push(message);}
try {
 await page.goto(pathToFileURL(path.join(root,'框架讲解.html')).href+'#fe-controls-start');
 await page.waitForSelector('#frontend[data-guide-ready="true"]');
 const catalog=JSON.parse(fs.readFileSync(path.join(root,'docs/frontend-controls/catalog.json'),'utf8'));
 check(await page.locator('#frontend .fe-control-topic').count()===catalog.controls.length,'44 个控件全部进入前端目录');
 check(await page.locator('#frontend .fe-properties tbody tr').count()===catalog.controls.reduce((n,c)=>n+c.properties.length,0),'公开依赖属性表与实际程序集导出一致');
 check(await page.locator('#nav summary').filter({hasText:/^前端$/}).count()===1,'原有自动目录包含独立前端模块');
 check(await page.locator('#nav a[href="#g1-1"]').count()===1,'原有 gRPC 目录仍正常');
 const ids=await page.locator('[id]').evaluateAll(els=>els.map(e=>e.id));check(new Set(ids).size===ids.length,'全文无重复锚点');
 await page.locator('#fe-controls-start').screenshot({path:path.join(output,'frontend-catalog.png'),timeout:60000});
 const search=page.locator('#fe-search');await search.fill('sensor');
 check(await page.locator('#fe-control-SignalLamp').isVisible(),'控件搜索支持 Sensor 别名');
 check(!(await page.locator('#fe-control-HoldButton').isVisible()),'控件筛选隐藏无关项');await search.fill('');
 const inputLab=page.locator('.fe-input-lab');await inputLab.locator('input').fill('150');await inputLab.locator('button').click();
 check((await inputLab.locator('output').textContent()).includes('Value 仍为 25.5'),'非法输入保留上次提交值');
 await inputLab.locator('input').fill('66.8');await inputLab.locator('button').click();
 check((await inputLab.locator('output').textContent()).includes('Value = 66.8'),'合法输入更新教学演示的提交值');
 const filter=page.locator('#fe-control-LiftVisual3D .fe-property-filter input');await filter.fill('MountHeight');
 check(await page.locator('#fe-control-LiftVisual3D tbody tr:visible').count()===1,'依赖属性支持按名称筛选');await filter.fill('');
 const meshControls=catalog.controls.filter(c=>c.is3D);
 for(const c of meshControls) {
   const demo=page.locator(`.fe-demo[data-demo="${c.name}"]`);await demo.scrollIntoViewIfNeeded();
   await page.waitForFunction(name=>document.querySelector(`.fe-demo[data-demo="${name}"]`).dataset.meshReady==='true',c.name,{timeout:30000});
   await demo.locator('canvas').waitFor();check(await demo.locator('canvas').count()===1,c.name+' 实际 WPF 网格可在离线 HTML 加载');
 }
 const arm=page.locator('.fe-demo[data-demo="ArmVisual3D"]');await arm.scrollIntoViewIfNeeded();await arm.locator('canvas').waitFor();
 const canvas=arm.locator('canvas');const before=await canvas.getAttribute('data-pose');const box=await canvas.boundingBox();
 await page.mouse.move(box.x+box.width*.5,box.y+box.height*.5);await page.mouse.down();await page.mouse.move(box.x+box.width*.65,box.y+box.height*.6,{steps:6});await page.mouse.up();
 check(await canvas.getAttribute('data-pose')!==before,'鼠标拖动改变三维视角');
 const zoomBefore=await canvas.getAttribute('data-pose');await page.mouse.wheel(0,-250);await page.waitForTimeout(100);
 check(await canvas.getAttribute('data-pose')!==zoomBefore,'滚轮改变三维缩放');
 await arm.locator('select').selectOption('2');await page.waitForFunction(()=>document.querySelector('.fe-demo[data-demo="ArmVisual3D"] output').textContent.includes('90'));
 check((await arm.locator('output').textContent()).includes('Angle = 90'),'示例切换显示真实依赖属性值');
 await arm.locator('.fe-native').click();check(await arm.locator('canvas').count()===0,'可切换到原生 WPF 截图');await arm.locator('.fe-native').click();await arm.locator('canvas').waitFor();
 await page.locator('#fe-control-ArmVisual3D').screenshot({path:path.join(output,'arm-control.png')});
 const tank=page.locator('#fe-control-TankVisual3D');await tank.scrollIntoViewIfNeeded();await tank.locator('canvas').waitFor();await tank.screenshot({path:path.join(output,'tank-control.png')});
 await page.setViewportSize({width:760,height:1000});await page.goto(pathToFileURL(path.join(root,'框架讲解.html')).href+'#fe-control-SignalLamp');
 await page.waitForSelector('#frontend[data-guide-ready="true"]');
 const overflow=await page.locator('#frontend').evaluate(e=>e.getBoundingClientRect().right>innerWidth+1);check(!overflow,'窄窗口前端内容保持在屏幕内');
 await page.locator('#fe-control-SignalLamp').screenshot({path:path.join(output,'signal-lamp-small.png')});
 check(errors.length===0,'浏览器无脚本错误、缺失资源或 WebGL 错误');
 fs.writeFileSync(path.join(output,'checks.txt'),checks.map(c=>'PASS '+c).join('\n'));
 console.log(`${checks.length} checks passed; ${meshControls.length} 3D controls verified offline.`);
}catch(error){await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});fs.writeFileSync(path.join(output,'errors.txt'),[String(error),...errors].join('\n'));throw error;}
finally{await browser.close();}
