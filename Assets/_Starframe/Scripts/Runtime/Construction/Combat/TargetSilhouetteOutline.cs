// @role Draw a red through-cover silhouette for only the currently locked ship using an independent mask and cached live geometry.
// @calls TargetLockSystem, FlightShipRuntime, TargetAimGraphic
// @tune resolutionScale:float:0.25-1:Selected-target mask resolution relative to the existing world view.
// @tune outlinePixels:float:1-6:Exterior silhouette thickness in world-view pixels.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Flight;

namespace Starframe.Combat
{
    [DefaultExecutionOrder(90)]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TargetSilhouetteOutline : UnityEngine.UI.RawImage
    {
        public readonly struct MaskDraw
        {
            public readonly Mesh Mesh;
            public readonly int Submesh;
            public readonly Matrix4x4 LocalMatrix;
            public MaskDraw(Mesh mesh, int submesh, Matrix4x4 matrix) { Mesh = mesh; Submesh = submesh; LocalMatrix = matrix; }
        }

        sealed class Batch
        {
            public Mesh mesh;
            public int submesh;
            public Matrix4x4[] matrices;
            public ComputeBuffer buffer;
            public MaterialPropertyBlock properties;
        }

        static readonly int ViewProjectionId = Shader.PropertyToID("_StarframeTargetViewProjection");
        static readonly int RootMatrixId = Shader.PropertyToID("_StarframeTargetRoot");
        static readonly int MatricesId = Shader.PropertyToID("_StarframeSelectedMatrices");
        static readonly int OutlineRadiusId = Shader.PropertyToID("_OutlineRadius");
        [SerializeField, Range(.25f, 1)] float resolutionScale = .5f;
        [SerializeField, Range(1, 6)] float outlinePixels = 2;
        Camera viewCamera;
        TargetLockSystem targeting;
        FlightShipRuntime selected;
        int selectedLife = -1, selectedRevision = -1;
        bool geometryDirty, procedural;
        readonly List<MaskDraw> draws = new List<MaskDraw>();
        readonly List<Batch> batches = new List<Batch>();
        Material maskMaterial, filterMaterial, edgeMaterial;
        RenderTexture mask, expandedMask, closedMask;
        CommandBuffer commands;
        public int CachedDrawCount => draws.Count;
        public int SubmittedMaskDraws { get; private set; }
        public bool OutlineVisible { get; private set; }

        public void Initialize(Camera view, TargetLockSystem target)
        {
            viewCamera = view; targeting = target;
            raycastTarget = false;
            color = Color.clear;
            texture = null;
        }

        /// <summary>Capture attached hull meshes even when instancing hides their source renderers.</summary>
        public static void CaptureGeometry(FlightShipRuntime ship, List<MaskDraw> destination)
        {
            destination.Clear();
            if (!ship) return;
            Matrix4x4 inverse = ship.transform.worldToLocalMatrix;
            foreach (var part in ship.Parts)
            {
                if (!part.present || part.detached || part.hp <= 0 || !part.view) continue;
                var filter = part.view.GetComponent<MeshFilter>();
                var mesh = filter ? filter.sharedMesh : null;
                if (!mesh) continue;
                Matrix4x4 matrix = inverse * filter.transform.localToWorldMatrix;
                // Root meshes describe the housing/hull; child shield bubbles, forward rays and plumes are excluded.
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    if (mesh.GetTopology(submesh) == MeshTopology.Triangles && mesh.GetIndexCount(submesh) > 0)
                        destination.Add(new MaskDraw(mesh, submesh, matrix));
            }
        }

        void LateUpdate()
        {
            SubmittedMaskDraws = 0; OutlineVisible = false;
            var next = targeting && targeting.HasTarget ? targeting.LockedTarget : null;
            if (!object.ReferenceEquals(next, selected)) Bind(next);
            if (!selected || !selected.IsAlive || !viewCamera || !EnsureMaterials()) { color = Color.clear; return; }
            if (selected.LifeId != selectedLife || selected.StructureRevision != selectedRevision || geometryDirty) RebuildGeometry();
            if (draws.Count == 0) { color = Color.clear; return; }
            EnsureTextures();
            if (!mask || !expandedMask || !closedMask || !mask.IsCreated() || !expandedMask.IsCreated() || !closedMask.IsCreated()) { color = Color.clear; return; }

            commands.Clear();
            commands.SetRenderTarget(mask);
            commands.SetViewport(new Rect(0, 0, mask.width, mask.height));
            commands.ClearRenderTarget(false, true, Color.black);
            commands.SetGlobalMatrix(ViewProjectionId, GL.GetGPUProjectionMatrix(viewCamera.projectionMatrix, true) * viewCamera.worldToCameraMatrix);
            commands.SetGlobalMatrix(RootMatrixId, selected.transform.localToWorldMatrix);
            if (procedural)
            {
                foreach (var batch in batches)
                {
                    if (!batch.mesh || batch.matrices.Length == 0) continue;
                    commands.DrawMeshInstancedProcedural(batch.mesh, batch.submesh, maskMaterial, 0, batch.matrices.Length, batch.properties);
                    SubmittedMaskDraws++;
                }
            }
            else foreach (var draw in draws)
            {
                if (!draw.Mesh) continue;
                commands.DrawMesh(draw.Mesh, draw.LocalMatrix, maskMaterial, draw.Submesh, 1);
                SubmittedMaskDraws++;
            }
            // Closing the union mask bridges sub-pixel model seams before detecting the single hull silhouette.
            commands.Blit(mask, expandedMask, filterMaterial, 0);
            commands.Blit(expandedMask, closedMask, filterMaterial, 1);
            var previousTarget = RenderTexture.active;
            try { Graphics.ExecuteCommandBuffer(commands); }
            finally { RenderTexture.active = previousTarget; }
            edgeMaterial.SetFloat(OutlineRadiusId, Mathf.Max(.5f, outlinePixels * mask.width / Mathf.Max(1, viewCamera.pixelWidth)));
            texture = closedMask;
            color = TargetAimGraphic.EnemyTint;
            OutlineVisible = true;
        }

        void Bind(FlightShipRuntime ship)
        {
            if (!object.ReferenceEquals(selected, null))
            {
                selected.PartChanged -= PartChanged;
                selected.PerformanceChanged -= GeometryChanged;
            }
            selected = ship;
            selectedLife = selectedRevision = -1;
            geometryDirty = true;
            commands?.Clear(); ReleaseBatches(); draws.Clear();
            if (selected)
            {
                selected.PartChanged += PartChanged;
                selected.PerformanceChanged += GeometryChanged;
            }
        }

        void PartChanged(ShipPartState _) => geometryDirty = true;
        void GeometryChanged() => geometryDirty = true;

        void RebuildGeometry()
        {
            commands?.Clear(); ReleaseBatches();
            CaptureGeometry(selected, draws);
            procedural = SystemInfo.supportsInstancing && SystemInfo.supportsComputeShaders && SystemInfo.graphicsShaderLevel >= 45;
            if (procedural)
            {
                var groups = new Dictionary<(Mesh, int), List<Matrix4x4>>();
                foreach (var draw in draws)
                {
                    var key = (draw.Mesh, draw.Submesh);
                    if (!groups.TryGetValue(key, out var matrices)) groups.Add(key, matrices = new List<Matrix4x4>());
                    matrices.Add(draw.LocalMatrix);
                }
                foreach (var pair in groups)
                {
                    var batch = new Batch { mesh = pair.Key.Item1, submesh = pair.Key.Item2, matrices = pair.Value.ToArray(), properties = new MaterialPropertyBlock() };
                    batch.buffer = new ComputeBuffer(batch.matrices.Length, 64, ComputeBufferType.Structured);
                    batch.buffer.SetData(batch.matrices);
                    batch.properties.SetBuffer(MatricesId, batch.buffer);
                    batches.Add(batch);
                }
            }
            selectedLife = selected.LifeId; selectedRevision = selected.StructureRevision; geometryDirty = false;
        }

        bool EnsureMaterials()
        {
            if (maskMaterial && filterMaterial && edgeMaterial)
            {
                if (commands == null) commands = new CommandBuffer { name = "Starframe selected-target silhouette only" };
                return true;
            }
            var maskShader = Resources.Load<Shader>("Combat/TargetSilhouetteMask");
            var filterShader = Resources.Load<Shader>("Combat/TargetSilhouetteFilter");
            var edgeShader = Resources.Load<Shader>("Combat/TargetSilhouetteEdge");
            if (!maskShader || !filterShader || !edgeShader || !maskShader.isSupported || !filterShader.isSupported || !edgeShader.isSupported) return false;
            if (!maskMaterial) maskMaterial = new Material(maskShader) { name = "Selected ship union mask", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            if (!filterMaterial) filterMaterial = new Material(filterShader) { name = "Selected ship seam closing", hideFlags = HideFlags.HideAndDontSave };
            if (!edgeMaterial) edgeMaterial = new Material(edgeShader) { name = "Selected ship red outline", hideFlags = HideFlags.HideAndDontSave };
            material = edgeMaterial;
            if (commands == null) commands = new CommandBuffer { name = "Starframe selected-target silhouette only" };
            return true;
        }

        void EnsureTextures()
        {
            int width = Mathf.Max(1, Mathf.RoundToInt(viewCamera.pixelWidth * resolutionScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(viewCamera.pixelHeight * resolutionScale));
            if (mask && expandedMask && closedMask && mask.width == width && mask.height == height
                && mask.IsCreated() && expandedMask.IsCreated() && closedMask.IsCreated()) return;
            ReleaseTextures();
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            mask = MakeTexture("Selected ship mask", width, height, format);
            expandedMask = MakeTexture("Selected ship expanded union", width, height, format);
            closedMask = MakeTexture("Selected ship closed silhouette", width, height, format);
        }

        static RenderTexture MakeTexture(string label, int width, int height, RenderTextureFormat format)
        {
            var result = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            { name = label, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, antiAliasing = 1 };
            if (result.Create()) return result;
            DestroyOwned(result); return null;
        }

        void ReleaseBatches()
        {
            foreach (var batch in batches) batch.buffer?.Release();
            batches.Clear();
        }

        void ReleaseTextures()
        {
            commands?.Clear();
            texture = null;
            ReleaseTexture(ref mask); ReleaseTexture(ref expandedMask); ReleaseTexture(ref closedMask);
        }

        static void ReleaseTexture(ref RenderTexture value)
        {
            if (!value) return;
            value.Release(); DestroyOwned(value); value = null;
        }

        static void DestroyOwned(Object value)
        { if (!value) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }

        protected override void OnDisable()
        {
            Bind(null); ReleaseTextures();
            commands?.Release(); commands = null;
            OutlineVisible = false; SubmittedMaskDraws = 0; color = Color.clear;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            Bind(null); ReleaseTextures();
            commands?.Release(); commands = null;
            material = null;
            DestroyOwned(maskMaterial); DestroyOwned(filterMaterial); DestroyOwned(edgeMaterial);
            maskMaterial = filterMaterial = edgeMaterial = null;
            base.OnDestroy();
        }
    }
}
