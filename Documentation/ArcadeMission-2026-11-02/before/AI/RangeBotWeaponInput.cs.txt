// @role Convert bot firing intent into weapon-group activation events.
// @calls ShipWeapons

using System.Collections.Generic;
using Starframe.Flight;

namespace Starframe.Combat
{
    // Bot controls use ordinary trigger edges. Aiming never bypasses weapon charge, lock or power rules.
    public sealed class RangeBotWeaponInput
    {
        sealed class TriggerGroup
        {
            public int id;
            public bool held, hasPress;
            public float tapSeconds;
            public double nextTap;
            public readonly List<string> chargeParts = new List<string>();
        }
        readonly List<TriggerGroup> groups = new List<TriggerGroup>();
        ShipWeapons weapons;
        bool edgeModes;

        public void Initialize(ShipWeapons value, FlightShipRuntime ship)
        {
            weapons = value; groups.Clear(); edgeModes = false;
            for (int i = 0; i < value.Groups.Count; i++) groups.Add(new TriggerGroup { id = value.Groups[i].id });
            foreach (var part in ship.Parts)
            {
                var definition = ship.Tuning.GetDefinition(part.record.shape);
                if (!definition || !definition.weapon) continue;
                var weapon = definition.weapon;
                edgeModes |= weapon.activationMode != WeaponActivationMode.Hold;
                int id = value.GetGroupId(part.record.id);
                foreach (var group in groups)
                {
                    if (group.id != id) continue;
                    if (weapon.activationMode == WeaponActivationMode.Press)
                    {
                        group.hasPress = true;
                        group.tapSeconds = UnityEngine.Mathf.Max(group.tapSeconds, 1 / UnityEngine.Mathf.Max(.01f, weapon.shotsPerSecond));
                    }
                    if (weapon.activationMode == WeaponActivationMode.ChargeRelease) group.chargeParts.Add(part.record.id);
                    break;
                }
            }
        }

        public void Reset()
        {
            weapons?.ClearGroupInputs();
            foreach (var group in groups) { group.held = false; group.nextTap = 0; }
        }

        public void Apply(bool canFire, bool hasTarget, double time)
        {
            if (weapons == null) return;
            weapons.HasValidTargetLock = hasTarget;
            if (!canFire) { Reset(); return; }
            // Keep existing direct FireRequested callers compatible with ordinary continuous-fire bots.
            weapons.UseGroupedInput = edgeModes;
            weapons.FireRequested = !edgeModes;
            if (!edgeModes) return;
            foreach (var group in groups)
            {
                bool charged = false;
                foreach (var id in group.chargeParts)
                    if (weapons.GetChargeProgress(id) >= 1) { charged = true; break; }
                bool held = !(group.held && (charged || (group.hasPress && time >= group.nextTap)));
                bool pressed = held && !group.held, released = !held && group.held;
                weapons.SetGroupInput(group.id, held, pressed, released);
                if (pressed) group.nextTap = time + group.tapSeconds + .04f;
                group.held = held;
            }
        }
    }
}
