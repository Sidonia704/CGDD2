// @role Read configured group controls and pass latched events to ship weapons.
// @calls ShipWeapons, GameAudio

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Starframe.Construction;

namespace Starframe.Combat
{
    public static class WeaponGroupInputRouter
    {
        /// <summary>Call once per render Update. Bindings come from the initialized runtime snapshot.</summary>
        public static void Apply(ShipWeapons weapons,BlueprintData blueprint,bool hasValidLock,bool allowed)
        {
            if(weapons==null)return;
            weapons.UseGroupedInput=true;weapons.HasValidTargetLock=hasValidLock;
            if(!allowed){weapons.ClearGroupInputs();return;}
            weapons.FireRequested=false;
            foreach(var group in weapons.Groups){
                var toggle=ToggleButton(group.id);
                if(toggle!=null&&toggle.wasPressedThisFrame){weapons.ToggleGroup(group.id);Starframe.App.GameAudio.Click();}
                var button=Button(group.binding);
                weapons.SetGroupInput(group.id,button!=null&&button.isPressed,button!=null&&button.wasPressedThisFrame,button!=null&&button.wasReleasedThisFrame);
            }
        }

        /// <summary>Group 1..8 use the keyboard's number row, independent of their fire binding.</summary>
        static ButtonControl ToggleButton(int groupId)
        {
            var keyboard=Keyboard.current;if(keyboard==null)return null;
            switch(groupId){
                case 0:return keyboard.digit1Key;case 1:return keyboard.digit2Key;
                case 2:return keyboard.digit3Key;case 3:return keyboard.digit4Key;
                case 4:return keyboard.digit5Key;case 5:return keyboard.digit6Key;
                case 6:return keyboard.digit7Key;case 7:return keyboard.digit8Key;
                default:return null;
            }
        }

        static ButtonControl Button(WeaponInputBinding binding)
        {
            if(binding==null)return null;
            switch(binding.source){
                case WeaponInputSource.MouseLeft:return Mouse.current?.leftButton;
                case WeaponInputSource.MouseRight:return Mouse.current?.rightButton;
                case WeaponInputSource.MouseMiddle:return Mouse.current?.middleButton;
                case WeaponInputSource.Keyboard:
                    if(Keyboard.current==null||binding.key==Key.None)return null;
                    return Keyboard.current[binding.key];
                default:return null;
            }
        }
    }
}
