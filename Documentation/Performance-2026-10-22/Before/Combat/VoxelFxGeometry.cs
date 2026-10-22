using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.Combat
{
    /// <summary>Shared solid geometry for every voxel effect. No particle owns a mesh.</summary>
    public static class VoxelFxGeometry
    {
        public const int LaserSegments = 9;
        static Mesh cube, beam, plume;
        public static Mesh Cube => cube ? cube : cube = BuildCube();
        public static Mesh Beam => beam ? beam : beam = BuildBeam();
        public static Mesh Plume => plume ? plume : plume = BuildPlume();

        public static Material CreateGlow(Shader fallback = null)
        {
            // The caller's serialized legacy shader remains a fallback for older builds.
            var shader = Shader.Find("Starframe/Combat/VoxelGlow");
            if (!shader) shader = fallback;
            return shader ? new Material(shader) { name = "Shared cyan voxel glow", enableInstancing = true } : null;
        }

        public static void Configure(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Mesh BuildCube()
        {
            var builder = new Builder();
            builder.AddCube(Vector3.zero, Vector3.one, false);
            return builder.Build("Shared solid FX voxel");
        }

        static Mesh BuildBeam()
        {
            var builder = new Builder();
            // Tail to nose: separated, irregular blue cubes followed by overlapping pale cubes and a white head.
            float[] positions={.025f,.14f,.265f,.39f,.515f,.64f,.8f,.885f,.96f};
            float[] sizes={.34f,.42f,.5f,.61f,.72f,.8f,.91f,.95f,1};
            Color[] colors={new Color(.12f,.28f,.4f,.35f),new Color(.13f,.32f,.47f,.45f),new Color(.13f,.36f,.55f,.55f),
                new Color(.1f,.35f,.7f,.7f),new Color(.015f,.34f,1,.85f),new Color(.04f,.52f,1),
                new Color(.35f,.72f,1),new Color(.65f,.9f,1),Color.white};
            for (int i = 0; i < LaserSegments; i++)
            {
                float offset=i==LaserSegments-1?0:Mathf.Sin(i*2.7f)*.045f;
                builder.AddCube(new Vector3(offset,positions[i],0),new Vector3(sizes[i],sizes[i]*.06f,sizes[i]),true,color:colors[i]);
            }
            var mesh=builder.Build("Shared white laser head and eight blue tail voxels");
            mesh.bounds=new Bounds(Vector3.up*.5f,new Vector3(1.2f,1,1.2f));
            return mesh;
        }

        static Mesh BuildPlume()
        {
            var builder = new Builder();
            builder.AddCube(new Vector3(0,.018f,0),new Vector3(.72f,.036f,.72f),true);
            // A straight, dense stream; all phases advance away from the nozzle together.
            for (int i = 0; i < 14; i++)
            {
                float t=(i+.5f)/14f,side=.49f+(i%3)*.025f;
                builder.AddCube(new Vector3(Mathf.Sin(i*2.4f)*.035f,t,Mathf.Cos(i*1.7f)*.035f),Vector3.one*side,true,t);
            }
            for(int i=0;i<6;i++){
                float angle=i*2.399963f,spread=.5f+(i%2)*.2f;
                builder.AddCube(new Vector3(Mathf.Cos(angle)*spread,.5f,Mathf.Sin(angle)*spread),Vector3.one*(.13f+(i%3)*.035f),true,(i*.173f+.09f)%1,flowChannel:2);
            }
            var mesh=builder.Build("Shared flowing white cyan voxel exhaust");
            mesh.bounds=new Bounds(new Vector3(0,.55f,0),new Vector3(2.4f,1.8f,2.4f));
            return mesh;
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> centres = new List<Vector2>();
            readonly List<Vector2> flow = new List<Vector2>();
            readonly List<Vector2> radialCentres = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> indices = new List<int>();
            static readonly Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

            public void AddCube(Vector3 centre, Vector3 size, bool axial, float flowPhase = -1,Color? color=null,float flowChannel=1)
            {
                foreach (var normal in axes)
                {
                    var tangent = Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.up;
                    var bitangent = Vector3.Cross(normal, tangent);
                    int start = vertices.Count;
                    foreach (var corner in new[] { new Vector2(-1,-1), new Vector2(1,-1), new Vector2(1,1), new Vector2(-1,1) })
                    {
                        vertices.Add(centre + Vector3.Scale((normal + tangent * corner.x + bitangent * corner.y) * .5f, size));
                        normals.Add(normal);
                        centres.Add(new Vector2(centre.y, axial ? 1 : 0));
                        flow.Add(new Vector2(Mathf.Max(0, flowPhase), flowPhase >= 0 ? flowChannel : 0));
                        radialCentres.Add(new Vector2(centre.x, centre.z));
                        colors.Add(color??Color.white);
                    }
                    indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                    indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(1, centres); mesh.SetTriangles(indices, 0);
                mesh.SetUVs(2, flow); mesh.SetUVs(3, radialCentres);
                mesh.SetColors(colors);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
