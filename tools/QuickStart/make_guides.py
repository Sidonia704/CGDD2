from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Paragraph
from reportlab.lib.colors import HexColor, Color
pdfmetrics.registerFont(TTFont('CN','C:/Windows/Fonts/simhei.ttf'))
pdfmetrics.registerFont(TTFont('EN','C:/Windows/Fonts/arial.ttf'))
pdfmetrics.registerFont(TTFont('ENB','C:/Windows/Fonts/arialbd.ttf'))
W,H=595.28,841.89;M=34;CW=(W-2*M-22)/2
ink=HexColor('#152a40');muted=HexColor('#53677a');accent=HexColor('#256e8b')
content={
'EN':{
 'title':'Pilot quick start','sub':'Build a ship. Fly a sortie. Refine your blueprint.',
 'start':'01  Your first sortie',
 'steps':['Open Ship Assembly from the hangar. Use your current ship or load one from Blueprint Library.','Fit a core, power, thrusters and weapons. Check Handling & Power; keep weapon muzzles and thruster exhausts clear.','Save a named blueprint. Return to the hangar, choose the mission, then check the ship and budget before confirming deployment. Use Test Flight / range setup to practise first.'],
 'assembly':'02  Assembly controls',
 'build':[('Left click','Place the selected module'),('R','Rotate the placement preview'),('X + left click','Toggle delete mode, then remove a part'),('Hold middle mouse','Orbit the ship'),('Shift + middle mouse','Pan the view'),('Mouse wheel','Zoom in / out'),('F','Frame the whole ship')],
 'blueprints':'Blueprints & weapon groups',
 'blueprinttext':['Save Blueprint stores the current design. Blueprint Library loads or deletes saved designs; an existing name asks before overwriting.','Open Weapon Groups, select a weapon in the 3D preview, assign its group and set that group\'s fire key. Apply your changes before leaving.'],
 'flight':'03  Flight controls',
 'fly':[('W / S','Forward / backward thrust'),('A / D','Left / right thrust'),('Space / Ctrl','Up / down thrust'),('Q / E','Roll left / right'),('Mouse movement','Continuous steering input'),('Hold Shift','Boost; consumes energy'),('Left mouse','Fire enabled groups (default binding)'),('F','Lock / unlock the target ahead'),('R','Deploy one ready flare launcher'),('V','Ballistic salvo / alternating fire'),('1 - 8','Enable / disable each weapon group'),('Mouse wheel','Adjust chase-camera distance'),('Esc','Pause / options / exit')],
 'combat':'04  Aim & survive',
 'tips':['Aim at a red target marker and press F. With a lock, use the lead indicator for moving targets. Missiles also need their weapon lock to complete.','Mouse offset is a sustained turn command: move back toward the centre to stop turning. The ship turns at its own capability; releasing movement input engages braking.','All weapon groups start enabled. Number keys only toggle groups; their assigned fire keys actually fire them. V affects ballistic guns, not beams or missiles.','Watch shield and energy gauges. Use flares when a missile is incoming; each launcher needs clear space above and has its own cooldown.'],
 'foot':'Default bindings shown. Settings can rebind flight actions; weapon fire keys belong to each blueprint.',
 'date':'DEMO GUIDE  /  03 NOV 2026'},
'ZH':{
 'title':'新手操作速查','sub':'拼装飞船 · 靶场试飞 · 确认出击',
 'start':'01  第一次出击',
 'steps':['从首页进入装配编辑器，使用当前飞船，或在蓝图库中载入已有设计。','配置核心、电源、推进器和武器。查看机动性与电力面板，保证炮口、喷口净空，部件通过面连接成整体。','保存具名蓝图。返回首页选择关卡，核对出击飞船与资源预算后确认。建议先使用测试／靶场设置练习飞行和开火。'],
 'assembly':'02  装配操作',
 'build':[('鼠标左键','放置当前选中的组件'),('R','旋转待放置组件'),('X + 鼠标左键','切换删除模式，再点击删除'),('按住鼠标中键','绕飞船旋转视角'),('Shift + 鼠标中键','平移视角'),('鼠标滚轮','缩放视角'),('F','聚焦整艘飞船')],
 'blueprints':'蓝图与武器组',
 'blueprinttext':['“保存蓝图”保存当前设计；“蓝图库”可载入或删除蓝图。保存重名蓝图时会询问是否覆盖。','在“武器组”中选取三维预览里的武器，将它分配到对应组，并设置该组的开火键；离开前应用更改。'],
 'flight':'03  飞行操作',
 'fly':[('W / S','前进／后退推力'),('A / D','左移／右移推力'),('空格 / Ctrl','上升／下降推力'),('Q / E','向左／向右滚转'),('移动鼠标','持续控制转向方向与力度'),('按住 Shift','加力加速，消耗能量'),('鼠标左键','按默认绑定开火'),('F','锁定／解除前方目标'),('R','释放一台就绪发射器的干扰弹'),('V','切换弹道武器齐射／交替射击'),('数字键 1 - 8','分别启用／关闭对应武器组'),('鼠标滚轮','调整第三人称镜头距离'),('Esc','暂停、选项或退出')],
 'combat':'04  瞄准与生存',
 'tips':['对准红色敌机标志后按 F 锁定。使用预瞄点瞄准移动目标；导弹还需完成自身的武器锁定。','鼠标偏离准星会持续转向，移回中央才停止转向输入。船头转速受机动性限制；松开移动键后飞船会自动制动。','战斗默认启用全部武器组。数字键只切换启用状态，真正开火仍需按该组开火键。V 对弹道武器生效，不影响光束与导弹。','关注护盾和能量仪表。导弹来袭时可按 R；发射器上方须无遮挡，每台有独立冷却时间。'],
 'foot':'本页为默认键位。飞行动作可在设置中改键；武器组开火键保存在各自蓝图中。',
 'date':'DEMO 操作指南  /  2026.11.03'}
}
def make(lang):
 d=content[lang];font='CN' if lang=='ZH' else 'EN';bold='CN' if lang=='ZH' else 'ENB'
 out=Path('output/pdf')/('Starframe_Quick_Start_'+lang+'.pdf');c=canvas.Canvas(str(out),pagesize=(W,H));c.setTitle('Starframe - '+d['title']);c.setAuthor('Starframe')
 c.setFillColor(HexColor('#0b2039'));c.rect(0,H-112,W,112,fill=1,stroke=0)
 c.setFillColor(HexColor('#9ecad5'));c.setFont('EN',10);c.drawString(M,H-28,'STARFRAME   /   QUICK START')
 c.setFillColor(HexColor('#ffffff'));c.setFont(bold,25);c.drawString(M,H-62,d['title']);c.setFont(font,10);c.setFillColor(HexColor('#cbd8e4'));c.drawString(M,H-86,d['sub'])
 ps=ParagraphStyle('body',fontName=font,fontSize=9.5,leading=13.5,textColor=ink,wordWrap='CJK' if lang=='ZH' else None)
 def para(text,x,y,width=CW,number=None):
  if number is not None:text=f'<font color="#256e8b">{number:02d}</font>  '+text
  p=Paragraph(text,ps);_,h=p.wrap(width,800);p.drawOn(c,x,y-h);return y-h-7
 def section(title,x,y):
  c.setFillColor(accent);c.setFont(bold,12.2);c.drawString(x,y,title);c.setStrokeColor(HexColor('#d4e0e9'));c.line(x,y-9,x+CW,y-9);return y-24
 def table(rows,x,y):
  keyw=92 if lang=='ZH' else 97
  for key,text in rows:
   p=Paragraph(text,ps);_,h=p.wrap(CW-keyw-9,100);rh=max(26,h+9)
   c.setFillColor(HexColor('#eef3f7'));c.rect(x,y-rh+3,keyw-3,rh-4,fill=1,stroke=0)
   kp=Paragraph(key,ParagraphStyle('k',parent=ps,fontName=bold,fontSize=9,leading=12));_,kh=kp.wrap(keyw-11,100);kp.drawOn(c,x+5,y-(rh+kh)/2+2)
   p.drawOn(c,x+keyw+4,y-(rh+h)/2+2);y-=rh
  return y-17
 x=M;y=H-140;y=section(d['start'],x,y)
 for i,text in enumerate(d['steps'],1):y=para(text,x,y,number=i)
 y-=7;y=section(d['assembly'],x,y);y=table(d['build'],x,y)
 y=section(d['blueprints'],x,y)
 for text in d['blueprinttext']:y=para(text,x,y)
 left=y
 x=M+CW+22;y=H-140;y=section(d['flight'],x,y);y=table(d['fly'],x,y);y=section(d['combat'],x,y)
 for text in d['tips']:y=para(text,x,y)
 assert min(left,y)>64,(lang,left,y)
 c.setStrokeColor(HexColor('#d4e0e9'));c.line(M,55,W-M,55)
 foot=Paragraph(d['foot'],ParagraphStyle('f',parent=ps,fontSize=8,leading=11,textColor=muted));_,fh=foot.wrap(W-2*M,50);foot.drawOn(c,M,43-fh)
 c.setFont(font,7.5);c.setFillColor(muted);c.drawString(M,15,d['date']);c.drawRightString(W-M,15,'01 / 01');c.save();print(out,min(left,y))
for lang in ('ZH','EN'):make(lang)

