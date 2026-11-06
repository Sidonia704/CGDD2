"""One-sided A4 beginner guides, English and Simplified Chinese. Run with bundled Python."""
from pathlib import Path
import json, shutil
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Paragraph

ROOT=Path(__file__).resolve().parents[2]
FONTS=ROOT/'Assets/_Starframe/assetsForCodex/fonts/Sulphur_Point'
pdfmetrics.registerFont(TTFont('EN',str(FONTS/'SulphurPoint-Regular.ttf')))
pdfmetrics.registerFont(TTFont('ENB',str(FONTS/'SulphurPoint-Bold.ttf')))
pdfmetrics.registerFont(TTFont('ZH','C:/Windows/Fonts/simhei.ttf'))
pdfmetrics.registerFontFamily('EN',normal='EN',bold='ENB',italic='EN',boldItalic='ENB')
pdfmetrics.registerFontFamily('ZH',normal='ZH',bold='ZH',italic='ZH',boldItalic='ZH')
W,H=841.89,595.28
M=25;GAP=15;CW=(W-M*2-GAP*2)/3
BLUE=HexColor('#12388f');INK=HexColor('#172339');MUTED=HexColor('#52647b')
LIME=HexColor('#b9ff03');LINE=HexColor('#d6dfe9');PALE=HexColor('#f0f4f8')
CONTENT={
'EN':dict(title='YOUR FIRST 5 MINUTES',sub='BUILD  >  PRACTISE  >  DEPLOY',
 build='1  BUILD & SAVE',steps=[
 '<b>EDIT SHIP</b> to build, or <b>BLUEPRINTS</b> to load a saved ship. Cards show <b>COST</b>.',
 'Fit a <b>core, power, thrusters and weapons</b>. Connect the hull; keep exhausts and launch paths clear. Direct guns face the core\'s nose.',
 '<b>SAVE</b>, then use <b>TEST FLIGHT</b> first. For a mission: <b>PLAY</b>, choose ship/difficulty, stay within budget, then <b>START</b>.'],
 buildkeys=[('Left click','Place; X toggles delete mode'),('Space','Open / close module catalogue'),('R','Rotate selected module'),('Alt + click','Copy a module to your hand'),('M','Mirror about the core; both sides must fit'),('P','Open / close WEAPON GROUPS'),('Middle drag','Orbit; Shift + drag pans'),('Wheel / F','Zoom / frame the whole ship')],
 groups='MAKE FIRE BUTTONS WORK',grouptext='In <b>WEAPON GROUPS</b>, drag a weapon to a group and <b>release</b> to assign it. A group click only shows its members. New/default groups: <b>1 = direct guns / left mouse</b>, <b>2 = missiles / right mouse</b>. Saved ships may use different bindings.',
 flight='2  PRACTISE & FIGHT',practice='<b>TRAINING RANGE:</b> SELECT TARGET, set ATTACK and MOVE, then ENTER. Try movement and weapons here before deploying.',
 flykeys=[('W / S','Forward / backward thrust'),('A / D','Strafe left / right'),('Space / Ctrl','Rise / descend'),('Q / E','Roll left / right'),('Mouse','Steer the ship'),('Hold Shift','Boost; high energy use'),('Left / right mouse','Fire their assigned weapon groups'),('F','Lock / unlock target ahead'),('Tab','Deploy a fitted, ready flare launcher'),('1 - 8','Toggle weapon groups ON / OFF'),('V','Direct-shot salvo / alternating cadence'),('Wheel / Esc','Camera distance / pause & options')],
 steering='Mouse offset is a <b>continuous turn command</b>. Bring it back toward the centre to ease the turn. Releasing movement keys engages automatic braking.',
 goal='YOUR MISSION',goaltext='Defeat <b>all 6 waves</b> and remaining enemies. The final wave brings the <b>t8 flagship + escorts</b>. Your core being destroyed ends the sortie. Repair the design and redeploy.',
 hud='3  READ THE FIGHT',hudrows=[('RED TARGETS','Radar contacts; F selects your main target.'),('SHIELD','Absorbs hits. It recovers using energy.'),('ENERGY','Powers propulsion, weapons and shield recovery.'),('WEAPON CELLS','Bright = ready. Dim / a number = unavailable or reloading.'),('HEAT','Gatling orange heat rises during fire. After overheating, cool to empty to fire again.')],
 shoot='AIM, FIRE, RECOVER',tips=[
 '<b>Beams:</b> aim at the ship. <b>Projectile guns:</b> use the red lead marker for moving targets. Slight assistance corrects mounting offset only.',
 '<b>Regular missiles:</b> F-lock a target, wait for missile acquisition, then hold the missile group\'s fire button. Reloading missiles must acquire again.',
 '<b>VLS:</b> keep radar-marked enemies in view for <b>4 seconds</b>, then fire its group. It splits the magazine across targets; each unit reloads independently.',
 '<b>Low energy?</b> Release boost and stop firing briefly. Shield recovery gets power first, then propulsion, then weapons. Boost raises the speed limit; releasing it restores the normal limit.'],
 footer='Default keys shown. Settings can rebind flight actions; weapon fire bindings belong to each blueprint.  |  ? / HELP explains the current screen.'),
'ZH':dict(title='新手五分钟速查',sub='拼装  >  靶场练习  >  正式出击',
 build='1  拼装并保存',steps=[
 '点 <b>EDIT SHIP / 编辑飞船</b> 开始拼装，或从 <b>BLUEPRINTS / 蓝图库</b> 载入。蓝图卡片的 <b>COST</b> 是造价。',
 '装上<b>核心、电源、推进器和武器</b>，让舰体连成整体，炮口、喷口和发射区保持净空。直射武器朝向核心船头。',
 '先 <b>SAVE / 保存</b>，再进 <b>TEST FLIGHT / 试飞</b>。正式关卡点 <b>PLAY</b>，选船与难度，确认不超预算后点 <b>START</b>。'],
 buildkeys=[('鼠标左键','放置；X 切换删除模式'),('空格','打开 / 关闭组件目录'),('R','旋转手中的组件'),('Alt + 左键','复制组件到手中'),('M','按核心左右镜像；两侧都须能安装'),('P','打开 / 关闭武器组界面'),('鼠标中键拖动','旋转视角；按 Shift 拖动可平移'),('滚轮 / F','缩放 / 聚焦整艘船')],
 groups='先确认怎么开火',grouptext='在<b>武器组</b>中把武器拖到组框，<b>松手即完成分配</b>。之后点击组框只查看成员。新建默认组：<b>1 组直射武器 / 左键</b>，<b>2 组导弹 / 右键</b>。旧蓝图可能保存了其他开火键。',
 flight='2  练习并出击',practice='<b>靶场：</b>点 SELECT TARGET 选靶机，设置 ATTACK（是否攻击）和 MOVE（是否移动），再点 ENTER 进入。先熟悉操作。',
 flykeys=[('W / S','前进 / 后退推力'),('A / D','左移 / 右移'),('空格 / Ctrl','上升 / 下降'),('Q / E','左滚转 / 右滚转'),('移动鼠标','控制船头转向'),('按住 Shift','加力，耗电明显增加'),('鼠标左 / 右键','发射绑定到该键的武器组'),('F','锁定 / 解除前方目标'),('Tab','释放已安装且就绪的干扰弹'),('数字 1 - 8','启用 / 关闭对应武器组'),('V','切换直射弹道武器齐射 / 交替'),('滚轮 / Esc','镜头距离 / 暂停与选项')],
 steering='鼠标偏移是<b>持续转向指令</b>，移回中央才会减小转向。松开移动键会自动制动；转向速度受舰船机动性限制。',
 goal='正式关卡要做什么',goaltext='消灭<b>全部六波</b>及剩余敌人。最后一波是 <b>t8 旗舰和护卫舰队</b>。自己的核心被摧毁则本次出击失败；返回修改蓝图后重新出击。',
 hud='3  看懂战斗界面',hudrows=[('红色目标','雷达发现的敌人；F 选择主锁定目标。'),('护盾','抵挡伤害；恢复时消耗电力。'),('能量','供推进、武器和护盾恢复使用。'),('武器格','亮色为就绪；暗色或数字表示暂不可用 / 冷却。'),('热量','机枪橙色热量上升；过热后必须冷却到底。')],
 shoot='瞄准、射击、恢复',tips=[
 '<b>光束</b>瞄准敌舰；<b>弹道武器</b>攻击移动目标时参考红色预瞄点。轻微辅助只补偿炮口位置偏差。',
 '<b>常规导弹：</b>按 F 锁定敌人，等导弹完成锁定，再按住导弹组开火键。装填完成后还要重新锁定。',
 '<b>垂发导弹：</b>让有雷达标记的敌人留在视野内<b>四秒</b>，再按该组开火键；弹药会分给多个目标，各组件独立装填。',
 '<b>没电了？</b>松开加力并暂停开火。供电优先护盾恢复，其次推进，最后武器。加力可临时提高限速，松开后会回落。'],
 footer='本页为默认键位；飞行动作可在设置改键，武器组开火键随蓝图保存。  |  ? / 帮助可查看当前界面的说明。')}

def make(lang):
    d=CONTENT[lang];font='ZH' if lang=='ZH' else 'EN';bold='ZH' if lang=='ZH' else 'ENB'
    out=ROOT/'output/pdf'/f'Starframe_Quick_Start_{lang}.pdf';out.parent.mkdir(parents=True,exist_ok=True)
    c=canvas.Canvas(str(out),pagesize=(W,H));c.setTitle('Starframe - '+d['title']);c.setAuthor('Starframe')
    c.setFillColor(BLUE);c.rect(0,H-78,W,78,fill=1,stroke=0)
    c.setFont('ENB',21);c.setFillColor(LIME);c.drawString(M,H-29,'STARFRAME')
    c.setFont(bold,16);c.setFillColor(HexColor('#ffffff'));c.drawString(M+158,H-28,d['title'])
    c.setFont(bold,10);c.setFillColor(HexColor('#e2ebff'));c.drawString(M,H-56,d['sub'])
    c.setFont('EN',8);c.drawRightString(W-M,H-55,'QUICK START / 07 NOV 2026')
    ps=ParagraphStyle('body',fontName=font,fontSize=9.6,leading=12.8,textColor=INK,wordWrap='CJK' if lang=='ZH' else None)
    small=ParagraphStyle('small',parent=ps,fontSize=9.2,leading=12.1)
    key=ParagraphStyle('key',parent=ps,fontName=bold,fontSize=9,leading=11.2,textColor=BLUE)
    def para(text,x,y,width=CW,style=ps,gap=6):
        p=Paragraph(text,style);_,h=p.wrap(width,1000);p.drawOn(c,x,y-h);return y-h-gap
    def section(text,x,y):
        c.setFillColor(BLUE);c.setFont(bold,11.9);c.drawString(x,y,text)
        c.setStrokeColor(LINE);c.line(x,y-8,x+CW,y-8);return y-20
    def table(rows,x,y,keywidth):
        for label,text in rows:
            kp=Paragraph(label,key);bp=Paragraph(text,small)
            _,kh=kp.wrap(keywidth-8,100);_,bh=bp.wrap(CW-keywidth-7,100)
            height=max(kh,bh)+6
            c.setFillColor(PALE);c.roundRect(x,y-height+2,keywidth-2,height-2,3,fill=1,stroke=0)
            kp.drawOn(c,x+4,y-(height+kh)/2+1);bp.drawOn(c,x+keywidth+4,y-(height+bh)/2+1)
            y-=height
        return y-9
    bottom=[]
    x=M;y=section(d['build'],x,H-103)
    for i,text in enumerate(d['steps'],1):y=para(f'<font color="#12388f"><b>{i:02d}</b></font>  '+text,x,y)
    y-=3;y=table(d['buildkeys'],x,y,91 if lang=='ZH' else 84)
    y=section(d['groups'],x,y);y=para(d['grouptext'],x,y,style=small)
    bottom.append(y)
    x=M+CW+GAP;y=section(d['flight'],x,H-103);y=para(d['practice'],x,y,style=small)
    y=table(d['flykeys'],x,y,87 if lang=='ZH' else 93)
    y=para(d['steering'],x,y,style=small)
    y=section(d['goal'],x,y-3);y=para(d['goaltext'],x,y,style=small)
    bottom.append(y)
    x=M+(CW+GAP)*2;y=section(d['hud'],x,H-103)
    for label,text in d['hudrows']:
        y=para('<b>'+label+'</b>  '+text,x,y,style=ps,gap=9)
    y=section(d['shoot'],x,y-4)
    for text in d['tips']:y=para(text,x,y,style=ps,gap=9)
    bottom.append(y)
    c.setStrokeColor(LINE);c.line(M,39,W-M,39)
    foot=Paragraph(d['footer'],ParagraphStyle('footer',parent=small,fontSize=7.7,leading=10,textColor=MUTED));_,fh=foot.wrap(W-M*2-35,30);foot.drawOn(c,M,26-fh/2)
    c.setFillColor(MUTED);c.setFont('ENB',8);c.drawRightString(W-M,20,'1 / 1')
    assert min(bottom)>47,(lang,bottom)
    c.showPage();c.save()
    for target in [ROOT/'site/guides'/out.name,ROOT/'docs/guides'/out.name]:target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(out,target)
    print(lang,{'bottoms':bottom,'file':str(out)})
    return {'language':lang,'bottoms':bottom,'path':str(out),'pageSize':'A4 landscape','pages':1}

if __name__=='__main__':
    results=[make(lang) for lang in ['EN','ZH']]
    evidence=ROOT/'Documentation/QuickStart-2026-11-07';evidence.mkdir(parents=True,exist_ok=True)
    (evidence/'content.json').write_text(json.dumps(CONTENT,ensure_ascii=False,indent=2),encoding='utf8')
    (evidence/'layout.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf8')
