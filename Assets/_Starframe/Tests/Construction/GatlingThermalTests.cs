using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class GatlingThermalTests
    {
        WeaponDefinition definition;
        [SetUp] public void SetUp()
        {
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            definition.family = WeaponFamily.Gatling; definition.shotsPerSecond = 20;
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(definition); }

        [Test] public void PoweredSpinReachesFullRateAfterOnePointFiveSecondsAndStopsInHalfSecond()
        {
            var state = new GatlingThermalState();
            float average = state.Advance(definition, 1.5f, true, 1);
            Assert.That(state.SpinFraction, Is.EqualTo(1).Within(.00001f));
            Assert.That(average, Is.EqualTo(.6f).Within(.00001f), "Integrate the 20%-to-100% ramp instead of applying its end rate to the entire step.");
            state.Advance(definition, .25f, false, 0);
            Assert.That(state.SpinFraction, Is.EqualTo(.5f).Within(.00001f));
            state.Advance(definition, .25f, false, 0);
            Assert.That(state.SpinFraction, Is.Zero.Within(.00001f));
        }

        [Test] public void ContinuousFiringIncludesSimultaneousCoolingAndLocksAtEightSeconds()
        {
            var state = new GatlingThermalState();
            for (int i = 0; i < 400; i++) state.Advance(definition, .01f, true, 1);
            Assert.That(state.HeatFraction, Is.EqualTo(.5f).Within(.00001f));
            Assert.That(state.IsOverheated, Is.False);
            for (int i = 0; i < 400; i++) state.Advance(definition, .01f, true, 1);
            Assert.That(state.HeatFraction, Is.EqualTo(1).Within(.00001f));
            Assert.That(state.IsOverheated, Is.True);
            Assert.That(state.Advance(definition, 5.9f, true, 1), Is.Zero);
            Assert.That(state.IsOverheated, Is.True, "Holding the trigger cannot interrupt the passive recovery lockout.");
            Assert.That(state.Advance(definition, .1f, true, 1), Is.Zero);
            Assert.That(state.HeatFraction, Is.Zero.Within(.00001f));
            Assert.That(state.IsOverheated, Is.False);
            Assert.That(state.Advance(definition, .01f, true, 1), Is.GreaterThan(0));
        }

        [Test] public void PowerAllocationControlsSpinAndHeatingWhilePassiveCoolingNeedsNoPower()
        {
            var state = new GatlingThermalState();
            state.Advance(definition, 1.5f, true, .5f);
            Assert.That(state.SpinFraction, Is.EqualTo(.5f).Within(.00001f));
            Assert.That(state.HeatFraction, Is.Zero.Within(.00001f));
            Assert.That(state.Advance(definition, .5f, true, 0), Is.Zero);
            Assert.That(state.SpinFraction, Is.Zero.Within(.00001f));
            Assert.That(state.HeatFraction, Is.Zero.Within(.00001f));
            state.Advance(definition, .25f, false, 0);
            Assert.That(state.HeatFraction, Is.Zero.Within(.00001f));
        }

        [Test] public void PausedAndInvalidStepsDoNotAdvanceThermalStateAndClearStartsANewLife()
        {
            var state = new GatlingThermalState(); state.Advance(definition, 3, true, 1);
            float spin = state.SpinFraction, heat = state.HeatFraction;
            state.Advance(definition, 0, false, 0); state.Advance(definition, float.NaN, false, 0);
            Assert.That(state.SpinFraction, Is.EqualTo(spin)); Assert.That(state.HeatFraction, Is.EqualTo(heat));
            state.Clear(); Assert.That(state.SpinFraction, Is.Zero); Assert.That(state.HeatFraction, Is.Zero);
            Assert.That(state.IsOverheated, Is.False);
        }

        [Test] public void SpinIntegralAndThermalClockAgreeAcrossStepSizes()
        {
            var whole = new GatlingThermalState(); var split = new GatlingThermalState();
            float wholeIntegral = whole.Advance(definition, 3, true, 1) * 3;
            float splitIntegral = 0;
            for (int i = 0; i < 300; i++) splitIntegral += split.Advance(definition, .01f, true, 1) * .01f;
            Assert.That(splitIntegral, Is.EqualTo(wholeIntegral).Within(.00002f));
            Assert.That(split.HeatFraction, Is.EqualTo(whole.HeatFraction).Within(.00001f));
        }

        [Test] public void EachMountOwnsItsHeatAndInputCancellationCannotClearIt()
        {
            var hot = new GatlingThermalState(); var cold = new GatlingThermalState();
            var activation = new WeaponActivationState();
            activation.Prepare(definition, new WeaponInputState(true), true);
            hot.Advance(definition, 3, true, 1);
            activation.Cancel(); activation.SynchronizeCycle(definition);
            hot.Advance(definition, 1, false, 0);
            Assert.That(hot.HeatFraction, Is.EqualTo(5f / 24f).Within(.00001f));
            Assert.That(cold.HeatFraction, Is.Zero);
        }

        [Test] public void GenericWeaponsRetainTheirOriginalCadenceAndBeamRangeIsExplicit()
        {
            definition.family = WeaponFamily.Generic;
            var thermal = new GatlingThermalState();
            Assert.That(thermal.Advance(definition, 1, true, 1), Is.EqualTo(1));
            var activation = new WeaponActivationState(); activation.Prepare(definition, new WeaponInputState(true), true);
            Assert.That(activation.Advance(definition, .5f, 1), Is.EqualTo(10));
            definition.unboundedBeam = true; definition.range = 45;
            Assert.That(definition.EffectiveRange, Is.EqualTo(45));
            definition.delivery = WeaponDelivery.Beam;
            Assert.That(definition.EffectiveRange, Is.EqualTo(100000));
        }
    }
}
