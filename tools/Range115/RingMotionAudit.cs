using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Flight;

// External Editor eval utility. Run only in an already-loaded runtime mission.
public static class RingMotionAudit
{
    const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const string Output = "tools/Range115/ring-motion-audit.json";
    static readonly Vector3 Start = new Vector3(0, -560, -1140);
    static readonly Vector3 End = new Vector3(4300, -560, 15000);
    static Camera view;
    static FlightChaseCamera chase;
    static RangeAsteroidBackdrop backdrop;
    static Vector3 savedPosition;
    static Quaternion savedRotation;
    static bool savedChase, running;
    static double watchdogStart, motionStart;
    static int lastFrame;
    static ProfilerRecorder cpu, gpu;
    static Report report;
    static readonly Dictionary<long, Source> sources = new Dictionary<long, Source>();
    static readonly List<BatchView> batches = new List<BatchView>();
    static readonly List<GroupView> groups = new List<GroupView>();
    static readonly HashSet<long> submitted = new HashSet<long>();
    static readonly List<double> cpuTimes = new List<double>(), gpuTimes = new List<double>();
    static readonly Plane[] planes = new Plane[6];

    [Serializable] public sealed class Report
    {
        public bool done, passed;
        public string reason, scope = "Editor rendering audit; CPU includes this audit, not a standalone performance claim.";
        public float durationSeconds, minimumPathProgress = 1, maximumPathProgress, finalPathProgress;
        public int sourceRocks, sampledFrames, missingIds, duplicateIds, unknownIds, changedMatrices;
        public int invalidMatrices, nonUnitMatrices, invalidLods, lodReversals, wrongLodGroups;
        public int outsideFrustumSubmissions, triangleDiagnosticMismatches, drawDiagnosticMismatches;
        public int cpuSampleCount, gpuSampleCount;
        public double cpuMainMedianMs = -1, gpuMedianMs = -1;
        public bool gpuAvailable;
        public Vector3 pathStart, pathEnd;
        public float negativeBandClearGap, positiveBandClearGap;
        public List<string> errors = new List<string>();
        public List<FrameRow> frames = new List<FrameRow>();
        public List<LayoutRow> segments = new List<LayoutRow>();
    }
    [Serializable] public sealed class FrameRow
    {
        public int frame, expected, submitted, triangles, draws, missing, lodChanges;
        public float elapsed, pathProgress;
        public Vector3 camera;
        public double cpuMs = -1, gpuMs = -1;
    }
    [Serializable] public sealed class LayoutRow
    {
        public int band, segmentStart, count;
        public float minX = float.MaxValue, maxX = float.MinValue;
        public float minY = float.MaxValue, maxY = float.MinValue;
        public float minZ = float.MaxValue, maxZ = float.MinValue, largestCentreGapZ;
        public float minSphereX = float.MaxValue, maxSphereX = float.MinValue;
        public bool intersectsIntentionalMapOpening;
    }
    sealed class Source
    {
        public Matrix4x4 matrix;
        public Vector3 position;
        public float radius;
        public int lastLod = -1, beforeSwitchLod = -1;
        public double lastSwitchTime = -100;
        public float lastSwitchDistance;
    }
    sealed class BatchView
    {
        public object value;
        public int index, band, segment;
        public Matrix4x4[] matrices;
        public float[] radii;
        public int[] lods;
        public Array lodGroups;
    }
    sealed class GroupView
    {
        public object value;
        public FieldInfo count;
        public int[] batchIds, instanceIds;
        public Matrix4x4[] matrices;
        public int triangles, lod;
    }
    static object Get(object value, string name) { return value.GetType().GetField(name, Fields).GetValue(value); }
    static long Id(int batch, int instance) { return ((long)batch << 32) | (uint)instance; }
    static void Error(string text) { if (report.errors.Count < 80) report.errors.Add(text); }

    public static string Begin()
    {
        if (running) return "Ring motion audit already running.";
        if (!Application.isPlaying) throw new InvalidOperationException("Start the loaded Saturn mission in Play mode first.");
        backdrop = UnityEngine.Object.FindFirstObjectByType<RangeAsteroidBackdrop>();
        if (!backdrop || !backdrop.backdropEnabled) throw new InvalidOperationException("Enabled runtime backdrop required.");
        view = (Camera)Get(backdrop, "targetCamera");
        if (!view) throw new InvalidOperationException("Backdrop camera required.");
        report = new Report { pathStart = Start, pathEnd = End };
        sources.Clear(); batches.Clear(); groups.Clear(); cpuTimes.Clear(); gpuTimes.Clear();
        foreach (object item in (IEnumerable)Get(backdrop, "batches"))
        {
            var b = new BatchView { value = item, index = (int)Get(item, "index"), band = (int)Get(item, "band"),
                segment = (int)Get(item, "segmentStart"), matrices = (Matrix4x4[])Get(item, "matrices"),
                radii = (float[])Get(item, "radii"), lods = (int[])Get(item, "lastLods"), lodGroups = (Array)Get(item, "renderGroups") };
            batches.Add(b);
            for (int i = 0; i < b.matrices.Length; i++)
            {
                long id = Id(b.index, i);
                if (sources.ContainsKey(id)) throw new InvalidOperationException("Duplicate source ID " + id);
                sources.Add(id, new Source { matrix = b.matrices[i], position = b.matrices[i].GetColumn(3), radius = b.radii[i] });
            }
        }
        if (sources.Count == 0) throw new InvalidOperationException("Backdrop has not built its permanent rocks yet.");
        foreach (object item in (IEnumerable)Get(backdrop, "renderGroups"))
            groups.Add(new GroupView { value = item, count = item.GetType().GetField("submittedCount", Fields),
                batchIds = (int[])Get(item, "submittedSourceIndices"), instanceIds = (int[])Get(item, "submittedInstanceIndices"),
                matrices = (Matrix4x4[])Get(item, "submittedMatrices"), triangles = (int)Get(item, "triangles"), lod = (int)Get(item, "lod") });
        report.sourceRocks = sources.Count;
        BuildLayout();
        savedPosition = view.transform.position; savedRotation = view.transform.rotation;
        chase = view.GetComponent<FlightChaseCamera>(); savedChase = chase && chase.enabled;
        if (chase) chase.enabled = false;
        watchdogStart = EditorApplication.timeSinceStartup; motionStart = -1; lastFrame = -1;
        running = true;
        try
        {
            cpu = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            try { gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1); } catch { gpu = default; }
            SetPose(0);
            // Observe after rendering, when the backdrop's LateUpdate submission matches this exact camera pose.
            RenderPipelineManager.endCameraRendering += SampleRenderedFrame;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Finish("Setup failed: " + e.Message); throw; }
        return "20-second out-and-back camera audit started; 60-second watchdog; camera and chase restore automatically.";
    }

    static void Tick()
    {
        if (!running) return;
        if (!Application.isPlaying || !view || !backdrop) { Finish("Runtime exited or audit object destroyed."); return; }
        if (EditorApplication.timeSinceStartup - watchdogStart > 60) Finish("60-second watchdog expired; rendering did not complete the path.");
    }
    static void SetPose(float phase)
    {
        float progress = Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI);
        progress *= progress;
        view.transform.SetPositionAndRotation(Vector3.Lerp(Start, End, progress),
            Quaternion.LookRotation(new Vector3(0.06f, -0.10f, 1), Vector3.up));
    }
    static void SampleRenderedFrame(ScriptableRenderContext context, Camera camera)
    {
        if (!running || camera != view || Time.frameCount == lastFrame) return;
        try
        {
            lastFrame = Time.frameCount;
            double now = EditorApplication.timeSinceStartup;
            if (motionStart < 0) motionStart = now;
            float elapsed = (float)(now - motionStart);
            float progress = Mathf.Clamp01(Vector3.Dot(view.transform.position - Start, End - Start) / (End - Start).sqrMagnitude);
            report.minimumPathProgress = Mathf.Min(report.minimumPathProgress, progress);
            report.maximumPathProgress = Mathf.Max(report.maximumPathProgress, progress);
            report.finalPathProgress = progress;
            GeometryUtility.CalculateFrustumPlanes(view, planes);
            var row = new FrameRow { frame = lastFrame, elapsed = elapsed, camera = view.transform.position, pathProgress = progress };
            submitted.Clear();
            foreach (var group in groups)
            {
                int count = (int)group.count.GetValue(group.value);
                row.draws += (count + 510) / 511;
                row.triangles += count * group.triangles;
                for (int j = 0; j < count; j++)
                {
                    long id = Id(group.batchIds[j], group.instanceIds[j]);
                    if (!submitted.Add(id)) { report.duplicateIds++; Error("Duplicate submission " + id + " frame " + lastFrame); }
                    Source source;
                    if (!sources.TryGetValue(id, out source)) { report.unknownIds++; Error("Unknown submitted ID " + id); continue; }
                    if (!Same(source.matrix, group.matrices[j])) { report.changedMatrices++; Error("Submitted matrix moved for " + id); }
                    if (!InFrustum(source.position, source.radius)) report.outsideFrustumSubmissions++;
                    var batch = batches[group.batchIds[j]];
                    int lod = batch.lods[group.instanceIds[j]];
                    if (lod >= 0 && lod < batch.lodGroups.Length && !ReferenceEquals(batch.lodGroups.GetValue(lod), group.value)) report.wrongLodGroups++;
                }
            }
            foreach (var batch in batches)
                for (int i = 0; i < batch.matrices.Length; i++)
                {
                    long id = Id(batch.index, i); Source source = sources[id]; Matrix4x4 matrix = batch.matrices[i];
                    if (!Same(source.matrix, matrix)) { report.changedMatrices++; Error("Source matrix moved for " + id); }
                    bool finite = true; for (int k = 0; k < 16; k++) finite &= !float.IsNaN(matrix[k]) && !float.IsInfinity(matrix[k]);
                    if (!finite || float.IsNaN(batch.radii[i]) || float.IsInfinity(batch.radii[i]) || batch.radii[i] <= 0) report.invalidMatrices++;
                    bool unit = true; for (int axis = 0; axis < 3; axis++) unit &= Mathf.Abs(matrix.GetColumn(axis).magnitude - 1) < 0.0001f;
                    if (!unit) report.nonUnitMatrices++;
                    if (InFrustum(source.position, source.radius))
                    {
                        row.expected++;
                        if (!submitted.Contains(id)) { row.missing++; report.missingIds++; Error("Frustum ID missing " + id + " frame " + lastFrame); }
                    }
                    int lod = batch.lods[i];
                    if (lod < 0 || lod > 2) report.invalidLods++;
                    if (source.lastLod >= 0 && lod != source.lastLod)
                    {
                        row.lodChanges++;
                        float distance = Vector3.Distance(source.position, view.transform.position);
                        // Detect rapid inverse switches without the travel needed to cross the hysteresis band.
                        if (lod == source.beforeSwitchLod && now - source.lastSwitchTime < 0.25 && Mathf.Abs(distance - source.lastSwitchDistance) < 300)
                        { report.lodReversals++; Error("Rapid LOD reversal " + id); }
                        source.beforeSwitchLod = source.lastLod; source.lastSwitchTime = now; source.lastSwitchDistance = distance;
                    }
                    source.lastLod = lod;
                }
            row.submitted = submitted.Count;
            if (row.triangles != backdrop.estimatedTriangles) report.triangleDiagnosticMismatches++;
            if (row.draws != backdrop.visibleBatchCount) report.drawDiagnosticMismatches++;
            if (report.sampledFrames > 5)
            {
                if (cpu.Valid && cpu.LastValue > 0) { row.cpuMs = cpu.LastValue / 1e6; cpuTimes.Add(row.cpuMs); }
                if (gpu.Valid && gpu.LastValue > 0) { row.gpuMs = gpu.LastValue / 1e6; gpuTimes.Add(row.gpuMs); }
            }
            report.frames.Add(row); report.sampledFrames++;
            if (elapsed >= 20) { Finish("Completed continuous camera path."); return; }
            SetPose(Mathf.Clamp01(elapsed / 20));
        }
        catch (Exception e) { Error(e.ToString()); Finish("Audit exception: " + e.Message); }
    }
    static bool Same(Matrix4x4 a, Matrix4x4 b) { for (int i = 0; i < 16; i++) if (a[i] != b[i]) return false; return true; }
    static bool InFrustum(Vector3 position, float radius)
    { for (int i = 0; i < planes.Length; i++) if (planes[i].GetDistanceToPoint(position) < -radius) return false; return true; }

    static void BuildLayout()
    {
        var rows = new SortedDictionary<string, LayoutRow>();
        var zValues = new Dictionary<string, List<float>>();
        for (int band = -1; band <= 1; band++)
            for (int segment = -25000; segment < 25000; segment += 2000)
            {
                string key = band + ":" + segment;
                rows[key] = new LayoutRow { band = band, segmentStart = segment,
                    intersectsIntentionalMapOpening = band == 0 && segment < 1789 && segment + 2000 > -1789 };
                zValues[key] = new List<float>();
            }
        float centreLeft = float.MaxValue, centreRight = float.MinValue, negativeInner = float.MinValue, positiveInner = float.MaxValue;
        foreach (var b in batches)
        {
            string key = b.band + ":" + b.segment;
            if (!rows.ContainsKey(key)) { rows[key] = new LayoutRow { band = b.band, segmentStart = b.segment,
                intersectsIntentionalMapOpening = b.band == 0 && b.segment < 1789 && b.segment + 2000 > -1789 }; zValues[key] = new List<float>(); }
            var r = rows[key];
            for (int i = 0; i < b.matrices.Length; i++)
            {
                Vector3 p = b.matrices[i].GetColumn(3); float radius = b.radii[i]; r.count++;
                r.minX = Mathf.Min(r.minX, p.x); r.maxX = Mathf.Max(r.maxX, p.x);
                r.minY = Mathf.Min(r.minY, p.y); r.maxY = Mathf.Max(r.maxY, p.y);
                r.minZ = Mathf.Min(r.minZ, p.z); r.maxZ = Mathf.Max(r.maxZ, p.z);
                r.minSphereX = Mathf.Min(r.minSphereX, p.x - radius); r.maxSphereX = Mathf.Max(r.maxSphereX, p.x + radius);
                zValues[key].Add(p.z);
                if (b.band == 0) { centreLeft = Mathf.Min(centreLeft, p.x - radius); centreRight = Mathf.Max(centreRight, p.x + radius); }
                else if (b.band < 0) negativeInner = Mathf.Max(negativeInner, p.x + radius);
                else positiveInner = Mathf.Min(positiveInner, p.x - radius);
            }
        }
        foreach (var pair in rows)
        {
            var z = zValues[pair.Key]; z.Sort();
            for (int i = 1; i < z.Count; i++) pair.Value.largestCentreGapZ = Mathf.Max(pair.Value.largestCentreGapZ, z[i] - z[i - 1]);
            if (pair.Value.count == 0)
            {
                pair.Value.minX = pair.Value.maxX = pair.Value.minY = pair.Value.maxY = pair.Value.minZ = pair.Value.maxZ = 0;
                pair.Value.minSphereX = pair.Value.maxSphereX = 0;
            }
            report.segments.Add(pair.Value);
        }
        report.negativeBandClearGap = centreLeft - negativeInner; report.positiveBandClearGap = positiveInner - centreRight;
    }
    static double Median(List<double> values)
    { if (values.Count == 0) return -1; values.Sort(); int mid = values.Count / 2; return values.Count % 2 == 0 ? (values[mid - 1] + values[mid]) * 0.5 : values[mid]; }
    static void Finish(string reason)
    {
        if (!running) return;
        running = false; EditorApplication.update -= Tick; RenderPipelineManager.endCameraRendering -= SampleRenderedFrame;
        try { if (view) view.transform.SetPositionAndRotation(savedPosition, savedRotation); if (chase) chase.enabled = savedChase; }
        finally { cpu.Dispose(); gpu.Dispose(); }
        report.done = true; report.reason = reason;
        report.durationSeconds = motionStart < 0 ? 0 : (float)(EditorApplication.timeSinceStartup - motionStart);
        report.cpuSampleCount = cpuTimes.Count; report.gpuSampleCount = gpuTimes.Count;
        report.cpuMainMedianMs = Median(cpuTimes); report.gpuMedianMs = Median(gpuTimes); report.gpuAvailable = gpuTimes.Count > 0;
        report.passed = reason == "Completed continuous camera path." && report.sampledFrames >= 10 && report.maximumPathProgress > 0.95f && report.finalPathProgress < 0.05f
            && report.missingIds == 0 && report.duplicateIds == 0 && report.unknownIds == 0 && report.changedMatrices == 0
            && report.invalidMatrices == 0 && report.nonUnitMatrices == 0 && report.invalidLods == 0 && report.lodReversals == 0
            && report.wrongLodGroups == 0 && report.outsideFrustumSubmissions == 0 && report.triangleDiagnosticMismatches == 0 && report.drawDiagnosticMismatches == 0;
        Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, JsonUtility.ToJson(report, true));
    }
    public static string Read()
    {
        if (running) return "Running: " + report.sampledFrames + " rendered frames; " + (EditorApplication.timeSinceStartup - watchdogStart).ToString("F1") + " seconds.";
        return report != null ? JsonUtility.ToJson(report, true) : File.Exists(Output) ? File.ReadAllText(Output) : "Not started.";
    }
    public static void Cancel() { Finish("Cancelled by caller."); }
}
