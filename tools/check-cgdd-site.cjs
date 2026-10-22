const fs=require('fs'),path=require('path'),vm=require('vm'),crypto=require('crypto'),assert=require('assert/strict');
const root=path.resolve(__dirname,'..'),site=path.join(root,'site');
const context={window:{}};vm.runInNewContext(fs.readFileSync(path.join(site,'data/bundle.js'),'utf8'),context);
const data=context.window.CGDD_DATA;
assert.ok(data&&Object.keys(data.scripts).length>0,'The export must contain actual game scripts.');
const audit=JSON.parse(fs.readFileSync(path.join(root,'Documentation/CGDD/annotation-audit.json'),'utf8'));
const verifyBaseline=process.argv.includes('--verify-setup-baseline');
function walk(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(dir,e.name)):e.name.endsWith('.cs')?[path.join(dir,e.name)]:[]);}
const currentFiles=walk(path.join(root,'Assets/_Starframe/Scripts/Runtime')).filter(file=>/^\/\/ @role /m.test(fs.readFileSync(file,'utf8')));
assert.equal(Object.keys(data.scripts).length,currentFiles.length,'Every annotated source file must have a unique export key.');
let expectedTunes=0;
for(const file of currentFiles){
 const row={file:path.relative(root,file).replaceAll('\\','/')};
 const source=fs.readFileSync(path.join(root,row.file),'utf8');
 const body=source.replace(/^(?:\/\/ @[^\r\n]*(?:\r?\n))+\r?\n/,'');
 if(verifyBaseline){const saved=audit.scripts.find(s=>s.file===row.file);assert.ok(saved);assert.equal(crypto.createHash('sha256').update(body).digest('hex'),saved.bodySha256,'Executable body changed during setup: '+row.file);}
 const key=path.basename(row.file,'.cs');
 assert.equal(data.scripts[key].path,row.file);
 assert.equal(data.scripts[key].source,source);
 const calls=(source.match(/^\/\/ @calls (.+)$/m)||[])[1]||'';
 for(const target of calls.split(/[,\s]+/).filter(Boolean))assert.ok(data.scripts[target],'Unknown collaborator: '+target);
 expectedTunes+=(source.match(/^\/\/ @tune /gm)||[]).length;
}
for(const [file,key] of [['script-flow.mmd','flow'],['tunables.md','tunables'],['challenge-map.md','challenge'],['DEVLOG.md','devlog']]){
 assert.equal(fs.readFileSync(path.join(site,'data',file),'utf8'),data[key]);
 const original=file==='DEVLOG.md'?path.join(root,file):path.join(root,'docs',file);
 assert.equal(fs.readFileSync(original,'utf8'),data[key],'Stale export: '+file);
}
const tunes=data.tunables.split('\n').filter(s=>/^\| [A-Za-z_]\w* \|/.test(s)&&!s.startsWith('| Script |'));
assert.equal(tunes.length,expectedTunes);
assert.ok(tunes.every(row=>!row.includes('| — |')),'Missing C# field default.');
if(verifyBaseline){
 assert.ok(data.tunables.includes('| WeaponDefinition | damage | float | 0.01+ | 40 |'),'Grouped declaration default missing.');
 assert.ok(data.tunables.includes('| BattleTuning | unlimitedRangePower | bool | false-true | true |'),'Boolean default missing.');
}
for(const name of ['app.js','lang.js','data/bundle.js'])new vm.Script(fs.readFileSync(path.join(site,name),'utf8'),{filename:name});
if(verifyBaseline)assert.equal(crypto.createHash('sha256').update(fs.readFileSync(path.join(root,'DEVLOG.md'))).digest('hex').toUpperCase(),'1CFA3036EECB82B195278E513097BA1583AB984873412FA6F116D2862F542F75','Existing DEVLOG history was modified during setup.');
console.log(JSON.stringify({scripts:currentFiles.length,tunables:tunes.length,uniquePartialFiles:true,exportSynchronized:true,...(verifyBaseline?{gameplayBodiesUnchanged:true,devlogHistoryUnchanged:true}:{})}));
