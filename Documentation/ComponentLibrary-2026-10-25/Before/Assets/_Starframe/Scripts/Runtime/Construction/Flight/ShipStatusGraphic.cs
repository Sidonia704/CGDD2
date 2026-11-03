// @role Draw the blueprint and currently surviving ship parts as an isometric diagram.
// @calls FlightShipRuntime

using System.Collections.Generic;
using Starframe.Construction;
using UnityEngine;
using UnityEngine.UI;

namespace Starframe.Flight
{
    /// <summary>Draws a compact, isometric overview of the fixed blueprint and its current surviving parts.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ShipStatusGraphic : UnityEngine.UI.Graphic
    {
        const int MaxVertices = 65000;
        const float RefreshInterval = .12f;
        static readonly Color[] FacePalette =
        {
            new Color(.08f, .42f, .78f, .96f), new Color(.035f, .22f, .48f, .96f),
            new Color(.12f, .58f, .88f, .96f), new Color(.025f, .14f, .34f, .96f),
            new Color(.07f, .34f, .66f, .96f), new Color(.045f, .27f, .56f, .96f)
        };
        static readonly float[] RefinedFaceShade =
        {
            .72f, .46f, .94f, .32f, .62f, .54f
        };
        static readonly Color EdgeColor = new Color(.12f, .8f, 1f, .92f);
        static readonly Color DamageColor = new Color(1f, .055f, .035f, 1f);
        static readonly Color CoreColor = new Color(.2f, .95f, 1f, 1f);
        static readonly int[][] FacesCube =
        {
            new[]{0,1,2,3}, new[]{4,7,6,5}, new[]{0,4,5,1},
            new[]{3,2,6,7}, new[]{0,3,7,4}, new[]{1,5,6,2}
        };
        static readonly int[][] FacesWedge =
        {
            new[]{0,1,2,3}, new[]{3,2,5,4}, new[]{0,3,4}, new[]{1,5,2}, new[]{0,4,5,1}
        };
        static readonly int[][] FacesPyramid =
        {
            new[]{0,1,2,3}, new[]{0,4,1}, new[]{1,4,2}, new[]{2,4,3}, new[]{3,4,0}
        };

        struct Face
        {
            public Vector2 a, b, c;
            public float depth;
            public Color color;
        }

        sealed class FaceDepthComparer : IComparer<Face>
        {
            public int Compare(Face a, Face b) => a.depth.CompareTo(b.depth);
        }

        static readonly FaceDepthComparer DepthComparer = new FaceDepthComparer();
        readonly List<Face> faces = new List<Face>(4096);
        readonly List<ShipPartState> blueprintParts = new List<ShipPartState>(128);
        readonly List<Vector3> points = new List<Vector3>(8);
        FlightShipRuntime ship;
        Transform relativeObserver;
        int lastStructureRevision = -1, lastHealthRevision = -1;
        float nextRefresh;
        Vector2 projectionCenter, panelCenter;
        float projectionScale = 1f;
        bool hasCore;
        bool lastRefinedStyle;
        Vector2 corePosition;
        Quaternion shipLocalRotation = Quaternion.identity;
        public int VisiblePartCount { get; private set; }
        public int DamagedPartCount { get; private set; }
        public bool CorePresent { get; private set; }

        public FlightShipRuntime Ship => ship;
        public Quaternion DisplayRotation=>shipLocalRotation;
        public void SetRelativeObserver(Transform observer){relativeObserver=observer;nextRefresh=0;}

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
            nextRefresh = 0f;
        }

        protected override void OnDisable()
        {
            ship = null;
            blueprintParts.Clear();
            faces.Clear();
            VisiblePartCount = 0;
            DamagedPartCount = 0;
            CorePresent = false;
            lastStructureRevision = -1;
            lastHealthRevision = -1;
            SetVerticesDirty();
            base.OnDisable();
        }

        public void Bind(FlightShipRuntime value)
        {
            if (ship == value && lastStructureRevision == (value ? value.StructureRevision : -1))
                return;
            ship = value;
            if (!ship)
            {
                blueprintParts.Clear();
                faces.Clear();
                VisiblePartCount = 0;
                DamagedPartCount = 0;
                CorePresent = false;
                hasCore = false;
                lastStructureRevision = -1;
                SetVerticesDirty();
                return;
            }
            CacheBlueprintBounds();
            lastRefinedStyle=Starframe.Presentation.UiTheme.Current.statusSchematic;
            lastStructureRevision = -1;
            nextRefresh = 0f;
            Rebuild();
        }

        void Update()
        {
            if (!ship)
                return;
            bool refined=Starframe.Presentation.UiTheme.Current.statusSchematic;
            if(refined!=lastRefinedStyle)
            {
                lastRefinedStyle=refined;
                CacheBlueprintBounds();
                Rebuild();
                return;
            }
            // Own-ship projection is fixed: only damage, recovery or structure changes its geometry.
            if (!relativeObserver && lastStructureRevision == ship.StructureRevision && lastHealthRevision == ship.HealthRevision)
                return;
            if (Time.unscaledTime < nextRefresh && lastStructureRevision == ship.StructureRevision)
                return;
            nextRefresh = Time.unscaledTime + (relativeObserver?(ship.Parts.Count>256?1f/15f:1f/30f):RefreshInterval);
            if (relativeObserver||lastStructureRevision != ship.StructureRevision)
                CacheBlueprintBounds();
            Rebuild();
        }

        void CacheBlueprintBounds()
        {
            blueprintParts.Clear();
            hasCore = false;
            if (!ship)
                return;
            shipLocalRotation = Quaternion.Inverse(ship.CoreRotation);
            if(relativeObserver)
            {
                // View the target along the player's actual bearing, retaining the player's up/roll reference.
                Vector3 bearing=ship.CorePosition-relativeObserver.position;
                Quaternion frame=relativeObserver.rotation;
                if(bearing.sqrMagnitude>.0001f)
                {
                    Vector3 up=relativeObserver.up;
                    if(Vector3.Cross(bearing.normalized,up).sqrMagnitude<.0001f)up=relativeObserver.forward;
                    frame=Quaternion.LookRotation(bearing,up);
                }
                shipLocalRotation=Quaternion.Inverse(frame)*ship.transform.rotation*shipLocalRotation;
            }

            Bounds projected = new Bounds(Vector3.zero, Vector3.zero);
            Bounds sourceBounds=new Bounds();
            bool hasBounds = false;
            for (int i = 0; i < ship.Parts.Count; i++)
            {
                ShipPartState part = ship.Parts[i];
                if (part == null || part.record == null)
                    continue;
                blueprintParts.Add(part);
                if (part.record.shape == BlockShape.Core)
                    hasCore = true;
                GetShapePoints(part.record.shape, points);
                for (int p = 0; p < points.Count; p++)
                {
                    Vector3 local = part.record.position + part.record.rotation * points[p];
                    Vector3 projectedPoint = ProjectRaw(local);
                    if (!hasBounds)
                    {
                        projected = new Bounds(projectedPoint, Vector3.zero);
                        sourceBounds=new Bounds(local,Vector3.zero);
                        hasBounds = true;
                    }
                    else{projected.Encapsulate(projectedPoint);sourceBounds.Encapsulate(local);}
                }
            }

            if (!hasBounds)
            {
                projectionCenter = Vector2.zero;
                projectionScale = 1f;
                return;
            }

            projectionCenter = new Vector2(projected.center.x, projected.center.y);
            Rect rect = GetPixelAdjustedRect();
            panelCenter = rect.center;
            float usableWidth = Mathf.Max(1f, rect.width - 24f);
            float usableHeight = Mathf.Max(1f, rect.height - 24f);
            projectionScale = Mathf.Min(usableWidth / Mathf.Max(.01f, projected.size.x),
                usableHeight / Mathf.Max(.01f, projected.size.y));
            if(relativeObserver)
            {
                var center=ProjectRaw(sourceBounds.center);
                projectionCenter=new Vector2(center.x,center.y);
                if(Starframe.Presentation.UiTheme.Current.statusSchematic)
                {
                    // Fit from the actual blueprint vertices, not the diagonal of their local AABB.
                    // The enclosing radius is rotation invariant, so rolling cannot clip the schematic
                    // or make its size pulse as the observer-relative frame changes.
                    float radiusSquared=0f;
                    for(int i=0;i<blueprintParts.Count;i++)
                    {
                        ShipPartState part=blueprintParts[i];
                        GetShapePoints(part.record.shape,points);
                        for(int p=0;p<points.Count;p++)
                        {
                            Vector3 local=part.record.position+part.record.rotation*points[p];
                            radiusSquared=Mathf.Max(radiusSquared,(local-sourceBounds.center).sqrMagnitude);
                        }
                    }
                    projectionScale=Mathf.Min(usableWidth,usableHeight)/Mathf.Max(.01f,Mathf.Sqrt(radiusSquared)*2f);
                }
                else projectionScale=Mathf.Min(usableWidth,usableHeight)/Mathf.Max(.01f,sourceBounds.extents.magnitude*2f);
            }
        }

        void Rebuild()
        {
            faces.Clear();
            hasCore = false;
            VisiblePartCount = 0;
            DamagedPartCount = 0;
            CorePresent = false;
            if (ship)
            {
                for (int i = 0; i < blueprintParts.Count; i++)
                {
                    ShipPartState part = blueprintParts[i];
                    if (part != null && part.record != null && part.record.shape == BlockShape.Core)
                    {
                        hasCore = true;
                        corePosition = Project(part.record.position);
                        CorePresent = part.present && part.hp > 0f;
                        break;
                    }
                }
                for (int i = 0; i < blueprintParts.Count; i++)
                {
                    ShipPartState part = blueprintParts[i];
                    if (part == null || !part.present || part.hp <= 0f || part.record == null)
                        continue;
                    VisiblePartCount++;
                    if (part.maxHp > 0f && part.hp < part.maxHp - .001f)
                        DamagedPartCount++;
                    AddPartFaces(part);
                }
                faces.Sort(DepthComparer);
            }
            lastStructureRevision = ship ? ship.StructureRevision : -1;
            lastHealthRevision = ship ? ship.HealthRevision : -1;
            SetVerticesDirty();
        }

        void AddPartFaces(ShipPartState part)
        {
            GetShapePoints(part.record.shape, points);
            int[][] shapeFaces = part.record.shape == BlockShape.Wedge ? FacesWedge :
                part.record.shape == BlockShape.Pyramid ? FacesPyramid : FacesCube;
            bool damaged = part.maxHp > 0f && part.hp < part.maxHp - .001f;
            float damageMix = damaged ? Mathf.Clamp01(.8f + .16f * (1f - part.hp / part.maxHp)) : 0f;
            var theme=Starframe.Presentation.UiTheme.Current;
            for (int f = 0; f < shapeFaces.Length; f++)
            {
                int[] indices = shapeFaces[f];
                Vector3 a3 = part.record.position + part.record.rotation * points[indices[0]];
                Vector3 b3 = part.record.position + part.record.rotation * points[indices[1]];
                Vector3 c3 = part.record.position + part.record.rotation * points[indices[2]];
                Vector3 faceCenter = (a3 + b3 + c3) / 3f;
                Vector2 a = Project(a3), b = Project(b3), c = Project(c3);
                float shade=RefinedFaceShade[f%RefinedFaceShade.Length];
                Color baseColor=lastRefinedStyle
                    ? new Color(theme.accent.r*.36f*shade,theme.accent.g*.62f*shade,theme.accent.b*.80f*shade,.96f)
                    : FacePalette[f%FacePalette.Length];
                Color damageColor=lastRefinedStyle?theme.danger:DamageColor;
                Color color = damaged ? Color.Lerp(baseColor, damageColor, damageMix) : baseColor;
                float depth = Depth(faceCenter);
                for (int tri = 1; tri < indices.Length - 1; tri++)
                {
                    Vector3 p3 = part.record.position + part.record.rotation * points[indices[tri]];
                    Vector3 q3 = part.record.position + part.record.rotation * points[indices[tri + 1]];
                    Vector2 p = Project(p3), q = Project(q3);
                    faces.Add(new Face { a = a, b = p, c = q, depth = depth, color = color });
                }
            }
            AddEdge(part, points, 0, 1, damaged);
            AddEdge(part, points, 1, 2, damaged);
            AddEdge(part, points, 2, 3, damaged);
            AddEdge(part, points, 3, 0, damaged);
        }

        void AddEdge(ShipPartState part, List<Vector3> shapePoints, int start, int end, bool damaged)
        {
            if (faces.Count + 4 > MaxVertices / 3)
                return;
            Vector3 a3 = part.record.position + part.record.rotation * shapePoints[start];
            Vector3 b3 = part.record.position + part.record.rotation * shapePoints[end];
            Vector2 a = Project(a3), b = Project(b3);
            Vector2 direction = b - a;
            if (direction.sqrMagnitude < .0001f)
                return;
            direction.Normalize();
            Vector2 side = new Vector2(-direction.y, direction.x) * 0.65f;
            var theme=Starframe.Presentation.UiTheme.Current;
            Color color = damaged ? (lastRefinedStyle?theme.danger:DamageColor)
                : lastRefinedStyle ? new Color(theme.accent.r*.58f,theme.accent.g*.72f,theme.accent.b*.84f,.62f) : EdgeColor;
            float depth = (Depth(a3) + Depth(b3)) * .5f + .0001f;
            faces.Add(new Face { a = a - side, b = a + side, c = b + side, depth = depth, color = color });
            faces.Add(new Face { a = a - side, b = b + side, c = b - side, depth = depth, color = color });
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var theme=Starframe.Presentation.UiTheme.Current;
            bool refined=theme.statusSchematic;
            if(refined) AddSupportingGrid(vh,GetPixelAdjustedRect());
            if (faces.Count == 0 && !hasCore)
                return;

            int vertexCount = vh.currentVertCount;
            for (int i = 0; i < faces.Count && vertexCount + 3 <= MaxVertices - 18; i++)
            {
                Face face = faces[i];
                AddTriangle(vh, face.a, face.b, face.c, face.color, ref vertexCount);
            }

            if (hasCore && vertexCount + 18 <= MaxVertices)
            {
                float radius = refined ? 6f : 5f;
                Vector2 center = corePosition;
                if (CorePresent)
                {
                    if(refined)
                    {
                        Color halo=new Color(theme.accent.r,theme.accent.g,theme.accent.b,.12f);
                        AddTriangle(vh,center+Vector2.up*(radius+3f),center+Vector2.right*(radius+3f),center-Vector2.up*(radius+3f),halo,ref vertexCount);
                        AddTriangle(vh,center+Vector2.up*(radius+3f),center-Vector2.up*(radius+3f),center-Vector2.right*(radius+3f),halo,ref vertexCount);
                    }
                    Color core=refined?theme.accent:CoreColor;
                    Color highlight=refined?Color.Lerp(theme.accent,Color.white,.7f):Color.white;
                    AddTriangle(vh, center + new Vector2(0, radius), center + new Vector2(radius, 0), center + new Vector2(0, -radius), core, ref vertexCount);
                    AddTriangle(vh, center + new Vector2(0, radius), center + new Vector2(0, -radius), center + new Vector2(-radius, 0), core, ref vertexCount);
                    AddTriangle(vh, center + new Vector2(0, radius * .55f), center + new Vector2(radius * .55f, 0), center + new Vector2(0, -radius * .55f), highlight, ref vertexCount);
                    AddTriangle(vh, center + new Vector2(0, radius * .55f), center + new Vector2(0, -radius * .55f), center + new Vector2(-radius * .55f, 0), highlight, ref vertexCount);
                }
                else
                {
                    Color failed = refined?theme.danger:DamageColor;
                    AddTriangle(vh, center + new Vector2(-radius, -radius), center + new Vector2(-radius * .55f, -radius), center + new Vector2(radius, radius), failed, ref vertexCount);
                    AddTriangle(vh, center + new Vector2(-radius * .55f, -radius), center + new Vector2(radius, radius), center + new Vector2(radius * .55f, radius), failed, ref vertexCount);
                    AddTriangle(vh, center + new Vector2(-radius, radius), center + new Vector2(-radius * .55f, radius), center + new Vector2(radius, -radius), failed, ref vertexCount);
                    AddTriangle(vh, center + new Vector2(-radius * .55f, radius), center + new Vector2(radius, -radius), center + new Vector2(radius * .55f, -radius), failed, ref vertexCount);
                }
            }
        }

        static void AddSupportingGrid(VertexHelper vh,Rect rect)
        {
            // Quiet registration marks leave the silhouette and damaged parts unobstructed.
            var muted=Starframe.Presentation.UiTheme.Current.muted;
            const float inset=5f;
            Color axis=new Color(muted.r,muted.g,muted.b,.14f);
            AddStroke(vh,new Vector2(rect.xMin+inset,rect.center.y),new Vector2(rect.xMin+inset+12f,rect.center.y),1f,axis);
            AddStroke(vh,new Vector2(rect.xMax-inset-12f,rect.center.y),new Vector2(rect.xMax-inset,rect.center.y),1f,axis);
        }

        static void AddStroke(VertexHelper vh,Vector2 a,Vector2 b,float width,Color color)
        {
            Vector2 delta=b-a;
            if(delta.sqrMagnitude<.0001f)return;
            Vector2 normal=new Vector2(-delta.y,delta.x).normalized*width*.5f;
            int index=vh.currentVertCount;
            vh.AddVert(a-normal,color,Vector2.zero);vh.AddVert(a+normal,color,Vector2.zero);
            vh.AddVert(b+normal,color,Vector2.zero);vh.AddVert(b-normal,color,Vector2.zero);
            vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);
        }

        static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color, ref int vertexCount)
        {
            int first = vertexCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
            vertexCount += 3;
        }

        Vector2 Project(Vector3 point)
        {
            Vector3 p = ProjectRaw(point);
            return new Vector2(p.x - projectionCenter.x, p.y - projectionCenter.y) * projectionScale + panelCenter;
        }

        Vector3 ProjectRaw(Vector3 p)
        {
            p = shipLocalRotation * p;
            return new Vector3((p.x - p.z) * .70710678f,
                p.y * .81649658f - (p.x + p.z) * .40824829f, p.x + p.y + p.z);
        }

        float Depth(Vector3 p)
        {
            p = shipLocalRotation * p;
            return p.x + p.y + p.z;
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (ship)
            {
                CacheBlueprintBounds();
                Rebuild();
            }
        }

        static void GetShapePoints(BlockShape shape, List<Vector3> target)
        {
            target.Clear();
            switch (shape)
            {
                case BlockShape.Wedge:
                    target.Add(new Vector3(-.5f,-.5f,-.5f)); target.Add(new Vector3(.5f,-.5f,-.5f));
                    target.Add(new Vector3(.5f,-.5f,.5f)); target.Add(new Vector3(-.5f,-.5f,.5f));
                    target.Add(new Vector3(-.5f,.5f,.5f)); target.Add(new Vector3(.5f,.5f,.5f)); break;
                case BlockShape.Pyramid:
                    target.Add(new Vector3(-.5f,-.5f,-.5f)); target.Add(new Vector3(.5f,-.5f,-.5f));
                    target.Add(new Vector3(.5f,-.5f,.5f)); target.Add(new Vector3(-.5f,-.5f,.5f)); target.Add(new Vector3(0,.5f,0)); break;
                case BlockShape.Slab:
                    target.Add(new Vector3(-.5f,-.5f,-.5f)); target.Add(new Vector3(.5f,-.5f,-.5f));
                    target.Add(new Vector3(.5f,-.5f,.5f)); target.Add(new Vector3(-.5f,-.5f,.5f));
                    target.Add(new Vector3(-.5f,0,-.5f)); target.Add(new Vector3(.5f,0,-.5f));
                    target.Add(new Vector3(.5f,0,.5f)); target.Add(new Vector3(-.5f,0,.5f)); break;
                case BlockShape.Thruster2:
                    target.Add(new Vector3(-.5f,-.5f,-.5f)); target.Add(new Vector3(.5f,-.5f,-.5f));
                    target.Add(new Vector3(.5f,-.5f,.5f)); target.Add(new Vector3(-.5f,-.5f,.5f));
                    target.Add(new Vector3(-.5f,1.5f,-.5f)); target.Add(new Vector3(.5f,1.5f,-.5f));
                    target.Add(new Vector3(.5f,1.5f,.5f)); target.Add(new Vector3(-.5f,1.5f,.5f)); break;
                case BlockShape.Laser:
                    target.Add(new Vector3(-.5f,-.5f,-.5f)); target.Add(new Vector3(.5f,-.5f,-.5f));
                    target.Add(new Vector3(.5f,-.5f,2.5f)); target.Add(new Vector3(-.5f,-.5f,2.5f));
                    target.Add(new Vector3(-.5f,.5f,-.5f)); target.Add(new Vector3(.5f,.5f,-.5f));
                    target.Add(new Vector3(.5f,.5f,2.5f)); target.Add(new Vector3(-.5f,.5f,2.5f)); break;
                default:
                    target.Add(new Vector3(-.5f,-.5f,-.5f)); target.Add(new Vector3(.5f,-.5f,-.5f));
                    target.Add(new Vector3(.5f,-.5f,.5f)); target.Add(new Vector3(-.5f,-.5f,.5f));
                    target.Add(new Vector3(-.5f,.5f,-.5f)); target.Add(new Vector3(.5f,.5f,-.5f));
                    target.Add(new Vector3(.5f,.5f,.5f)); target.Add(new Vector3(-.5f,.5f,.5f)); break;
            }
        }
    }
}
