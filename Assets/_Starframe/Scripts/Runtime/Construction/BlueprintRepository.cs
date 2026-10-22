// @role Validate, migrate, save and load player blueprints.
// @calls BlockPlacement, BlockConnectivity, WeaponGroups

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
namespace Starframe.Construction
{
    public sealed class BlueprintRepository
    {
        const long MaxFileSizeBytes = 16L * 1024 * 1024;
        readonly string directory;
        public BlueprintRepository(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A directory is required.");
            this.directory = Path.GetFullPath(directory);
        }
        public string Save(BlueprintData data,bool overwrite=false)
        {
            data=Upgrade(data);
            data.name=data.name.Trim().Normalize(NormalizationForm.FormC);
            Directory.CreateDirectory(directory);
            string path=FindExistingPath(data.name);
            if(path!=null&&!overwrite)throw new IOException("已有同名蓝图。");
            if(path==null)path=Path.Combine(directory,SafeName(data.name)+".json");
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,JsonUtility.ToJson(data,true),new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temp,path,null);
                else File.Move(temp,path);
                return path;
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        [Serializable] sealed class NameHeader {public string name;}
        public bool NameExists(string name)=>FindExistingPath(name)!=null;
        string FindExistingPath(string name)
        {
            if(string.IsNullOrWhiteSpace(name))return null;
            name=name.Trim().Normalize(NormalizationForm.FormC);
            string target=SafeName(name)+".json";
            foreach(var file in List())
            {
                string path=Path.Combine(directory,file);
                if(string.Equals(file,target,StringComparison.OrdinalIgnoreCase))return path;
                try
                {
                    var info=new FileInfo(path);
                    if(!info.Exists||info.Length>MaxFileSizeBytes)continue;
                    var header=JsonUtility.FromJson<NameHeader>(File.ReadAllText(path,Encoding.UTF8));
                    if(header!=null&&header.name!=null&&string.Equals(header.name.Trim().Normalize(NormalizationForm.FormC),name,StringComparison.OrdinalIgnoreCase))return path;
                }
                catch(ArgumentException){}
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
            return null;
        }
        public bool Delete(string fileName)
        {
            if(string.IsNullOrWhiteSpace(fileName)||fileName.IndexOfAny(new[]{'/','\\'})>=0||
                Path.GetFileName(fileName)!=fileName||
                !string.Equals(Path.GetExtension(fileName),".json",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Delete requires a JSON base file name.");
            string path=Path.Combine(directory,fileName);
            if(!File.Exists(path))return false;
            File.Delete(path);
            return true;
        }
        public BlueprintData Load(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(new[] {'/', '\\'}) >= 0 ||
                Path.GetFileName(fileName) != fileName ||
                !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Load requires a JSON base file name.");
            string path = Path.Combine(directory, fileName);
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("Blueprint was not found.", path);
            if (info.Length > MaxFileSizeBytes) throw new InvalidDataException("Blueprint exceeds the 16 MiB file size limit.");
            var data = JsonUtility.FromJson<BlueprintData>(File.ReadAllText(path, Encoding.UTF8));
            return Upgrade(data);
        }
        public string[] List()
        {
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            var files = new List<FileInfo>();
            foreach (var path in Directory.GetFiles(directory, "*.json")) files.Add(new FileInfo(path));
            files.Sort((a,b) => { int order=b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);return order!=0?order:StringComparer.Ordinal.Compare(a.Name,b.Name); });
            return files.ConvertAll(f=>f.Name).ToArray();
        }

        public static BlueprintData Upgrade(BlueprintData data)
        {
            Validate(data);
            var next=new BlueprintData{name=data.name};
            foreach(var old in data.parts)
            {
                var p=BlockPlacement.Copy(old);
                if(data.version==1){
                    p.position=new Vector3(p.cell.x,p.cell.y+.5f,p.cell.z);
                    p.rotation=Quaternion.Euler(0,p.yawQuarterTurns*90,0);
                }
                p.rotation=p.rotation.normalized;
                if(p.shape==BlockShape.Core){next.hasCore=true;next.corePartId=p.id;next.shipForward=(p.rotation*Vector3.forward).normalized;}
                next.parts.Add(p);
            }
            WeaponGroups.CopyTo(data,next);
            WeaponGroups.Normalize(next);
            return next;
        }
        public static void Validate(BlueprintData data)
        {
            if(data==null || (data.version!=1 && data.version!=2) || string.IsNullOrWhiteSpace(data.name) || data.parts==null)
                throw new InvalidDataException("Invalid blueprint header or version.");
            int cores=0;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            var cells=new HashSet<Vector3Int>();
            var poses=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in data.parts)
            {
                if(p==null || string.IsNullOrWhiteSpace(p.id) || !ids.Add(p.id))
                    throw new InvalidDataException("Part IDs must be unique and nonempty.");
                if(!Enum.IsDefined(typeof(BlockShape),p.shape))throw new InvalidDataException("Unsupported shape.");
                if(p.shape==BlockShape.Core&&++cores>1)throw new InvalidDataException("每个船体只能放置一个核心。");
                if(p.yawQuarterTurns<0 || p.yawQuarterTurns>3)throw new InvalidDataException("Invalid face rotation.");
                if(data.version==1){
                    foreach(var cell in BlockModules.AllCells(new BlockRecord{shape=p.shape,position=new Vector3(p.cell.x,p.cell.y+.5f,p.cell.z),rotation=Quaternion.Euler(0,p.yawQuarterTurns*90,0)}))
                        if(!cells.Add(cell))throw new InvalidDataException("实体或推进器喷口净空被占用。");
                    continue;
                }
                var r=p.rotation;var position=p.position;
                if(!BlockPlacement.IsCellAligned(position,r))throw new InvalidDataException("Part must fit one standard grid cell.");
                foreach(var cell in BlockModules.AllCells(p))if(!cells.Add(cell))throw new InvalidDataException("实体或推进器喷口净空被占用。");
                if(!Finite(position.x)||!Finite(position.y)||!Finite(position.z)||!Finite(r.x)||!Finite(r.y)||!Finite(r.z)||!Finite(r.w))
                    throw new InvalidDataException("Part pose is not finite.");
                float magnitude=r.x*r.x+r.y*r.y+r.z*r.z+r.w*r.w;
                if(Mathf.Abs(magnitude-1)>.01f)throw new InvalidDataException("Part quaternion must be normalized.");
                r=r.normalized;
                float sign=r.w!=0?Mathf.Sign(r.w):r.x!=0?Mathf.Sign(r.x):r.y!=0?Mathf.Sign(r.y):Mathf.Sign(r.z);
                string key=p.shape+"/"+Number(position.x)+"/"+Number(position.y)+"/"+Number(position.z)+"/"+
                    Number(r.x*sign)+"/"+Number(r.y*sign)+"/"+Number(r.z*sign)+"/"+Number(r.w*sign);
                if(!poses.Add(key))throw new InvalidDataException("Two identical parts share the same pose.");
            }
            if(!BlockConnectivity.IsConnected(data))throw new InvalidDataException("蓝图必须通过面连接组成一个整体。");
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static string Number(float value)=>Math.Round(value,5).ToString("0.#####",System.Globalization.CultureInfo.InvariantCulture);
        static string SafeName(string name)
        {
            var result=new StringBuilder();
            const string invalid="<>:\"/\\|?*";
            foreach(char c in name) result.Append(char.IsControl(c) || invalid.IndexOf(c)>=0 || Array.IndexOf(Path.GetInvalidFileNameChars(),c)>=0?'_':c);
            string safe=result.ToString().Trim().TrimEnd('.',' ');
            if(safe.Length==0) safe="Blueprint";
            string device=safe.Split('.')[0].TrimEnd('.',' ').ToUpperInvariant();
            if(device=="CON" || device=="PRN" || device=="AUX" || device=="NUL" ||
                (device.Length==4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3]>='1' && device[3]<='9'))
                safe="_"+safe;
            if(safe.Length>120) safe=safe.Substring(0,char.IsHighSurrogate(safe[119])?119:120).TrimEnd('.',' ');
            return safe;
        }
    }
}
