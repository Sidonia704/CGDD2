using System;
using System.Collections.Generic;
using System.IO;
using Starframe.Maps;
using Starframe.Construction;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    public sealed partial class SpaceMapStudioWindow
    {
        sealed class PreviewItem { public VoxelStructureRuntime runtime; public int index; public VoxelPreviewInstances instances; public int renderRevision = -1; public bool overview; public Mesh damagedOverview; }
        
        readonly Dictionary<VoxelAssetDefinition, (int revision, Mesh mesh, long signature, bool valid)> overviewSignatures = new Dictionary<VoxelAssetDefinition, (int, Mesh, long, bool)>();
        bool ValidOverview(VoxelAssetDefinition definition)
        {
            if (!definition.initialLodMesh || definition.initialLodMesh.vertexCount == 0 || definition.initialLodMesh.subMeshCount != definition.RenderMaterialCount) return false;
            if (!overviewSignatures.TryGetValue(definition, out var cached) || cached.revision != definition.GeometryRevision ||
                cached.mesh != definition.initialLodMesh || cached.signature != definition.initialLodSignature)
            {
                cached = (definition.GeometryRevision, definition.initialLodMesh, definition.initialLodSignature, definition.HasValidInitialLod);
                overviewSignatures[definition] = cached;
            }
            return cached.valid;
        }
        static bool RuntimeRenderResourcesValid(VoxelStructureRuntime runtime)
        {
            if (runtime.AliveCount == 0) return true;
            var meshes = runtime.RenderMeshes;
            if (meshes.Count == 0) return false;
            for (int i = 0; i < meshes.Count; i++) if (!meshes[i]) return false;
            return true;
        }
        readonly List<PreviewItem> previewItems = new List<PreviewItem>();
        PreviewRenderUtility preview;
        GameObject previewRoot;
        Mesh grid;
        Material gridMaterial;
        readonly List<Material> previewMaterials = new List<Material>();
        [SerializeField] Vector3 pivot;
        [SerializeField] Vector2 orbit = new Vector2(30, -35);
        [SerializeField] float distance = 24;
        bool frameRequested, gridDirty = true, moving;
        Vector3 moveOffset;
        int strokeGroup = -1;
        readonly HashSet<Vector3Int> painted = new HashSet<Vector3Int>();
        string capturePath;
        string previewError;
        readonly MapBrushRepeat brushRepeat = new MapBrushRepeat();
        int viewportControl;
        Texture renderedPreview;
        bool renderDirty = true;
        Rect renderedRect;
        Vector3 renderedPivot;
        Vector2 renderedOrbit;
        float renderedDistance;
        bool renderedMapMode;

        void OnInspectorUpdate() { if (preview != null) Repaint(); }
        public void CapturePreview(string path) { capturePath = path; renderDirty = true; Repaint(); }
        void EndStroke()
        {
            brushRepeat.End(); moving = false;
            if (viewportControl != 0 && GUIUtility.hotControl == viewportControl) GUIUtility.hotControl = 0;
            if (strokeGroup >= 0) Undo.CollapseUndoOperations(strokeGroup);
            strokeGroup = -1; painted.Clear();
        }
        void OnLostFocus() => EndStroke();
        void DisposePreview()
        {
            foreach (var item in previewItems) { item.instances?.Dispose(); if (item.damagedOverview) DestroyImmediate(item.damagedOverview); }
            if (previewRoot)
                foreach (var runtime in previewRoot.GetComponentsInChildren<VoxelStructureRuntime>(true)) runtime.DisposeResources();
            previewItems.Clear();
            if (preview != null) { preview.Cleanup(); preview = null; }
            previewRoot = null;
            renderedPreview = null; renderDirty = true;
            if (grid) DestroyImmediate(grid); grid = null;
            if (gridMaterial) DestroyImmediate(gridMaterial); gridMaterial = null;
            foreach (var material in previewMaterials) if (material) DestroyImmediate(material); previewMaterials.Clear();
        }
        void RebuildPreview()
        {
            DisposePreview(); rebuildRequested = false; previewError = null;
            preview = new PreviewRenderUtility();
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.035f, .045f, .06f);
            preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100000;
            preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(40, -35, 0);
            preview.lights[1].intensity = .65f; preview.lights[1].transform.rotation = Quaternion.Euler(330, 150, 0);
            preview.ambientColor = new Color(.34f, .37f, .42f);
            previewRoot = new GameObject("Map studio isolated preview") { hideFlags = HideFlags.HideAndDontSave };
            preview.AddSingleGO(previewRoot);
            if (mapMode && map)
            {
                for (int i = 0; i < map.instances.Count; i++)
                {
                    var item = map.instances[i]; if (!item.asset) continue;
                    var runtime = AddPreview(item.asset, item.label, item.position, Quaternion.Euler(item.eulerAngles), item.scale);
                    bool overview = runtime.DataOnlyPreview && runtime.GetComponent<MeshFilter>().sharedMesh;
                    previewItems.Add(new PreviewItem { runtime = runtime, index = i, overview = overview, instances = runtime.DataOnlyPreview && !overview ? new VoxelPreviewInstances(runtime) : null });
                }
            }
            else if (asset)
            {
                var runtime = AddPreview(asset, asset.name, Vector3.zero, Quaternion.identity, 1);
                bool overview = runtime.DataOnlyPreview && runtime.GetComponent<MeshFilter>().sharedMesh;
                previewItems.Add(new PreviewItem { runtime = runtime, index = 0, overview = overview, instances = runtime.DataOnlyPreview && !overview ? new VoxelPreviewInstances(runtime) : null });
            }
            gridDirty = true;
            if (frameRequested) { frameRequested = false; FrameView(); }
        }
        VoxelStructureRuntime AddPreview(VoxelAssetDefinition definition, string label, Vector3 position, Quaternion rotation, float scale)
        {
            var go = new GameObject(label); go.transform.SetParent(previewRoot.transform, false);
            go.transform.localPosition = position; go.transform.localRotation = rotation; go.transform.localScale = Vector3.one * scale;
            bool overview = authoredSurfacePreview && ValidOverview(definition);
            bool fastPreview = overview || definition.hullModules && definition.cells.Count >= 512;
            var runtime = go.AddComponent<VoxelStructureRuntime>(); runtime.Initialize(definition, true, !fastPreview);
            if (overview) { runtime.GetComponent<MeshFilter>().sharedMesh = definition.initialLodMesh; runtime.GetComponent<MeshRenderer>().enabled = true; }
            var shader = Shader.Find("Starframe/Tools/VoxelPreview");
            if (shader && !definition.hullModules)
            {
                var renderer = go.GetComponent<MeshRenderer>(); var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    material.SetColor("_BaseColor", materials[i] && materials[i].HasProperty("_BaseColor") ? materials[i].GetColor("_BaseColor") : Color.gray);
                    previewMaterials.Add(material); materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
            return runtime;
        }
        void FrameView()
        {
            if (previewItems.Count == 0) { pivot = Vector3.zero; distance = mapMode ? 1500 : 20; return; }
            Bounds bounds = new Bounds(); bool first = true;
            foreach (var item in previewItems)
            {
                if (mapMode && selectedInstance >= 0 && item.index != selectedInstance) continue;
                var next = TransformBounds(item.runtime.LocalBounds, item.runtime.transform.localToWorldMatrix);
                if (first) { bounds = next; first = false; } else bounds.Encapsulate(next);
            }
            if (first) return;
            pivot = bounds.center; distance = Mathf.Max(4, bounds.extents.magnitude * 2.7f); Repaint();
        }
        void DrawViewport()
        {
            if (viewport == null || viewport.contentRect.width < 2 || viewport.contentRect.height < 2 || float.IsNaN(viewport.contentRect.width) || float.IsNaN(viewport.contentRect.height)) return;
            var rect = new Rect(0, 0, viewport.contentRect.width, viewport.contentRect.height); var e = Event.current;
            viewportControl = GUIUtility.GetControlID("MapStudioBrush".GetHashCode(), FocusType.Passive);
            if (EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying) return;
            if (preview != null && (!previewRoot || !preview.camera || previewItems.Exists(item => !item.runtime || (item.instances != null ? !item.instances.Valid : item.overview ? !item.runtime.GetComponent<MeshFilter>().sharedMesh : !RuntimeRenderResourcesValid(item.runtime))))) rebuildRequested = true;
            if (rebuildRequested)
            {
                try { RebuildPreview(); }
                catch (Exception exception) { DisposePreview(); previewError = exception.Message; SetStatus("预览加载失败：" + previewError); }
            }
            if (preview == null) { GUI.Label(new Rect(20, 20, rect.width - 40, 70), previewError ?? "请选择体块资产或地图。", EditorStyles.wordWrappedLabel); return; }
            var rotation = Quaternion.Euler(orbit.x, orbit.y, 0);
            preview.camera.orthographic = !mapMode;
            preview.camera.orthographicSize = Mathf.Max(1, distance * .38f);
            preview.camera.fieldOfView = 45; preview.camera.aspect = rect.width / rect.height;
            preview.camera.transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
            Vector2 pointer = e.mousePosition;
            if (float.IsNaN(pointer.x) || float.IsNaN(pointer.y) || float.IsInfinity(pointer.x) || float.IsInfinity(pointer.y)) pointer = rect.center;
            Ray ray = preview.camera.ViewportPointToRay(new Vector3(pointer.x / rect.width, 1 - pointer.y / rect.height));
            bool inside = rect.Contains(e.mousePosition);
            var hit = Pick(ray, out var hitCell, out var hitNormal, out _);
            bool hasPlane = new Plane(Vector3.up, new Vector3(0, mapMode ? planeHeight : -.5f * (asset ? asset.cellSize : 1), 0)).Raycast(ray, out float enter);
            Vector3 planePoint = hasPlane ? ray.GetPoint(enter) : Vector3.zero;
            Vector3Int placeCell = hit != null ? hitCell + hitNormal : VoxelGeometry.Cell(planePoint / (asset ? asset.cellSize : 1));
            var mounting = hit != null ? (Vector3)hitNormal : Vector3.up;
            var angles = BlockPlacement.FaceRotation(mounting, brushTurns).eulerAngles / 90f; placementTurns = Vector3Int.RoundToInt(angles);
            if (!mapMode && hit == null) placeCell.y = 0;
            if (inside || e.type == EventType.MouseUp && strokeGroup >= 0) HandleViewportInput(e, ray, hit, hitCell, placeCell, hasPlane, planePoint);
            if (e.type == EventType.Repaint)
            {
                if (gridDirty || renderedRect != rect || renderedPivot != pivot || renderedOrbit != orbit || renderedDistance != distance || renderedMapMode != mapMode) renderDirty = true;
                foreach (var item in previewItems)
                {
                    if (item.instances != null && item.instances.Refresh()) renderDirty = true;
                    if (item.renderRevision != item.runtime.RenderRevision)
                    { item.renderRevision = item.runtime.RenderRevision; renderDirty = true; }
                }
                if (renderDirty || !renderedPreview)
                {
                    if (gridDirty) BuildGrid();
                    preview.BeginPreview(rect, GUIStyle.none);
                    try
                    {
                        if (grid && gridMaterial) preview.DrawMesh(grid, Matrix4x4.identity, gridMaterial, 0);
                        preview.Render(true);
                    }
                    finally { renderedPreview = preview.EndPreview(); }
                    renderedRect = rect; renderedPivot = pivot; renderedOrbit = orbit; renderedDistance = distance; renderedMapMode = mapMode; renderDirty = false;
                }
                GUI.DrawTexture(rect, renderedPreview, ScaleMode.StretchToFill, false);
                if (!string.IsNullOrEmpty(capturePath)) SavePreviewImage(renderedPreview);
                DrawOverlays(rect, inside, hit, hitCell, placeCell, hasPlane, planePoint);
            }
            int cells = 0;
            foreach (var item in previewItems) { cells += item.runtime.AliveCount;  }
            info.text = $"{(mapMode ? "地图" : "资产")}  |  {cells:N0} 格 · {previewItems.Count} 个资产\n中键旋转 · Shift 中键平移 · 滚轮缩放 · F 聚焦";
        }
        PreviewItem Pick(Ray ray, out Vector3Int cell, out Vector3Int normal, out float distanceHit)
        {
            PreviewItem found = null; cell = normal = Vector3Int.zero; distanceHit = float.PositiveInfinity;
            foreach (var item in previewItems)
                if (VoxelAuthoring.Pick(item.runtime, ray, out var nextCell, out var nextNormal, out float next) && next < distanceHit)
                { found = item; cell = nextCell; normal = nextNormal; distanceHit = next; }
            return found;
        }
        void HandleViewportInput(Event e, Ray ray, PreviewItem hit, Vector3Int hitCell, Vector3Int placeCell, bool hasPlane, Vector3 planePoint)
        {
            if (e.type == EventType.ScrollWheel || e.type == EventType.MouseDown && (e.button == 2 || e.alt)) EndStroke();
            if (e.type == EventType.ScrollWheel)
            { distance = Mathf.Clamp(distance * Mathf.Exp(e.delta.y * .055f), .5f, 30000); e.Use(); Repaint(); return; }
            if (e.type == EventType.MouseDrag && (e.button == 2 || e.alt && e.button == 0))
            {
                if (e.shift) pivot += (-preview.camera.transform.right * e.delta.x + preview.camera.transform.up * e.delta.y) * distance / Mathf.Max(100, viewport.contentRect.height);
                else { orbit.x = Mathf.Clamp(orbit.x + e.delta.y * .35f, -89.8f, 89.8f); orbit.y += e.delta.x * .35f; }
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.X || e.keyCode == KeyCode.R || e.keyCode == KeyCode.Delete) EndStroke();
                if (e.keyCode == KeyCode.F) { FrameView(); e.Use(); }
                else if (!mapMode && e.keyCode == KeyCode.X) { tool = tool == 1 ? 0 : 1; BuildToolbar(); e.Use(); }
                else if (!mapMode && e.keyCode == KeyCode.R) { brushTurns = (brushTurns + 1) % 4; BuildInspector(); Repaint(); e.Use(); }
                else if (mapMode && e.keyCode == KeyCode.R && selectedInstance >= 0) { var item = map.instances[selectedInstance]; MoveInstance(item, item.position, item.eulerAngles + Vector3.up * 90, item.scale); BuildInspector(); e.Use(); }
                else if (mapMode && e.keyCode == KeyCode.Delete) { DeleteInstance(); e.Use(); }
                else if (mapMode && (e.control || e.command) && e.keyCode == KeyCode.D) { DuplicateInstance(); e.Use(); }
            }
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                EndStroke();
                if (mapMode) BuildInspector(); e.Use(); return;
            }
            bool repeat = e.type == EventType.Repaint && brushRepeat.Held;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag && e.type != EventType.MouseMove && !repeat) return;
            if (e.type == EventType.MouseMove) { Repaint(); return; }
            if (e.button == 1 && e.type == EventType.MouseDown && mapMode)
            { selectedInstance = hit?.index ?? -1; BuildInspector(); viewport.Focus(); e.Use(); Repaint(); return; }
            if ((!repeat && e.button != 0) || e.alt) return;
            bool down = e.type == EventType.MouseDown;
            if (down)
            {
                viewport.Focus(); Undo.IncrementCurrentGroup(); strokeGroup = Undo.GetCurrentGroup(); painted.Clear(); GUIUtility.hotControl = viewportControl;
                if (!mapMode && tool <= 2) brushRepeat.Begin(EditorApplication.timeSinceStartup, brushRepeatDelay, brushRepeatInterval);
            }
            if (!mapMode && !down && !brushRepeat.TryRepeat(EditorApplication.timeSinceStartup)) { if (!repeat) e.Use(); return; }
            if (mapMode)
            {
                if (!map) return;
                if (tool == 0)
                {
                    if (down)
                    {
                        selectedInstance = hit?.index ?? -1; BuildInspector(); moving = hit != null;
                        if (moving)
                        {
                            var item = map.instances[selectedInstance]; var plane = new Plane(Vector3.up, item.position);
                            if (plane.Raycast(ray, out float travel)) moveOffset = item.position - ray.GetPoint(travel);
                        }
                    }
                    else if (moving && selectedInstance >= 0)
                    {
                        var item = map.instances[selectedInstance]; var plane = new Plane(Vector3.up, item.position);
                        if (plane.Raycast(ray, out float travel)) MoveInstance(item, Snap(ray.GetPoint(travel) + moveOffset), item.eulerAngles, item.scale);
                    }
                }
                else if (tool == 1 && down && hasPlane) PlaceInstance(Snap(planePoint));
            }
            else if (asset)
            {
                if (tool == 0 && (hit != null || hasPlane) && painted.Add(placeCell)) PaintCells(placeCell, 0);
                else if ((tool == 1 || tool == 2) && hit != null && painted.Add(hitCell)) { selectedCell = hitCell; PaintCells(hitCell, tool); }
            }
            if (!repeat) e.Use(); Repaint();
        }
        void BuildGrid()
        {
            gridDirty = false; if (grid) DestroyImmediate(grid);
            if (!gridMaterial)
            {
                gridMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
                gridMaterial.SetColor("_BaseColor", new Color(.13f, .18f, .22f));
            }
            float unit = mapMode ? 100 : asset ? asset.cellSize : 1;
            float extent = mapMode && map ? Mathf.Max(map.size.x, map.size.z) * .5f : Mathf.Max(10, asset ? asset.LocalBounds.extents.magnitude * 1.5f : 10);
            extent = Mathf.Ceil(extent / unit) * unit;
            float height = mapMode ? planeHeight : asset && asset.cells.Count > 0 ? asset.LocalBounds.min.y - unit * .02f : -.5f * unit; var vertices = new List<Vector3>(); var lines = new List<int>();
            int count = Mathf.Min(160, Mathf.CeilToInt(extent / unit));
            for (int i = -count; i <= count; i++)
            {
                vertices.Add(new Vector3(i * unit, height, -extent)); vertices.Add(new Vector3(i * unit, height, extent));
                vertices.Add(new Vector3(-extent, height, i * unit)); vertices.Add(new Vector3(extent, height, i * unit));
            }
            for (int i = 0; i < vertices.Count; i++) lines.Add(i);
            grid = new Mesh { name = "Map studio reference grid", hideFlags = HideFlags.HideAndDontSave }; grid.SetVertices(vertices); grid.SetIndices(lines, MeshTopology.Lines, 0); grid.RecalculateBounds();
        }
        void DrawOverlays(Rect rect, bool inside, PreviewItem hit, Vector3Int hitCell, Vector3Int placeCell, bool hasPlane, Vector3 planePoint)
        {
            Handles.BeginGUI(); GUI.BeginClip(rect);
            if (mapMode && map)
            {
                WireBox(new Bounds(Vector3.zero, map.size), Matrix4x4.identity, new Color(.4f, .52f, .6f, .3f));
                foreach (var item in previewItems) if (item.index == selectedInstance) WireBox(item.runtime.LocalBounds, item.runtime.transform.localToWorldMatrix, new Color(.5f, .85f, .9f));
                Marker(map.playerSpawn, new Color(.4f, 1, .75f), "玩家");
                for (int i = 0; i < map.enemySpawns.Count; i++) Marker(map.enemySpawns[i], new Color(1, .55f, .43f), "敌 " + (i + 1));
                if (inside && tool == 1 && hasPlane && asset) WireBox(asset.LocalBounds, Matrix4x4.TRS(Snap(planePoint), Quaternion.identity, Vector3.one), new Color(.4f, 1, .8f));
            }
            else if (asset && inside)
            {
                if (tool == 0 && (hasPlane || hit != null))
                {
                    WireBox(new Bounds((Vector3)placeCell * asset.cellSize, (Vector3)brushSize * asset.cellSize), Matrix4x4.identity, new Color(.4f, 1, .8f, .35f));
                    WireShape(placeCell);
                }
                else if (hit != null) WireBox(new Bounds((Vector3)hitCell * asset.cellSize, (Vector3)brushSize * asset.cellSize), Matrix4x4.identity, tool == 2 ? new Color(1, .8f, .4f) : new Color(1, .4f, .35f));
            }
            GUI.EndClip(); Handles.EndGUI();
        }
        void WireBox(Bounds bounds, Matrix4x4 matrix, Color color)
        {
            var points = new Vector3[8]; Vector3 lo = bounds.min, hi = bounds.max;
            for (int i = 0; i < 8; i++) points[i] = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
            Handles.color = color;
            for (int i = 0; i < 8; i++) for (int axis = 0; axis < 3; axis++) if ((i & 1 << axis) == 0)
            {
                Vector3 a = preview.camera.WorldToViewportPoint(points[i]), b = preview.camera.WorldToViewportPoint(points[i | 1 << axis]);
                if (a.z <= 0 || b.z <= 0) continue;
                Handles.DrawAAPolyLine(1.7f, new Vector3(a.x * viewport.contentRect.width, (1 - a.y) * viewport.contentRect.height), new Vector3(b.x * viewport.contentRect.width, (1 - b.y) * viewport.contentRect.height));
            }
        }
        void WireShape(Vector3Int cell)
        {
            var points = BlockGeometry.Points(brushShape); var edges = BlockGeometry.Edges(brushShape);
            var rotation = Quaternion.Euler((Vector3)placementTurns * 90); Handles.color = new Color(.5f, 1, .85f);
            for (int i = 0; i < edges.Length; i += 2)
            {
                var a = preview.camera.WorldToViewportPoint(((Vector3)cell + rotation * points[edges[i]]) * asset.cellSize);
                var b = preview.camera.WorldToViewportPoint(((Vector3)cell + rotation * points[edges[i + 1]]) * asset.cellSize);
                if (a.z <= 0 || b.z <= 0) continue;
                Handles.DrawAAPolyLine(2, new Vector3(a.x * viewport.contentRect.width, (1 - a.y) * viewport.contentRect.height), new Vector3(b.x * viewport.contentRect.width, (1 - b.y) * viewport.contentRect.height));
            }
        }
        void Marker(Vector3 point, Color color, string label)
        {
            var p = preview.camera.WorldToViewportPoint(point); if (p.z <= 0) return;
            var center = new Vector2(p.x * viewport.contentRect.width, (1 - p.y) * viewport.contentRect.height);
            Handles.color = color; Handles.DrawAAPolyLine(2, center + Vector2.left * 6, center + Vector2.up * 6, center + Vector2.right * 6, center + Vector2.down * 6, center + Vector2.left * 6);
            GUI.Label(new Rect(center.x + 8, center.y - 9, 90, 20), label, EditorStyles.whiteMiniLabel);
        }
        static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 x = matrix.MultiplyVector(Vector3.right * bounds.extents.x), y = matrix.MultiplyVector(Vector3.up * bounds.extents.y), z = matrix.MultiplyVector(Vector3.forward * bounds.extents.z);
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), 2 * new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
        }
        Vector3 Snap(Vector3 point) => new Vector3(Mathf.Round(point.x / snap), Mathf.Round(point.y / snap), Mathf.Round(point.z / snap)) * snap;
        void SavePreviewImage(Texture texture)
        {
            string path = capturePath; capturePath = null; var target = texture as RenderTexture;
            if (!target) return; var active = RenderTexture.active; var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                if (QualitySettings.activeColorSpace == ColorSpace.Linear && !target.sRGB)
                { var colors = pixels.GetPixels(); for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma; pixels.SetPixels(colors); pixels.Apply(); }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = active; DestroyImmediate(pixels); }
        }
    }
}
