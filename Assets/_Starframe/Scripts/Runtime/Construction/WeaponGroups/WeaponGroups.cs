// @role Validate and maintain exactly one group assignment per weapon part.
// @calls ModuleDatabase, BlockModules

using System;
using System.Collections.Generic;
using System.Text;
using Starframe.Flight;
using UnityEngine.InputSystem;

namespace Starframe.Construction
{
    /// <summary>Blueprint-owned groups. Each weapon part has exactly one assignment.</summary>
    public static class WeaponGroups
    {
        public const int DefaultGroupId=0;
        public const int MaxNameLength=48;

        public static bool IsWeapon(BlockRecord part,ModuleDatabase database=null)
        {
            if(part==null)return false;
            if(!database)database=BlockModules.ConfiguredDatabase;
            var definition=database?database.Get(part.shape):null;
            return part.shape==BlockShape.Laser||(definition&&(definition.weapon||definition.category==ModuleCategory.Weapon));
        }

        /// <summary>Repairs optional metadata without changing geometry or existing valid IDs.</summary>
        public static void Normalize(BlueprintData data,ModuleDatabase database=null)
        {
            if(data==null)throw new ArgumentNullException(nameof(data));
            var groups=new List<WeaponGroupRecord>();var ids=new HashSet<int>();
            if(data.weaponGroups!=null)foreach(var group in data.weaponGroups){
                if(group==null||group.id<0||!ids.Add(group.id))continue;
                groups.Add(new WeaponGroupRecord{id=group.id,name=CleanName(group.name,group.id),binding=CleanBinding(group.binding,group.id)});
            }
            if(ids.Add(DefaultGroupId))groups.Insert(0,new WeaponGroupRecord{id=DefaultGroupId,name=DefaultName(DefaultGroupId),binding=WeaponInputBinding.MouseLeft()});
            groups.Sort((a,b)=>a.id.CompareTo(b.id));

            var assignments=new List<WeaponGroupAssignment>();
            var byPart=new Dictionary<string,int>(StringComparer.Ordinal);
            if(data.weaponAssignments!=null)foreach(var value in data.weaponAssignments){
                if(value==null||string.IsNullOrWhiteSpace(value.partId)||byPart.ContainsKey(value.partId))continue;
                byPart.Add(value.partId,ids.Contains(value.groupId)?value.groupId:DefaultGroupId);
            }
            var seen=new HashSet<string>(StringComparer.Ordinal);
            if(data.parts!=null)foreach(var part in data.parts){
                if(!IsWeapon(part,database)||string.IsNullOrWhiteSpace(part.id)||!seen.Add(part.id))continue;
                assignments.Add(new WeaponGroupAssignment{partId=part.id,groupId=byPart.TryGetValue(part.id,out int id)?id:DefaultGroupId});
            }
            assignments.Sort((a,b)=>StringComparer.Ordinal.Compare(a.partId,b.partId));
            data.weaponGroups=groups;data.weaponAssignments=assignments;
        }

        /// <summary>Deep-copies only metadata, allowing an assembly to export current geometry.</summary>
        public static void CopyTo(BlueprintData source,BlueprintData destination)
        {
            if(destination==null)throw new ArgumentNullException(nameof(destination));
            var groups=new List<WeaponGroupRecord>();var assignments=new List<WeaponGroupAssignment>();
            if(source?.weaponGroups!=null)foreach(var group in source.weaponGroups)if(group!=null)
                groups.Add(new WeaponGroupRecord{id=group.id,name=group.name,binding=group.binding?.Copy()});
            if(source?.weaponAssignments!=null)foreach(var value in source.weaponAssignments)if(value!=null)
                assignments.Add(new WeaponGroupAssignment{partId=value.partId,groupId=value.groupId});
            destination.weaponGroups=groups;destination.weaponAssignments=assignments;
        }

        public static WeaponGroupRecord FindGroup(BlueprintData data,int groupId)=>data?.weaponGroups?.Find(group=>group!=null&&group.id==groupId);
        public static int GetGroupId(BlueprintData data,string partId)
        {
            if(data?.weaponAssignments!=null)foreach(var value in data.weaponAssignments)
                if(value!=null&&string.Equals(value.partId,partId,StringComparison.Ordinal))return value.groupId;
            return DefaultGroupId;
        }

        public static WeaponGroupRecord AddGroup(BlueprintData data,string name=null,WeaponInputBinding binding=null,ModuleDatabase database=null)
        {
            Normalize(data,database);var used=new HashSet<int>();foreach(var group in data.weaponGroups)used.Add(group.id);
            int id=1;while(used.Contains(id)){if(id==int.MaxValue)throw new InvalidOperationException("No weapon group ID is available.");id++;}
            var result=new WeaponGroupRecord{id=id,name=CleanName(name,id),binding=CleanBinding(binding,id)};
            data.weaponGroups.Add(result);data.weaponGroups.Sort((a,b)=>a.id.CompareTo(b.id));return result;
        }

        public static bool RemoveGroup(BlueprintData data,int groupId,ModuleDatabase database=null)
        {
            Normalize(data,database);if(groupId==DefaultGroupId)return false;
            bool removed=data.weaponGroups.RemoveAll(group=>group.id==groupId)>0;
            if(removed)foreach(var value in data.weaponAssignments)if(value.groupId==groupId)value.groupId=DefaultGroupId;
            return removed;
        }

        public static bool Assign(BlueprintData data,string partId,int groupId,ModuleDatabase database=null)
        {
            Normalize(data,database);if(FindGroup(data,groupId)==null)return false;
            foreach(var value in data.weaponAssignments)if(string.Equals(value.partId,partId,StringComparison.Ordinal)){value.groupId=groupId;return true;}
            return false;
        }

        public static bool ConfigureGroup(BlueprintData data,int groupId,string name,WeaponInputBinding binding,ModuleDatabase database=null)
        {
            Normalize(data,database);var group=FindGroup(data,groupId);if(group==null)return false;
            group.name=CleanName(name,groupId);group.binding=CleanBinding(binding,groupId);return true;
        }

        static string DefaultName(int id)=>"Group "+((long)id+1);
        static string CleanName(string name,int id)
        {
            if(string.IsNullOrWhiteSpace(name))return DefaultName(id);
            var clean=new StringBuilder();foreach(char c in name.Trim())clean.Append(char.IsControl(c)?' ':c);
            string value=clean.ToString().Trim().Normalize(NormalizationForm.FormC);
            if(value.Length>MaxNameLength)value=value.Substring(0,char.IsHighSurrogate(value[MaxNameLength-1])?MaxNameLength-1:MaxNameLength).TrimEnd();
            return value.Length==0?DefaultName(id):value;
        }

        static WeaponInputBinding CleanBinding(WeaponInputBinding binding,int groupId)
        {
            if(binding==null)return groupId==DefaultGroupId?WeaponInputBinding.MouseLeft():new WeaponInputBinding{source=WeaponInputSource.None};
            var result=binding.Copy();
            if(!Enum.IsDefined(typeof(WeaponInputSource),result.source))result.source=WeaponInputSource.None;
            if(result.source==WeaponInputSource.Keyboard){
                if(result.key==Key.None||!Enum.IsDefined(typeof(Key),result.key)){result.source=WeaponInputSource.None;result.key=Key.None;}
            }else result.key=Key.None;
            return result;
        }
    }
}
