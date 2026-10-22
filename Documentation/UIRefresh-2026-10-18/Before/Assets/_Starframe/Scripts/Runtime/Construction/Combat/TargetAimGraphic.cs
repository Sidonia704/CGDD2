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
            if (!viewCamera || !targeting || !targeting.HasTarget || !targeting.LockedTarget || !targeting.LockedTarget.IsAlive || !ownerCanvas)
                return;

            var uiCamera = ownerCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : ownerCanvas.worldCamera;
            Vector3 targetPosition = targeting.LockedTarget.CorePosition;
            float scale=1f/Mathf.Max(.01f,ownerCanvas.scaleFactor);
            if(TargetIndicatorProjection.TryEdge(viewCamera,targetPosition,rectTransform.rect,26f*scale,out var edge,out var direction)){
                DrawDirection(vh,edge,direction,scale);EdgeIndicatorVisible=true;return;
            }
            if (!TryProject(targetPosition, uiCamera, out var targetPoint)) return;

            float radiusWorld = Mathf.Max(1f, targeting.LockedTarget.HullRadius);
            Vector3 edgeWorld = targetPosition + viewCamera.transform.right * radiusWorld;
            Vector3 edgeScreen = viewCamera.WorldToScreenPoint(edgeWorld);
            float radiusPixels = edgeScreen.z > 0 ? Mathf.Abs(edgeScreen.x - viewCamera.WorldToScreenPoint(targetPosition).x) : 24f;
            float halfFrame = Mathf.Clamp(radiusPixels / Mathf.Max(.01f, ownerCanvas.scaleFactor),28f*scale,90f*scale);
            DrawFrame(vh,targetPoint,halfFrame,scale,new Color(.38f,.85f,1,1));

            IReadOnlyList<LeadPoint> leads = targeting.LeadPoints;
            if (leads == null) return;
            for (int i = 0; i < leads.Count; i++)
            {
                LeadPoint lead = leads[i];
                if (!lead.weapon || lead.flightTime < 0 ||
                    lead.flightTime * Mathf.Max(.01f, lead.weapon.projectileSpeed) > lead.weapon.range)
                    continue;
                if (lead.aimDirection.sqrMagnitude<.000001f||!TryScreenPoint(AimProjection.ScreenPoint(viewCamera,lead.aimDirection),uiCamera,out var point)) continue;
                Color tint = Palette[i % Palette.Length];
                tint.a=1;
                DrawDashed(vh,targetPoint,point,8f*scale,6f*scale,2.5f*scale,tint);
                DrawLead(vh,point,11f*scale,3.5f*scale,tint,(i&1)==0);
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
