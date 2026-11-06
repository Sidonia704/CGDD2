using System;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;
public static class InstallWarningLocalization7
{
    public static object Run()
    {
        var collection=LocalizationEditorSettings.GetStringTableCollection("GameUI");
        var rows=new[]{
            new[]{"error.mountSupport","武器或装甲的宽度方向需要全部底部接触面。","Support the full width of the weapon or armour from underneath."},
            new[]{"error.mirrorClearance","镜像两侧占用格或净空互相重叠。","The mirrored modules overlap occupied cells or clearance areas."},
            new[]{"error.mirrorOverlap","镜像实体模型互相重叠。","The mirrored solid modules overlap."},
            new[]{"error.mirrorBlocksWeapon","镜像组件会遮挡另一侧的开火路径。","The mirrored module would block the other weapon's firing path."},
            new[]{"error.mirrorNeedCore","镜像编辑需要先安装核心。","Install a core before using mirror editing."},
            new[]{"error.mirrorGrid","此组件的镜像朝向无法保持标准格子。","This module cannot be mirrored while preserving the standard grid."},
            new[]{"error.mirrorDisconnected","镜像删除会使舰体失去连接。","Removing the mirrored pair would disconnect the ship."},
            new[]{"error.mirrorInvalid","镜像编辑失败：两侧必须同时满足安装条件。","Mirror edit blocked: both sides must fit and remain connected."},
            new[]{"error.mirrorOtherSide","镜像另一侧无法安装：{0}","The mirrored side cannot be installed: {0}"}
        };
        foreach(string code in new[]{"en","zh-Hans"})
        {
            var table=(StringTable)collection.GetTable(code);
            foreach(var row in rows){string value=row[code=="en"?2:1];var entry=table.GetEntry(row[0])??table.AddEntry(row[0],value);entry.Value=value;}
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(collection);EditorUtility.SetDirty(collection.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(InstallWarningLocalization7),collection);
        AssetDatabase.SaveAssets();return new{keys=rows.Length,locales=new[]{"en","zh-Hans"}};
    }
}
