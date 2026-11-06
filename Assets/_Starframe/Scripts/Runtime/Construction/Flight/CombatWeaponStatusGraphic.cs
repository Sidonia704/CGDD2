// @role Present per-mount readiness, compact 8/16-round VLS magazines, missile reload seconds and Gatling heat.
// @calls ShipWeapons, ShipCountermeasureController, FlightShipRuntime, FigmaVisuals
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Starframe.Combat;
using Starframe.Presentation;

namespace Starframe.Flight
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CombatWeaponStatusGraphic:MaskableGraphic
    {
        readonly List<WeaponHudState> states=new List<WeaponHudState>();
        readonly List<WeaponHudState> missiles=new List<WeaponHudState>(),lasers=new List<WeaponHudState>(),ammo=new List<WeaponHudState>();
        readonly List<ShipPartState> flares=new List<ShipPartState>();
        readonly List<bool> flareBlocked=new List<bool>();
        readonly List<bool> flareReady=new List<bool>();
        readonly List<TextMeshProUGUI> timers=new List<TextMeshProUGUI>();
        readonly List<Rect> missileRects=new List<Rect>();
        readonly List<RawImage> heatWaves=new List<RawImage>();
        FlightShipRuntime ship;ShipWeapons weapons;ShipCountermeasureController countermeasures;
        int flareStructureRevision=-1,flareLife=-1,flareTuningRevision=-1,flarePartCount=-1;
        const float FirstMountTop=72;
        static readonly Color Lime=new Color32(185,255,3,255),Orange=new Color32(255,137,0,255),Heat=new Color32(255,78,0,255),Unavailable=new Color32(117,87,83,255),Cooling=new Color32(181,76,35,255);
        public void Bind(FlightShipRuntime owner,ShipWeapons source,ShipCountermeasureController flareSource)
        {if(ship!=owner)flareStructureRevision=flareLife=flareTuningRevision=flarePartCount=-1;ship=owner;weapons=source;countermeasures=flareSource;raycastTarget=false;Refresh();}
        public void Refresh()
        {
            if(!ship||weapons==null)return;
            weapons.GetHudWeaponStates(states);missiles.Clear();lasers.Clear();ammo.Clear();RefreshFlareGeometry();
            for(int i=0;i<flares.Count;i++){var part=flares[i];flareReady[i]=ship.IsAlive&&part.present&&part.hp>0&&part.hp>=part.maxHp-.001f&&!flareBlocked[i]&&countermeasures&&countermeasures.GetCooldownRemaining(part.record.id)<=0;}
            foreach(var state in states)
                if(state.Definition.delivery==WeaponDelivery.GuidedMissile)missiles.Add(state);
                else if(state.Definition.family==WeaponFamily.RapidLaser||state.Definition.family==WeaponFamily.BeamLaser||state.Definition.delivery==WeaponDelivery.Beam)lasers.Add(state);
                else ammo.Add(state);
            while(timers.Count<missiles.Count)
            {
                var go=new GameObject("MissileReloadSeconds"+timers.Count,typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));go.layer=5;go.transform.SetParent(transform,false);
                var text=go.GetComponent<TextMeshProUGUI>();FigmaVisuals.SetFont(text,true);text.fontSize=18;text.color=Color.white;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Overflow;timers.Add(text);
            }
            PackMissiles(missiles,missileRects);
            for(int i=0;i<timers.Count;i++)
            {
                var text=timers[i];bool visible=i<missiles.Count&&missiles[i].Part.present&&missiles[i].ReloadRemaining>.01f;text.gameObject.SetActive(visible);if(!visible)continue;
                var bounds=missileRects[i];text.SetText("{0:0}",Mathf.Ceil(missiles[i].ReloadRemaining));text.fontSize=Mathf.Min(18,bounds.width*.74f);
                Place(text.rectTransform,bounds.x,bounds.y,bounds.width,bounds.height);
            }
            while(heatWaves.Count<ammo.Count*3)
            {
                var go=new GameObject("GatlingHeatWave"+heatWaves.Count,typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));go.layer=5;go.transform.SetParent(transform,false);
                var image=go.GetComponent<RawImage>();image.raycastTarget=false;image.texture=Resources.Load<Texture2D>("UI/Figma/Hud"+(heatWaves.Count%3==2?"Vector42":"Vector40"));image.rectTransform.localEulerAngles=new Vector3(0,0,-11.26f);heatWaves.Add(image);
            }
            ammo.Sort((a,b)=>(a.Definition.family==WeaponFamily.Gatling?0:1).CompareTo(b.Definition.family==WeaponFamily.Gatling?0:1));
            for(int i=0;i<heatWaves.Count;i++)
            {
                var image=heatWaves[i];int mount=i/3;AmmoLayout(mount,out float mountTop,out float ammoPitch);float ammoHeight=Mathf.Max(3,ammoPitch-31);bool visible=mount<ammo.Count&&ammo[mount].Definition.family==WeaponFamily.Gatling&&ammoPitch>=50;image.gameObject.SetActive(visible);if(!visible)continue;
                float x=226+(mount%2)*28+2+(i%3)*6.72f,y=mountTop+ammoHeight+5;
                Place(image.rectTransform,x,y,8.706f,22.555f);image.color=Orange;
            }
            SetVerticesDirty();
        }
        void RefreshFlareGeometry()
        {
            int tuningRevision=ship.Tuning?ship.Tuning.Revision:0;
            if(flareStructureRevision==ship.StructureRevision&&flareLife==ship.LifeId&&flareTuningRevision==tuningRevision&&flarePartCount==ship.Parts.Count)return;
            flares.Clear();flareBlocked.Clear();flareReady.Clear();
            foreach(var part in ship.Parts)
            {
                var definition=ship.Tuning.GetDefinition(part.record.shape);if(!definition||!definition.countermeasure)continue;
                flares.Add(part);bool blocked=false;
                foreach(var obstacle in ship.Parts)
                    if(obstacle!=part&&obstacle.present&&obstacle.hp>0&&WeaponClearance.CountermeasurePathBlockedBy(part.record,obstacle.record)){blocked=true;break;}
                flareBlocked.Add(blocked);flareReady.Add(false);
            }
            flareStructureRevision=ship.StructureRevision;flareLife=ship.LifeId;flareTuningRevision=tuningRevision;flarePartCount=ship.Parts.Count;
        }
        static float Pitch(int count,float maximum)=>Mathf.Min(maximum,246f/Mathf.Max(1,Mathf.CeilToInt(count/2f)));
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(!ship||weapons==null)return;
            float flarePitch=Pitch(flares.Count,16);
            for(int i=0;i<flares.Count;i++)
            {
                // Every launcher keeps a slot; cooling and destroyed launchers are intentionally dim.
                Box(vh,(i%2)*28,FirstMountTop+(i/2)*flarePitch,24,Mathf.Max(2,flarePitch-4),Tint(Lime,flareReady[i]?1:.28f));
            }
            MissileMounts(vh);Mounts(vh,lasers,151,false);Mounts(vh,ammo,226,true);
        }
        // Two skyline columns: a large magazine takes two vertical slots without leaving holes.
        public static void PackMissiles(IReadOnlyList<WeaponHudState> list,List<Rect> output)
        {
            output.Clear();float left=0,right=0;
            for(int i=0;i<list.Count;i++)
            {
                bool column=right<left;float top=column?right:left;
                float height=list[i].Definition.IsVls&&list[i].AmmoCapacity>8?54:27;
                output.Add(new Rect(78+(column?28:0),FirstMountTop+top,24,height-3));
                if(column)right+=height;else left+=height;
            }
            float scale=Mathf.Min(1,246/Mathf.Max(1,Mathf.Max(left,right)));
            for(int i=0;i<output.Count;i++){var r=output[i];r.y=FirstMountTop+(r.y-FirstMountTop)*scale;r.height*=scale;output[i]=r;}
        }
        void MissileMounts(VertexHelper vh)
        {
            for(int i=0;i<missiles.Count;i++)
            {
                var state=missiles[i];var part=state.Part;var r=missileRects[i];
                bool active=ship.IsAlive&&part.present&&part.hp>0&&state.Enabled&&!state.Blocked&&part.hp>=part.maxHp-.001f&&(state.Definition.sustainedPowerWatts<=0||ship.Power.WeaponRatio>0);
                if(!state.Definition.IsVls)
                {Box(vh,r.x,r.y,r.width,r.height,Tint(Lime,active&&state.ReloadRemaining<=.01f?1:.28f));continue;}
                int rows=state.AmmoCapacity>8?8:4;float gap=Mathf.Min(1,r.height/(rows*6f)),h=(r.height-gap*(rows-1))/rows,w=(r.width-1)/2;
                for(int cell=0;cell<rows*2;cell++)
                    Box(vh,r.x+(cell%2)*(w+1),r.y+(cell/2)*(h+gap),w,h,Tint(Lime,active&&cell<state.AmmoRemaining?1:.4f));
            }
        }
        void Mounts(VertexHelper vh,List<WeaponHudState> list,float x,bool ammunition)
        {
            float pitch=Pitch(list.Count,27);
            for(int i=0;i<list.Count;i++)
            {
                var state=list[i];var part=state.Part;bool present=part.present&&part.hp>0;
                bool active=ship.IsAlive&&present&&state.Enabled&&!state.Blocked&&part.hp>=part.maxHp-.001f&&(state.Definition.sustainedPowerWatts<=0||ship.Power.WeaponRatio>0);
                float top=FirstMountTop+(i/2)*pitch;if(ammunition)AmmoLayout(i,out top,out pitch);bool gatling=ammunition&&state.Definition.family==WeaponFamily.Gatling;float height=gatling?Mathf.Max(3,pitch-31):Mathf.Min(24,Mathf.Max(3,pitch-3)),left=x+(i%2)*28;
                if(ammunition&&state.Definition.family==WeaponFamily.Gatling)
                {
                    float baseHeight=Mathf.Min(7,height*.16f),heatHeight=height-baseHeight;
                    Box(vh,left,top,24,height,Unavailable);
                    Box(vh,left,top+heatHeight,24,baseHeight,active&&!state.Overheated?Orange:Unavailable);
                    float heat=Mathf.Clamp01(state.Heat);
                    Box(vh,left,top+heatHeight*(1-heat),24,heatHeight*heat,state.Overheated?Cooling:Heat);
                    // Heat belongs to this mount and remains visible when its firing group is disabled.
                }
                else
                {
                    bool loaded=state.ReloadRemaining<=.01f;
                    Box(vh,left,top,24,height,ammunition?(active&&loaded?Orange:Unavailable):Tint(Lime,active&&loaded?1:.28f));
                    if(present&&state.Definition.delivery==WeaponDelivery.GuidedMissile&&!loaded)
                        Box(vh,left,top+height-2,24*state.ReloadProgress,2,Lime);
                }
            }
        }
        void AmmoLayout(int mount,out float top,out float pitch)
        {
            float total=0,before=0,rowHeight=27;int row=mount/2;
            for(int r=0;r<(ammo.Count+1)/2;r++)
            {
                bool thermal=ammo[r*2].Definition.family==WeaponFamily.Gatling||(r*2+1<ammo.Count&&ammo[r*2+1].Definition.family==WeaponFamily.Gatling);
                float h=thermal?87:27;if(r<row)before+=h;if(r==row)rowHeight=h;total+=h;
            }
            float scale=Mathf.Min(1,246/Mathf.Max(1,total));top=FirstMountTop+before*scale;pitch=rowHeight*scale;
        }
        static Color Tint(Color c,float alpha){c.a=alpha;return c;}
        Vector2 Map(Vector2 p){var r=rectTransform.rect;return new Vector2(r.xMin+p.x/376*r.width,r.yMax-p.y/324*r.height);}
        void Place(RectTransform r,float x,float y,float w,float h){r.anchorMin=new Vector2(x/376,1-(y+h)/324);r.anchorMax=new Vector2((x+w)/376,1-y/324);r.offsetMin=r.offsetMax=Vector2.zero;}
        void Box(VertexHelper vh,float x,float y,float w,float h,Color tint)
        {if(w<=0||h<=0)return;Vector2 a=Map(new Vector2(x,y+h)),b=Map(new Vector2(x+w,y));int i=vh.currentVertCount;vh.AddVert(a,tint,Vector2.zero);vh.AddVert(new Vector2(a.x,b.y),tint,Vector2.zero);vh.AddVert(b,tint,Vector2.zero);vh.AddVert(new Vector2(b.x,a.y),tint,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
        void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
        {a=Map(a);b=Map(b);var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.375f;int i=vh.currentVertCount;vh.AddVert(a-n,tint,Vector2.zero);vh.AddVert(a+n,tint,Vector2.zero);vh.AddVert(b+n,tint,Vector2.zero);vh.AddVert(b-n,tint,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
    }
}
