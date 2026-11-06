// @role Manage saved ship blueprints in a paginated thumbnail grid with live metrics and confirmed file operations.
// @calls MenuSurface, HangarShipPreview, MenuShipMetrics, ShipWorkspace, BlueprintRepository, EnemyFleetLibrary, UiModalScope, L10n

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Starframe.Construction;
using Starframe.Combat;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.App
{
    /// <summary>Saved designs and the editable working ship are independent. Target rosters live in range setup.</summary>
    public sealed class BlueprintLibraryScreen:MonoBehaviour
    {
        sealed class Entry{public string file;public BlueprintData data;public RectTransform row;}
        readonly List<Entry> entries=new List<Entry>();
        MenuSurface surface;HangarShipPreview preview;ModuleDatabase database;MissionDefinition mission;Action closed;
        RectTransform modal;
        int gridPage;bool renaming;
        TextMeshProUGUI status;TMP_InputField nameInput;
        BlueprintData selection;string selectedFile,query="",notice="",pendingName;
        bool open;
        public string SelectedFile=>selectedFile;
        public bool ModalOpen=>modal;
        public void Initialize(MenuSurface target,HangarShipPreview shipPreview,ModuleDatabase modules,Action onClose,MissionDefinition operation=null)
        {
            surface=target;preview=shipPreview;database=modules;mission=operation;closed=onClose;open=true;
            selection=ShipWorkspace.Ship;L10n.Changed+=LocaleChanged;Build();
        }
        static string Copy(string zh,string en)=>L10n.IsEnglish?en:zh;
        static bool StorageError(Exception error)=>error is IOException||error is InvalidDataException||error is UnauthorizedAccessException||error is ArgumentException;
        void LocaleChanged(){if(open)Build();}
        void Build()
        {
            if(!open||!surface)return;surface.Clear();modal=null;nameInput=null;entries.Clear();preview.AllowOrbit=false;
            var background=MenuSurface.Rect(surface.Root,"BlueprintBackground",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color=FigmaVisuals.Background;background.raycastTarget=false;
            var pageTitle=surface.Label("BlueprintLibraryTitle",Copy("蓝图库","BLUEPRINTS"),37,28,1300,100,48);FigmaVisuals.SetFont(pageTitle,true);pageTitle.textWrappingMode=TextWrappingModes.NoWrap;pageTitle.overflowMode=TextOverflowModes.Overflow;
            try
            {
                foreach(string file in ShipWorkspace.Repository.List())
                {
                    BlueprintData data=null;try{data=ShipWorkspace.Repository.Load(file);}catch(Exception error)when(StorageError(error)){}
                    entries.Add(new Entry{file=file,data=data});
                }
            }catch(Exception error)when(StorageError(error)){notice=L10n.T("app.storageError");}
            var matching=entries.Where(e=>string.IsNullOrWhiteSpace(query)||(e.data?.name??Path.GetFileNameWithoutExtension(e.file)).IndexOf(query.Trim(),StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            int pages=Mathf.Max(1,(matching.Length+11)/12);gridPage=Mathf.Clamp(gridPage,0,pages-1);
            for(int i=gridPage*12;i<Mathf.Min(matching.Length,(gridPage+1)*12);i++)
            {
                var entry=matching[i];int slot=i-gridPage*12;float x=46+(slot%4)*294,y=147+(slot/4)*238;
                var card=surface.Button("PickShip_"+i,"",x,y,267,216,()=>Select(entry));card.interactable=entry.data!=null;entry.row=(RectTransform)card.transform;
                card.GetComponent<UnityEngine.UI.Image>().color=new Color(27/255f,31/255f,45/255f);
                var picture=MenuSurface.Rect(surface.Root,"BlueprintThumbnail_"+i,0,0,1,1).gameObject.AddComponent<UnityEngine.UI.RawImage>();Local(picture,entry.row,0,0,267,181);picture.raycastTarget=false;
                if(entry.data!=null)picture.texture=preview.Thumbnail(entry.data);
                var footer=MenuSurface.Rect(surface.Root,"BlueprintCardFooter",0,0,1,1).gameObject.AddComponent<UnityEngine.UI.Image>();Local(footer,entry.row,0,185,267,31);footer.color=new Color(53/255f,58/255f,74/255f);footer.raycastTarget=false;
                var title=surface.Label("LibraryName_"+i,entry.data?.name??Path.GetFileNameWithoutExtension(entry.file),0,0,1,1,20,GlassUiPalette.Accent);Local(title,entry.row,8,185,136,31);L10n.ApplyUserContentFont(title);title.textWrappingMode=TextWrappingModes.NoWrap;title.alignment=TextAlignmentOptions.MidlineLeft;title.enableAutoSizing=true;title.fontSizeMin=15;title.fontSizeMax=20;
                var cost=surface.Label("LibraryCost_"+i,entry.data!=null?BlueprintCostDisplay.Label(entry.data,database,mission):L10n.T("app.invalidBlueprint"),0,0,1,1,14,GlassUiPalette.Accent);Local(cost,entry.row,147,185,115,31);cost.alignment=TextAlignmentOptions.MidlineRight;cost.textWrappingMode=TextWrappingModes.NoWrap;cost.enableAutoSizing=true;cost.fontSizeMin=11;cost.fontSizeMax=14;
                if(selectedFile==entry.file){var outline=card.gameObject.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=GlassUiPalette.Accent;outline.effectDistance=new Vector2(3,3);}
            }
            if(matching.Length==0)surface.Label("NoSearchResults",entries.Count==0?L10n.T("app.noBlueprints"):L10n.T("app.ui.noSearchResults"),64,320,1000,100,28,Color.white);
            Button("LibraryPreviousPage",Copy("上一页","PREVIOUS"),49,878,148,45,()=>{gridPage--;Build();},19).interactable=gridPage>0;
            Button("LibraryNextPage",Copy("下一页","NEXT PAGE"),213,878,148,45,()=>{gridPage++;Build();},19).interactable=gridPage<pages-1;
            surface.Label("LibraryPage",(gridPage+1)+"/"+pages,383,886,120,30,20);
            var search=surface.Input("ShipPickerSearch",query,46,946,326,43,value=>query=value);
            search.onEndEdit.AddListener(value=>{query=value;gridPage=0;Build();});
            var placeholder=surface.Label("BlueprintSearchPlaceholder",L10n.T("app.ui.searchBlueprints"),0,0,1,1,18,GlassUiPalette.Muted);
            placeholder.rectTransform.SetParent(search.textViewport,false);placeholder.rectTransform.anchorMin=Vector2.zero;placeholder.rectTransform.anchorMax=Vector2.one;placeholder.rectTransform.offsetMin=placeholder.rectTransform.offsetMax=Vector2.zero;search.placeholder=placeholder;
            Button("LibraryCurrentShip",Copy("当前舰船","WORKING SHIP"),391,946,236,43,()=>{selectedFile=null;selection=ShipWorkspace.Ship;Build();},19);
            Button("LibrarySaveCurrent",Copy("保存当前舰船…","SAVE WORKING SHIP…"),643,946,278,43,ShowSave,19);
            var import=Button("LibraryImportEnemies",Copy("导入敌舰模板","IMPORT ENEMIES"),938,946,272,43,ImportEnemyTemplates,19);import.interactable=EnemyFleetLibrary.LoadTemplates().Length>0;
            surface.Hint(import,Copy("复制敌舰模板到蓝图库；同名自动编号，当前舰船不变。","Copy enemy templates into the library. Duplicate names are numbered; your working ship is preserved."));
            if(selection==null)selection=ShipWorkspace.Ship;
            surface.Panel("LibrarySelectionGlass",1432,37,462,208);
            var name=surface.Label("LibrarySelectedName",selection.name,1449,54,425,66,36,GlassUiPalette.Accent);FigmaVisuals.SetFont(name,true);L10n.ApplyUserContentFont(name);name.textWrappingMode=TextWrappingModes.NoWrap;
            var metrics=MenuShipMetrics.Calculate(selection,preview.Tuning,mission);MenuShipMetrics.Summary(surface,"LibraryStat",metrics,1457,138,426,20);
            surface.Panel("LibraryPerformanceGlass",1432,255,462,728);
            surface.Label("LibraryPerformanceHeading",Copy("舰船性能","SHIP PERFORMANCE"),1466,282,402,42,22);
            var detail=surface.Label("LibraryPerformance",MenuShipMetrics.Detail(metrics),1469,330,401,630,18,Color.white);
            detail.enableAutoSizing=true;detail.fontSizeMin=13;detail.fontSizeMax=18;detail.overflowMode=TextOverflowModes.Truncate;
            Button("LibraryLoad",Copy("载入","LOAD"),1432,995,147,65,LoadSelection,22,GlassUiPalette.Accent);
            var rename=Button("LibraryRename",Copy("重命名","RENAME"),1589,995,147,65,ShowRename,22);rename.interactable=selectedFile!=null;
            var delete=Button("LibraryDelete",Copy("删除","DELETE"),1747,995,147,65,ConfirmDelete,22,UiTheme.Current.danger);delete.interactable=selectedFile!=null;
            Button("ClosePicker",Copy("返回","BACK"),42,1008,115,45,Close,20);
            status=surface.Label("BlueprintLibraryStatus",notice,190,1019,1190,34,18,GlassUiPalette.Accent);
        }
        UnityEngine.UI.Button Button(string name,string text,float x,float y,float width,float height,Action action,float size=20,Color? color=null)
        {
            var button=surface.Button(name,text,x,y,width,height,action);var label=button.GetComponentInChildren<TextMeshProUGUI>();UiStyle.ApplyText(label,size);label.color=color??Color.white;FigmaVisuals.SetFont(label,true);return button;
        }
        void ImportEnemyTemplates()
        {
            if(modal)return;
            try{
                int count=EnemyFleetLibrary.ImportInto(ShipWorkspace.Repository,EnemyFleetLibrary.LoadTemplates()).Length;
                query="";notice=Copy($"已导入 {count} 份敌舰模板。选择后载入即可编辑。",$"Imported {count} enemy templates. Select and load one to edit it.");Build();
            }catch(Exception error)when(StorageError(error)){notice=L10n.T("app.storageError");Build();}
        }
        static void Local(Component child,RectTransform parent,float x,float y,float width,float height)
        {
            var rect=(RectTransform)child.transform;rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
        }
        void Select(Entry entry){if(entry.data==null||modal)return;selectedFile=entry.file;selection=ShipWorkspace.Clone(entry.data);notice="";Build();}
        void LoadSelection()
        {
            if(modal)return;if(selectedFile==null){Close();return;}string file=selectedFile;
            Action load=()=>{
                try{ShipWorkspace.Select(file);Close();}
                catch(Exception error)when(StorageError(error)){ClearModal();notice=L10n.T("app.storageError");Build();}
            };
            if(ShipWorkspace.Dirty)Confirm(Copy("载入并替换当前草稿？","Load and replace the working draft?"),L10n.T("app.replaceDraftBody"),Copy("载入蓝图","Load ship"),load);
            else load();
        }
        void ConfirmDelete()
        {
            if(selectedFile==null||modal)return;string file=selectedFile,name=selection.name;
            Confirm(Copy("删除这份蓝图？","Delete this blueprint?"),Copy($"将删除“{name}”。当前编辑中的舰船不会被移除。",$"Delete “{name}”? Your working ship will be preserved."),L10n.T("ui.delete"),()=>{
                try{
                    ShipWorkspace.Repository.Delete(file);
                    if(string.Equals(Path.GetFileName(ShipWorkspace.SourceFile),file,StringComparison.OrdinalIgnoreCase))ShipWorkspace.Set(ShipWorkspace.Ship,true);
                    selectedFile=null;selection=ShipWorkspace.Ship;notice=Copy("蓝图已删除。","Blueprint deleted.");ClearModal();Build();
                }catch(Exception error)when(StorageError(error)){ClearModal();notice=L10n.T("app.storageError");Build();}
            });
        }
        void ShowSave()
        {
            renaming=false;pendingName=ShipWorkspace.Ship.name;BuildSave();
        }
        void ShowRename(){if(selectedFile==null||modal)return;renaming=true;pendingName=selection.name;BuildSave();}
        void BuildSave()
        {
            ClearModal();CreateModal();
            InModal(surface.Panel("LibrarySaveGlass",520,324,880,432,true));
            InModal(surface.Label("LibrarySaveTitle",renaming?Copy("重命名蓝图","Rename blueprint"):Copy("保存当前舰船","Save working ship"),560,360,800,48,28));
            InModal(surface.Label("LibraryNameLabel",Copy("蓝图名称","Blueprint name"),560,432,800,28,17,GlassUiPalette.Muted));
            InModal(surface.Label("LibraryNameHint",Copy("输入名称。已有同名蓝图时会再次询问覆盖。","Enter a name. Existing designs require overwrite confirmation."),560,538,800,68,16,GlassUiPalette.Muted));
            var save=InModal(surface.Button("LibrarySaveConfirm",renaming?Copy("重命名","Rename"):Copy("保存蓝图","Save blueprint"),980,654,380,56,SaveNamed,true));
            nameInput=InModal(surface.Input("LibraryNameInput",pendingName??"",560,474,800,48,value=>{pendingName=value;save.interactable=!string.IsNullOrWhiteSpace(value);}));
            nameInput.characterLimit=64;save.interactable=!string.IsNullOrWhiteSpace(pendingName);
            InModal(surface.Button("LibraryModalCancel",L10n.T("app.cancel"),560,654,396,56,ClearModal));
            UiModalScope.Protect(surface.Root,modal);nameInput.Select();nameInput.ActivateInputField();
        }
        void SaveNamed()
        {
            if(string.IsNullOrWhiteSpace(pendingName))return;
            if(renaming){RenameNamed();return;}
            var data=ShipWorkspace.Ship;data.name=pendingName.Trim();
            try{
                if(ShipWorkspace.Repository.NameExists(data.name)){
                    string name=data.name;Confirm(Copy("覆盖同名蓝图？","Overwrite existing blueprint?"),Copy($"“{name}”已存在。确认后将用当前舰船替换它。",$"“{name}” already exists. Replace it with your working ship?"),Copy("确认覆盖","Overwrite"),()=>Save(data,true),BuildSave);return;
                }
                Save(data,false);
            }catch(Exception error)when(StorageError(error)){ClearModal();notice=L10n.T("app.storageError");Build();}
        }
        void RenameNamed()
        {
            string oldFile=selectedFile;var data=ShipWorkspace.Clone(selection);data.name=pendingName.Trim();
            if(string.Equals(data.name,selection.name,StringComparison.Ordinal)){ClearModal();return;}
            try
            {
                // Renaming may never overwrite another independent saved design.
                if(ShipWorkspace.Repository.NameExists(data.name)){notice=Copy("名称已被使用，请选择其他名称。","That name is already used. Choose another name.");ClearModal();Build();return;}
                string saved=Path.GetFileName(ShipWorkspace.Repository.Save(data));
                if(!string.Equals(saved,oldFile,StringComparison.OrdinalIgnoreCase))ShipWorkspace.Repository.Delete(oldFile);
                if(string.Equals(Path.GetFileName(ShipWorkspace.SourceFile),oldFile,StringComparison.OrdinalIgnoreCase))ShipWorkspace.Set(ShipWorkspace.Ship,ShipWorkspace.Dirty,saved);
                selectedFile=saved;selection=data;notice=Copy("蓝图已重命名。","Blueprint renamed.");ClearModal();Build();
            }
            catch(Exception error)when(StorageError(error)){ClearModal();notice=L10n.T("app.storageError");Build();}
        }
        void Save(BlueprintData data,bool overwrite)
        {
            try{
                string path=ShipWorkspace.Repository.Save(data,overwrite);ShipWorkspace.Set(data,false,Path.GetFileName(path));
                selectedFile=Path.GetFileName(path);selection=ShipWorkspace.Clone(data);notice=Copy("蓝图已保存。","Blueprint saved.");ClearModal();Build();
            }catch(Exception error)when(StorageError(error)){ClearModal();notice=L10n.T("app.storageError");Build();}
        }
        void Confirm(string title,string body,string accept,Action confirmed,Action cancelled=null)
        {
            ClearModal();CreateModal();
            InModal(surface.Panel("LibraryConfirmGlass",520,324,880,432,true));
            InModal(surface.Label("LibraryConfirmTitle",title,560,360,800,58,28));
            InModal(surface.Label("LibraryConfirmBody",body,560,442,800,144,18,GlassUiPalette.Muted));
            InModal(surface.Button("LibraryModalCancel",L10n.T("app.cancel"),560,654,396,56,()=>{ClearModal();cancelled?.Invoke();}));
            InModal(surface.Button("LibraryConfirmAccept",accept,980,654,380,56,confirmed,true));
            UiModalScope.Protect(surface.Root,modal);
        }
        void CreateModal()
        {
            modal=MenuSurface.Rect(surface.Root,"BlueprintLibraryModal",0,0,1920,1080);var blocker=modal.gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.55f);blocker.raycastTarget=true;preview.AllowOrbit=false;
        }
        T InModal<T>(T component)where T:Component{component.transform.SetParent(modal,false);return component;}
        void ClearModal(){if(modal){modal.gameObject.SetActive(false);Destroy(modal.gameObject);}modal=null;nameInput=null;if(preview)preview.AllowOrbit=true;}
        void Update()
        {
            if(!open||Keyboard.current==null||!Keyboard.current.escapeKey.wasPressedThisFrame)return;
            if(modal)ClearModal();else Close();
        }
        void Close(){if(!open)return;open=false;L10n.Changed-=LocaleChanged;closed?.Invoke();}
        void OnDisable(){open=false;L10n.Changed-=LocaleChanged;if(preview)preview.AllowOrbit=true;}
        void OnDestroy(){L10n.Changed-=LocaleChanged;}
    }
}

