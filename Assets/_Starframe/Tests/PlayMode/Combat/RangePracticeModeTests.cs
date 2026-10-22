using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class RangePracticeModeTests
    {
        bool hadRoster, hadMode;
        string savedRoster;
        int savedMode;

        [UnitySetUp] public IEnumerator SetUp()
        {
            hadRoster = PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);
            savedRoster = PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            hadMode = PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey);
            savedMode = PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, "{\"entries\":[]}");
            RangePracticeSettings.Mode = RangeBotMode.Stationary;
            FlightSession.Begin(FlightRangeBootstrap.DefaultShip(), true);
            yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);
            yield return null;
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            if (hadRoster) PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey, savedRoster);
            else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            if (hadMode) PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey, savedMode);
            else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);
            PlayerPrefs.Save();
            FlightSession.Return();
            yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);
            yield return null;
        }

        [UnityTest] public IEnumerator StationaryAndFlyOnlyModesRespectTheirDrillAndCombatUsesRealAcceleration()
        {
            var world = Object.FindFirstObjectByType<RangeCombatWorld>();
            world.Player.flight.SetExternalInput(Vector3.zero, Vector2.zero);
            world.SpawnTargets(FlightRangeBootstrap.DefaultShip(), 1);
            var bot = world.Bot;
            Vector3 start = bot.body.position;
            yield return new WaitForSeconds(.5f);
            Assert.That(world.BotMode, Is.EqualTo(RangeBotMode.Stationary));
            Assert.That(Vector3.Distance(bot.body.position, start), Is.LessThan(.01f));
            Assert.That(bot.weapons.ShotsFired, Is.Zero);
            Assert.That(bot.flight.RequestedBoost, Is.False);

            world.SetBotMode(RangeBotMode.FlyOnly, false);
            bool requestedBoost = false;
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForSeconds(.1f);
                requestedBoost |= bot.flight.RequestedBoost;
                Assert.That(bot.weapons.FireRequested, Is.False);
            }
            Assert.That(Vector3.Distance(bot.body.position, start), Is.GreaterThan(1));
            Assert.That(bot.weapons.ShotsFired, Is.Zero);
            Assert.That(requestedBoost, Is.True);
            Assert.That(bot.flight.CurrentSpeed, Is.LessThanOrEqualTo(bot.ship.Performance.maxSpeed + .01f));

            world.SetBotMode(RangeBotMode.Combat, false);
            world.ResetTargets();
            yield return new WaitForSeconds(.4f);
            Assert.That(bot.pilot.Phase, Is.EqualTo(RangeBotPhase.Attack),"A fresh combat pass should face and engage the player before disengaging.");
            Assert.That(bot.pilot.DesiredVelocity.magnitude, Is.GreaterThan(7), "Combat should use the ship's speed rather than the obsolete 7 m/s bot cap.");
            Assert.That(Vector3.Distance(bot.body.position, bot.initialPosition), Is.GreaterThan(.05f));
        }

        [UnityTest]public IEnumerator ClosePursuitCannotKeepTheBotEscapingIndefinitely()
        {
            var world=Object.FindFirstObjectByType<RangeCombatWorld>();
            world.SpawnTargets(FlightRangeBootstrap.DefaultShip(),1);
            var player=world.Player;var bot=world.Bot;var rules=ScriptableObject.CreateInstance<BattleTuning>();
            try
            {
                world.enabled=false;player.flight.enabled=false;bot.flight.enabled=false;
                float distance=player.ship.HullRadius+bot.ship.HullRadius+9;
                var position=player.body.position+Vector3.forward*distance;
                bot.flight.ResetMotion(position,Quaternion.LookRotation(player.ship.CorePhysicsPosition-position,Vector3.up));
                Physics.SyncTransforms();bot.pilot.Reset();
                bot.pilot.Step(bot,player,world.Actors,world.Debris.Clusters,rules,player.ship.Tuning,RangeBotMode.Combat,Vector3.zero,0,.02f);
                bot.pilot.Step(bot,player,world.Actors,world.Debris.Clusters,rules,player.ship.Tuning,RangeBotMode.Combat,Vector3.zero,1,.02f);
                Assert.That(bot.pilot.Phase,Is.EqualTo(RangeBotPhase.Breakaway));
                bot.pilot.Step(bot,player,world.Actors,world.Debris.Clusters,rules,player.ship.Tuning,RangeBotMode.Combat,Vector3.zero,1+rules.botBreakawaySeconds+.2f,.02f);
                Assert.That(bot.pilot.Phase,Is.EqualTo(RangeBotPhase.Attack),"Even when the player keeps the gap tight, retreat has a bounded duration.");
            }
            finally{Object.Destroy(rules);}
            yield return null;
        }

        [UnityTest] public IEnumerator StationaryFireAimsAndShootsWithoutTranslationAndResetRestoresOnlyTargets()
        {
            var world = Object.FindFirstObjectByType<RangeCombatWorld>();
            world.Player.flight.SetExternalInput(Vector3.zero, Vector2.zero);
            world.SpawnTargets(FlightRangeBootstrap.DefaultShip(), 1);
            var bot = world.Bot;
            var player = world.Player;
            Vector3 start = player.body.position + Vector3.forward * 120;
            bot.flight.ResetMotion(start, Quaternion.LookRotation(player.ship.CorePhysicsPosition - start, Vector3.up));
            world.SetBotMode(RangeBotMode.StationaryFire, false);
            int playerLife = player.ship.LifeId, botLife = bot.ship.LifeId;
            player.ship.InvulnerableUntil=0; // This check measures gun damage, independently of entry protection.
            string roster = PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            yield return new WaitForSeconds(3);
            Assert.That(bot.weapons.ShotsFired, Is.GreaterThan(0));
            Assert.That(player.ship.DamageTaken, Is.GreaterThan(0), "A correctly aligned stationary pilot must land real projectiles through the shared damage system.");
            Assert.That(Vector3.Distance(bot.body.position, start), Is.LessThan(.01f));
            Assert.That(bot.flight.RequestedBoost, Is.False);
            world.ResetTargets();
            Assert.That(bot.ship.LifeId, Is.GreaterThan(botLife));
            Assert.That(player.ship.LifeId, Is.EqualTo(playerLife));
            Assert.That(world.BotMode, Is.EqualTo(RangeBotMode.StationaryFire));
            Assert.That(PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey), Is.EqualTo(roster));
            foreach (var part in bot.ship.Parts) Assert.That(part.hp, Is.EqualTo(part.maxHp));
            Assert.That(bot.flight.CurrentSpeed, Is.Zero);
        }

        [UnityTest]public IEnumerator CombatAllowsReactionTimeAndLeavesGapsBetweenRealShots()
        {
            var world=Object.FindFirstObjectByType<RangeCombatWorld>();var player=world.Player;
            Assert.That(player.ship.InvulnerableUntil-player.ship.CombatTime,Is.GreaterThan(3),"Entering the range grants the same protection as respawning.");
            player.ship.InvulnerableUntil=100000;player.flight.SetExternalInput(Vector3.zero,Vector2.zero);
            world.SpawnTargets(FlightRangeBootstrap.DefaultShip(),1);var bot=world.Bot;
            var position=player.body.position+Vector3.forward*120;
            bot.flight.ResetMotion(position,Quaternion.LookRotation(player.ship.CorePhysicsPosition-position,Vector3.up));
            world.SetBotMode(RangeBotMode.Combat,false);
            yield return new WaitForSeconds(.5f);
            Assert.That(bot.weapons.ShotsFired,Is.Zero,"The bot must give a newly acquired player time to react.");
            int previous=0;float quiet=0,longestQuiet=0;
            for(int i=0;i<45;i++)
            {
                yield return new WaitForSeconds(.1f);int shots=bot.weapons.ShotsFired;
                if(shots>0&&shots==previous){quiet+=.1f;longestQuiet=Mathf.Max(longestQuiet,quiet);}else quiet=0;
                previous=shots;
            }
            Assert.That(bot.weapons.ShotsFired,Is.GreaterThan(0),"The easier bot still attacks.");
            Assert.That(bot.weapons.ShotsFired,Is.LessThanOrEqualTo(10),"Training fire must not become continuous full-rate battery fire.");
            Assert.That(longestQuiet,Is.GreaterThanOrEqualTo(.7f),"The player needs a visible opportunity to evade and counterattack.");
        }

        [UnityTest] public IEnumerator CombatPilotLeadsAndDamagesAMovingPlayerUsingActualProjectiles()
        {
            var world = Object.FindFirstObjectByType<RangeCombatWorld>();
            world.SpawnTargets(FlightRangeBootstrap.DefaultShip(), 1);
            var player = world.Player; var bot = world.Bot;
            player.ship.InvulnerableUntil=0;
            Vector3 start = player.body.position + Vector3.forward * 120;
            bot.flight.ResetMotion(start, Quaternion.LookRotation(player.ship.CorePhysicsPosition - start, Vector3.up));
            player.flight.SetExternalInput(Vector3.right, Vector2.zero);
            world.SetBotMode(RangeBotMode.Combat, false);
            bool damage = false, lead = false; float speed = 0;
            for (int i = 0; i < 80; i++)
            {
                yield return new WaitForSeconds(.1f);
                damage |= player.ship.DamageTaken > 0;
                speed = Mathf.Max(speed, bot.flight.CurrentSpeed);
                if (player.ship.IsAlive && bot.aimingAtPlayer && world.PlayerCoreVelocity.magnitude > 2)
                    lead |= Vector3.Dot(bot.pilot.AimPoint - player.ship.CorePhysicsPosition, world.PlayerCoreVelocity) > 1;
            }
            Assert.That(lead, Is.True, "The combat pilot must aim ahead of a crossing physical core.");
            Assert.That(damage, Is.True, "Full combat must land shared projectiles, not merely generate manoeuvres and trigger requests.");
            Assert.That(speed, Is.GreaterThan(7));
        }
    }
}
