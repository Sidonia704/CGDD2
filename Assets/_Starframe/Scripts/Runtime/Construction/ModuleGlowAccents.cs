// @role Attach shared nozzle and weapon glow meshes.
// @calls BlockModules, VoxelFxGeometry

using UnityEngine;
using Starframe.Combat;

namespace Starframe.Construction
{
    /// <summary>Small permanent nozzle lights, sharing their mesh and material across all modules.</summary>
    public static class ModuleGlowAccents
    {
        const string AccentName = "ModuleGlowAccent";
        static Material sharedGlow;

        /// <param name="serializedGlowShader">An optional serialized VoxelGlow reference retained in the build.</param>
        public static Transform Attach(Transform parent, BlockShape shape, Mesh visualMesh, Shader serializedGlowShader = null, Vector3? muzzle = null)
        {
            bool thruster = BlockModules.IsThruster(shape);
            if (!parent || !visualMesh || (!thruster && shape != BlockShape.Laser)) return null;
            var existing = parent.Find(AccentName);
            if (existing) return existing;

            if (!sharedGlow)
            {
                sharedGlow = VoxelFxGeometry.CreateGlow(serializedGlowShader);
                if (!sharedGlow) return null;
                sharedGlow.name = "Shared permanent cyan nozzle light";
                sharedGlow.SetColor("_Color", new Color(.045f, .62f, .95f, 1));
                sharedGlow.SetFloat("_Intensity", 1.65f);
                sharedGlow.SetFloat("_Opacity", 1);
            }

            var bounds = visualMesh.bounds;
            var centre = bounds.center;
            const float thickness = .025f;
            Vector3 size;
            if (thruster)
            {
                float width = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * .54f, .12f, .48f);
                centre.y = bounds.max.y + thickness * .5f + .001f;
                size = new Vector3(width, thickness, width);
            }
            else
            {
                float width = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.y) * .3f, .1f, .24f);
                centre = muzzle ?? centre;
                centre.z = bounds.max.z + thickness * .5f + .001f;
                size = new Vector3(width, width, thickness);
            }

            var go = new GameObject(AccentName, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshFilter>().sharedMesh = VoxelFxGeometry.Cube;
            VoxelFxGeometry.Configure(go.GetComponent<MeshRenderer>(), sharedGlow);
            return go.transform;
        }
    }
}
