const fs=require('fs'),sharp=require('C:/Users/34808/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const p=__dirname+'/reference-assets/';const files=fs.readdirSync(p).filter(x=>x.endsWith('.jpg'));
Promise.all(files.map(async(f,i)=>({input:await sharp(p+f).resize(420,236,{fit:'contain',background:'#fff'}).extend({top:25,bottom:0,left:0,right:0,background:'#ffffff'}).composite([{input:Buffer.from(`<svg width="420" height="25"><text x="5" y="19" font-size="16" font-family="Arial">${f}</text></svg>`),top:0,left:0}]).toBuffer(),top:Math.floor(i/3)*280,left:i%3*430}))).then(a=>sharp({create:{width:1290,height:Math.ceil(files.length/3)*280,channels:3,background:'#eee'}}).composite(a).png().toFile(p+'contact.png'));

