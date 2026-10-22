// @role Index configured module definitions by part shape.

using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
namespace Starframe.Flight
{
    [CreateAssetMenu(menuName="Starframe/Module Database")]
    public sealed class ModuleDatabase:ScriptableObject
    {
        public List<ModuleDefinition> modules=new List<ModuleDefinition>();
        readonly Dictionary<BlockShape,ModuleDefinition> index=new Dictionary<BlockShape,ModuleDefinition>();
        int revision;
        public int Revision{get{int hash=revision;foreach(var m in modules)if(m)hash=unchecked(hash*31+m.Revision);return hash;}}
        void OnEnable()=>Reindex();
        void OnValidate(){revision++;Reindex();}
        void Reindex(){index.Clear();foreach(var m in modules)if(m)index[m.shape]=m;}
        public ModuleDefinition Get(BlockShape shape){if(index.Count!=modules.Count)Reindex();return index.TryGetValue(shape,out var definition)?definition:null;}
    }
}
