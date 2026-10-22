// @role Draw range contacts, relative height and the selected target.
// @calls RangeCombatWorld

using UnityEngine;
using UnityEngine.UI;
using Starframe.Combat;

namespace Starframe.Flight
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SphereRadarGraphic : Graphic
    {
        const float RefreshInterval = .08f;
        const int CircleSegments = 48;
        const float EdgeInset = 8f;

        static readonly Color GridColor = new Color(.85f, .9f, .94f, .62f);
        static readonly Color ContactColor = new Color(1f, .27f, .24f, .98f);
        static readonly Color LockColor = new Color(1f, .78f, .24f, 1f);

        RangeCombatWorld world;
        float range = 3000f;
        float refreshTimer;
        int visibleContactCount;

        public int VisibleContactCount => visibleContactCount;

        public void Initialize(RangeCombatWorld combatWorld, float displayRange = 3000f)
        {
            world = combatWorld;
            range = Mathf.Max(1f, displayRange);
            raycastTarget = false;
            color = Color.white;
            refreshTimer = 0f;
            SetVerticesDirty();
        }

        void Update()
        {
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer < RefreshInterval) return;
            refreshTimer %= RefreshInterval;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            visibleContactCount = 0;
            if (!world || world.Player == null || world.Player.body == null) return;

            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Max(0f, rect.width * .5f - EdgeInset);
            if (radius <= 0f) return;

            var theme = Starframe.Presentation.UiTheme.Current;
            bool refined = theme.tacticalRadar;
            Vector2 center = rect.center;
            if(refined) DrawRefinedDisk(vh,center,radius,theme.muted,theme.accent);
            else DrawDisk(vh, center, radius);

            var player = world.Player;
            Vector3 origin = player.ship.CorePhysicsPosition;
            Quaternion inverseRotation = Quaternion.Inverse(player.body.rotation);
            var locked = world.Targeting && world.Targeting.HasTarget ? world.Targeting.LockedTarget : null;

            foreach (RangeCombatWorld.Actor actor in world.Actors)
            {
                if (actor == null || actor.ship == null || actor.ship == player.ship ||
                    !actor.ship.IsAlive || actor.ship.Team == player.ship.Team || actor.body == null)
                    continue;

                visibleContactCount++;
                Vector3 relative = inverseRotation * (actor.ship.CorePhysicsPosition - origin);
                float distance = relative.magnitude;
                if (distance < .001f) continue;

                float horizontal=Mathf.Sqrt(relative.x*relative.x+relative.z*relative.z);
                float radial=ScaleDistance(horizontal)*radius;
                Vector2 baseOffset=horizontal>.001f?new Vector2(relative.x,relative.z*.4f)*(radial/horizontal):Vector2.zero;
                float altitude=Mathf.Sign(relative.y)*ScaleDistance(Mathf.Abs(relative.y))*radius*.5f;
                Vector2 projected=baseOffset+Vector2.up*altitude;
                Vector2 point = center + projected;
                bool isLocked = locked == actor.ship;
                Color tint = refined
                    ? (isLocked ? theme.amber : theme.accent)
                    : (isLocked ? LockColor : ContactColor);
                if(refined)tint.a=isLocked?1f:.86f;
                float markerSize = isLocked ? 6f : 4.5f;
                if(refined)
                {
                    Color radialTint=new Color(theme.muted.r,theme.muted.g,theme.muted.b,isLocked ? .30f : .16f);
                    Color altitudeTint=new Color(tint.r,tint.g,tint.b,isLocked ? .76f : .42f);
                    Line(vh,center,center+baseOffset,isLocked?1.2f:.8f,radialTint);
                    Line(vh,center+baseOffset,point,isLocked?1.8f:1.2f,altitudeTint);
                    AddQuad(vh,center+baseOffset-Vector2.one*(isLocked?1.8f:1.2f),center+baseOffset+Vector2.one*(isLocked?1.8f:1.2f),altitudeTint);
                }
                else
                {
                    Line(vh,center,center+baseOffset,2f,tint);
                    Line(vh,center+baseOffset,point,2.5f,tint);
                    AddQuad(vh,center+baseOffset-Vector2.one*2,center+baseOffset+Vector2.one*2,tint);
                }
                float sign=relative.y>=0?1:-1;
                Triangle(vh,point-Vector2.up*markerSize*sign,point+new Vector2(-markerSize,markerSize*sign),point+new Vector2(markerSize,markerSize*sign),tint);
                if(refined&&isLocked) DrawContactBrackets(vh,point,markerSize+4f,tint);
            }
            if(refined) DrawRefinedPlayer(vh,center,theme.accent);
            else DrawPlayer(vh,center,radius);
        }

        float ScaleDistance(float distance)=>Mathf.Clamp01(Mathf.Log(1f+distance/15f)/Mathf.Log(1f+range/15f));

        static void DrawDisk(VertexHelper vh, Vector2 center, float radius)
        {
            Ellipse(vh,center,radius,radius*.4f,2.5f,new Color(.92f,.96f,1,.94f));
            Ellipse(vh,center,radius*.7f,radius*.28f,2f,GridColor);
            Ellipse(vh,center,radius*.36f,radius*.144f,2f,GridColor);
        }

        static void DrawPlayer(VertexHelper vh, Vector2 center, float radius)
        {
            Color playerColor = new Color(.18f, .58f, 1f, 1f);
            Vector2 tip = center + Vector2.up * 8f;
            Vector2 left = center + new Vector2(-5f, -5f);
            Vector2 right = center + new Vector2(5f, -5f);
            Triangle(vh, tip, left, right, playerColor);
        }

        static void DrawRefinedDisk(VertexHelper vh,Vector2 center,float radius,Color muted,Color accent)
        {
            Color outer=new Color(muted.r,muted.g,muted.b,.42f);
            Color middle=new Color(muted.r,muted.g,muted.b,.22f);
            Color inner=new Color(muted.r,muted.g,muted.b,.14f);
            // Preserve the flat three-ring ellipse; the upper and lower ticks clarify forward/back.
            Ellipse(vh,center,radius,radius*.4f,1.3f,outer);
            Ellipse(vh,center,radius*.7f,radius*.28f,1f,middle);
            Ellipse(vh,center,radius*.36f,radius*.144f,.8f,inner);
            for(int i=0;i<16;i++)
            {
                float angle=i*Mathf.PI*2f/16f;
                Vector2 normal=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                Vector2 tangent=new Vector2(-normal.y,normal.x);
                Vector2 edge=center+new Vector2(normal.x*radius,normal.y*radius*.4f);
                float length=(i==4||i==12)?8f:3.5f;
                Color tint=(i==4||i==12)?outer:middle;
                Line(vh,edge-tangent*length*.5f,edge+tangent*length*.5f,i==4||i==12?1.4f:1f,tint);
            }
            // A short forward cue at the front of the disk, mirrored by a quieter aft cue.
            Color forward=new Color(accent.r,accent.g,accent.b,.7f);
            Line(vh,center+new Vector2(-4f,radius*.4f+4f),center+new Vector2(0,radius*.4f-1f),1.4f,forward);
            Line(vh,center+new Vector2(0,radius*.4f-1f),center+new Vector2(4f,radius*.4f+4f),1.4f,forward);
            Color aft=new Color(muted.r,muted.g,muted.b,.24f);
            Line(vh,center+new Vector2(-3f,-radius*.4f-3f),center+new Vector2(0,-radius*.4f),1f,aft);
            Line(vh,center+new Vector2(0,-radius*.4f),center+new Vector2(3f,-radius*.4f-3f),1f,aft);
        }

        static void DrawRefinedPlayer(VertexHelper vh,Vector2 center,Color tint)
        {
            Vector2 tip=center+Vector2.up*8f,left=center+new Vector2(-5f,-5f),right=center+new Vector2(5f,-5f);
            Triangle(vh,tip,left,right,tint);
        }

        static void DrawContactBrackets(VertexHelper vh,Vector2 center,float size,Color tint)
        {
            float arm=3f;
            Color soft=new Color(tint.r,tint.g,tint.b,.75f);
            Vector2 a=center+new Vector2(-size,-size),b=center+new Vector2(-size,size);
            Vector2 c=center+new Vector2(size,size),d=center+new Vector2(size,-size);
            Line(vh,a,a+Vector2.right*arm,1f,soft);Line(vh,a,a+Vector2.up*arm,1f,soft);
            Line(vh,b,b+Vector2.right*arm,1f,soft);Line(vh,b,b+Vector2.down*arm,1f,soft);
            Line(vh,c,c+Vector2.left*arm,1f,soft);Line(vh,c,c+Vector2.down*arm,1f,soft);
            Line(vh,d,d+Vector2.left*arm,1f,soft);Line(vh,d,d+Vector2.up*arm,1f,soft);
        }

        static void Circle(VertexHelper vh, Vector2 center, float radius, float width, Color tint)
        {
            Vector2 previous = center + Vector2.right * radius;
            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Line(vh, previous, next, width, tint);
                previous = next;
            }
        }

        static void Ellipse(VertexHelper vh, Vector2 center, float radiusX, float radiusY, float width, Color tint)
        {
            Vector2 previous = center + Vector2.right * radiusX;
            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;
                Vector2 next = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                Line(vh, previous, next, width, tint);
                previous = next;
            }
        }

        static void DrawSquare(VertexHelper vh, Vector2 center, float halfSize, Color tint, bool diamond)
        {
            if (diamond)
            {
                Vector2 up = center + Vector2.up * halfSize;
                Vector2 right = center + Vector2.right * halfSize;
                Vector2 down = center + Vector2.down * halfSize;
                Vector2 left = center + Vector2.left * halfSize;
                Line(vh, up, right, 2f, tint); Line(vh, right, down, 2f, tint);
                Line(vh, down, left, 2f, tint); Line(vh, left, up, 2f, tint);
                return;
            }
            AddQuad(vh, center + new Vector2(-halfSize, -halfSize), center + new Vector2(halfSize, halfSize), tint);
        }

        static void DrawSquareOutline(VertexHelper vh, Vector2 center, float halfSize, float width, Color tint)
        {
            Vector2 a = center + new Vector2(-halfSize, -halfSize);
            Vector2 b = center + new Vector2(-halfSize, halfSize);
            Vector2 c = center + new Vector2(halfSize, halfSize);
            Vector2 d = center + new Vector2(halfSize, -halfSize);
            Line(vh, a, b, width, tint); Line(vh, b, c, width, tint);
            Line(vh, c, d, width, tint); Line(vh, d, a, width, tint);
        }

        static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            int index = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
        }

        static void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Color tint)
        {
            int index = vh.currentVertCount;
            vh.AddVert(min, tint, Vector2.zero);
            vh.AddVert(new Vector2(min.x, max.y), tint, Vector2.zero);
            vh.AddVert(max, tint, Vector2.zero);
            vh.AddVert(new Vector2(max.x, min.y), tint, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < .000001f) return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
            int index = vh.currentVertCount;
            vh.AddVert(a - normal, tint, Vector2.zero); vh.AddVert(a + normal, tint, Vector2.zero);
            vh.AddVert(b + normal, tint, Vector2.zero); vh.AddVert(b - normal, tint, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
