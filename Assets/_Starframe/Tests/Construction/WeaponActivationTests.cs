using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class WeaponActivationTests
    {
        WeaponDefinition definition;
        [SetUp]public void SetUp(){definition=ScriptableObject.CreateInstance<WeaponDefinition>();definition.shotsPerSecond=5;}
        [TearDown]public void TearDown(){Object.DestroyImmediate(definition);}

        [Test]public void HoldScalesCycleByPowerAndDiscardsUnfinishedCycleOnRelease()
        {
            var state=new WeaponActivationState();Assert.That(state.Prepare(definition,new WeaponInputState(true),true),Is.True);
            Assert.That(state.RequestsPower,Is.True);Assert.That(state.Advance(definition,.2f,.5f),Is.Zero);
            Assert.That(state.Advance(definition,.2f,.5f),Is.EqualTo(1));
            state.Advance(definition,.1f,1);state.Prepare(definition,new WeaponInputState(false),true);
            state.Prepare(definition,new WeaponInputState(true),true);Assert.That(state.Advance(definition,.1f,1),Is.Zero);
        }

        [Test]public void PressCreatesOnePoweredCycleAndDoesNotRepeatWhileHeld()
        {
            definition.activationMode=WeaponActivationMode.Press;var state=new WeaponActivationState();
            state.Prepare(definition,new WeaponInputState(true,true),true);Assert.That(state.Advance(definition,.1f,1),Is.Zero);
            state.Prepare(definition,new WeaponInputState(false,false,true),true);Assert.That(state.Advance(definition,.1f,1),Is.EqualTo(1));
            Assert.That(state.Prepare(definition,new WeaponInputState(false),true),Is.False);
            state.Prepare(definition,new WeaponInputState(true,true),true);Assert.That(state.Advance(definition,.2f,1),Is.EqualTo(1));
            Assert.That(state.Prepare(definition,new WeaponInputState(true),true),Is.False);Assert.That(state.Advance(definition,1,1),Is.Zero);
        }

        [Test]public void ChargeUsesPowerAndOnlyCompletedChargeFiresOnceOnRelease()
        {
            definition.activationMode=WeaponActivationMode.ChargeRelease;definition.chargeSeconds=1;var state=new WeaponActivationState();
            state.Prepare(definition,new WeaponInputState(true),true);Assert.That(state.Advance(definition,1,.5f),Is.Zero);
            Assert.That(state.ChargeFraction,Is.EqualTo(.5f).Within(.0001f));state.Prepare(definition,new WeaponInputState(false),true);
            Assert.That(state.Advance(definition,.02f,1),Is.Zero,"Early release cancels a partial charge.");
            state.Prepare(definition,new WeaponInputState(true),true);state.Advance(definition,2,.5f);
            Assert.That(state.ChargeFraction,Is.EqualTo(1));state.Prepare(definition,new WeaponInputState(true),true);
            Assert.That(state.RequestsPower,Is.False,"Holding a completed charge must not draw power repeatedly.");
            Assert.That(state.Advance(definition,1,1),Is.Zero);state.Prepare(definition,new WeaponInputState(false),true);
            Assert.That(state.RequestsPower,Is.False);Assert.That(state.Advance(definition,.02f,0),Is.EqualTo(1),"Release uses energy already allocated while charging.");
            Assert.That(state.Advance(definition,.02f,1),Is.Zero);
        }

        [Test]public void ToggleSurvivesReleaseAndClearsOnSecondPressOrInvalidActivation()
        {
            definition.activationMode=WeaponActivationMode.Toggle;var state=new WeaponActivationState();
            state.Prepare(definition,new WeaponInputState(true,true),true);Assert.That(state.Advance(definition,.2f,1),Is.EqualTo(1));
            state.Prepare(definition,new WeaponInputState(false,false,true),true);Assert.That(state.Advance(definition,.2f,1),Is.EqualTo(1));
            state.Prepare(definition,new WeaponInputState(true,true),true);Assert.That(state.IsToggled,Is.False);Assert.That(state.Advance(definition,1,1),Is.Zero);
            state.Prepare(definition,new WeaponInputState(false),true);state.Prepare(definition,new WeaponInputState(true,true),true);
            Assert.That(state.IsToggled,Is.True);state.Prepare(definition,new WeaponInputState(true),false);
            Assert.That(state.IsToggled,Is.False);Assert.That(state.RequestsPower,Is.False);Assert.That(state.Advance(definition,1,1),Is.Zero);
            state.Prepare(definition,new WeaponInputState(true),true);Assert.That(state.IsToggled,Is.False,"A held key cannot reactivate a cleared toggle without a new press.");
        }

        [Test]public void LosingRequiredGateCancelsChargeAndClearCancelsPendingPress()
        {
            definition.activationMode=WeaponActivationMode.ChargeRelease;var state=new WeaponActivationState();
            state.Prepare(definition,new WeaponInputState(true),true);state.Advance(definition,1,1);
            Assert.That(state.Prepare(definition,new WeaponInputState(false),false),Is.False);Assert.That(state.ChargeFraction,Is.Zero);
            Assert.That(state.Advance(definition,.02f,1),Is.Zero);
            definition.activationMode=WeaponActivationMode.Press;state.Prepare(definition,new WeaponInputState(true,true),true);state.Clear();
            Assert.That(state.Advance(definition,1,1),Is.Zero);
        }
    }
}
