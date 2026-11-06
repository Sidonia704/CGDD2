// @role Draw lock frames, edge directions and speed-keyed shared projectile lead offsets independent of camera motion.
// @calls TargetLockSystem, TargetIndicatorProjection, AimProjection, ShipWeapons

using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;

namespace Starframe.Combat
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TargetAimGraphic : UnityEngine.UI.Graphic
    {
        public static readonly Color EnemyTint = new Color32(255,50,39,255);

        Camera viewCamera;
        TargetLockSystem targeting;
        ShipWeapons weapons;
        Canvas ownerCanvas;
        struct LeadPresentation{public Vector2 offset;public int frame;}
        readonly Dictionary<float,LeadPresentation> leadPresentation=new Dictionary<float,LeadPresentation>();
        FlightShipRuntime presentationTarget;int presentationLife;
        public bool EdgeIndicatorVisible{get;private set;}

        public void Initialize(Camera camera, TargetLockSystem lockSystem,ShipWeapons missileWeapons=null)
        {
            viewCamera = camera;
            targeting = lockSystem;weapons=missileWeapons;
            ownerCanvas = GetComponentInParent<Canvas>();
            raycastTarget = false;
            color = Color.white;
        }

        void LateUpdate() => SetVerticesDirty();

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            EdgeIndicatorVisible=false;
            var theme = Starframe.Presentation.UiTheme.Current;
            bool refined = theme.aimReticle;
            if (!viewCamera || !targeting || !ownerCanvas)
                return;

            var uiCamera = ownerCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : ownerCanvas.worldCamera;
            float scale=1f/Mathf.Max(.01f,ownerCanvas.scaleFactor);
            foreach(var discovered in targeting.DetectedTargets)
                if(discovered&&discovered.IsAlive&&(!targeting.HasTarget||discovered!=targeting.LockedTarget)&&TryProject(discovered.CorePosition,uiCamera,out var marker))
                    {if(weapons!=null&&weapons.IsVlsLockedTarget(discovered))DrawVlsLockFrame(vh,marker,22f*scale,scale);else if(refined)DrawCornerFrame(vh,marker,19.875f,1.5f,15.75f,EnemyTint);else DrawDetectionMarker(vh,marker,scale);}
            if(!targeting.HasTarget||!targeting.LockedTarget||!targeting.LockedTarget.IsAlive){leadPresentation.Clear();presentationTarget=null;return;}
            if(presentationTarget!=targeting.LockedTarget||presentationLife!=targeting.LockedTarget.LifeId){leadPresentation.Clear();presentationTarget=targeting.LockedTarget;presentationLife=presentationTarget.LifeId;}
            Vector3 targetPosition = targeting.LockedTarget.CorePosition;
            if(TargetIndicatorProjection.TryEdge(viewCamera,targetPosition,rectTransform.rect,26f*scale,out var edge,out var direction)){
                if(refined) DrawRefinedDirection(vh,edge,direction,scale,EnemyTint);
                else DrawDirection(vh,edge,direction,scale);
                EdgeIndicatorVisible=true;return;
            }
            if (!TryProject(targetPosition, uiCamera, out var targetPoint)) return;

            float radiusWorld = Mathf.Max(.01f, targeting.LockedTargetRadius);
            Vector3 edgeWorld = targetPosition + viewCamera.transform.right * radiusWorld;
            Vector3 edgeScreen = viewCamera.WorldToScreenPoint(edgeWorld);
            float radiusPixels = edgeScreen.z > 0 ? Mathf.Abs(edgeScreen.x - viewCamera.WorldToScreenPoint(targetPosition).x) : 24f;
            float projectedRadius = radiusPixels / Mathf.Max(.01f, ownerCanvas.scaleFactor);
            float halfFrame = refined ? Mathf.Clamp(projectedRadius,18f*scale,76f*scale)
                : Mathf.Clamp(projectedRadius,28f*scale,90f*scale);
            Color frameTint = EnemyTint;
            float progress=weapons!=null&&weapons.HasMissileWeapons?Mathf.Clamp01(weapons.MissileLockProgress):1;
            if(refined) DrawLockState(vh,targetPoint,Mathf.Max(19.875f,halfFrame),progress,frameTint);
            else DrawFrame(vh,targetPoint,halfFrame,scale,frameTint);

            if(!refined&&weapons!=null&&weapons.HasMissileWeapons)
            {
                float legacyProgress=Mathf.Clamp01(weapons.MissileLockProgress);
                Color tint=EnemyTint;
                float radius=halfFrame+14f*scale;
                for(int i=0;i<32;i++)
                {
                    float a=(i/32f)*Mathf.PI*2+Mathf.PI*.5f,b=((i+.78f)/32f)*Mathf.PI*2+Mathf.PI*.5f;
                    Color segment=tint;segment.a=i/32f < legacyProgress ? .95f : .15f;
                    Line(vh,targetPoint+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,targetPoint+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,2.5f*scale,segment);
                }
            }
            IReadOnlyList<LeadPoint> leads = targeting.LeadPoints;
            if (leads == null) return;
            for (int i = 0; i < leads.Count; i++)
            {
                LeadPoint lead = leads[i];
                if (lead.projectileSpeed<=0 || lead.flightTime < 0 ||
                    lead.flightTime * lead.projectileSpeed > lead.range)
                    continue;
                if (lead.aimDirection.sqrMagnitude<.000001f||!TryScreenPoint(AimProjection.ScreenPoint(viewCamera,lead.aimDirection),uiCamera,out var point)) continue;
                // Smooth only the lead offset, so camera/target motion remains responsive.
                Vector2 desired=point-targetPoint;
                if(!leadPresentation.TryGetValue(lead.projectileSpeed,out var shown)||shown.frame<Time.frameCount-1||(shown.offset-desired).sqrMagnitude>10000*scale*scale)
                    shown=new LeadPresentation{offset=desired,frame=Time.frameCount};
                else if(shown.frame!=Time.frameCount){shown.offset=Vector2.Lerp(shown.offset,desired,1-Mathf.Exp(-24*Time.unscaledDeltaTime));shown.frame=Time.frameCount;}
                leadPresentation[lead.projectileSpeed]=shown;point=targetPoint+shown.offset;
                int shape=(int)((uint)lead.projectileSpeed*2654435761u>>30);
                Color tint = EnemyTint;
                if(refined)
                {
                    // Keep the lead relationship legible without drawing a long, competing tether.
                    // Leave the target and the lead marker unobscured at both ends of their relationship line.
                    Vector2 delta=point-targetPoint;float length=delta.magnitude;
                    if(length>halfFrame+22f*scale){var tetherDirection=delta/length;DrawDashed(vh,targetPoint+tetherDirection*(halfFrame+9f*scale),point-tetherDirection*13f*scale,3.5f*scale,10f*scale,1.1f*scale,new Color(tint.r,tint.g,tint.b,.29f));}
                    DrawRefinedLead(vh,point,11f*scale,tint,shape,scale);
                }
                else
                {
                    DrawDashed(vh,targetPoint,point,8f*scale,6f*scale,2.5f*scale,tint);
                    DrawLead(vh,point,11f*scale,3.5f*scale,tint,shape);
                }
            }
        }

        bool TryProject(Vector3 world, Camera uiCamera, out Vector2 local)
        {
            Vector3 screen = viewCamera.WorldToScreenPoint(world);
            return TryScreenPoint(screen,uiCamera,out local);
        }

        bool TryScreenPoint(Vector3 screen,Camera uiCamera,out Vector2 local)
        {
            var pixels=viewCamera.pixelRect;
            if (screen.z <= 0 || !pixels.Contains(new Vector2(screen.x,screen.y)))
            {
                local = default;
                return false;
            }
            var rect=rectTransform.rect;
            local=new Vector2(rect.xMin+(screen.x-pixels.xMin)/Mathf.Max(1,pixels.width)*rect.width,rect.yMin+(screen.y-pixels.yMin)/Mathf.Max(1,pixels.height)*rect.height);
            return true;
        }
        static void DrawDetectionMarker(UnityEngine.UI.VertexHelper vh,Vector2 tip,float scale)
        {
            Triangle(vh,tip,tip+new Vector2(-5.5f,10f)*scale,tip+new Vector2(5.5f,10f)*scale,EnemyTint);
        }
        static void DrawDirection(UnityEngine.UI.VertexHelper vh,Vector2 point,Vector2 direction,float scale)
        {
            Vector2 side=new Vector2(-direction.y,direction.x);
            var tip=point+direction*12f*scale;var a=point-direction*10f*scale+side*10f*scale;var b=point-direction*10f*scale-side*10f*scale;
            int index=vh.currentVertCount;var tint=EnemyTint;
            vh.AddVert(tip,tint,Vector2.zero);vh.AddVert(a,tint,Vector2.zero);vh.AddVert(b,tint,Vector2.zero);vh.AddTriangle(index,index+1,index+2);
            Line(vh,point-direction*17f*scale-side*7f*scale,point-direction*17f*scale+side*7f*scale,3f*scale,tint);
        }

        static void DrawRefinedDirection(UnityEngine.UI.VertexHelper vh,Vector2 point,Vector2 direction,float scale,Color tint)
        {
            Vector2 side=new Vector2(-direction.y,direction.x);
            Vector2 tip=point+direction*13f*scale;
            Vector2 shoulder=point-direction*7f*scale;
            Triangle(vh,tip,shoulder+side*8f*scale,shoulder-side*8f*scale,tint);
        }

        static void DrawRefinedFrame(UnityEngine.UI.VertexHelper vh,Vector2 center,float radius,float scale,Color tint)
        {
            float outer=radius+5f*scale;
            float arm=14f*scale;
            for(int sx=-1;sx<=1;sx+=2)
                for(int sy=-1;sy<=1;sy+=2)
                {
                    Vector2 corner=center+new Vector2(sx*outer,sy*outer);
                    Line(vh,corner+Vector2.left*sx*arm,corner,2.75f*scale,tint);
                    Line(vh,corner+Vector2.down*sy*arm,corner,2.75f*scale,tint);
                }
        }

        static void DrawCornerFrame(UnityEngine.UI.VertexHelper vh,Vector2 center,float radius,float width,float arm,Color tint)
        {
            for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2)
            {Vector2 corner=center+new Vector2(sx*radius,sy*radius);Line(vh,corner+Vector2.left*sx*arm,corner,width,tint);Line(vh,corner+Vector2.down*sy*arm,corner,width,tint);}
        }
        static void DrawVlsLockFrame(UnityEngine.UI.VertexHelper vh,Vector2 center,float radius,float scale)
        {
            DrawCornerFrame(vh,center,radius,1.7f*scale,radius*.65f,EnemyTint);
            for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2)
                Line(vh,center+new Vector2(sx*radius*.25f,sy*radius*1.25f),center+new Vector2(sx*radius*1.25f,sy*radius*.25f),2.1f*scale,EnemyTint);
        }
        static void DrawLockState(UnityEngine.UI.VertexHelper vh,Vector2 center,float radius,float progress,Color tint)
        {
            if(progress>=.999f)
            {
                DrawLead(vh,center,radius,2.25f,tint,1);
                DrawLead(vh,center,radius*1.273f,1.5f,tint,0);
                return;
            }
            DrawCornerFrame(vh,center,radius,2.25f,15.75f,tint);
            // Clockwise completion runs along the selected target's square border.
            float remaining=progress*4;
            Vector2 from=center+new Vector2(-radius,radius);
            for(int i=0;i<4;i++)
            {
                Vector2 to=center+(i==0?new Vector2(radius,radius):i==1?new Vector2(radius,-radius):i==2?new Vector2(-radius,-radius):new Vector2(-radius,radius));
                float amount=Mathf.Clamp01(remaining-i);if(amount>0)Line(vh,from,Vector2.Lerp(from,to,amount),2.25f,tint);from=to;
            }
        }

        static void DrawRefinedLead(UnityEngine.UI.VertexHelper vh,Vector2 center,float radius,Color tint,int shape,float scale)
        {
            float r=radius*.72f;
            float w=2.5f*scale;
            if(shape==0)
            {
                Vector2 up=center+Vector2.up*r,right=center+Vector2.right*r,down=center+Vector2.down*r,left=center+Vector2.left*r;
                Line(vh,up,right,w,tint);Line(vh,right,down,w,tint);Line(vh,down,left,w,tint);Line(vh,left,up,w,tint);
            }
            else if(shape==1)
            {
                float half=r;
                Vector2 a=center+new Vector2(-half,-half),b=center+new Vector2(-half,half),c=center+new Vector2(half,half),d=center+new Vector2(half,-half);
                float arm=half*.42f;
                Line(vh,a,a+Vector2.up*arm,w,tint);Line(vh,a,a+Vector2.right*arm,w,tint);
                Line(vh,b,b+Vector2.down*arm,w,tint);Line(vh,b,b+Vector2.right*arm,w,tint);
                Line(vh,c,c+Vector2.down*arm,w,tint);Line(vh,c,c+Vector2.left*arm,w,tint);
                Line(vh,d,d+Vector2.up*arm,w,tint);Line(vh,d,d+Vector2.left*arm,w,tint);
            }
            else DrawLead(vh,center,r,w,tint,shape);
            float core=1.6f*scale;
            AddQuad(vh,center-new Vector2(core,core),center+new Vector2(core,core),tint);
        }

        static void Triangle(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color tint)
        {
            int index=vh.currentVertCount;
            vh.AddVert(a,tint,Vector2.zero);vh.AddVert(b,tint,Vector2.zero);vh.AddVert(c,tint,Vector2.zero);
            vh.AddTriangle(index,index+1,index+2);
        }

        static void AddQuad(UnityEngine.UI.VertexHelper vh,Vector2 min,Vector2 max,Color tint)
        {
            int index=vh.currentVertCount;
            vh.AddVert(min,tint,Vector2.zero);vh.AddVert(new Vector2(min.x,max.y),tint,Vector2.zero);
            vh.AddVert(max,tint,Vector2.zero);vh.AddVert(new Vector2(max.x,min.y),tint,Vector2.zero);
            vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);
        }

        static void DrawFrame(UnityEngine.UI.VertexHelper vh, Vector2 center, float radius, float scale, Color tint)
        {
            float outer=radius+10f*scale;
            float arm=23f*scale;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector2 corner = center + new Vector2(sx * outer, sy * outer);
                    Line(vh,corner+Vector2.left*sx*arm,corner,6f*scale,tint);
                    Line(vh,corner+Vector2.down*sy*arm,corner,6f*scale,tint);
                }
        }

        static void DrawDashed(UnityEngine.UI.VertexHelper vh, Vector2 from, Vector2 to, float dash, float gap, float width, Color tint)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < .1f) return;
            Vector2 direction = delta / length;
            float step = dash + gap;
            for (float at = 0; at < length; at += step)
            {
                float end = Mathf.Min(length, at + dash);
                Line(vh, from + direction * at, from + direction * end, width, tint);
            }
        }

        static void DrawLead(UnityEngine.UI.VertexHelper vh, Vector2 center, float radius, float width, Color tint, int shape)
        {
            if(shape>=2)
            {
                int sides=shape==2?16:3;
                for(int i=0;i<sides;i++)
                {
                    float a=Mathf.PI*.5f+i*Mathf.PI*2/sides,b=Mathf.PI*.5f+(i+1)*Mathf.PI*2/sides;
                    Line(vh,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,width,tint);
                }
                return;
            }
            if (shape==0)
            {
                Vector2 up = center + Vector2.up * radius;
                Vector2 right = center + Vector2.right * radius;
                Vector2 down = center + Vector2.down * radius;
                Vector2 left = center + Vector2.left * radius;
                Line(vh, up, right, width, tint); Line(vh, right, down, width, tint);
                Line(vh, down, left, width, tint); Line(vh, left, up, width, tint);
                return;
            }
            var lowerLeft=center+new Vector2(-radius,-radius);var upperLeft=center+new Vector2(-radius,radius);
            var upperRight=center+new Vector2(radius,radius);var lowerRight=center+new Vector2(radius,-radius);
            Line(vh,lowerLeft,upperLeft,width,tint);Line(vh,upperLeft,upperRight,width,tint);
            Line(vh,upperRight,lowerRight,width,tint);Line(vh,lowerRight,lowerLeft,width,tint);
        }

        static void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            if((b-a).sqrMagnitude<.000001f)return;
            Stroke(vh,a,b,width,tint);
        }
        static void Stroke(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .000001f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * width * .5f;
            int index = vh.currentVertCount;
            vh.AddVert(a - normal, tint, Vector2.zero);
            vh.AddVert(a + normal, tint, Vector2.zero);
            vh.AddVert(b + normal, tint, Vector2.zero);
            vh.AddVert(b - normal, tint, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
