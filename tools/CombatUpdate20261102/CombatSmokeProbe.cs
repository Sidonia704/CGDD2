using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    /// <summary>Read live range-combat state and capture an optional screenshot; no assets or scenes are changed.</summary>
    public static class CombatSmokeProbe
    {
        public static string Read(bool captureScreenshot = true)
        {
            var world = UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
            if (!world) return "No active RangeCombatWorld found. Enter Play Mode in the initialized FlightRange scene, then call Read().";

            var sb = new StringBuilder(2048);
            sb.AppendLine("Combat smoke probe (read only)");
            sb.AppendLine("Scene: " + world.gameObject.scene.name + " | mode: " + world.BotMode + " | actors: " + world.Actors.Count + " | bots: " + world.Bots.Count);
            AppendActor(sb, "PLAYER", world, world.Player);
            if (world.Player != null && world.Player.ship)
            {
                int launchers = 0;
                FlareDefinition flare = null;
                foreach (var part in world.Player.ship.Parts)
                {
                    var module = world.Player.ship.Tuning ? world.Player.ship.Tuning.GetDefinition(part.record.shape) : null;
                    if (module && module.countermeasure)
                    {
                        launchers++;
                        if (!flare) flare = module.countermeasure;
                    }
                }
                sb.Append("Countermeasures: fitted=").Append(launchers);
                if (flare)
                    sb.Append(" burst=").Append(flare.burstCount)
                        .Append(" cooldown=").Append(flare.cooldownSeconds.ToString("0.##"))
                        .Append("s decoyChance=").Append((flare.decoyChance * 100).ToString("0"))
                        .Append("% radius=").Append(flare.decoyRadius.ToString("0.##"));
                if (world.Player.countermeasures)
                    sb.Append(" ready=").Append(world.Player.countermeasures.ReadyCount).Append('/').Append(world.Player.countermeasures.LauncherCount)
                        .Append(" nextReady=").Append(world.Player.countermeasures.NextReadySeconds.ToString("0.0")).Append('s');
                sb.AppendLine();
            }

            int detected = 0;
            if (world.Targeting) detected = world.Targeting.DetectedTargets.Count;
            sb.Append("Sensors: ").Append(world.Sensors != null ? "initialized" : "missing")
                .Append(" | detected by player: ").Append(detected)
                .Append(" | locks on player: ").Append(world.Sensors != null && world.Player != null ? world.Sensors.CountLocksOn(world.Player.ship) : 0)
                .AppendLine();
            if (world.Targeting)
                sb.Append("Player lock: ").Append(world.Targeting.HasTarget ? "locked" : "none")
                    .Append(world.Targeting.HasTarget ? " | " + world.Targeting.LockedTarget.name + " | distance=" + world.Targeting.TargetDistance.ToString("0.0") + "m" : "")
                    .AppendLine();

            for (int i = 0; i < world.Bots.Count; i++) AppendActor(sb, "BOT " + (i + 1), world, world.Bots[i]);

            var outline = UnityEngine.Object.FindFirstObjectByType<TargetSilhouetteOutline>();
            if (outline)
                sb.Append("Outline: enabled=").Append(outline.enabled)
                    .Append(" visible=").Append(outline.OutlineVisible)
                    .Append(" cachedDraws=").Append(outline.CachedDrawCount)
                    .Append(" submittedDraws=").Append(outline.SubmittedMaskDraws).AppendLine();
            else sb.AppendLine("Outline: TargetSilhouetteOutline component missing.");

            var markers = UnityEngine.Object.FindFirstObjectByType<TargetAimGraphic>();
            sb.AppendLine("Target marker: " + (markers ? "TargetAimGraphic present; edgeVisible=" + markers.EdgeIndicatorVisible : "TargetAimGraphic missing"));
            sb.AppendLine("Console: " + ReadConsoleCount());
            if (captureScreenshot && Application.isPlaying)
            {
                string screenshot = CaptureScreenshot("Read");
                if (!string.IsNullOrEmpty(screenshot)) sb.AppendLine("Screenshot: " + screenshot);
            }
            return sb.ToString();
        }

        /// <summary>Lock the closest visible, player-detected bot as an F-key tap would, then queue a screenshot.</summary>
        public static string Exercise()
        {
            var world = UnityEngine.Object.FindFirstObjectByType<RangeCombatWorld>();
            if (!world || !Application.isPlaying) return "Exercise requires Play Mode with an initialized RangeCombatWorld.";
            if (world.Player == null || !world.Player.ship || !world.Player.ship.IsAlive || !world.Player.flight || !world.Targeting || world.Sensors == null)
                return "Exercise stopped: player, target lock, or sensors are not ready.";
            var camera = Camera.main;
            if (!camera && world.Player.flight)
                camera = typeof(ShipFlightController).GetField("viewCamera", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(world.Player.flight) as Camera;
            if (!camera) return "Exercise stopped: no range camera was found.";

            var forwardScreen = AimProjection.ScreenPoint(camera, world.Player.flight.Forward);
            FlightShipRuntime chosen = null;
            float best = float.PositiveInfinity;
            foreach (var target in world.Targeting.DetectedTargets)
            {
                if (!target || !target.IsAlive) continue;
                var screen = camera.WorldToScreenPoint(target.CorePosition);
                if (screen.z <= 0 || !camera.pixelRect.Contains(new Vector2(screen.x, screen.y))) continue;
                float distance = ((Vector2)screen - (Vector2)forwardScreen).sqrMagnitude;
                if (distance < best) { best = distance; chosen = target; }
            }
            if (!chosen) return "Exercise stopped: no live, detected bot is currently visible; player rotation and target state were left alone.";

            // TargetLockSystem selects near its virtual cursor. Temporarily align that cursor, invoke the same public toggle used by F,
            // then restore the previous steering input before a physics step can consume it. The ship's body rotation is untouched.
            var aimField = typeof(ShipFlightController).GetField("aimInput", BindingFlags.Instance | BindingFlags.NonPublic);
            if (aimField == null) return "Exercise stopped: could not locate the virtual aim input.";
            var previousAim = (Vector2)aimField.GetValue(world.Player.flight);
            var previousRotation = world.Player.body ? world.Player.body.rotation : world.Player.ship.transform.rotation;
            var chosenScreen = camera.WorldToScreenPoint(chosen.CorePosition);
            float radius = Mathf.Max(1, world.Player.flight.AimRadiusPixels);
            var alignedAim = ((Vector2)chosenScreen - (Vector2)forwardScreen) / radius;
            try
            {
                world.Targeting.Clear();
                aimField.SetValue(world.Player.flight, alignedAim);
                world.Targeting.ToggleLock();
            }
            finally { aimField.SetValue(world.Player.flight, previousAim); }

            if (!world.Targeting.HasTarget || world.Targeting.LockedTarget != chosen)
                return "Exercise could not lock the selected bot using the normal target-selection API.\n" + Read(false);

            var outline = UnityEngine.Object.FindFirstObjectByType<TargetSilhouetteOutline>();
            if (outline)
            {
                if (!outline.enabled) outline.enabled = true;
                // Bring the normal outline pass forward so its cached draw count/state are readable immediately.
                typeof(TargetSilhouetteOutline).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(outline, null);
            }
            string screenshot = CaptureScreenshot("LockedTarget");
            var currentRotation = world.Player.body ? world.Player.body.rotation : world.Player.ship.transform.rotation;
            bool rotationUnchanged = Quaternion.Angle(previousRotation, currentRotation) < .001f;
            return "Exercise: F-equivalent lock acquired on " + chosen.name + " (" + Vector3.Distance(world.Player.ship.CorePhysicsPosition, chosen.CorePhysicsPosition).ToString("0.0") + "m); player rotation unchanged=" + rotationUnchanged + ".\n" +
                (string.IsNullOrEmpty(screenshot) ? "Screenshot capture unavailable." : "Screenshot queued: " + screenshot) + "\n" + Read(false);
        }

        static void AppendActor(StringBuilder sb, string label, RangeCombatWorld world, RangeCombatWorld.Actor actor)
        {
            if (actor == null || !actor.ship)
            {
                sb.AppendLine(label + ": missing");
                return;
            }
            sb.Append(label).Append(": ").Append(actor.ship.IsAlive ? "alive" : "dead")
                .Append(" | name=").Append(string.IsNullOrEmpty(actor.blueprintName) ? actor.ship.name : actor.blueprintName)
                .Append(" | life=").Append(actor.ship.LifeId)
                .Append(" | parts=").Append(actor.ship.Parts.Count)
                .Append(" | position=").Append(actor.body ? actor.body.position.ToString("F1") : actor.ship.CorePhysicsPosition.ToString("F1"))
                .Append(" | rotation=").Append(actor.body ? actor.body.rotation.eulerAngles.ToString("F1") : actor.ship.transform.eulerAngles.ToString("F1"));
            if (actor.ship.Team != 0)
                sb.Append(" | detectedByPlayer=").Append(world.Sensors != null && world.Player != null && world.Player.ship && world.Sensors.IsDetected(world.Player.ship, actor.ship));
            sb.AppendLine();
        }

        static string CaptureScreenshot(string label)
        {
            try
            {
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Documentation", "CombatUpdate20261102", "Probe"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "Combat-" + label + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                ScreenCapture.CaptureScreenshot(path);
                return path;
            }
            catch (Exception error) { return "capture failed: " + error.GetType().Name + ": " + error.Message; }
        }

        static string ReadConsoleCount()
        {
            // The Editor Console API is internal. Read only its public-to-reflection count method; never clear or alter log entries.
            try
            {
                Type entries = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    entries = assembly.GetType("UnityEditor.LogEntries");
                    if (entries != null) break;
                }
                if (entries == null) return "unavailable outside the Editor";
                MethodInfo method = null;
                foreach (var candidate in entries.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (candidate.Name == "GetCount" && candidate.GetParameters().Length == 0) { method = candidate; break; }
                return method == null ? "Editor API unavailable" : method.Invoke(null, null).ToString() + " entries (open Console for severity/details)";
            }
            catch (Exception error) { return "unavailable: " + error.GetType().Name; }
        }
    }
}
