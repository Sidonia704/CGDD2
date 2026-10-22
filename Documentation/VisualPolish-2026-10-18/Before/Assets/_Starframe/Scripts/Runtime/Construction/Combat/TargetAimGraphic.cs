using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Starframe.Flight;

namespace Starframe.Combat
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TargetAimGraphic : Graphic
    {
        static readonly Color[] Palette =
        {
            new Color(.30f, .88f, 1f, .94f),
            new Color(1f, .76f, .34f, .94f),
            new Color(.76f, .63f, 1f, .94f),
            new Color(.47f, 1f, .72f, .94f)
        };

        Camera viewCamera;
        TargetLockSystem targeting;
        Canvas ownerCanvas;
        public bool EdgeIndicatorVisible{get;private set;}

        public void Initialize(Camera camera, TargetLockSystem lockSystem)
        {
            viewCamera = camera;
            targeting = lockSystem;
            ownerCanvas = GetComponentInParent<Canvas>();
            raycastTarget = false;
            color = Color.white;
        }

        void LateUpdate() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            EdgeIndicatorVisible=false;
            bool refined = Starframe.Presentation.UiTheme.Current.aimReticle;
            if (!viewCamera || !targeting || !targeting.HasTarget || !targeting.LockedTarget || !targeting.LockedTarget.IsAlive || !ownerCanvas)
                return;

            var uiCamera = ownerCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : ownerCanvas.worldCamera;
            Vector3 targetPosition = targeting.LockedTarget.CorePosition;
            float scale=1f/Mathf.Max(.01f,ownerCanvas.scaleFactor);
            if(TargetIndicatorProjection.TryEdge(viewCamera,targetPosition,rectTransform.rect,26f*scale,out var edge,out var direction)){
                if(refined) DrawRefinedDirection(vh,edge,direction,scale,new Color(.16f,.72f,1f,1f));
                else DrawDirection(vh,edge,direction,scale);
                EdgeIndicatorVisible=true;return;
            }
            if (!TryProject(targetPosition, uiCamera, out var targetPoint)) return;

            float radiusWorld = Mathf.Max(1f, targeting.LockedTarget.HullRadius);
            Vector3 edgeWorld = targetPosition + viewCamera.transform.right * radiusWorld;
            Vector3 edgeScreen = viewCamera.WorldToScreenPoint(edgeWorld);
            float radiusPixels = edgeScreen.z > 0 ? Mathf.Abs(edgeScreen.x - viewCamera.WorldToScreenPoint(targetPosition).x) : 24f;
            float halfFrame = Mathf.Clamp(radiusPixels / Mathf.Max(.01f, ownerCanvas.scaleFactor),28f*scale,90f*scale);
            Color frameTint = refined ? Starframe.Presentation.UiTheme.Current.accent : new Color(.38f,.85f,1,1);
            if(refined) DrawRefinedFrame(vh,targetPoint,halfFrame,scale,frameTint);
            else DrawFrame(vh,targetPoint,halfFrame,scale,frameTint);

            IReadOnlyList<LeadPoint> leads = targeting.LeadPoints;
            if (leads == null) return;
            for (int i = 0; i < leads.Count; i++)
            {
                LeadPoint lead = leads[i];
                if (!lead.weapon || lead.flightTime < 0 ||
                    lead.flightTime * Mathf.Max(.01f, lead.weapon.projectileSpeed) > lead.weapon.range)
                    continue;
                if (lead.aimDirection.sqrMagnitude<.000001f||!TryScreenPoint(AimProjection.ScreenPoint(viewCamera,lead.aimDirection),uiCamera,out var point)) continue;
                Color tint = refined ? (i % 2 == 0 ? Starframe.Presentation.UiTheme.Current.accent : Starframe.Presentation.UiTheme.Current.amber) : Palette[i % Palette.Length];
                tint.a=1;
                if(refined)
                {
                    // Keep the lead relationship legible without drawing a long, competing tether.
                    DrawDashed(vh,targetPoint,point,3.5f*scale,10f*scale,1.2f*scale,new Color(tint.r,tint.g,tint.b,.45f));
                    DrawRefinedLead(vh,point,11f*scale,tint,(i&1)==0,scale);
                }
                else
                {
                    DrawDashed(vh,targetPoint,point,8f*scale,6f*scale,2.5f*scale,tint);
                    DrawLead(vh,point,11f*scale,3.5f*scale,tint,(i&1)==0);
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
            if (screen.z <= 0 || screen.x < 0 || screen.x > viewCamera.pixelWidth || screen.y < 0 || screen.y > viewCamera.pixelHeight)
            {
                local = default;
                return false;
            }
            var rect=rectTransform.rect;
            local=new Vector2(rect.xMin+screen.x/Mathf.Max(1,viewCamera.pixelWidth)*rect.width,rect.yMin+screen.y/Mathf.Max(1,viewCamera.pixelHeight)*rect.height);
            return true;
        }
        static void DrawDirection(VertexHelper vh,Vector2 point,Vector2 direction,float scale)
        {
            Vector2 side=new Vector2(-direction.y,direction.x);
            var tip=point+direction*12f*scale;var a=point-direction*10f*scale+side*10f*scale;var b=point-direction*10f*scale-side*10f*scale;
            int index=vh.currentVertCount;var tint=new Color(.15f,.72f,1,1);
            vh.AddVert(tip,tint,Vector2.zero);vh.AddVert(a,tint,Vector2.zero);vh.AddVert(b,tint,Vector2.zero);vh.AddTriangle(index,index+1,index+2);
            Line(vh,point-direction*17f*scale-side*7f*scale,point-direction*17f*scale+side*7f*scale,3f*scale,tint);
        }

        static void DrawRefinedDirection(VertexHelper vh,Vector2 point,Vector2 direction,float scale,Color tint)
        {
            Vector2 side=new Vector2(-direction.y,direction.x);
            Vector2 tip=point+direction*13f*scale;
            Vector2 shoulder=point-direction*7f*scale;
            Triangle(vh,tip,shoulder+side*8f*scale,shoulder-side*8f*scale,tint);
            Color secondary=new Color(tint.r,tint.g,tint.b,.68f);
            Line(vh,point-direction*15f*scale+side*6f*scale,point-direction*15f*scale+side*2f*scale,1.4f*scale,secondary);
            Line(vh,point-direction*15f*scale-side*2f*scale,point-direction*15f*scale-side*6f*scale,1.4f*scale,secondary);
        }

        static void DrawRefinedFrame(VertexHelper vh,Vector2 center,float radius,float scale,Color tint)
        {
            float outer=radius+9f*scale;
            float arm=19f*scale;
            Color inner=new Color(tint.r,tint.g,tint.b,.58f);
            for(int sx=-1;sx<=1;sx+=2)
                for(int sy=-1;sy<=1;sy+=2)
                {
                    Vector2 corner=center+new Vector2(sx*outer,sy*outer);
                    Line(vh,corner+Vector2.left*sx*arm,corner,4f*scale,tint);
                    Line(vh,corner+Vector2.down*sy*arm,corner,4f*scale,tint);
                    Vector2 innerCorner=center+new Vector2(sx*(outer-3f*scale),sy*(outer-3f*scale));
                    Line(vh,innerCorner+Vector2.left*sx*(arm*.6f),innerCorner,1f*scale,inner);
                }
            // Small registration marks keep the core position clear inside the open brackets.
            Line(vh,center+Vector2.left*3f*scale,center+Vector2.right*3f*scale,1f*scale,new Color(tint.r,tint.g,tint.b,.8f));
            Line(vh,center+Vector2.down*3f*scale,center+Vector2.up*3f*scale,1f*scale,new Color(tint.r,tint.g,tint.b,.8f));
        }

        static void DrawRefinedLead(VertexHelper vh,Vector2 center,float radius,Color tint,bool diamond,float scale)
        {
            float r=radius*.72f;
            float w=2.5f*scale;
            if(diamond)
            {
                Vector2 up=center+Vector2.up*r,right=center+Vector2.right*r,down=center+Vector2.down*r,left=center+Vector2.left*r;
                Line(vh,up,right,w,tint);Line(vh,right,down,w,tint);Line(vh,down,left,w,tint);Line(vh,left,up,w,tint);
            }
            else
            {
                float half=r;
                Vector2 a=center+new Vector2(-half,-half),b=center+new Vector2(-half,half),c=center+new Vector2(half,half),d=center+new Vector2(half,-half);
                float arm=half*.42f;
                Line(vh,a,a+Vector2.up*arm,w,tint);Line(vh,a,a+Vector2.right*arm,w,tint);
                Line(vh,b,b+Vector2.down*arm,w,tint);Line(vh,b,b+Vector2.right*arm,w,tint);
                Line(vh,c,c+Vector2.down*arm,w,tint);Line(vh,c,c+Vector2.left*arm,w,tint);
                Line(vh,d,d+Vector2.up*arm,w,tint);Line(vh,d,d+Vector2.left*arm,w,tint);
            }
            float core=1.6f*scale;
            AddQuad(vh,center-new Vector2(core,core),center+new Vector2(core,core),tint);
        }

        static void Triangle(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color tint)
        {
            int index=vh.currentVertCount;
            vh.AddVert(a,tint,Vector2.zero);vh.AddVert(b,tint,Vector2.zero);vh.AddVert(c,tint,Vector2.zero);
            vh.AddTriangle(index,index+1,index+2);
        }

        static void AddQuad(VertexHelper vh,Vector2 min,Vector2 max,Color tint)
        {
            int index=vh.currentVertCount;
            vh.AddVert(min,tint,Vector2.zero);vh.AddVert(new Vector2(min.x,max.y),tint,Vector2.zero);
            vh.AddVert(max,tint,Vector2.zero);vh.AddVert(new Vector2(max.x,min.y),tint,Vector2.zero);
            vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);
        }

        static void DrawFrame(VertexHelper vh, Vector2 center, float radius, float scale, Color tint)
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

        static void DrawDashed(VertexHelper vh, Vector2 from, Vector2 to, float dash, float gap, float width, Color tint)
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

        static void DrawLead(VertexHelper vh, Vector2 center, float radius, float width, Color tint, bool diamond)
        {
            if (diamond)
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

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            if((b-a).sqrMagnitude<.000001f)return;
            Stroke(vh,a,b,width,tint);
        }
        static void Stroke(VertexHelper vh,Vector2 a,Vector2 b,float width,Color tint)
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
