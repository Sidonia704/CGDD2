using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;
namespace Starframe.Tests
{
    public sealed class RangeLifecycleTests
    {
        bool hadSettings,hadMode;string originalSettings;int originalMode;
        [SetUp]public void SetUp()
        {
            hadSettings=PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);originalSettings=PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            hadMode=PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey);originalMode=PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);
            RangePracticeSettings.Mode=RangeBotMode.Combat;
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,"{\"entries\":[]}");
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            if(hadSettings)PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,originalSettings);else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);
            if(hadMode)PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey,originalMode);else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);PlayerPrefs.Save();
            FlightSession.Return();yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);yield return null;
        }
        [UnityTest]
        public IEnumerator ActualRangeMovesFiresDiesRespawnsAndReturnsIntactBlueprint()
        {
            Random.InitState(2701);
            var blueprint=FlightRangeBootstrap.DefaultShip();string before=JsonUtility.ToJson(blueprint);
            FlightSession.Begin(blueprint,true);
            yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);
            yield return null;
            var world=Object.FindFirstObjectByType<RangeCombatWorld>();Assert.That(world,Is.Not.Null);
            world.SpawnTargets(FlightRangeBootstrap.DefaultShip(),1);
            Assert.That(world.Player.ship.IsAlive&&world.Bot.ship.IsAlive,Is.True);
            world.Player.flight.SetExternalInput(Vector3.zero,Vector2.zero);
            var aimBefore=world.Player.flight.AimInput;
            world.Targeting.ToggleLock();Assert.That(world.Targeting.HasTarget,Is.True);
            Assert.That(world.Targeting.LockedTarget,Is.EqualTo(world.Bot.ship));
            Assert.That(world.Targeting.LeadPoints.Count,Is.EqualTo(1));
            Assert.That(world.Targeting.LeadPoints[0].weapon.projectileSpeed,Is.EqualTo(world.Player.ship.Tuning.GetDefinition(BlockShape.Laser).weapon.projectileSpeed));
            Assert.That(world.Player.flight.AimInput,Is.EqualTo(aimBefore),"Lock must not alter the virtual stick.");
            world.Targeting.ToggleLock();Assert.That(world.Targeting.HasTarget,Is.False);
            world.Targeting.ToggleLock();Assert.That(world.Targeting.HasTarget,Is.True);
            var initialPosition=world.Bot.body.position;
            world.Bot.weapons.FireRequested=true;world.Bot.weapons.Prepare();world.Bot.weapons.Execute(.2f,1);
            Assert.That(world.Bot.weapons.BlockedGuns,Is.Zero);
            Assert.That(world.Projectiles.TotalSpawned,Is.GreaterThan(0));
            yield return new WaitForSeconds(.5f);
            Assert.That((world.Bot.body.position-initialPosition).magnitude,Is.GreaterThan(.01f));
            var weaponPart=world.Bot.ship.Parts.Find(p=>p.record.shape==BlockShape.Laser);
            world.Bot.ship.ApplyDamage(weaponPart.record.id,100000,world.Player.ship);
            var partVfx=Object.FindFirstObjectByType<CombatVfxPool>();
            Assert.That(System.Array.Exists(partVfx.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.sharedMaterial.shader.name=="Universal Render Pipeline/Complex Lit"),Is.True,"Destroyed normal parts must emit solid plastic fragments.");
            var mount=world.Bot.ship.Parts.Find(p=>p.record.id=="range-mount");world.Bot.ship.ApplyDamage(mount.record.id,100000,world.Player.ship);
            Assert.That(world.Debris.PartCount,Is.GreaterThan(0),"Disconnected modules must survive as independent physical wreckage.");
            var wreck=world.Debris.Clusters[0];var wreckPosition=wreck.Body.position;
            Assert.That(wreck.Body.useGravity,Is.False);Assert.That(wreck.Body.isKinematic,Is.False);
            Assert.That(Vector3.Distance(wreck.Body.position,wreck.transform.position),Is.LessThan(.001f),"New wreckage must have its physics pose before separation checks run.");
            yield return new WaitForSeconds(.15f);
            Assert.That(Vector3.Distance(wreck.Body.position,wreckPosition),Is.GreaterThan(.01f),"Wreckage must actually drift during the physics simulation.");
            int oldLife=world.Bot.ship.LifeId;
            var core=world.Bot.ship.Parts.Find(p=>p.record.shape==BlockShape.Core);
            int wreckCount=world.Debris.PartCount,survivors=world.Bot.ship.Parts.FindAll(p=>p.present&&p!=core).Count;
            world.Bot.ship.ApplyDamage(core.record.id,100000,world.Player.ship);
            world.Targeting.RefreshLeadPoints();Assert.That(world.Targeting.HasTarget,Is.False);Assert.That(world.Targeting.LeadPoints.Count,Is.Zero);
            Assert.That(world.Bot.ship.IsAlive,Is.False);Assert.That(world.Bot.ship.Performance.totalMass,Is.Zero);
            Assert.That(world.Debris.PartCount,Is.EqualTo(wreckCount+survivors),"Core death must preserve every surviving module as destructible wreckage.");
            var deathWreck=world.Debris.Clusters[world.Debris.Clusters.Count-1];
            var bursts=Object.FindFirstObjectByType<CombatVfxPool>();Assert.That(bursts,Is.Not.Null);
            Assert.That(System.Array.Exists(bursts.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.sharedMaterial.shader.name=="Starframe/Combat/VoxelGlow"),Is.True);
            Assert.That(world.BotKills,Is.EqualTo(1));
            yield return new WaitForSeconds(3.2f);
            Assert.That(world.Bot.ship.IsAlive,Is.True);Assert.That(world.Bot.ship.LifeId,Is.GreaterThan(oldLife));
            Assert.That(deathWreck&&deathWreck.PartCount>0,Is.True,"Respawning a ship must not remove its old wreckage.");
            Assert.That(world.Targeting.HasTarget,Is.False,"Respawned enemies must not inherit the previous lock.");
            foreach(var part in world.Bot.ship.Parts){Assert.That(part.present,Is.True);Assert.That(part.hp,Is.EqualTo(part.maxHp));}
            var playerCore=world.Player.ship.Parts.Find(p=>p.record.shape==BlockShape.Core);
            world.Player.ship.InvulnerableUntil=0;
            world.Player.ship.ApplyDamage(playerCore.record.id,100000,world.Bot.ship);
            Assert.That(world.Player.ship.IsAlive,Is.False);
            yield return new WaitForSeconds(3.2f);
            Assert.That(world.Player.ship.IsAlive,Is.True);Assert.That(world.Player.flight.AimInput,Is.EqualTo(Vector2.zero));
            foreach(var part in world.Player.ship.Parts)Assert.That(part.hp,Is.EqualTo(part.maxHp));
            var hud=Object.FindFirstObjectByType<RangeHUD>();hud.OpenExit();Assert.That(hud.ExitDialogOpen,Is.True);hud.CloseExit();
            Assert.That(JsonUtility.ToJson(FlightSession.EditorDraft),Is.EqualTo(before));
            FlightSession.Return();yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);yield return null;
            var editor=Object.FindFirstObjectByType<ShipEditorController>();Assert.That(editor,Is.Not.Null);
            Assert.That(editor.Assembly.Count,Is.EqualTo(blueprint.parts.Count));Assert.That(editor.Dirty,Is.True);
            Assert.That(JsonUtility.ToJson(blueprint),Is.EqualTo(before));
        }
    }
}
