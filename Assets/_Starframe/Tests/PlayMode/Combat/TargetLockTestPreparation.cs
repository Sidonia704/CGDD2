using System.Collections;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    /// <summary>Point the real virtual cursor at a discovered marker before exercising F in range fixtures.</summary>
    static class TargetLockTestPreparation
    {
        public static IEnumerator WaitForWorld(float timeoutSeconds = 120)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                var world = Object.FindFirstObjectByType<RangeCombatWorld>();
                if (world && world.Player != null && world.Sensors != null && world.Targeting)
                {
                    // Bootstrap completion can precede the chase camera's first LateUpdate in this frame.
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("The range did not finish scenery preparation and initialize combat before the fixture timeout.");
        }

        public static void PointAt(RangeCombatWorld world, FlightShipRuntime target = null)
        {
            Assert.That(world, Is.Not.Null);
            Assert.That(world.Sensors, Is.Not.Null);
            target = target ? target : world.Bot.ship;
            world.Sensors.Update(world.Actors, 0);
            Assert.That(world.Sensors.IsDetected(world.Player.ship, target), Is.True);
            var hud = Object.FindFirstObjectByType<RangeHUD>();
            Assert.That(hud, Is.Not.Null);
            var camera = hud.WorldCamera;
            Assert.That(camera, Is.Not.Null);
            var marker = camera.WorldToScreenPoint(target.CorePosition);
            string projection = $"marker={marker:R}; targetCore={target.CorePosition:R}; targetPhysicsCore={target.CorePhysicsPosition:R}; playerCore={world.Player.ship.CorePosition:R}; camera={camera.transform.position:R}; cameraRotation={camera.transform.rotation:R}; pixelRect={camera.pixelRect}";
            Assert.That(marker.z, Is.GreaterThan(0), projection);
            Assert.That(camera.pixelRect.Contains((Vector2)marker), Is.True, "The fixture's initial target must be on screen before acquiring. " + projection);
            var flight = world.Player.flight;
            Vector2 offset = (Vector2)marker - (Vector2)AimProjection.ScreenPoint(camera, flight.Forward);
            Vector2 input = offset / flight.AimRadiusPixels;
            Assert.That(input.magnitude, Is.LessThanOrEqualTo(1), "Choose a fixture target reachable by the ordinary virtual aiming stick.");
            flight.SetExternalInput(Vector3.zero, input);
        }
    }
}
