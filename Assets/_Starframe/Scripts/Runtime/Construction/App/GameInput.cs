// @role Resolve configurable actions and capture valid input bindings.
// @calls GameSettings

using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Starframe.Presentation;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.App
{
    public enum GameAction { Forward, Backward, Left, Right, Up, Down, RollLeft, RollRight, Boost, TargetLock }

    [Serializable]
    public sealed class GameBinding
    {
        public GameAction action;
        public string path;
        public string alternatePath;
        public GameBinding(GameAction action, string path, string alternatePath = null)
        { this.action=action;this.path=path;this.alternatePath=alternatePath; }
        public GameBinding Clone()=>new GameBinding(action,path,alternatePath);
    }

    /// <summary>Flight bindings. Weapon group bindings remain part of each blueprint.</summary>
    public static class GameInput
    {
        public static bool Suspended { get; set; }
        public static GameBinding[] DefaultBindings()=>new[]
        {
            new GameBinding(GameAction.Forward,"<Keyboard>/w"),new GameBinding(GameAction.Backward,"<Keyboard>/s"),
            new GameBinding(GameAction.Left,"<Keyboard>/a"),new GameBinding(GameAction.Right,"<Keyboard>/d"),
            new GameBinding(GameAction.Up,"<Keyboard>/space"),new GameBinding(GameAction.Down,"<Keyboard>/leftCtrl","<Keyboard>/rightCtrl"),
            new GameBinding(GameAction.RollLeft,"<Keyboard>/q"),new GameBinding(GameAction.RollRight,"<Keyboard>/e"),
            new GameBinding(GameAction.Boost,"<Keyboard>/leftShift","<Keyboard>/rightShift"),new GameBinding(GameAction.TargetLock,"<Keyboard>/f")
        };

        public static GameBinding Get(GameAction action, GameSettingsData data=null)
        {
            var bindings=(data??GameSettings.Current).bindings;
            if(bindings!=null)foreach(var binding in bindings)if(binding!=null&&binding.action==action)return binding;
            return DefaultBindings()[(int)action];
        }

        public static bool Held(GameAction action)
        {
            if(Suspended)return false;
            var binding=Get(action);
            return Read(binding.path)?.isPressed==true||Read(binding.alternatePath)?.isPressed==true;
        }
        public static bool Pressed(GameAction action)
        {
            if(Suspended)return false;
            var binding=Get(action);
            return Read(binding.path)?.wasPressedThisFrame==true||Read(binding.alternatePath)?.wasPressedThisFrame==true;
        }
        static ButtonControl Read(string path)=>string.IsNullOrEmpty(path)?null:InputSystem.FindControl(path) as ButtonControl;

        public static bool IsSupported(string path)
        {
            if(string.IsNullOrEmpty(path))return false;
            if(path=="<Mouse>/leftButton"||path=="<Mouse>/rightButton"||path=="<Mouse>/middleButton")return true;
            if(!path.StartsWith("<Keyboard>/",StringComparison.Ordinal))return false;
            string name=path.Substring(11);
            if(name.Length==1&&name[0]>='0'&&name[0]<='9')return true;
            // Validate without needing a keyboard device (settings load and EditMode tests).
            return Enum.TryParse<Key>(name,true,out var key)&&key!=Key.None&&Enum.IsDefined(typeof(Key),key) ||
                name=="leftCtrl"||name=="rightCtrl"||name=="leftShift"||name=="rightShift"||
                name=="leftAlt"||name=="rightAlt"||name=="leftMeta"||name=="rightMeta"||name=="backquote";
        }
        public static bool IsReserved(string path)=>string.Equals(path,"<Keyboard>/escape",StringComparison.OrdinalIgnoreCase)||
            string.Equals(path,"<Keyboard>/f1",StringComparison.OrdinalIgnoreCase)||IsWeaponGroupToggle(path);

        public static bool IsWeaponGroupToggle(string path)
        {
            if(string.IsNullOrEmpty(path)||!path.StartsWith("<Keyboard>/",StringComparison.OrdinalIgnoreCase))return false;
            string key=path.Substring(11);
            if(key.StartsWith("digit",StringComparison.OrdinalIgnoreCase))key=key.Substring(5);
            return key.Length==1&&key[0]>='1'&&key[0]<='8';
        }

        public static bool TryCapture(out string path)
        {
            path=null;
            var keyboard=Keyboard.current;
            if(keyboard!=null)foreach(var key in keyboard.allKeys)
                if(key.wasPressedThisFrame){path="<Keyboard>/"+key.name;return true;}
            var mouse=Mouse.current;
            if(mouse!=null)
            {
                if(mouse.leftButton.wasPressedThisFrame)path="<Mouse>/leftButton";
                else if(mouse.rightButton.wasPressedThisFrame)path="<Mouse>/rightButton";
                else if(mouse.middleButton.wasPressedThisFrame)path="<Mouse>/middleButton";
            }
            return path!=null;
        }

        public static List<GameAction> Conflicts(GameSettingsData data, GameAction selected, string path)
        {
            var result=new List<GameAction>();
            foreach(var binding in data.bindings)
                if(binding!=null&&binding.action!=selected&&
                    (string.Equals(binding.path,path,StringComparison.OrdinalIgnoreCase)||string.Equals(binding.alternatePath,path,StringComparison.OrdinalIgnoreCase)))
                    result.Add(binding.action);
            return result;
        }

        /// <summary>Compares persisted bindings without requiring an attached input device.</summary>
        public static bool MatchesWeaponBinding(string path, WeaponInputBinding binding)
        {
            if(string.IsNullOrEmpty(path)||binding==null)return false;
            switch(binding.source)
            {
                case WeaponInputSource.MouseLeft:return string.Equals(path,"<Mouse>/leftButton",StringComparison.OrdinalIgnoreCase);
                case WeaponInputSource.MouseRight:return string.Equals(path,"<Mouse>/rightButton",StringComparison.OrdinalIgnoreCase);
                case WeaponInputSource.MouseMiddle:return string.Equals(path,"<Mouse>/middleButton",StringComparison.OrdinalIgnoreCase);
                case WeaponInputSource.Keyboard:
                    if(binding.key==Key.None||!path.StartsWith("<Keyboard>/",StringComparison.OrdinalIgnoreCase))return false;
                    string name=path.Substring(11);
                    if(name.Length==1&&name[0]>='0'&&name[0]<='9')name="Digit"+name;
                    else if(name.Length==0||char.IsDigit(name[0])||name[0]=='+'||name[0]=='-'||name!=name.Trim()||name.IndexOf(',')>=0)return false;
                    switch(name.ToLowerInvariant())
                    {
                        case "leftcontrol":name="LeftCtrl";break;
                        case "rightcontrol":name="RightCtrl";break;
                        case "control":case "ctrl":return binding.key==Key.LeftCtrl||binding.key==Key.RightCtrl;
                        case "shift":return binding.key==Key.LeftShift||binding.key==Key.RightShift;
                        case "alt":return binding.key==Key.LeftAlt||binding.key==Key.RightAlt;
                    }
                    return Enum.TryParse<Key>(name,true,out var key)&&key!=Key.None
                        &&Enum.IsDefined(typeof(Key),key)&&key==binding.key;
                default:return false;
            }
        }

        /// <summary>Only groups with an existing weapon assignment conflict; never mutates the blueprint.</summary>
        public static bool HasWeaponBindingConflict(BlueprintData blueprint,string path,ModuleDatabase database=null)
        {
            if(blueprint?.parts==null||string.IsNullOrEmpty(path))return false;
            var matching=new HashSet<int>();var valid=new HashSet<int>{WeaponGroups.DefaultGroupId};
            bool hasDefault=false;
            if(blueprint.weaponGroups!=null)foreach(var group in blueprint.weaponGroups)
            {
                if(group==null||group.id<0||group.id!=WeaponGroups.DefaultGroupId&&!valid.Add(group.id))continue;
                if(group.id==WeaponGroups.DefaultGroupId)
                {
                    if(hasDefault)continue;
                    hasDefault=true;
                }
                if(MatchesWeaponBinding(path,group.binding??(group.id==WeaponGroups.DefaultGroupId?WeaponInputBinding.MouseLeft():null)))
                    matching.Add(group.id);
            }
            if(!hasDefault&&MatchesWeaponBinding(path,WeaponInputBinding.MouseLeft()))matching.Add(WeaponGroups.DefaultGroupId);
            if(matching.Count==0)return false;
            foreach(var part in blueprint.parts)
            {
                if(!WeaponGroups.IsWeapon(part,database)||string.IsNullOrWhiteSpace(part.id))continue;
                int groupId=WeaponGroups.GetGroupId(blueprint,part.id);
                if(matching.Contains(valid.Contains(groupId)?groupId:WeaponGroups.DefaultGroupId))return true;
            }
            return false;
        }

        public static string BindingLabel(GameAction action,GameSettingsData data=null)
        {
            var binding=Get(action,data);
            string primary=PathLabel(binding.path);
            return string.IsNullOrEmpty(binding.alternatePath)?primary:primary+" / "+PathLabel(binding.alternatePath);
        }
        public static string PathLabel(string path)
        {
            if(path=="<Mouse>/leftButton")return L10n.T("settings.mouseLeft");
            if(path=="<Mouse>/rightButton")return L10n.T("settings.mouseRight");
            if(path=="<Mouse>/middleButton")return L10n.T("settings.mouseMiddle");
            if(string.IsNullOrEmpty(path))return L10n.T("settings.unbound");
            var control=Read(path);
            if(control!=null&&!string.IsNullOrEmpty(control.displayName))return control.displayName;
            return path.Replace("<Keyboard>/","").ToUpperInvariant();
        }

        public static GameBinding[] Normalize(GameBinding[] input)
        {
            var defaults=DefaultBindings();
            if(input==null)return defaults;
            for(int i=0;i<defaults.Length;i++)
                foreach(var binding in input)
                    if(binding!=null&&binding.action==defaults[i].action&&IsSupported(binding.path)&&!IsReserved(binding.path))
                    {
                        defaults[i]=new GameBinding(binding.action,binding.path,
                            IsSupported(binding.alternatePath)&&!IsReserved(binding.alternatePath)?binding.alternatePath:null);
                        break;
                    }
            return defaults;
        }
    }
}
