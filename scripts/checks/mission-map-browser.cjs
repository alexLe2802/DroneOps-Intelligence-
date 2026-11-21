// Synthetic browser regression. Requires FE dependencies, playwright-core and Chrome.
// Uses public basemap/DEM services; no login, API credentials or database.
const assert = require('node:assert/strict');
const http = require('node:http');
const path = require('node:path');
const fs = require('node:fs/promises');
const os = require('node:os');
const { createRequire } = require('node:module');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright-core');
const root = path.resolve(__dirname, '../..');
const fe = path.join(root, 'fe');
const feRequire = createRequire(path.join(fe, 'package.json'));
const { build } = feRequire('esbuild');

async function main() {
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'droneops-map-'));
  let browser, server;
  try {
    const result = await build({
      stdin: {
        contents: `import React, {useState} from 'react';
          import {createRoot} from 'react-dom/client';
          import MissionMap from './src/app/portal/missions/mission-map';
          function Fixture() {
            const [waypoints,setWaypoints]=useState([
              {sequenceOrder:1,latitude:10.849,longitude:106.7725,altitude:100,actionType:'TakeOff'},
              {sequenceOrder:2,latitude:10.85,longitude:106.775,altitude:null,actionType:'Land'}]);
            return <MissionMap waypoints={waypoints} geofences={[]}
              onAdd={(longitude,latitude)=>setWaypoints(w=>[...w,{sequenceOrder:w.length+1,longitude,latitude,altitude:100,actionType:'Land'}])}
              onUpdate={(index,patch)=>setWaypoints(w=>w.map((p,i)=>i===index?{...p,...patch}:p))}/>;
          }
          createRoot(document.getElementById('root')).render(<Fixture/>);`,
        resolveDir: fe, loader: 'tsx',
      },
      bundle: true, format: 'esm', write: false, jsx: 'automatic',
      tsconfig: path.join(fe, 'tsconfig.json'),
      plugins: [{name: 'browser-fixture', setup(b) {
        b.onResolve({filter: /^next\/dynamic$/}, () => ({path:'dynamic',namespace:'fixture'}));
        b.onLoad({filter: /.*/,namespace:'fixture'}, () => ({
          contents: `import React,{lazy,Suspense} from 'react';
            export default function dynamic(load){const C=lazy(load);return p=><Suspense fallback={null}><C {...p}/></Suspense>}`,
          loader:'jsx',resolveDir:fe,
        }));
        b.onResolve({filter: /^maplibre-gl$/}, () => ({path:'maplibre',namespace:'map-fixture'}));
        b.onLoad({filter: /.*/,namespace:'map-fixture'}, () => ({
          contents: `export * from ${JSON.stringify(path.join(fe,'node_modules/maplibre-gl/dist/maplibre-gl.mjs'))};
            import {Map as RealMap} from ${JSON.stringify(path.join(fe,'node_modules/maplibre-gl/dist/maplibre-gl.mjs'))};
            export class Map extends RealMap {constructor(options){super(options);window.fixtureMap=this;}}`,
          resolveDir:fe,
        }));
      }}],
    });
    // Execute the real proxy to get its current CSP instead of maintaining a test copy.
    const proxyBuild = await build({entryPoints:[path.join(fe,'src/proxy.ts')],bundle:true,platform:'node',format:'cjs',write:false,external:['next/server']});
    const proxyModule = {exports:{}};
    new Function('require','module','exports',proxyBuild.outputFiles[0].text)(feRequire,proxyModule,proxyModule.exports);
    const {NextRequest}=feRequire('next/server');
    const response=proxyModule.exports.proxy(new NextRequest('http://localhost/login'));
    const csp=response.headers.get('Content-Security-Policy');
    const nonce=csp.match(/'nonce-([^']+)'/)[1];
    server=http.createServer(async(req,res)=>{
      try {
        if(req.url==='/') {
          res.setHeader('Content-Security-Policy',csp);
          res.setHeader('Content-Type','text/html');
          res.end(`<html><head><link rel="stylesheet" href="/leaflet.css"><link rel="stylesheet" href="/maplibre.css"><link rel="stylesheet" href="/dashboard.css"></head><body style="margin:20px;background:#101720"><div id="root"></div><script type="module" nonce="${nonce}" src="/fixture.js"></script></body></html>`);
        } else if(req.url==='/fixture.js') {
          res.setHeader('Content-Type','application/javascript');res.end(result.outputFiles[0].contents);
        } else {
          const assets={
            '/maplibre-gl-worker.mjs':'public/maplibre-gl-worker.mjs',
            '/leaflet.css':'node_modules/leaflet/dist/leaflet.css',
            '/maplibre.css':'node_modules/maplibre-gl/dist/maplibre-gl.css',
            '/dashboard.css':'src/app/portal/dashboard.css',
          };
          if(!assets[req.url]) {res.writeHead(404);res.end();return;}
          res.setHeader('Content-Type',req.url.endsWith('.css')?'text/css':'application/javascript');
          res.end(await fs.readFile(path.join(fe,assets[req.url])));
        }
      } catch(error) {res.writeHead(500);res.end(String(error));}
    });
    await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
    browser=await chromium.launch({executablePath:process.env.CHROME_EXECUTABLE || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true});
    const page=await browser.newPage({viewport:{width:1280,height:800}});
    const errors=[],requests=[];
    page.on('pageerror',error=>errors.push(error.message));
    page.on('console',message=>{if(message.type()==='error' && /Content Security Policy|Worker failed|MIME type/.test(message.text())) errors.push(message.text());});
    page.on('request',request=>requests.push(request.url()));
    // Deliberately slower than the old 1500ms one-shot sampling timer.
    await page.route('https://s3.amazonaws.com/elevation-tiles-prod/**',async route=>{
      await new Promise(resolve=>setTimeout(resolve,1800));await route.continue();
    });
    await page.goto(`http://127.0.0.1:${server.address().port}`);
    await page.waitForSelector('.leaflet-container .leaflet-tile-loaded');
    assert.equal(requests.some(url=>url.includes('openfreemap')),false,'2D must not request OpenFreeMap');
    await page.locator('.mission-map').click({position:{x:800,y:300}});
    await page.waitForFunction(()=>document.querySelector('.map-hint')?.textContent.includes('3 WAYPOINTS'));
    await page.getByRole('button',{name:'3D',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('.terrain-status')?.textContent.includes('Terrain loaded'),{},{timeout:60000});
    await page.waitForFunction(()=>!!document.querySelector('.elevation-svg path[fill="#3a2e1e"]'),{},{timeout:15000});
    assert.match(await page.locator('.elevation-profile').textContent(),/Unknown/);
    assert.doesNotMatch(await page.locator('.elevation-profile').textContent(),/Min clearance:/);
    const hint=await page.locator('.map-hint').boundingBox();
    const canvas=await page.locator('.mission-map').boundingBox();
    assert.ok(hint.y + hint.height <= canvas.y + canvas.height,'map hint stays inside map, above profile');
    const terrain=await page.evaluate(()=>({
      terrain:window.fixtureMap.getTerrain(),
      shade:!!window.fixtureMap.getLayer('droneops-hillshade'),
      elevation:window.fixtureMap.queryTerrainElevation(window.fixtureMap.getCenter()),
      pitch:window.fixtureMap.getPitch(),
      center:window.fixtureMap.getCenter(),zoom:window.fixtureMap.getZoom(),
    }));
    assert.equal(terrain.terrain.exaggeration,1);
    assert.equal(terrain.shade,true);
    assert.ok(terrain.pitch>=50);
    assert.ok(Number.isFinite(terrain.elevation));
    await page.getByRole('button',{name:'2D',exact:true}).click();
    await page.waitForSelector('.leaflet-container');
    assert.match(await page.locator('.map-hint').textContent(),/3 WAYPOINTS/);
    await page.getByRole('button',{name:'3D',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('.terrain-status')?.textContent.includes('Terrain loaded'),{},{timeout:60000});
    const restored=await page.evaluate(()=>({center:window.fixtureMap.getCenter(),zoom:window.fixtureMap.getZoom()}));
    assert.ok(Math.abs(restored.center.lng-terrain.center.lng)<0.001);
    assert.ok(Math.abs(restored.zoom-terrain.zoom)<0.1);
    // Confirm actual relief in a mountainous area, not just a pitched flat city view.
    await page.evaluate(()=>window.fixtureMap.jumpTo({center:[108.0,15.07],zoom:12,pitch:65}));
    await page.waitForFunction(()=>{
      const map=window.fixtureMap;
      const elevation=map.queryTerrainElevation(map.getCenter());
      return map.isSourceLoaded('droneops-terrain-dem') && elevation>500;
    },{},{timeout:60000});
    await page.waitForFunction(()=>window.fixtureMap.loaded(),{},{timeout:60000});
    await page.screenshot({path:path.join(temp,'terrain.png')});
    await page.getByRole('button',{name:'2D',exact:true}).click();
    await page.waitForSelector('.leaflet-container');
    await page.unroute('https://s3.amazonaws.com/elevation-tiles-prod/**');
    await page.route('https://s3.amazonaws.com/elevation-tiles-prod/**',route=>route.fulfill({status:503,body:'Synthetic DEM outage'}));
    await page.getByRole('button',{name:'3D',exact:true}).click();
    await page.waitForFunction(()=>document.querySelector('.terrain-status')?.textContent.includes('could not be loaded'),{},{timeout:30000});
    await page.getByRole('button',{name:'2D',exact:true}).click();
    await page.waitForSelector('.leaflet-container');
    assert.deepEqual(errors,[]);
    console.log(JSON.stringify({passed:true,checks:['Leaflet/Esri default','2D waypoint editing','3D delayed DEM/profile','hillshade + terrain 1x','2D/3D route and viewport preserved','mountain elevation >500m','no CSP/worker errors','DEM outage reported + 2D remains available','missing altitude preserved; no invalid clearance'],terrain,screenshot:path.join(temp,'terrain.png')},null,2));
  } finally {
    await browser?.close();
    if(server) await new Promise(resolve=>server.close(resolve));
  }
}
main().catch(error=>{console.error(error);process.exitCode=1;});
