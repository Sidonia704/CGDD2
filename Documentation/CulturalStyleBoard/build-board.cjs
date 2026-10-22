const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/34808/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const out = __dirname;
const C={bg:'#F2F0E8',ink:'#172D36',muted:'#50656B',cyan:'#177D87',orange:'#B7502C',line:'#CDD5D0',white:'#FCFBF6'};
let s=[];
const esc=t=>t.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;');
function rect(x,y,w,h,fill,stroke='none',r=0){s.push(`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${fill}" stroke="${stroke}"/>`)}
function txt(x,y,t,size=24,color=C.ink,weight=400){s.push(`<text x="${x}" y="${y}" font-family="Arial, sans-serif" font-size="${size}" font-weight="${weight}" fill="${color}">${esc(t)}</text>`)}
function para(x,y,t,width=1120,size=25,color=C.ink,leading=35){
 let words=t.split(' '),line='',lines=[];
 const cap=width/(size*.51);
 for(const w of words){if((line+' '+w).length>cap){lines.push(line);line=w;}else line+=(line?' ':'')+w;}if(line)lines.push(line);
 lines.forEach((l,i)=>txt(x,y+i*leading,l,size,color));return y+lines.length*leading;
}
function label(y,n,title){rect(70,y-29,46,37,C.cyan);txt(80,y-2,n,24,C.white,700);txt(135,y,title,29,C.ink,700);}
function sub(y,title,body){txt(92,y,title,25,C.cyan,700);return para(92,y+35,body,1050,25,C.ink,35);}
function shot(y,n,title,caption){
 rect(1230,y,500,310,'#E6EAE4',C.line,5);
 s.push(`<path d="M1260 ${y+35}h35m-35 0v35M1700 ${y+35}h-35m35 0v35M1260 ${y+275}h35m-35 0v-35M1700 ${y+275}h-35m35 0v-35" stroke="${C.cyan}" fill="none" stroke-width="2"/>`);
 txt(1270,y+125,n+' / GAME SCREENSHOT',19,C.muted,700);
 txt(1270,y+168,'PLACE IMAGE HERE',27,C.muted,700);
 txt(1230,y+349,title,23,C.ink,700);
 para(1230,y+382,caption,490,21,C.muted,29);
}
s.push('<svg xmlns="http://www.w3.org/2000/svg" width="1800" height="2760" viewBox="0 0 1800 2760">');
rect(0,0,1800,2760,C.bg);
rect(0,0,1800,14,C.cyan);
txt(70,65,'STARFRAME  /  CULTURAL STYLE BOARD',19,C.cyan,700);
txt(1730,65,'01',19,C.muted,700);
txt(70,131,'Starframe — Cultural Style: DIY Maker Culture',57,C.ink,700);
txt(70,176,'A personal workshop in space. Build something you understand.',27,C.muted);
rect(70,213,1660,299,C.ink,undefined,8);
txt(98,252,'01  REAL-LIFE OBSERVATION',19,'#72CED0',700);
para(98,287,'I miss the freedom to build that I experienced in Robocraft and Minecraft, and I want to bring that creative excitement into my own game.',1570,25,'#FFFFFF',34);
txt(98,366,'02  CLAIM',19,'#72CED0',700);
para(98,401,'DIY culture gives players ownership and understanding through making, testing and improving their own spacecraft.',1570,25,'#FFFFFF',34);
txt(98,450,'03  PLAYER ACTIONS',19,'#72CED0',700);
txt(98,486,'Assemble a ship • Configure thrust • Compare performance • Diagnose failures • Revise blueprints',25,'#FFFFFF');
label(575,'1','MEANING');
let themes=[['Ownership','Create a ship with a personal identity through its parts and layout.'],['Learning by doing','Test an idea, observe what fails, then change the design.'],['Resourcefulness','Balance protection, firepower, mass and energy within a budget.'],['Persistence','Keep the blueprint and improve it after a failed attempt.']];
themes.forEach((t,i)=>{txt(92,625+i*64,t[0].toUpperCase(),19,C.cyan,700);txt(92,653+i*64,t[1],24);});
rect(70,878,1110,1122,C.white,C.line,8);
label(929,'2','PLAY & INTERACTION');
txt(92,968,'THE WORKSHOP LOOP',18,C.muted,700);
['BUILD','TEST','DIAGNOSE','REVISE','FIGHT'].forEach((t,i)=>{let x=92+i*213;rect(x,990,184,54,i===4?C.orange:C.ink,undefined,4);txt(x+18,1025,t,21,C.white,700);if(i<4)txt(x+190,1024,'›',26,C.cyan,700);});
para(92,1088,'Assemble a modular ship on a 3D grid. Arrange directional propulsion, protection and weapons. Test it in the range, inspect its behavior, then revise the blueprint. Arcade combat puts the design under pressure.',1050,25);
sub(1248,'FREEDOM + CONSEQUENCES','All parts are available from the start. Save unfinished experiments freely; deployment requires a connected ship within budget. Blocked exhaust reduces thrust, armor adds mass, and low power reduces output.');
rect(90,1432,1070,181,'#F2E8DC',undefined,5);
txt(112,1470,'WORKSHOP TABOO → GAME RULE',24,C.orange,700);
para(112,1508,'Never adjust a machine while it is running. In Starframe, manual structural editing is unavailable during combat. Finish or exit the run and return to the build area; the design must face the consequences until then.',1015,24,C.ink,32);
sub(1661,'UI AS A WORKBENCH','A parts catalogue, blueprint name and component data frame the ship. The flight HUD acts as onboard diagnostics: speed, energy and component condition help players explain a failure and decide what to change.');
txt(92,1851,'REFERENCES / WHAT TO BORROW',21,C.cyan,700);
para(92,1888,'Robocraft: modular construction shapes combat performance. Minecraft: simple blocks support personal invention. Borrow these relationships between creation and play.',1050,24,C.ink,33);
label(2073,'3','SPACES');
para(92,2117,'A proposed assembly bay uses modular frames and component racks. The test range becomes a ritual space for first launch, measurement and return. Cold planetary light and worn orbital panels suggest repeated use, repair and experimentation.',1070,24,C.ink,33);
label(2300,'4','SOUND');
para(92,2344,'Equipment hum and restrained electronics support concentration. Placement clicks mark decisions; engine response makes tests readable. A power warning prompts diagnosis, while a clean launch after revision marks progress.',1070,24,C.ink,33);
label(2520,'5','LOOK');
para(92,2564,'Modular blocks, exposed joints and clear silhouettes reveal how things fit. Graphite, off-white and cyan keep tools readable; amber and red mark warnings. Work lights and scuffed metal suggest a machine made to be used.',1070,24,C.ink,33);
txt(1230,575,'THE PLAYER AS MAKER',23,C.cyan,700);
para(1230,618,'Design intention: a practical workshop culture expressed through play, space and feedback.',485,23,C.muted,33);
shot(760,'01','THE PERSONAL WORKBENCH','Assembly view: show the player’s ship, parts catalogue and component choices.');
shot(1280,'02','UNDERSTANDING THE MACHINE','Test flight or diagnostics: show how performance reveals design decisions.');
shot(1800,'03','IDEAS UNDER PRESSURE','Combat or revised ship: show the consequences of a player-built design.');
rect(1230,2310,500,374,C.ink,undefined,6);
txt(1256,2354,'6  SOURCES',25,C.white,700);
para(1256,2396,'DIY Maker Culture comes from hands-on workshop and makerspace practice, represented by Maker Faire and student machine shops, with Robocraft and Minecraft as game references.',445,22,C.white,30);
txt(1256,2614,'Maker Faire / Mission',18,'#72CED0');
txt(1256,2641,'CMU / Makerspace Safety Program',18,'#72CED0');
s.push('</svg>');
fs.writeFileSync(path.join(out,'Starframe-Cultural-Style-Board.svg'),s.join('\n'));
sharp(Buffer.from(s.join('\n'))).resize({width:1304}).png().toFile(path.join(out,'Starframe-Cultural-Style-Board-preview.png'));
