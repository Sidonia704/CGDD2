const fs=require('fs');const path=require('path');
const root=path.join(__dirname,'reference-assets');fs.mkdirSync(root,{recursive:true});
async function get(url){const r=await fetch(url,{signal:AbortSignal.timeout(45000)});if(!r.ok)throw new Error(r.status+' '+url);return r;}
async function save(url,name){const b=Buffer.from(await(await get(url)).arrayBuffer());fs.writeFileSync(path.join(root,name),b);return {file:name,url,bytes:b.length};}
async function main(){
const records=[];
if(process.argv.includes('--airships')){
 const j=await(await get('https://store.steampowered.com/api/appdetails?appids=342560&l=english')).json();
 const d=j['342560'].data;
 for(let i=0;i<2;i++)console.log(await save(d.screenshots[i].path_full,'Airships-'+i+'.jpg'));
 return;
}
if(process.argv.includes('--minecraft')){
 const source='https://www.minecraft.net/en-us/article/safe-haven-on-a-dark-night';
 const h=await(await get(source)).text();
 const all=[...new Set([...h.matchAll(/(?:https:\/\/www.minecraft.net)?\/content\/dam\/minecraftnet\/[^\s"<>]+\.(?:jpg|png|jpeg)/g)].map(x=>x[0]))];
 console.log(all.filter(x=>!x.includes('Global-Header')&&!x.includes('global-header')).slice(-22));
 const hero=all.find(x=>/mango|haven|dark-night|castle/i.test(x));
 if(hero)console.log(await save(hero.startsWith('http')?hero:'https://www.minecraft.net'+hero,'Minecraft-build.jpg'));
 return;
}
for(const [name,id] of [['Reassembly',329130],['Robocraft',301520],['Cosmoteer',799600],['Nimbatus',383840],['CaptainForever',298560]]){
try{let j=await(await get('https://store.steampowered.com/api/appdetails?appids='+id+'&l=english')).json();let d=j[id].data;if(!d){console.log(name,'unavailable');continue;}
fs.writeFileSync(path.join(root,name+'-metadata.json'),JSON.stringify(d,null,2));
for(let i=0;i<Math.min(3,d.screenshots.length);i++){records.push({...await save(d.screenshots[i].path_full,name+'-'+i+'.jpg'),game:name,source:'https://store.steampowered.com/app/'+id+'/'});}
console.log(name,d.screenshots.length);
}catch(e){console.log(name,e.message);}}
const m='https://www.minecraft.net/en-us/article/what-minecraft';
try{let html=await(await get(m)).text();fs.writeFileSync(path.join(root,'minecraft-page.html'),html);let urls=[...html.matchAll(/(?:https:\/\/www.minecraft.net)?\/content\/dam\/minecraftnet\/[^\s"<>]+\.(?:jpg|png|jpeg)/g)].map(x=>x[0]);urls=[...new Set(urls)];console.log('MINECRAFT IMAGES',JSON.stringify(urls.slice(0,25)));}catch(e){console.log(e.message)}
fs.writeFileSync(path.join(root,'sources.json'),JSON.stringify(records,null,2));}
main();
