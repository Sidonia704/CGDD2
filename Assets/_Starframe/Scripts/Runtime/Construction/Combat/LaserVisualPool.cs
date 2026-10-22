// @role Reuse laser head and tail visuals independently of projectile damage.
// @calls VoxelFxGeometry

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.Combat
{
    /// <summary>Pooled solid voxel laser segments with a moving muzzle boundary.</summary>
    public sealed class LaserVisualPool : MonoBehaviour
    {
        sealed class Visual
        {
            public Transform transform;
            public MeshRenderer renderer;
            public MaterialPropertyBlock properties;
            public int handle;
            public float length;
            public float tailLength;
            public float width;
            public float intensity;
            public Vector3 direction;
            public Vector3 origin,position,muzzleLocalPosition;
            public Transform muzzleSource;
        }

        readonly List<Visual> visuals = new List<Visual>();
        readonly Stack<int> freeSlots = new Stack<int>();
        readonly Dictionary<int, int> activeHandles = new Dictionary<int, int>();
        Mesh sharedMesh;
        Material sharedMaterial;
        int nextHandle = 1;
        float visualScale=1;

        static readonly int TailFractionId = Shader.PropertyToID("_TailFraction");
        static readonly int ProfileId=Shader.PropertyToID("_EffectProfile");
        static readonly int AxialShrinkId = Shader.PropertyToID("_AxialShrink"), ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

        public void Initialize(Shader shader,float effectScale=1)
        {

            if (sharedMaterial) Destroy(sharedMaterial);
            sharedMesh = null;
            sharedMaterial = null;
            activeHandles.Clear();
            freeSlots.Clear();
            for (int i = 0; i < visuals.Count; i++)
                if (visuals[i].transform) Destroy(visuals[i].transform.gameObject);
            visuals.Clear();

            sharedMaterial = VoxelFxGeometry.CreateGlow(shader);
            visualScale=Mathf.Max(.1f,effectScale);
            if (!sharedMaterial)
            {
                enabled = false;
                return;
            }

            enabled = true;

            sharedMesh = VoxelFxGeometry.Beam;
        }

        public int Acquire(Vector3 position, Vector3 direction, float diameter, float visualLength, float tailLength, Transform muzzleSource=null, Vector3 muzzleLocalPosition=default,float widthScale=.55f,float intensity=4.2f)
        {
            if (!sharedMaterial || !sharedMesh) return -1;
            int slot;
            Visual visual;
            if (freeSlots.Count > 0)
            {
                slot = freeSlots.Pop();
                visual = visuals[slot];
            }
            else
            {
                slot = visuals.Count;
                visual = CreateVisual(slot);
                visuals.Add(visual);
            }

            int handle = nextHandle++;
            if (nextHandle <= 0) nextHandle = 1;
            while (activeHandles.ContainsKey(handle))
            {
                handle = nextHandle++;
                if (nextHandle <= 0) nextHandle = 1;
            }
            visual.handle = handle;
            // The source projectile is a small simulation point; give its rendered beam a broad, readable body.
            visual.width = Mathf.Max(.001f, diameter * Mathf.Max(.01f,widthScale)*visualScale);
            visual.intensity=Mathf.Max(0,intensity);
            visual.length = Mathf.Max(.001f, visualLength*visualScale);
            visual.tailLength = Mathf.Max(0f, tailLength*visualScale);
            visual.origin=position;visual.muzzleSource=muzzleSource;visual.muzzleLocalPosition=muzzleLocalPosition;
            activeHandles.Add(handle, slot);
            MoveVisual(visual, position, direction);
            return handle;
        }

        public void Move(int handle, Vector3 position, Vector3 direction)
        {
            if (activeHandles.TryGetValue(handle, out int slot))
                MoveVisual(visuals[slot], position, direction);
        }

        public void Release(int handle)
        {
            if (!activeHandles.TryGetValue(handle, out int slot)) return;
            activeHandles.Remove(handle);
            Visual visual = visuals[slot];
            visual.renderer.enabled = false;
            visual.handle = 0;
            freeSlots.Push(slot);
        }

        public void Clear()
        {
            activeHandles.Clear();
            freeSlots.Clear();
            for (int i = 0; i < visuals.Count; i++)
            {
                Visual visual = visuals[i];
                if (visual.renderer) visual.renderer.enabled = false;
                visual.handle = 0;
                freeSlots.Push(i);
            }
        }

        Visual CreateVisual(int slot)
        {
            var go = new GameObject("LaserVisual_" + slot, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = sharedMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;
            return new Visual { transform = go.transform, renderer = renderer, properties = new MaterialPropertyBlock(), direction = Vector3.forward };
        }

        static void MoveVisual(Visual visual, Vector3 position, Vector3 direction)
        {
            visual.position=position;
            if (direction.sqrMagnitude > 1e-8f) visual.direction = direction.normalized;
            // The visible column grows with travel; neither its body nor its tail may extend behind the muzzle.
            float available=Mathf.Max(0,Vector3.Dot(position-visual.origin,visual.direction));
            if(visual.muzzleSource)available=Mathf.Min(available,Mathf.Max(0,Vector3.Dot(position-visual.muzzleSource.TransformPoint(visual.muzzleLocalPosition),visual.direction)));
            float fullLength = Mathf.Min(visual.length + visual.tailLength,available);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, visual.direction);
            visual.transform.SetPositionAndRotation(position - visual.direction * fullLength, rotation);
            visual.transform.localScale = new Vector3(visual.width, fullLength, visual.width);
            visual.properties.SetFloat(AxialShrinkId,fullLength>.0001f?visual.width/(.06f*fullLength):1);
            visual.properties.SetFloat(ProfileId,1);
            visual.properties.SetColor(ColorId,Color.white);
            visual.properties.SetFloat(IntensityId,visual.intensity);
            visual.properties.SetFloat(TailFractionId,fullLength>.0001f?Mathf.Max(0,fullLength-visual.length)/fullLength:0);
            visual.renderer.SetPropertyBlock(visual.properties);visual.renderer.enabled=fullLength>.0001f;
        }

        void LateUpdate(){foreach(int slot in activeHandles.Values){var v=visuals[slot];if(v.muzzleSource)MoveVisual(v,v.position,v.direction);}}

        void OnDestroy()
        {

            if (sharedMaterial) Destroy(sharedMaterial);
        }
    }
}

