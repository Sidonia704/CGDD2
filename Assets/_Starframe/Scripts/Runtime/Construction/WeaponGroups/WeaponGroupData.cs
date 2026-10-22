// @role Serialize weapon-group membership and control bindings with the blueprint.

using System;
using UnityEngine.InputSystem;

namespace Starframe.Construction
{
    public enum WeaponInputSource { None=0, MouseLeft=1, MouseRight=2, MouseMiddle=3, Keyboard=4 }

    [Serializable]
    public sealed class WeaponInputBinding
    {
        public WeaponInputSource source=WeaponInputSource.MouseLeft;
        public Key key=Key.None;
        public WeaponInputBinding Copy()=>new WeaponInputBinding{source=source,key=key};
        public static WeaponInputBinding MouseLeft()=>new WeaponInputBinding();
        public static WeaponInputBinding Keyboard(Key value)=>new WeaponInputBinding{source=WeaponInputSource.Keyboard,key=value};
    }

    [Serializable]
    public sealed class WeaponGroupRecord
    {
        public int id;
        public string name;
        public WeaponInputBinding binding=new WeaponInputBinding();
    }

    [Serializable]
    public sealed class WeaponGroupAssignment
    {
        public string partId;
        public int groupId;
    }
}
