import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {Presentation,PresentationFile} from '@oai/artifact-tool';
const build=path.dirname(fileURLToPath(import.meta.url));
const root=path.dirname(build), assets=path.join(root,'reference-assets'), output=path.join(root,'redesign-output');
const W=3000,H=3600,C={bg:'#FAF9F5',ink:'#20262A',red:'#CB4A4F',gray:'#657177',line:'#C5C5C1',pale:'#F7E9E7'};
const p=Presentation.create({slideSize:{width:W,height:H}}), slide=p.slides.add();slide.background.fill=C.bg;
const textRecords=[];
function box(x,y,w,h,fill='none',stroke='none',lw=0){return slide.shapes.add({geometry:'rect',position:{left:x,top:y,width:w,height:h},fill,line:{fill:stroke,width:lw,style:'solid'}})}
function text(x,y,w,h,t,size=30,color=C.ink,bold=false){let s=box(x,y,w,h);s.text=t;s.text.style={typeface:'Arial',fontSize:size,color,bold,alignment:'left',verticalAlignment:'top',insets:0,wrap:'square',autoFit:'none'};textRecords.push({shape:s,value:t});return s;}
function section(y,h,n,title){box(70,y,1840,h,'none',C.line,1.3);text(97,y+24,1785,58,`${n}   ${title}`,39,C.red,true);}
function sub(x,y,w,title,body,h=130){text(x,y,w,40,title,28,C.red,true);text(x,y+46,w,h,body,29);}
async function photo(file,x,y,w,h,alt,fit='cover'){const bytes=await fs.readFile(path.join(assets,file));slide.images.add({blob:bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),contentType:file.endsWith('.png')?'image/png':'image/jpeg',alt,fit,position:{left:x,top:y,width:w,height:h}});}
function cap(x,y,w,title,body){text(x,y,w,40,title,29,C.ink,true);text(x,y+43,w,110,body,25,C.gray);}

text(70,49,2800,80,'Starframe — Cultural Style: DIY Maker Culture',62,C.red,true);
text(73,140,2800,50,'Cultural style board / modular construction, practical learning and the pleasure of making',29,C.gray);
box(70,219,2860,363,'#FFFFFF',C.line,1.3);
sub(100,248,2780,'01  WHAT I NOTICED','I miss the freedom to build that I experienced in Robocraft and Minecraft, and I want to bring that creative excitement into my own game.',76);
sub(100,365,2780,'02  CLAIM','DIY culture gives players ownership and understanding through making, testing and improving their own spacecraft.',70);
sub(100,474,2780,'03  PLAYER ACTIONS','Assemble a ship; configure thrust; compare performance; diagnose failures; revise and save blueprints.',60);

section(630,370,'1','Meaning / the player as maker');
sub(100,721,835,'Creative ownership','Arrange ordinary parts into a ship that expresses your own idea.',80);
sub(100,858,835,'Learning through play','Test a design, observe its weakness and change it.',80);
sub(1000,721,850,'Resourcefulness','Balance armor, firepower, mass and energy within a fixed budget.',80);
sub(1000,858,850,'Persistence','Keep the blueprint after failure and return with a better version.',80);

section(1030,1220,'2','Play & interaction / a workshop loop');
text(100,1116,1760,49,'BUILD   /   TEST   /   DIAGNOSE   /   REVISE   /   FIGHT',35,C.ink,true);
text(100,1192,1760,159,'Assemble a modular spacecraft on a 3D grid. Configure directional propulsion, then try movement and weapons in the range. Compare behavior with the design you intended. Return to the blueprint before facing arcade combat waves.',31);
sub(100,1355,830,'Freedom to experiment','All parts are available from the start. Save unfinished or over-budget ideas. Deployment requires a connected ship within the mission budget.',150);
sub(1000,1355,830,'Choices have consequences','Blocked exhaust reduces usable thrust. Armor adds mass. Insufficient power reduces output. Each limit gives the player a reason to redesign.',150);
await photo('Reassembly-1.jpg',100,1582,850,380,'Reassembly official ship editor showing a modular ship and component palette');
await photo('Robocraft-2.jpg',1000,1582,850,380,'Robocraft official build bay showing a player-built robot');
cap(100,1984,850,'Reassembly [1]','Borrow: a readable parts palette and the link between ship design and performance.');
cap(1000,1984,850,'Robocraft [2]','Borrow: the build bay and the satisfaction of taking your own machine into combat.');
box(99,2114,1751,2,C.line);
text(100,2139,290,65,'UI / UX',27,C.red,true);
text(400,2139,1430,90,'The editor feels like a workbench. Speed, energy and component diagnostics explain failures and guide the next revision.',28);

section(2280,350,'3','Spaces / a workshop in orbit');
sub(100,2366,840,'Structures & wear','Component racks, modular frames and orbital platforms make space feel constructed. Scuffed metal suggests repeated use and repair.',150);
sub(1000,2366,830,'Ritual places','The test range is a place to repeat first launch, measurement and return. Cold planetary light contrasts with warm work lights in the bay.',150);

section(2660,220,'4','Sound / feedback for experiments');
text(100,2750,1730,106,'An equipment hum supports concentration. Placement clicks confirm decisions. Changing engine response makes tests readable. A power warning prompts diagnosis; a clean launch after revision marks progress.',29);

section(2910,290,'5','Look / visible construction');
sub(100,3000,830,'Shape & material','Modular blocks, exposed joints and clear silhouettes reveal how things fit. Worn panels and work lights suggest practical use.',130);
sub(1000,3000,830,'Color & light','Graphite and off-white keep parts readable. Cyan identifies tools, amber signals attention and red marks a fault.',130);

section(3230,260,'6','Sources / DIY Maker Culture');
text(100,3320,1720,106,'DIY Maker Culture comes from hands-on workshops and makerspaces, represented by Maker Faire and student machine shops, with Robocraft, Reassembly and Minecraft as game references for player-built creations.',28);
text(100,3431,1730,37,'Culture: Maker Faire, Mission [7]. Workshop rule: CMU, Makerspace Safety Program [8].',23,C.gray);

text(1980,635,940,62,'DIY CONSTRUCTION / GAME REFERENCES',34,C.red,true);
await photo('Reassembly-0.jpg',1980,715,950,500,'Reassembly modular spacecraft in combat');
cap(1980,1233,950,'REASSEMBLY [1]','A ship made from visible parts. Construction choices remain legible during battle.');
await photo('Robocraft-0.jpg',1980,1395,461,330,'Robocraft player-created aircraft flying in a canyon');
await photo('Minecraft-build.jpg',2469,1395,461,330,'Minecraft castle by Mr Mega Mango featured on Minecraft.net');
cap(1980,1744,461,'ROBOCRAFT [2]','A custom machine becomes a vehicle you can pilot.');
cap(2469,1744,461,'MINECRAFT [3]','Personal expression through blocks. Build: Mr_Mega_Mango.');
await photo('Cosmoteer-1.jpg',1980,1920,950,480,'Cosmoteer cutaway ship fighting with lasers');
cap(1980,2420,950,'COSMOTEER [4]','Readable interiors and functional modules connect the look of a ship with its operation.');
await photo('Nimbatus-2.jpg',1980,2590,461,330,'Nimbatus constructed drone in a destructible environment');
await photo('Airships-0.jpg',2469,2590,461,330,'Airships Conquer the Skies pixel airships made from functional modules','contain');
cap(1980,2940,461,'NIMBATUS [5]','A machine built to solve a practical challenge.');
cap(2469,2940,461,'AIRSHIPS [6]','Conquer the Skies: pixel modules make construction readable.');

box(1980,3150,950,340,C.pale);
text(2010,3180,890,50,'WORKSHOP TABOO / GAME RULE',29,C.red,true);
text(2010,3243,890,204,'Never adjust machinery while it is running. Manual structural editing is unavailable during combat. The player must finish or exit the run and return to construction. Until then, the design must face its weaknesses. Automatic recovery remains a separate system.',29);

text(70,3521,2850,35,'Image credits: developer screenshots via official Steam listings [1, 2, 4, 5, 6]. Minecraft image: Mr_Mega_Mango / Minecraft.net [3]. Reference images inform the design direction.',22,C.gray);

const sources=[
['1','Reassembly / Anisoptera Games','https://store.steampowered.com/app/329130/Reassembly/'],
['2','Robocraft / Freejam','https://store.steampowered.com/app/301520/Robocraft/'],
['3','Minecraft / Safe Haven on a Dark Night / Mr_Mega_Mango','https://www.minecraft.net/en-us/article/safe-haven-on-a-dark-night'],
['4','Cosmoteer / Walternate Realities','https://store.steampowered.com/app/799600/'],
['5','Nimbatus / Stray Fawn Studio','https://store.steampowered.com/app/383840/'],
['6','Airships: Conquer the Skies / David Stark','https://store.steampowered.com/app/342560/'],
['7','Maker Faire, Mission','https://makerfaire.com/community/mission/'],
['8','Carnegie Mellon University, Student Makerspace Safety Program','https://www.cmu.edu/ehs/Workplace-Construction/documents/ehs-student-makerspace-safety-program.pdf']];
slide.speakerNotes.text=sources.map(s=>`[${s[0]}] ${s[1]}: ${s[2]}`).join('\n')+'\nOpening lines translate the user’s stated motivation and approved direction. Proposed spatial/audio choices are design intentions.';
for(const {shape,value} of textRecords){for(const source of sources){const marker=`[${source[0]}]`;if(value.includes(marker))shape.text.get(marker).link={uri:source[2],isExternal:true};}}
await fs.writeFile(path.join(root,'reference-sources.md'),sources.map(s=>`[${s[0]}] ${s[1]}: ${s[2]}`).join('\n'));
await fs.mkdir(output,{recursive:true});
await fs.writeFile(path.join(build,'board-preview.png'),new Uint8Array(await (await p.export({slide,format:'png',scale:0.45})).arrayBuffer()));
await (await PresentationFile.exportPptx(p)).save(path.join(build,'candidate.pptx'));
const skill='C:/Users/34808/.codex/plugins/cache/openai-primary-runtime/presentations/26.1015.12040/skills/presentations';
const {finalizePresentation}=await import(pathToFileURL(path.join(skill,'container_tools/artifact_tool_utils.mjs')).href);
const result=await finalizePresentation({explicitTotalSlideCount:1,workspaceDir:root,candidatePath:path.join(build,'candidate.pptx'),finalPath:path.join(output,'Starframe-Cultural-Style-Final.pptx'),pythonExecutable:'C:/Users/34808/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe',integrityValidatorPath:path.join(skill,'container_tools/inspect_presentation_package_integrity.py'),layoutValidatorPath:path.join(skill,'container_tools/inspect_presentation_layout_geometry.py'),layoutArgs:['--expected-slide-size-emu',`${W*9525},${H*9525}`,'--validate-heading-fit'],fontPolicy:{basis:'design',families:['Arial']},verifyArtifactToolImport:true,receiptPath:path.join(build,'validation-final.json')});
console.log(result);
