using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Starframe.Combat;
using Starframe.Flight;
namespace Starframe.Tests
{
    public sealed class FlightAimStabilityTests
    {
        bool hadSettings;string settings;
        [UnitySetUp]public IEnumerator SetUp()
        {
            hadSettings=PlayerPrefs.HasKey(RangeTargetRoster.PreferenceKey);settings=PlayerPrefs.GetString(RangeTargetRoster.PreferenceKey);
            PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,"{\"entries\":[]}");
            FlightSession.Begin(FlightRangeBootstrap.DefaultShip(),false);yield return SceneManager.LoadSceneAsync(FlightSession.RangeScene);yield return null;
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            if(hadSettings)PlayerPrefs.SetString(RangeTargetRoster.PreferenceKey,settings);else PlayerPrefs.DeleteKey(RangeTargetRoster.PreferenceKey);PlayerPrefs.Save();
            FlightSession.Return();yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);yield return null;
        }
        [UnityTest]public IEnumerator FineAimStaysSmoothAndPersistentThenSettlesWhenCentered()
        {
            var world=Object.FindFirstObjectByType<RangeCombatWorld>();var player=world.Player;var flight=player.flight;
            var input=new Vector2(.15f,0);flight.SetExternalInput(Vector3.zero,input);var previous=player.body.rotation;
            float maxStep=0,total=0;
            for(int i=0;i<40;i++){
                yield return new WaitForFixedUpdate();float step=AngularDifference(previous,player.body.rotation);maxStep=Mathf.Max(maxStep,step);total+=step;previous=player.body.rotation;
                Assert.That(flight.AimInput,Is.EqualTo(input),"Ship rotation must not recenter the mouse's persistent input.");
            }
            Assert.That(total,Is.GreaterThan(.5f));Assert.That(total,Is.LessThan(5));Assert.That(maxStep,Is.LessThan(.2f),"Fine input must not jump at a physics step.");
            flight.SetExternalInput(Vector3.zero,Vector2.zero);yield return new WaitForSeconds(.5f);var settled=player.body.rotation;
            yield return new WaitForSeconds(.25f);Assert.That(AngularDifference(settled,player.body.rotation),Is.LessThan(.03f));
        }
        // Quaternion.Angle snaps tiny per-step rotations to zero; atan2 retains the precision needed for fine aim.
        static float AngularDifference(Quaternion a,Quaternion b){var d=Quaternion.Inverse(a)*b;return 2*Mathf.Atan2(new Vector3(d.x,d.y,d.z).magnitude,Mathf.Abs(d.w))*Mathf.Rad2Deg;}
    }
}
