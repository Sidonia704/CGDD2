// @role Present the assembly workbench, native catalogue, blueprint dialogs, live ship performance and construction feedback.
// @calls ShipEditorController, ShipAssembly, AssemblyModuleCatalogue, AssemblyWeaponGroups, FlightTuning, ModuleDatabase, BlockModules, WeaponDefinition, FlareDefinition, L10n, GlassUiPalette, FigmaVisuals

using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Starframe.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Palette=Starframe.Flight.GlassUiPalette;
namespace Starframe.Construction
{
    public sealed partial class ShipEditorUI : MonoBehaviour
    {
        [SerializeField] Shader backdropBlurShader;
        [SerializeField] Starframe.Flight.FlightTuning flightTuning;
        [SerializeField] Camera presentationCamera;
        ShipEditorController controller;
        Camera worldCamera;
        RenderTexture sceneTexture;
        Canvas canvas;
        UnityEngine.UI.ScrollRect moduleLibraryScroll;
        TMP_FontAsset font;
        BlockShape localizedShape;
        UnityEngine.UI.RawImage background;
        readonly List<UnityEngine.UI.RawImage> glass=new List<UnityEngine.UI.RawImage>();
        readonly List<Material> runtimeMaterials=new List<Material>();
        readonly Dictionary<BlockShape,UnityEngine.UI.Button> shapeButtons=new Dictionary<BlockShape,UnityEngine.UI.Button>();
        readonly Dictionary<BlockShape,UnityEngine.UI.Image> shapeSelectionMarks=new Dictionary<BlockShape,UnityEngine.UI.Image>();
        readonly UnityEngine.UI.Button[] fileButtons=new UnityEngine.UI.Button[6];
        [SerializeField] Sprite[] moduleIcons,moduleSelectedIcons;
        [SerializeField] Sprite rectangleIcon,triangleIcon;
        readonly Dictionary<BlockShape,UnityEngine.UI.Image> shapeIcons=new Dictionary<BlockShape,UnityEngine.UI.Image>();
        readonly TextMeshProUGUI[] fileLabels=new TextMeshProUGUI[6];
        TextMeshProUGUI moduleStatsText;
        TextMeshProUGUI moduleNameText,blueprintIdentityText,rotationReadoutText,assemblyCostText;
        const float ReferenceAssemblyBudget=30000;
        bool showShipPerformance;
        ShipAssembly performanceAssembly;
        Starframe.Flight.FlightTuning cachedPerformanceTuning;
        Starframe.Flight.ModuleDatabase cachedPerformanceDatabase;
        Starframe.Flight.ShipPerformance cachedShipPerformance;
        float cachedAssemblyCost,cachedGeneration,cachedEnergyCapacity,cachedWeaponDemand,cachedShieldDemand,cachedPropulsionDemand;
        int cachedWeaponPowerRevision;
        UnityEngine.UI.ScrollRect shipSummaryScroll;GameObject shipSummaryDetails;
        TextMeshProUGUI shipSummaryHeading,shipSummaryCompact,shipSummaryToggleText;
        bool shipSummaryExpanded=true,summaryLayoutDirty,summaryScrollInitialized;
        int assemblyPerformanceRevision,cachedAssemblyPerformanceRevision=-1,cachedFlightRevision=-1,cachedDatabaseRevision=-1;
        TextMeshProUGUI countText,modeText,shapeText,coordinateText,rotationText,idText,statusText,headerMode,pageText;
        TMP_InputField nameField,moduleSearch;
        TextMeshProUGUI moduleSearchPlaceholder,moduleSearchEmpty,shipSummaryText,placeToolText,deleteToolText;
        UnityEngine.UI.Button placeTool,deleteTool;
        UnityEngine.UI.ScrollRect moduleInfoScroll;
        sealed class CatalogueItem { public BlockShape shape;public RectTransform slot,name,mark;public Vector2 icon; }
        sealed class CatalogueSection { public RectTransform root;public UnityEngine.UI.LayoutElement size;public TextMeshProUGUI count;public string category;public readonly List<CatalogueItem> items=new List<CatalogueItem>(); }
        readonly List<CatalogueSection> catalogue=new List<CatalogueSection>();
        UnityEngine.UI.Button loadButton,previousButton,nextButton,deleteButton,actionConfirmButton;
        TextMeshProUGUI actionConfirmButtonLabel;
        string[] files=Array.Empty<string>();
        int page,selectedFile;
        bool initialized,placementReasonShown;
        string lastPlacementFailure;
        string rawStatusMessage;bool rawStatusError;
        GameObject modalRoot,saveDialog,loadDialog,actionConfirmRoot;
        TextMeshProUGUI modalError,emptyFiles,actionConfirmTitle,actionConfirmMessage;
        Action confirmAction;
        string pendingDeleteFile;
        bool savingDialog;
        bool assemblyLayout;
        int confirmOpenedFrame;
        string blueprintName;
        static Color Ink=>Palette.Ink;
        static Color Muted=>Palette.Muted;
        static Color Accent=>Palette.Accent;
        public TMP_FontAsset Font=>font;
        public Starframe.Flight.ModuleDatabase Database=>flightTuning?flightTuning.database:null;
        public bool IsDialogOpen=>modalRoot && modalRoot.activeSelf;
        public bool EditingName=>IsDialogOpen || (nameField && nameField.isFocused) || (moduleSearch && moduleSearch.isFocused);
        public string BlueprintName {get=>IsDialogOpen&&savingDialog?nameField.text:blueprintName??L10n.T("editor.blueprintDefault");set{blueprintName=value;UpdateBlueprintIdentity();}}
        void InitializeLegacy(ShipEditorController editor,Camera camera)
        {
            controller=editor;worldCamera=camera;ObservePerformanceAssembly(controller.Assembly);
            assemblyLayout=Starframe.Presentation.UiTheme.Current.assemblyLayout;
            font=L10n.CurrentFont;
            L10n.Changed+=RefreshLocalizedContent;Starframe.App.GameSettings.Changed+=OnUiSettingsChanged;
            var go=new GameObject("ConstructionCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            go.layer=5;go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();
            if(!presentationCamera)throw new InvalidOperationException("Presentation camera is not assigned.");
            presentationCamera.enabled=true;canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=presentationCamera;canvas.planeDistance=1;
            worldCamera.cullingMask&=~(1<<5);
            var scaler=go.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;UiStyle.ApplyScaler(scaler);
            if(FindFirstObjectByType<EventSystem>()==null)
            {
                var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform,false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var root=(RectTransform)canvas.transform;
            background=MakeRect(root,"WorldViewport",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            background.raycastTarget=false;
            var header=FullGroup(root,"Header");
            var toolbox=FullGroup(root,"Toolbox");
            var footer=FullGroup(root,"ViewportHelp");
            Glass(header,"HeaderGlass",assemblyLayout?32:37,assemblyLayout?32:36,assemblyLayout?520:476,assemblyLayout?112:100,Palette.Glass);
            Glass(toolbox,"ModuleLibraryGlass",assemblyLayout?1504:1515,assemblyLayout?32:10,assemblyLayout?384:376,assemblyLayout?600:778,Palette.Glass);
            Glass(toolbox,"ModuleInfoGlass",assemblyLayout?1504:1515,assemblyLayout?648:798,assemblyLayout?384:376,assemblyLayout?320:204,Palette.Glass);
            Glass(toolbox,"SaveButtonGlass",assemblyLayout?1504:1515,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,Palette.Glass);
            Glass(toolbox,"LoadButtonGlass",assemblyLayout?1704:1708,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,Palette.Glass);
            Label(header,"Title","editor.title",assemblyLayout?56:60,assemblyLayout?44:55,333,34,assemblyLayout?23:21,Ink);
            if(assemblyLayout)
            {
                Label(header,"BlueprintCaption","editor.blueprintCaption",56,84,72,19,12,Muted);
                blueprintIdentityText=Label(header,"BlueprintIdentity",BlueprintName,128,81,280,22,15,Ink);
                blueprintIdentityText.overflowMode=TextOverflowModes.Ellipsis;L10n.ApplyUserContentFont(blueprintIdentityText);
            }
            headerMode=Label(header,"ModeBadge","editor.mode.place",assemblyLayout?464:430,assemblyLayout?48:53,56,28,16,Accent);

            if(assemblyLayout){
                moduleSearch=NameInput(toolbox,1528,52,304,42);moduleSearch.name="ModuleSearch";
                moduleSearch.characterLimit=40;moduleSearchPlaceholder=(TextMeshProUGUI)moduleSearch.placeholder;
                L10n.Unbind(moduleSearchPlaceholder);moduleSearchPlaceholder.text=Copy("搜索组件或分类…","Search modules or category…");
                Button(toolbox,"ClearModuleSearch","×",1838,52,30,42,()=>{moduleSearch.text=string.Empty;moduleSearch.ActivateInputField();});
                moduleSearch.onValueChanged.AddListener(_=>FilterCatalogue());
                moduleSearchEmpty=Label(toolbox,"ModuleSearchEmpty","",1538,144,306,92,16,Muted);moduleSearchEmpty.textWrappingMode=TextWrappingModes.Normal;
            }
            BuildModuleLibrary(toolbox);

            Button(toolbox,"ShowModuleInfo","editor.moduleInfo",assemblyLayout?1528:1532,assemblyLayout?664:809,153,32,()=>SetShape(localizedShape));
            Button(toolbox,"ShowShipPerformance","editor.shipInfo",assemblyLayout?1689:1693,assemblyLayout?664:809,153,32,ShowShipPerformance);
            Line(toolbox,assemblyLayout?1528:1532,assemblyLayout?704:842,319,2);
            if(assemblyLayout)
            {
                moduleNameText=Label(toolbox,"ModuleName","editor.selectModule",1528,718,330,25,17,Ink);
                moduleStatsText=Label(toolbox,"ModuleStatistics","",1528,752,330,198,14,Ink,TextAlignmentOptions.TopLeft);
            }
            else moduleStatsText=Label(toolbox,"ModuleStatistics","",1540,853,320,138,14,Color.white);
            moduleStatsText.textWrappingMode=TextWrappingModes.Normal;
            if(assemblyLayout)BuildModuleInfoScroll(toolbox);
            Button(toolbox,"SaveBlueprint","editor.saveBlueprint",assemblyLayout?1504:1515,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,OpenSaveDialog);
            Button(toolbox,"LoadBlueprint",Copy("蓝图库","Blueprint library"),assemblyLayout?1704:1708,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,OpenLoadDialog);
            countText=Label(header,"Count",L10n.T("editor.parts",0,""),assemblyLayout?448:402,assemblyLayout?88:83,91,17,12,Muted,TextAlignmentOptions.Right);
            assemblyCostText=Label(header,"AssemblyCost","",assemblyLayout?56:60,assemblyLayout?112:110,assemblyLayout?472:430,24,13,Muted);
            var hidden=FullGroup(root,"EditorData");hidden.gameObject.SetActive(false);
            modeText=Label(hidden,"Mode","",0,0,10,10,12,Ink);shapeText=Label(hidden,"Shape","",0,0,10,10,12,Ink);
            coordinateText=Label(hidden,"Coordinates","",0,0,10,10,12,Ink);
            rotationText=Label(hidden,"Rotation","",0,0,10,10,12,Ink);idText=Label(hidden,"ID","",0,0,10,10,12,Ink);
            BuildDialogs(root);
            Glass(footer,"TestButtonGlass",assemblyLayout?32:37,assemblyLayout?936:877,assemblyLayout?144:170,56,Palette.Glass);
            Button(footer,"EnterFlightRange",assemblyLayout?"editor.testFlight":"editor.test",assemblyLayout?32:37,assemblyLayout?936:877,assemblyLayout?144:170,56,controller.EnterFlightRange);
            if(assemblyLayout)
            {
                Glass(footer,"WorkspaceStatusGlass",192,936,824,56,Palette.Glass);
                statusText=Label(footer,"Status","editor.selectHint",212,950,550,22,15,Ink);
                rotationReadoutText=Label(footer,"RotationReadout",L10n.T("editor.rotation",0),784,950,212,22,13,Muted,TextAlignmentOptions.Right);
                Label(footer,"MouseHelp","editor.mouseHelp",32,1008,1000,18,13,Muted);
                Label(footer,"BuildHelp","editor.buildHelp",32,1032,1100,16,13,Muted);
            }
            else
            {
                statusText=Label(footer,"Status","editor.selectHint",37,955,1120,26,18,Ink);
                Label(footer,"MouseHelp","editor.mouseHelp",37,1000,1130,21,15,Muted);
                Label(footer,"BuildHelp","editor.buildHelp",37,1029,1250,21,15,Muted);
            }
            if(assemblyLayout){
                BuildShipReadout(footer);
                placeTool=Button(header,"PlaceTool",Copy("放置","Place"),32,176,140,44,()=>{if(controller.DeleteMode)controller.ToggleDelete();});
                deleteTool=Button(header,"DeleteTool",Copy("删除  X","Delete  X"),32,228,140,44,()=>{if(!controller.DeleteMode)controller.ToggleDelete();});
                placeToolText=header.Find("PlaceToolText").GetComponent<TextMeshProUGUI>();deleteToolText=header.Find("DeleteToolText").GetComponent<TextMeshProUGUI>();
                Button(header,"FrameShipTool",Copy("聚焦  F","Frame ship  F"),32,280,140,44,()=>controller.Orbit.Frame(controller.Assembly.ContentBounds()));
                SetMode(controller.DeleteMode);
            }
            initialized=true;FilterCatalogue();ResizeTarget();Canvas.ForceUpdateCanvases();
            moduleLibraryScroll.StopMovement();moduleLibraryScroll.verticalNormalizedPosition=1;
            foreach(var child in go.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
        }
        static string Copy(string zh,string en)=>L10n.IsEnglish?en:zh;
        void BuildModuleInfoScroll(RectTransform toolbox)
        {
            var root=MakeRect(toolbox,"ModuleInfoScroll",1528,752,340,198);moduleInfoScroll=root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var viewport=LocalRect(root,"Viewport",0,0,0,0);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=viewport.offsetMax=Vector2.zero;
            var image=viewport.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Color.clear;image.canvasRenderer.cullTransparentMesh=false;viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var content=LocalRect(viewport,"Content",0,0,330,198);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,198);
            moduleStatsText.rectTransform.SetParent(content,false);moduleStatsText.rectTransform.anchorMin=Vector2.zero;moduleStatsText.rectTransform.anchorMax=Vector2.one;moduleStatsText.rectTransform.offsetMin=Vector2.zero;moduleStatsText.rectTransform.offsetMax=new Vector2(-10,0);
            moduleStatsText.overflowMode=TextOverflowModes.Overflow;moduleInfoScroll.viewport=viewport;moduleInfoScroll.content=content;moduleInfoScroll.horizontal=false;moduleInfoScroll.vertical=true;moduleInfoScroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;moduleInfoScroll.scrollSensitivity=26;
        }
        void RefreshModuleInfoHeight()
        {
            if(!moduleInfoScroll)return;
            if(figmaRoot){float extent=Mathf.Max(174,moduleStatsText.GetPreferredValues(moduleStatsText.text,330,Mathf.Infinity).y+8);moduleInfoScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,extent);moduleStatsText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,extent);moduleInfoScroll.StopMovement();moduleInfoScroll.verticalNormalizedPosition=1;return;}
            float width=330;float height=Mathf.Max(198,moduleStatsText.GetPreferredValues(moduleStatsText.text,width,Mathf.Infinity).y+8);
            moduleInfoScroll.content.sizeDelta=new Vector2(0,height);moduleInfoScroll.StopMovement();moduleInfoScroll.verticalNormalizedPosition=1;
        }
        void FilterCatalogue(bool resetScroll=true)
        {
            float scrollPosition=moduleLibraryScroll?moduleLibraryScroll.verticalNormalizedPosition:1;
            string query=moduleSearch?moduleSearch.text.Trim():string.Empty;int shown=0;
            foreach(var group in catalogue){
                float x=10,y=36,row=0;int count=0;
                foreach(var item in group.items){
                    string name=L10n.T("module."+item.shape),shortName=L10n.T(ShapeShortName(item.shape));
                    bool visible=query.Length==0||name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||shortName.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||L10n.T("category."+group.category).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||item.shape.ToString().IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;
                    item.slot.gameObject.SetActive(visible);item.name.gameObject.SetActive(visible);item.mark.gameObject.SetActive(visible&&item.shape==localizedShape);if(!visible)continue;
                    if(x>10&&x+item.icon.x>318){y+=row+10;x=10;row=0;}
                    PlaceLocal(item.slot,x,y,item.icon.x,item.icon.y+22);PlaceLocal(item.name,x,y+item.icon.y+3,item.icon.x,18);PlaceLocal(item.mark,x+(item.icon.x-18)/2,y+item.icon.y+24,18,2);
                    row=Mathf.Max(row,item.icon.y+30);x+=item.icon.x+10;count++;
                }
                group.root.gameObject.SetActive(count>0);group.size.minHeight=group.size.preferredHeight=Mathf.Max(112,y+row+14);group.count.text=count.ToString("00");shown+=count;
            }
            if(moduleSearchEmpty){moduleSearchEmpty.text=Copy("未找到组件。尝试名称或分类。","No matching modules. Try a name or category.");moduleSearchEmpty.gameObject.SetActive(shown==0);}
            if(moduleLibraryScroll){
                // Set the scroll position against the completed layout. Locale callbacks can arrive
                // after input; they must not send a player who just scrolled back to the top.
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(moduleLibraryScroll.content);
                moduleLibraryScroll.StopMovement();moduleLibraryScroll.verticalNormalizedPosition=resetScroll?1:scrollPosition;
            }
        }
        void BuildModuleLibrary(RectTransform toolbox)
        {
            var scrollRoot=MakeRect(toolbox,"ModuleLibraryScroll",assemblyLayout?1524:1536,assemblyLayout?110:24,assemblyLayout?344:350,assemblyLayout?500:744);
            var scroll=scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            moduleLibraryScroll=scroll;
            scroll.horizontal=false;scroll.vertical=true;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity=28;scroll.inertia=true;scroll.decelerationRate=.135f;
            var viewport=LocalRect(scrollRoot,"Viewport",0,0,0,0);
            viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=Vector2.zero;viewport.offsetMax=new Vector2(-22,0);
            var viewportImage=viewport.gameObject.AddComponent<UnityEngine.UI.Image>();viewportImage.color=Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=false;
            var content=LocalRect(viewport,"Content",0,0,0,0);
            content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            var layout=content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            layout.padding=new RectOffset(0,0,0,8);
            var fitter=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit=UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport=viewport;scroll.content=content;
            var bar=LocalRect(scrollRoot,"ModuleScrollbar",0,0,12,0);
            bar.anchorMin=new Vector2(1,0);bar.anchorMax=Vector2.one;bar.offsetMin=new Vector2(-12,0);bar.offsetMax=Vector2.zero;
            var hitArea=bar.gameObject.AddComponent<UnityEngine.UI.Image>();hitArea.color=Color.clear;hitArea.canvasRenderer.cullTransparentMesh=false;
            var track=LocalRect(bar,"Track",0,0,0,0);track.anchorMin=new Vector2(.42f,0);track.anchorMax=new Vector2(.58f,1);track.offsetMin=track.offsetMax=Vector2.zero;
            var trackImage=track.gameObject.AddComponent<UnityEngine.UI.Image>();trackImage.color=new Color(.92f,.96f,1,.14f);trackImage.raycastTarget=false;
            var sliding=LocalRect(bar,"SlidingArea",0,0,0,0);sliding.anchorMin=new Vector2(.25f,0);sliding.anchorMax=new Vector2(.75f,1);sliding.offsetMin=sliding.offsetMax=Vector2.zero;
            var handle=LocalRect(sliding,"Handle",0,0,0,0);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;
            var handleImage=handle.gameObject.AddComponent<UnityEngine.UI.Image>();handleImage.color=new Color(.92f,.96f,1,.65f);
            var scrollbar=bar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();scrollbar.direction=UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect=handle;scrollbar.targetGraphic=handleImage;scrollbar.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=UnityEngine.UI.ScrollRect.ScrollbarVisibility.Permanent;
            var groups=new Dictionary<string,List<BlockShape>>();
            foreach(BlockShape shape in Enum.GetValues(typeof(BlockShape)))
            {
                string category=CategoryFor(shape);
                if(!groups.TryGetValue(category,out var values)){values=new List<BlockShape>();groups.Add(category,values);}
                values.Add(shape);
            }
            string[] order={"Hull","Propulsion","Core","Power","Weapon","Shield"};
            string[] labels={"category.Hull","category.Propulsion","category.Core","category.Power","category.Weapon","category.Shield"};
            for(int g=0;g<order.Length;g++)
            {
                if(!groups.TryGetValue(order[g],out var values)||values.Count==0)continue;
                values.Sort((a,b)=>ShapeOrder(a).CompareTo(ShapeOrder(b)));
                BuildRefinedCategory(content,order[g],labels[g],values);
            }
        }
        void BuildRefinedCategory(RectTransform content,string category,string title,List<BlockShape> values)
        {
            const float left=10,right=318,gap=10,top=36;
            var placements=new List<Vector4>(values.Count);
            float x=left,y=top,rowHeight=0;
            foreach(var shape in values)
            {
                var size=IconSize(shape);
                if(x>left&&x+size.x>right){y+=rowHeight+10;x=left;rowHeight=0;}
                placements.Add(new Vector4(x,y,size.x,size.y));
                rowHeight=Mathf.Max(rowHeight,size.y+24);
                x+=size.x+gap;
            }
            float height=Mathf.Max(112,y+rowHeight+10);
            var section=LocalRect(content,category+"Category",0,0,0,height);
            var item=section.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            item.minHeight=item.preferredHeight=height;
            LocalLabel(section,category+"Title",title,10,0,230,26,16,Ink);
            var counter=LocalLabel(section,category+"Count",values.Count.ToString("00"),284,3,34,20,12,Muted,TextAlignmentOptions.Right);
            var group=new CatalogueSection{root=section,size=item,count=counter,category=category};catalogue.Add(group);
            var divider=LocalRect(section,"Divider",10,29,308,1).gameObject.AddComponent<UnityEngine.UI.Image>();
            divider.color=new Color(Ink.r,Ink.g,Ink.b,.10f);divider.raycastTarget=false;
            for(int i=0;i<values.Count;i++)
            {
                var shape=values[i];var place=placements[i];float width=place.z,heightIcon=place.w;
                var button=LocalButton(section,shape+"Slot",place.x,place.y,width,heightIcon+18,()=>controller.SelectShape(shape));
                var plate=(UnityEngine.UI.Image)button.targetGraphic;
                plate.color=Color.clear;plate.canvasRenderer.cullTransparentMesh=false;plate.raycastTarget=true;
                var image=LocalRect((RectTransform)button.transform,shape+"Icon",0,0,width,heightIcon).gameObject.AddComponent<UnityEngine.UI.Image>();
                image.preserveAspect=true;image.raycastTarget=false;image.color=Color.white;
                int index=(int)shape;
                if(moduleIcons!=null&&index>=0&&index<moduleIcons.Length)image.sprite=moduleIcons[index];
                var definition=GetDefinition(shape);
                if(definition&&definition.icon)image.sprite=definition.icon;
                var caption=LocalLabel(section,shape+"Name",ShapeShortName(shape),place.x,place.y+heightIcon+3,width,18,12,Muted,TextAlignmentOptions.Center);
                var mark=LocalRect(section,shape+"SelectedMark",place.x+(width-18)/2,place.y+heightIcon+21,18,2).gameObject.AddComponent<UnityEngine.UI.Image>();
                mark.color=Accent;mark.raycastTarget=false;mark.gameObject.SetActive(false);shapeSelectionMarks.Add(shape,mark);
                shapeButtons.Add(shape,button);shapeIcons.Add(shape,image);
                group.items.Add(new CatalogueItem{shape=shape,slot=(RectTransform)button.transform,name=caption.rectTransform,mark=mark.rectTransform,icon=new Vector2(width,heightIcon)});
            }
        }
        static string ShapeShortName(BlockShape shape)=>"module.short."+shape;
        TextMeshProUGUI LocalLabel(RectTransform parent,string name,string text,float x,float y,float width,float height,float size,Color color,TextAlignmentOptions alignment=TextAlignmentOptions.Left)
        {
            var tmp=LocalRect(parent,name,x,y,width,height).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font=font;UiStyle.ApplyText(tmp,size);tmp.color=color;tmp.alignment=alignment;
            if(L10n.IsKey(text))L10n.Bind(tmp,text);else{tmp.text=text;L10n.ApplyFont(tmp);}
            tmp.enableAutoSizing=false;tmp.textWrappingMode=TextWrappingModes.NoWrap;tmp.overflowMode=TextOverflowModes.Ellipsis;
            tmp.raycastTarget=false;tmp.margin=Vector4.zero;return tmp;
        }
        UnityEngine.UI.Button LocalButton(RectTransform parent,string name,float x,float y,float width,float height,Action action)
        {
            var rect=LocalRect(parent,name,x,y,width,height);
            var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.72f,.82f,.88f,.10f);
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
            var colors=UiStyle.ControlColors();
            button.colors=colors;button.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            button.onClick.AddListener(()=>{Starframe.App.GameAudio.Click();action();});button.transition=UnityEngine.UI.Selectable.Transition.None;
            return button;
        }
        static RectTransform LocalRect(RectTransform parent,string name,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;var rect=(RectTransform)go.transform;rect.SetParent(parent,false);PlaceLocal(rect,x,y,width,height);return rect;
        }
        static void PlaceLocal(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
        static int ShapeOrder(BlockShape shape)
        {
            BlockShape[] order={BlockShape.Cube,BlockShape.Wedge,BlockShape.Slab,BlockShape.Pyramid,BlockShape.Block5,BlockShape.Block8,BlockShape.Block7,BlockShape.Block6,
                BlockShape.Thruster1,BlockShape.Thruster2,BlockShape.Thruster3,BlockShape.Thruster4,BlockShape.Thruster5,BlockShape.Thruster6,
                BlockShape.Core,BlockShape.Power,BlockShape.Power2,
                BlockShape.Gun1,BlockShape.Gun2,BlockShape.Gun3,BlockShape.Laser,BlockShape.Laser2,BlockShape.Laser3,
                BlockShape.Missile1,BlockShape.Missile2,BlockShape.Missile3,BlockShape.VLS2,BlockShape.VLS1,BlockShape.Railgun1,BlockShape.Railgun2,BlockShape.Railgun3,
                BlockShape.Sword1,BlockShape.Sword2,BlockShape.Shield,BlockShape.Armor1,BlockShape.Armor2,BlockShape.Shield2,BlockShape.Armor4,BlockShape.Armor3};
            int index=Array.IndexOf(order,shape);return index<0?(int)shape:index;
        }
        static Vector2 IconSize(BlockShape shape)
        {
            switch(shape)
            {
                case BlockShape.Block8:case BlockShape.Power2:case BlockShape.Shield2:return new Vector2(96,96);
                case BlockShape.Block7:case BlockShape.Armor2:return new Vector2(100,48);
                case BlockShape.Armor3:return new Vector2(144,48);
                case BlockShape.Armor4:return new Vector2(144,96);
                case BlockShape.Thruster6:return new Vector2(48,48);
                case BlockShape.Thruster2:return new Vector2(96,52);
                case BlockShape.Thruster3:return new Vector2(104,52);
                case BlockShape.Thruster4:return new Vector2(208,52);
                case BlockShape.Thruster5:return new Vector2(152,100);
                case BlockShape.Gun1:case BlockShape.Laser:case BlockShape.Missile1:case BlockShape.Sword1:return new Vector2(132,56);
                case BlockShape.Gun2:case BlockShape.Laser2:case BlockShape.Missile2:case BlockShape.Sword2:return new Vector2(156,56);
                case BlockShape.Gun3:case BlockShape.Laser3:return new Vector2(220,88);
                case BlockShape.Missile3:return new Vector2(250,64);
                case BlockShape.VLS2:return new Vector2(88,120);
                case BlockShape.VLS1:return new Vector2(120,120);
                case BlockShape.Railgun1:return new Vector2(184,52);
                case BlockShape.Railgun2:return new Vector2(240,60);
                case BlockShape.Railgun3:return new Vector2(296,80);
                default:return new Vector2(48,48);
            }
        }
        static float IconWidth(BlockShape shape)=>IconSize(shape).x;
        string CategoryFor(BlockShape shape)
        {
            var definition=GetDefinition(shape);
            if(definition)
            {
                string value=definition.category.ToString();
                if(value=="Hull"||value=="Propulsion"||value=="Weapon"||value=="Core"||value=="Power"||value=="Shield")return value;
            }
            if(shape==BlockShape.Core)return "Core";
            if(shape==BlockShape.Shield||shape==BlockShape.Shield2||BlockModules.IsArmor(shape))return "Shield";
            if(shape==BlockShape.Power||shape==BlockShape.Power2)return "Power";
            if(BlockModules.IsThruster(shape))return "Propulsion";
            if(BlockModules.IsWeapon(shape))return "Weapon";
            return "Hull";
        }
        Starframe.Flight.ModuleDefinition GetDefinition(BlockShape shape)=>flightTuning?flightTuning.GetDefinition(shape):null;
        string BuildRefinedModuleSummary(Starframe.Flight.ModuleDefinition definition,Starframe.Flight.ModuleStats stats,float mass,float hp)
        {
            var entries=new List<string>();
            if(definition)entries.Add(definition.category==Starframe.Flight.ModuleCategory.Hull&&definition.cost==0?L10n.T("module.stat.freeHull"):L10n.T("module.stat.cost",definition.cost));
            entries.Add(L10n.T("module.stat.mass",mass));entries.Add(L10n.T("module.stat.hp",hp));
            float thrust=definition?definition.thrustNewtons:stats!=null?stats.thrustNewtons:0;
            string directionalThrust=null;
            if(definition&&definition.shape==BlockShape.Thruster6)
                directionalThrust=L10n.T("module.stat.multithrust",definition.localExhaustDirections.Length,thrust/1000,BlockModules.PlacementClearance(definition.shape));
            else if(thrust>0)entries.Add(L10n.T("module.stat.thrust",thrust/1000));
            if(definition&&BlockModules.IsThruster(definition.shape)&&flightTuning){
                entries.Add(Copy($"持续推进 {flightTuning.PropulsionPowerDemandWatts:N0} W（整舰）",$"SUSTAINED {flightTuning.PropulsionPowerDemandWatts:N0} W (SHIP)"));
                entries.Add(Copy($"加力推进 {flightTuning.BoostPowerDemandWatts:N0} W（整舰）",$"BOOST {flightTuning.BoostPowerDemandWatts:N0} W (SHIP)"));
            }
            if(definition)
            {
                if(definition.storageJoules>0)entries.Add(L10n.T("module.stat.storage",definition.storageJoules));
                if(definition.generationWatts>0)entries.Add(L10n.T("module.stat.generation",definition.generationWatts));
            }
            if(definition&&definition.shieldCapacity>0)
            {
                entries.Add(L10n.T("module.stat.shield",definition.shieldCapacity,definition.shieldRadius));
                entries.Add(L10n.T("module.stat.recovery",definition.shieldRecoveryPerSecond,definition.shieldRecoveryDelay));
                if(definition.shieldPowerWatts>0)entries.Add(L10n.T("module.stat.power",definition.shieldPowerWatts));
            }
            entries.Add(L10n.T("module.stat.cells",BlockModules.LocalCells(localizedShape).Length));
            var weapon=definition?definition.weapon:null;
            if(weapon)
            {
                if(weapon.shotsPerSecond>0)entries.Add(L10n.T("module.stat.rate",weapon.shotsPerSecond));
                float playerDamage=weapon.damage*(flightTuning&&flightTuning.damageTuning?flightTuning.damageTuning.GetPlayerMultiplier(weapon.delivery):1);
                if(playerDamage>0)entries.Add(L10n.T("module.stat.damage",playerDamage));
                if(weapon.sustainedPowerWatts>0)entries.Add(L10n.T("module.stat.power",weapon.sustainedPowerWatts));
                if(weapon.delivery!=Starframe.Flight.WeaponDelivery.Beam&&weapon.projectileSpeed>0)entries.Add(L10n.T("module.stat.projectile",weapon.projectileSpeed));
                entries.Add(L10n.T("module.stat.range",weapon.range));
                if(weapon.delivery==Starframe.Flight.WeaponDelivery.GuidedMissile){entries.Add(L10n.T("module.stat.lock",weapon.missileLockSeconds));entries.Add(L10n.T("module.stat.reload",weapon.reloadSeconds));}
            }
            var rows=new List<string>();
            for(int i=0;i<entries.Count;i+=2)
                rows.Add(i+1<entries.Count?entries[i]+"  ·  "+entries[i+1]:entries[i]);
            if(directionalThrust!=null)rows.Insert(Mathf.Min(1,rows.Count),directionalThrust);
            if(definition&&definition.countermeasure)
            {
                var flare=definition.countermeasure;
                rows.Add(L10n.T("module.stat.flareSalvo",flare.burstCount,flare.cooldownSeconds));
                rows.Add(L10n.T("module.stat.flareDecoyChance",flare.decoyChance*100));
                rows.Add(L10n.T("module.stat.flareClearance"));
            }
            if(localizedShape==BlockShape.Thruster6)rows.Add(L10n.T("module.mount.thruster6"));
            else if(BlockModules.IsArmor(localizedShape))rows.Add(L10n.T("module.mount.armor"));
            else if(BlockModules.IsWeapon(localizedShape))rows.Add(L10n.T("module.mount.base"));
            return string.Join("\n",rows);
        }
        void UpdateBlueprintIdentity()
        {
            if(blueprintIdentityText){blueprintIdentityText.text=string.IsNullOrWhiteSpace(blueprintName)?L10n.T("editor.blueprintDefault"):blueprintName;L10n.ApplyUserContentFont(blueprintIdentityText);}
        }
        RectTransform FullGroup(RectTransform parent,string name)=>MakeRect(parent,name,0,0,1920,1080);
        static RectTransform MakeRect(RectTransform parent,string name,float x,float y,float w,float h)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;var rt=go.GetComponent<RectTransform>();
            rt.SetParent(parent,false);
            rt.anchorMin=new Vector2(x/1920f,(1080-y-h)/1080f);rt.anchorMax=new Vector2((x+w)/1920f,(1080-y)/1080f);
            rt.offsetMin=rt.offsetMax=Vector2.zero;return rt;
        }
        TextMeshProUGUI Label(RectTransform parent,string name,string text,float x,float y,float w,float h,float size,Color color,TextAlignmentOptions alignment=TextAlignmentOptions.Left)
        {
            var tmp=MakeRect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font=font;UiStyle.ApplyText(tmp,size);tmp.color=color;tmp.alignment=alignment;
            if(L10n.IsKey(text))L10n.Bind(tmp,text);else{tmp.text=text;L10n.ApplyFont(tmp);}
            tmp.enableAutoSizing=false;tmp.textWrappingMode=TextWrappingModes.NoWrap;tmp.overflowMode=size>=20?TextOverflowModes.Overflow:TextOverflowModes.Ellipsis;
            if(name.EndsWith("Hint",StringComparison.Ordinal)||name.EndsWith("Help",StringComparison.Ordinal)||name.EndsWith("Message",StringComparison.Ordinal)||name=="DialogError")
            {tmp.textWrappingMode=TextWrappingModes.Normal;tmp.overflowMode=TextOverflowModes.Overflow;}
            tmp.raycastTarget=false;tmp.margin=Vector4.zero;return tmp;
        }
        TextMeshProUGUI ValueRow(RectTransform parent,string name,float y,string value)
        {Label(parent,name+"Label",name,1544,y,109,25,15,Muted);return Label(parent,name+"Value",value,1666,y,197,25,16,Ink);}
        void Line(RectTransform parent,float x,float y,float w,float h)
        {
            var image=MakeRect(parent,"Divider",x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color=new Color(.9f,.94f,.97f,.24f);image.raycastTarget=false;
        }
        void Glass(RectTransform parent,string name,float x,float y,float w,float h,Color tint)
        {
            var shadow=MakeRect(parent,name+"Shadow",x+3,y+4,w,h).gameObject.AddComponent<UnityEngine.UI.Image>();
            shadow.color=new Color(0,0,0,.08f);shadow.raycastTarget=false;
            var image=MakeRect(parent,name,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            var material=new Material(backdropBlurShader);Palette.Apply(material,tint);
            material.SetVector("_PanelUVRect",new Vector4(x/1920,(1080-y-h)/1080,w/1920,h/1080));
            image.material=material;image.uvRect=new Rect(x/1920,(1080-y-h)/1080,w/1920,h/1080);
            image.raycastTarget=true;glass.Add(image);runtimeMaterials.Add(material);
        }
        UnityEngine.UI.Button Button(RectTransform parent,string name,string text,float x,float y,float w,float h,Action action)
        {
            var rt=MakeRect(parent,name,x,y,w,h);
            var image=rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            bool iconOnly=name.EndsWith("Slot",StringComparison.Ordinal)||name.StartsWith("HullSlot",StringComparison.Ordinal)||name.StartsWith("ThrusterSlot",StringComparison.Ordinal);
            bool destructive=name=="DeleteBlueprint"||(name=="ActionConfirmYes"&&(actionConfirmButtonLabel&&actionConfirmButtonLabel.text==L10n.T("ui.delete")));
            bool primary=name=="SaveBlueprint"||name=="ConfirmSave"||name=="ConfirmLoad"||name=="EnterFlightRange"||name=="ActionConfirmYes";
            Color baseTint=Palette.Button;
            image.color=iconOnly?Color.clear:(assemblyLayout&&primary?Palette.Action:baseTint);
            if(iconOnly)image.canvasRenderer.cullTransparentMesh=false;
            var button=rt.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
            var colors=UiStyle.ControlColors();
            button.colors=colors;button.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            button.onClick.AddListener(()=>{Starframe.App.GameAudio.Click();action();});
            if(iconOnly)button.transition=UnityEngine.UI.Selectable.Transition.None;
            if(text.Length>0)
            {
                var buttonLabel=Label(parent,name+"Text",text,x+5,y+4,w-10,h-8,16,Ink,TextAlignmentOptions.Center);
                if(name=="ShowModuleInfo"||name=="ShowShipPerformance")UiStyle.ApplyText(buttonLabel,14);
                if(assemblyLayout&&primary)buttonLabel.color=Palette.ActionInk;
                else if(assemblyLayout&&destructive)buttonLabel.color=Starframe.Presentation.UiTheme.Current.amber;
            }
            return button;
        }
        static Color ContrastingText(Color background)
        {
            float luminance=.2126f*background.r+.7152f*background.g+.0722f*background.b;
            return luminance>.52f?new Color(.07f,.11f,.12f,1):Color.white;
        }
        TMP_InputField NameInput(RectTransform parent,float x,float y,float w,float h)
        {
            var rt=MakeRect(parent,"BlueprintName",x,y,w,h);
            var image=rt.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=assemblyLayout?Palette.Button:new Color(.02f,.07f,.14f,.6f);
            var field=rt.gameObject.AddComponent<TMP_InputField>();
            var viewport=new GameObject("TextViewport",typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.gameObject.layer=5;viewport.SetParent(rt,false);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=new Vector2(12,4);viewport.offsetMax=new Vector2(-12,-4);
            var textObject=new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI));
            textObject.layer=5;var textRect=(RectTransform)textObject.transform;textRect.SetParent(viewport,false);textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=textRect.offsetMax=Vector2.zero;
            var text=textObject.GetComponent<TextMeshProUGUI>();text.font=font;text.fontSize=17;text.color=Ink;text.alignment=TextAlignmentOptions.MidlineLeft;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;L10n.ApplyUserContentFont(text);
            field.targetGraphic=image;field.textViewport=viewport;field.textComponent=text;field.fontAsset=font;
            field.characterLimit=64;field.lineType=TMP_InputField.LineType.SingleLine;field.text=string.Empty;
            UiStyle.ApplyText(text,17);
            field.placeholder=Label(parent,"NamePlaceholder","editor.namePlaceholder",x+12,y+8,w-24,h-16,17,Muted);
            field.caretColor=Accent;field.customCaretColor=true;field.selectionColor=assemblyLayout?new Color(Accent.r,Accent.g,Accent.b,.35f):new Color(.3f,.6f,.85f,.35f);
            return field;
        }

        bool LegacyWorkspaceContains(Vector2 screen)
        {
            if(IsDialogOpen)return false;
            float x=screen.x/Mathf.Max(1,Screen.width),y=1-screen.y/Mathf.Max(1,Screen.height);
            if(assemblyLayout)
            {
                float px=x*1920f,py=y*1080f;
                if(px<0||px>=1920||py<0||py>=1080)return false;
                if(px>=1504)return false;
                if(px>=628&&px<=1472&&py>=32&&py<=80)return false;
                if(px>=32&&px<=552&&py>=32&&py<=144)return false;
                if(px>=32&&px<=176&&py>=936&&py<=992)return false;
                if(px>=192&&px<=1016&&py>=936&&py<=992)return false;
                if(px>=32&&px<=1132&&py>=1008&&py<=1048)return false;
                if(px>=1040&&px<=1472&&py>=936&&py<=992)return false;
                if(shipSummaryExpanded&&px>=1040&&px<=1472&&py>=568&&py<=928)return false;
                if(px>=32&&px<=172&&py>=176&&py<=324)return false;
                return true;
            }
            if(x<0 || x>=1515f/1920 || y<0 || y>1)return false;
            if(x>=37f/1920&&x<=207f/1920&&y>=877f/1080&&y<=933f/1080)return false;
            return !(x>=37f/1920 && x<=513f/1920 && y>=36f/1080 && y<=136f/1080);
        }

        public void SetShape(BlockShape shape)
        {
            localizedShape=shape;showShipPerformance=false;
            UiStyle.ApplyText(moduleStatsText,14);
            var definition=GetDefinition(shape);
            string displayName=ShipEditorController.ShapeName(shape);
            L10n.Bind(shapeText,"module."+shape);
            var stats=flightTuning?flightTuning.Get(shape):null;
            float mass=definition?definition.massKg:stats!=null?stats.massKg:0;
            float hp=definition?definition.maxHp:stats!=null?stats.maxHp:0;
            float thrust=definition?definition.thrustNewtons:stats!=null?stats.thrustNewtons:0;
            if(assemblyLayout)
            {
                if(moduleNameText)L10n.Bind(moduleNameText,"module."+shape);
                moduleStatsText.text=BuildRefinedModuleSummary(definition,stats,mass,hp);
            }
            else moduleStatsText.text=displayName+"\n\n"+BuildRefinedModuleSummary(definition,stats,mass,hp);
            L10n.ApplyFont(moduleStatsText);RefreshModuleInfoHeight();
            foreach(var pair in shapeIcons)
            {
                bool active=pair.Key==shape;var iconDefinition=GetDefinition(pair.Key);
                var sprite=iconDefinition?(active?iconDefinition.selectedIcon:iconDefinition.icon):null;
                int index=(int)pair.Key;
                if(!sprite&&moduleIcons!=null&&index>=0&&index<moduleIcons.Length)sprite=active&&moduleSelectedIcons!=null&&index<moduleSelectedIcons.Length?moduleSelectedIcons[index]:moduleIcons[index];
                if(sprite)pair.Value.sprite=sprite;
                pair.Value.color=Color.white;
                if(assemblyLayout&&shapeSelectionMarks.TryGetValue(pair.Key,out var mark))
                {
                    mark.color=Accent;mark.gameObject.SetActive(active&&shapeButtons.TryGetValue(pair.Key,out var slot)&&slot.gameObject.activeSelf);
                }
            }
            RefreshFigmaModule();
        }
        public void SetMode(bool delete)
        {
            L10n.Bind(modeText,delete?"editor.mode.delete":"editor.mode.place");L10n.Bind(headerMode,delete?"editor.mode.delete":"editor.mode.place");
            modeText.color=headerMode.color=assemblyLayout?
                (delete?Starframe.Presentation.UiTheme.Current.danger:Starframe.Presentation.UiTheme.Current.accent):
                (delete?new Color(1,.44f,.4f):Accent);
            RefreshModeTools(delete);
            RefreshFigmaMode(delete);
        }
        void RefreshModeTools(bool delete)
        {
            if(placeTool){((UnityEngine.UI.Image)placeTool.targetGraphic).color=delete?Palette.Button:Palette.Action;placeToolText.color=delete?Muted:Ink;}
            if(deleteTool){((UnityEngine.UI.Image)deleteTool.targetGraphic).color=delete?Palette.Action:Palette.Button;deleteToolText.color=delete?UiTheme.Current.danger:Muted;}
        }
        public void UpdateStats(int count,bool dirty)
        {
            L10n.Bind(countText,"editor.parts",count,dirty?" *":"");
            if(!EnsurePerformanceCache())return;
            L10n.Bind(assemblyCostText,"editor.assemblyCost",cachedAssemblyCost,ReferenceAssemblyBudget);
            RefreshShipReadout();
            if(showShipPerformance)ShowShipPerformance();
            RefreshFigmaStats();
        }
        void BuildShipReadout(RectTransform parent)
        {
            // Keep a compact footer visible; the transparent detail sheet expands above it.
            var details=FullGroup(parent,"ShipSummaryDetails");shipSummaryDetails=details.gameObject;
            Glass(details,"ShipSummaryDetailGlass",1040,568,432,360,Palette.Glass);
            var root=MakeRect(details,"ShipSummaryScroll",1056,584,400,328);shipSummaryScroll=root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var viewport=LocalRect(root,"Viewport",0,0,0,0);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=Vector2.zero;viewport.offsetMax=new Vector2(-12,0);
            var image=viewport.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Color.clear;image.canvasRenderer.cullTransparentMesh=false;viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var content=LocalRect(viewport,"Content",0,0,0,328);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,328);
            shipSummaryText=LocalLabel(content,"ShipSummary","",0,0,388,328,14,Ink,TextAlignmentOptions.TopLeft);
            shipSummaryText.rectTransform.anchorMin=Vector2.zero;shipSummaryText.rectTransform.anchorMax=Vector2.one;shipSummaryText.rectTransform.offsetMin=shipSummaryText.rectTransform.offsetMax=Vector2.zero;
            shipSummaryText.textWrappingMode=TextWrappingModes.Normal;shipSummaryText.overflowMode=TextOverflowModes.Overflow;shipSummaryText.lineSpacing=2;
            var rail=LocalRect(root,"Scrollbar",0,0,12,0);rail.anchorMin=new Vector2(1,0);rail.anchorMax=Vector2.one;rail.offsetMin=new Vector2(-12,0);rail.offsetMax=Vector2.zero;
            var hit=rail.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=new Color(Ink.r,Ink.g,Ink.b,.035f);
            var handle=LocalRect(rail,"Handle",0,0,0,0);handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=new Vector2(4,0);handle.offsetMax=new Vector2(-4,0);
            var handleImage=handle.gameObject.AddComponent<UnityEngine.UI.Image>();handleImage.color=new Color(Ink.r,Ink.g,Ink.b,.38f);
            var scrollbar=rail.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();scrollbar.handleRect=handle;scrollbar.targetGraphic=handleImage;scrollbar.direction=UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            shipSummaryScroll.viewport=viewport;shipSummaryScroll.content=content;shipSummaryScroll.horizontal=false;shipSummaryScroll.vertical=true;shipSummaryScroll.verticalScrollbar=scrollbar;shipSummaryScroll.verticalScrollbarVisibility=UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;shipSummaryScroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;shipSummaryScroll.scrollSensitivity=26;
            Glass(parent,"ShipSummaryGlass",1040,936,432,56,Palette.Glass);
            shipSummaryHeading=Label(parent,"ShipSummaryHeading","",1060,941,340,27,15,Ink);
            shipSummaryCompact=Label(parent,"ShipSummaryCompact","",1060,970,340,21,12,Muted);
            Button(parent,"ToggleShipSummary","−",1420,946,34,32,()=>SetShipSummaryExpanded(!shipSummaryExpanded));
            shipSummaryToggleText=parent.Find("ToggleShipSummaryText").GetComponent<TextMeshProUGUI>();
        }
        void SetShipSummaryExpanded(bool expanded)
        {
            shipSummaryExpanded=expanded;if(shipSummaryDetails)shipSummaryDetails.SetActive(expanded);
            if(shipSummaryToggleText)shipSummaryToggleText.text=expanded?"−":"+";
        }
        bool EnsurePerformanceCache()
        {
            if(!controller||!controller.Assembly||!flightTuning)return false;ObservePerformanceAssembly(controller.Assembly);
            if(PerformanceCacheIsCurrent())return true;
            cachedShipPerformance=Starframe.Flight.ShipPerformance.CalculateAssembly(performanceAssembly,flightTuning);
            cachedAssemblyCost=cachedGeneration=cachedEnergyCapacity=cachedWeaponDemand=cachedShieldDemand=cachedPropulsionDemand=0;
            foreach(var block in performanceAssembly.Blocks){
                var definition=GetDefinition(block.Record.shape);if(!definition)continue;
                cachedAssemblyCost+=Mathf.Max(0,definition.cost);cachedGeneration+=Mathf.Max(0,definition.generationWatts);cachedEnergyCapacity+=Mathf.Max(0,definition.storageJoules);
                if(definition.weapon)cachedWeaponDemand+=Mathf.Max(0,definition.weapon.sustainedPowerWatts);
                if(BlockModules.IsThruster(definition.shape))cachedPropulsionDemand=flightTuning.PropulsionPowerDemandWatts;
                if(definition.shieldCapacity>0&&definition.shieldRecoveryPerSecond>0)cachedShieldDemand+=Mathf.Max(0,definition.shieldPowerWatts);
            }
            cachedPerformanceTuning=flightTuning;cachedPerformanceDatabase=flightTuning.database;
            cachedAssemblyPerformanceRevision=assemblyPerformanceRevision;cachedFlightRevision=flightTuning.Revision;
            cachedDatabaseRevision=cachedPerformanceDatabase?cachedPerformanceDatabase.Revision:0;cachedWeaponPowerRevision=WeaponPowerRevision();return true;
        }
        int WeaponPowerRevision()
        {
            int hash=17;var database=flightTuning?flightTuning.database:null;if(!database)return hash;
            // Definitions are a small fixed catalogue. This does not walk hundreds of ship parts.
            foreach(var definition in database.modules)if(definition&&definition.weapon)hash=unchecked(hash*31+definition.weapon.sustainedPowerWatts.GetHashCode());return hash;
        }
        static Vector3 WeakerAxes(Vector3 positive,Vector3 negative)=>new Vector3(Mathf.Min(Mathf.Abs(positive.x),Mathf.Abs(negative.x)),Mathf.Min(Mathf.Abs(positive.y),Mathf.Abs(negative.y)),Mathf.Min(Mathf.Abs(positive.z),Mathf.Abs(negative.z)));
        void RefreshShipReadout()
        {
            if(!shipSummaryText||cachedShipPerformance==null)return;var p=cachedShipPerformance;
            const float gravity=9.80665f;var positive=p.accelerationPositive/gravity;var negative=p.accelerationNegative/gravity;
            var rates=WeakerAxes(p.angularRatePositive,p.angularRateNegative);var angular=WeakerAxes(p.angularAccelerationPositive,p.angularAccelerationNegative);
            float boost=flightTuning?Mathf.Max(0,flightTuning.BoostPowerDemandWatts):0,peak=cachedWeaponDemand+cachedShieldDemand+boost;
            string warning=ColorUtility.ToHtmlStringRGB(peak>cachedGeneration+.01f?UiTheme.Current.amber:Ink),muted=ColorUtility.ToHtmlStringRGB(Muted);
            string pair(string a,string b,float av,float ab,float bv,float bb)=>$"{a} {av:0.00} / {ab:0.00}  ·  {b} {bv:0.00} / {bb:0.00}";
            string rotation(string axis,float rate,float acceleration)=>$"{axis}  {rate:0.0} °/s  ·  {acceleration:0.0} °/s²";
            var rows=new List<string>{
                Copy($"质量 {p.totalMass/1000f:0.0} t  ·  总耐久 {p.totalHp:N0} HP",$"MASS {p.totalMass/1000f:0.0} t  ·  TOTAL HULL {p.totalHp:N0} HP"),
                Copy($"供电 {cachedGeneration:N0} W  ·  容量 {cachedEnergyCapacity:N0} J",$"OUTPUT {cachedGeneration:N0} W  ·  CAPACITY {cachedEnergyCapacity:N0} J"),
                Copy($"持续需求 {cachedWeaponDemand+cachedPropulsionDemand:N0} W  ·  闲置 0 W",$"SUSTAINED {cachedWeaponDemand+cachedPropulsionDemand:N0} W  ·  IDLE 0 W"),
                $"<color=#{warning}>"+Copy($"峰值需求 {peak:N0} W",$"PEAK DEMAND {peak:N0} W")+"</color>",
                Copy($"峰值含加力 {boost:N0} W + 护盾回充 {cachedShieldDemand:N0} W",$"Includes boost {boost:N0} W + shield recharge {cachedShieldDemand:N0} W"),
                Copy($"统一限速 {p.maxSpeed:0} m/s  ·  加力 ×{flightTuning.boostMultiplier:0.##}",$"SPEED CAP {p.maxSpeed:0} m/s  ·  BOOST ×{flightTuning.boostMultiplier:0.##}"),
                $"<color=#{muted}>"+Copy("各向 加速度 / 制动能力（g）","DIRECTION · ACCEL / BRAKE (g)")+"</color>",
                pair(Copy("前","FWD"),Copy("后","AFT"),positive.z,negative.z,negative.z,positive.z),
                pair(Copy("左","LEFT"),Copy("右","RIGHT"),negative.x,positive.x,positive.x,negative.x),
                pair(Copy("上","UP"),Copy("下","DOWN"),positive.y,negative.y,negative.y,positive.y),
                $"<color=#{muted}>"+Copy("旋转速度 / 角加速度","ROTATION · RATE / ACCELERATION")+"</color>",
                rotation(Copy("俯仰","PITCH"),rates.x,angular.x),rotation(Copy("偏航","YAW"),rates.y,angular.y),rotation(Copy("滚转","ROLL"),rates.z,angular.z),
                Copy($"满速制动 前 / 后 {PerformanceDistance(p.maxSpeedStoppingDistancePositive.z)} / {PerformanceDistance(p.maxSpeedStoppingDistanceNegative.z)} m",$"FULL-SPEED STOP F / A {PerformanceDistance(p.maxSpeedStoppingDistancePositive.z)} / {PerformanceDistance(p.maxSpeedStoppingDistanceNegative.z)} m"),
                Copy($"转弯半径 俯仰 / 偏航 {PerformanceDistance(p.turningRadiusAtMaxSpeed.x)} / {PerformanceDistance(p.turningRadiusAtMaxSpeed.y)} m",$"TURN RADIUS P / Y {PerformanceDistance(p.turningRadiusAtMaxSpeed.x)} / {PerformanceDistance(p.turningRadiusAtMaxSpeed.y)} m"),
                $"<color=#{muted}><size=90%>"+Copy("单轴独立值；同时输入共享推力。峰值按所有武器同时工作估算。","Single-axis values; combined inputs share thrust. Peak assumes all weapons active.")+"</size></color>"
            };
            shipSummaryText.text=string.Join("\n",rows);L10n.ApplyFont(shipSummaryText);
            shipSummaryHeading.text=Copy("舰船性能与供能","SHIP HANDLING & POWER");shipSummaryCompact.text=Copy($"{p.totalMass/1000f:0.0} t  ·  {p.totalHp:N0} 总耐久",$"{p.totalMass/1000f:0.0} t  ·  {p.totalHp:N0} TOTAL HP");
            RefreshShipReadoutHeight();
        }
        void RefreshShipReadoutHeight()
        {
            if(!shipSummaryScroll||!shipSummaryText)return;
            float position=summaryScrollInitialized&&shipSummaryScroll.content.rect.height>shipSummaryScroll.viewport.rect.height+1?shipSummaryScroll.verticalNormalizedPosition:1;
            shipSummaryText.enableAutoSizing=false;
            float height=Mathf.Max(328,shipSummaryText.GetPreferredValues(shipSummaryText.text,388,Mathf.Infinity).y+6);shipSummaryScroll.content.sizeDelta=new Vector2(0,height);shipSummaryScroll.verticalNormalizedPosition=position;summaryScrollInitialized=true;
        }
        void OnUiSettingsChanged(){summaryLayoutDirty=true;RefreshFigmaStats();}
        void ShowShipPerformance()
        {
            if(!EnsurePerformanceCache())return;showShipPerformance=true;
            var performance=cachedShipPerformance;
            var positive=performance.accelerationPositive;var negative=performance.accelerationNegative;
            var angular=new Vector3(
                Mathf.Min(Mathf.Abs(performance.angularRatePositive.x),Mathf.Abs(performance.angularRateNegative.x)),
                Mathf.Min(Mathf.Abs(performance.angularRatePositive.y),Mathf.Abs(performance.angularRateNegative.y)),
                Mathf.Min(Mathf.Abs(performance.angularRatePositive.z),Mathf.Abs(performance.angularRateNegative.z)));
            var rows=new List<string>{
                L10n.T("editor.performance.speed",performance.maxSpeed,flightTuning.boostMultiplier),
                L10n.T("editor.performance.forwardBack",positive.z,negative.z),
                L10n.T("editor.performance.leftRight",negative.x,positive.x),
                L10n.T("editor.performance.upDown",positive.y,negative.y),
                L10n.T("editor.performance.stopping",PerformanceDistance(performance.maxSpeedStoppingDistancePositive.z),PerformanceDistance(performance.maxSpeedStoppingDistanceNegative.x),PerformanceDistance(performance.maxSpeedStoppingDistancePositive.y)),
                L10n.T("editor.performance.stoppingOther",PerformanceDistance(performance.maxSpeedStoppingDistanceNegative.z),PerformanceDistance(performance.maxSpeedStoppingDistancePositive.x),PerformanceDistance(performance.maxSpeedStoppingDistanceNegative.y)),
                L10n.T("editor.performance.turnRate",angular.x,angular.y,angular.z),
                L10n.T("editor.performance.turnRadius",PerformanceDistance(performance.turningRadiusAtMaxSpeed.x),PerformanceDistance(performance.turningRadiusAtMaxSpeed.y))
            };
            if(moduleNameText)L10n.Bind(moduleNameText,"editor.performance.title");
            UiStyle.ApplyText(moduleStatsText,14);
            moduleStatsText.text=string.Join("\n",rows);L10n.ApplyFont(moduleStatsText);RefreshModuleInfoHeight();
        }
        void ObservePerformanceAssembly(ShipAssembly assembly)
        {
            if(performanceAssembly==assembly)return;
            if(performanceAssembly)performanceAssembly.Changed-=InvalidateAssemblyPerformance;
            performanceAssembly=assembly;
            if(performanceAssembly)performanceAssembly.Changed+=InvalidateAssemblyPerformance;
            InvalidateAssemblyPerformance();
        }
        void InvalidateAssemblyPerformance(){unchecked{assemblyPerformanceRevision++;}}
        bool PerformanceCacheIsCurrent()
        {
            if(cachedShipPerformance==null||!controller||performanceAssembly!=controller.Assembly||cachedPerformanceTuning!=flightTuning||!flightTuning)return false;
            var database=flightTuning.database;
            return cachedAssemblyPerformanceRevision==assemblyPerformanceRevision&&cachedFlightRevision==flightTuning.Revision&&
                cachedPerformanceDatabase==database&&cachedDatabaseRevision==(database?database.Revision:0)&&cachedWeaponPowerRevision==WeaponPowerRevision();
        }
        static string PerformanceDistance(float value)=>float.IsNaN(value)||float.IsInfinity(value)?L10n.T("editor.performance.infinite"):value.ToString("0");
        public void SetRotation(int degrees)
        {
            rotationText.text=degrees+"°   /   R";
            if(rotationReadoutText)L10n.Bind(rotationReadoutText,"editor.rotation",degrees);
        }
        public void SetHover(BlockRecord part)
        {
            if(part==null){coordinateText.text="—";idText.text="ID  —";ClearPlacementReason();return;}
            var c=part.position;coordinateText.text=c.x.ToString("0.##")+" , "+c.y.ToString("0.##")+" , "+c.z.ToString("0.##");
            idText.text=string.IsNullOrEmpty(part.id)?L10n.T("editor.preview",ShipEditorController.ShapeName(part.shape)):"ID  "+part.id.Substring(0,Math.Min(12,part.id.Length));
            if(!controller.DeleteMode&&!controller.HasValidPreview&&!string.IsNullOrEmpty(controller.Assembly.PlacementFailure)){
                string failure=controller.Assembly.PlacementFailure;if(!placementReasonShown||lastPlacementFailure!=failure){string key=L10n.LegacyKey(failure);if(key!=null)SetStatusKey(key,true);else SetStatus(failure,true);lastPlacementFailure=failure;placementReasonShown=true;}
            }else ClearPlacementReason();
        }
        void ClearPlacementReason(){if(!placementReasonShown)return;placementReasonShown=false;lastPlacementFailure=null;SetStatusKey(controller.DeleteMode?"editor.status.deleteMode":"editor.selectHint");}

        void BuildDialogs(RectTransform root)
        {
            var modal=FullGroup(root,"BlueprintModal");modalRoot=modal.gameObject;
            var dim=MakeRect(modal,"ModalBlocker",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();
            dim.color=new Color(0,0,0,.46f);dim.raycastTarget=true;
            var save=FullGroup(modal,"SaveDialog");saveDialog=save.gameObject;
            Glass(save,"SaveGlass",635,345,650,360,Palette.Dialog);
            Label(save,"SaveTitle","editor.saveBlueprint",670,377,500,42,24,Ink);
            Button(save,"CloseSave","×",1220,367,36,36,CloseDialog);
            Label(save,"NameHint","editor.nameHint",670,423,575,26,16,Muted);
            nameField=NameInput(save,670,465,580,50);nameField.onSubmit.AddListener(_=>ConfirmSave());
            Button(save,"CancelSave","ui.cancel",670,633,160,43,CloseDialog);
            Button(save,"ConfirmSave","ui.save",1000,633,250,43,ConfirmSave);
            var load=FullGroup(modal,"LoadDialog");loadDialog=load.gameObject;
            Glass(load,"LoadGlass",615,255,690,620,Palette.Dialog);
            Label(load,"LoadTitle","editor.loadBlueprint",650,285,500,42,24,Ink);
            Button(load,"CloseLoad","×",1240,278,36,36,CloseDialog);
            Label(load,"LoadHint",Copy("选择要载入的蓝图。","Select the blueprint to load."),650,335,575,27,16,Muted);
            for(int i=0;i<6;i++)
            {
                int row=i;float y=382+i*45;
                fileButtons[i]=Button(load,"BlueprintChoice"+i,"",650,y,620,39,()=>{selectedFile=page*6+row;RefreshFileRows();});
                fileLabels[i]=Label(load,"ChoiceLabel"+i,"",663,y+7,594,27,17,Ink);
            }
            emptyFiles=Label(load,"EmptyBlueprints","editor.emptyBlueprints",650,454,620,62,20,Muted,TextAlignmentOptions.Center);
            previousButton=Button(load,"PreviousPage","‹",650,675,42,32,()=>{page=Mathf.Max(0,page-1);RefreshFileRows();});
            pageText=Label(load,"Page","01 / 01",706,680,172,25,14,Muted,TextAlignmentOptions.Center);
            nextButton=Button(load,"NextPage","›",892,675,42,32,()=>{page=Mathf.Min(Mathf.Max(0,(files.Length-1)/6),page+1);RefreshFileRows();});
            Button(load,"CancelLoad","ui.cancel",650,810,125,43,CloseDialog);
            deleteButton=Button(load,"DeleteBlueprint","ui.delete",785,810,115,43,RequestDeleteSelected);
            loadButton=Button(load,"ConfirmLoad","ui.load",1095,810,175,43,ConfirmLoad);
            // Shared message layer remains above either dialog.
            modalError=Label(modal,"DialogError","",670,537,580,54,16,new Color(1,.53f,.47f));
            modalError.textWrappingMode=TextWrappingModes.Normal;

            var confirm=FullGroup(modal,"ActionConfirm");
            actionConfirmRoot=confirm.gameObject;
            var confirmDim=MakeRect(confirm,"ActionConfirmDim",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();
            confirmDim.color=new Color(0,0,0,.42f);confirmDim.raycastTarget=true;
            Glass(confirm,"ActionConfirmGlass",690,410,540,260,Palette.Dialog);
            actionConfirmTitle=Label(confirm,"ActionConfirmTitle","",725,442,470,38,23,Ink);
            actionConfirmMessage=Label(confirm,"ActionConfirmMessage","",725,493,470,54,16,Muted);
            actionConfirmMessage.textWrappingMode=TextWrappingModes.Normal;
            actionConfirmButton=Button(confirm,"ActionConfirmYes","ui.confirm",725,590,210,44,RunConfirmAction);
            actionConfirmButtonLabel=confirm.Find("ActionConfirmYesText").GetComponent<TextMeshProUGUI>();
            Button(confirm,"ActionConfirmCancel","ui.cancel",985,590,210,44,CancelActionConfirm);
            actionConfirmRoot.SetActive(false);
            modalRoot.SetActive(false);
        }
        public void OpenSaveDialog()
        {
            savingDialog=true;modalRoot.SetActive(true);saveDialog.SetActive(true);loadDialog.SetActive(false);
            CancelActionConfirm();pendingDeleteFile=null;
            L10n.Unbind(modalError);modalError.text=string.Empty;SetErrorPosition(true);nameField.text=blueprintName??L10n.T("editor.blueprintDefault");
            nameField.Select();nameField.ActivateInputField();modalRoot.transform.SetAsLastSibling();UiModalScope.Protect((RectTransform)canvas.transform,modalRoot.transform);
        }
        public void OpenLoadDialog()
        {
            // The shared blueprint library owns loading/deleting/renaming. Capture geometry and
            // dirty state before leaving construction; returning uses the same workspace draft.
            try{
                var navigation=controller.GetComponent<Starframe.App.AssemblyAppNavigation>();
                if(!navigation){navigation=controller.gameObject.AddComponent<Starframe.App.AssemblyAppNavigation>();navigation.Initialize(controller);}
                navigation.OpenBlueprintLibrary();
            }catch(Exception error)when(error is IOException||error is InvalidDataException||error is UnauthorizedAccessException){SetStatusKey(L10n.ErrorKey(error),true);}
        }
        void SetErrorPosition(bool save)
        {
            var rt=modalError.rectTransform;
            rt.anchorMin=new Vector2(670f/1920,(1080-(save?537:763)-40)/1080f);
            rt.anchorMax=new Vector2(1250f/1920,(1080-(save?537:763))/1080f);
            rt.offsetMin=rt.offsetMax=Vector2.zero;
        }
        public void CloseDialog()
        {
            CancelActionConfirm();
            if(modalRoot)modalRoot.SetActive(false);
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
        }
        void ConfirmSave()
        {
            if(!IsDialogOpen||!savingDialog||actionConfirmRoot.activeSelf)return;
            string name=nameField.text.Trim();
            if(string.IsNullOrWhiteSpace(name)){L10n.Bind(modalError,"editor.nameRequired");return;}
            try
            {
                if(controller.BlueprintExists(name))
                {
                    L10n.Unbind(modalError);modalError.text=string.Empty;
                    ShowActionConfirm("editor.overwriteTitle","editor.overwriteMessage","ui.overwrite",()=>ConfirmOverwrite(name),name);
                    return;
                }
            }
            catch(Exception e){L10n.Bind(modalError,L10n.ErrorKey(e));return;}
            if(controller.SaveBlueprint()){blueprintName=name;UpdateBlueprintIdentity();CloseDialog();}
        }
        void ConfirmOverwrite(string name)
        {
            if(!IsDialogOpen||!savingDialog||name!=nameField.text.Trim())return;
            if(controller.SaveBlueprint(true)){blueprintName=name;UpdateBlueprintIdentity();CloseDialog();}
        }
        void ConfirmLoad()
        {
            if(files.Length==0||selectedFile<0||selectedFile>=files.Length||actionConfirmRoot.activeSelf)return;
            string file=files[selectedFile];
            if(controller.Dirty){ShowActionConfirm("app.replaceDraftTitle","app.replaceDraftBody","ui.load",()=>{if(controller.LoadBlueprint(file))CloseDialog();});return;}
            if(controller.LoadBlueprint(file))CloseDialog();
        }
        void RequestDeleteSelected()
        {
            if(files.Length==0||selectedFile<0||selectedFile>=files.Length)return;
            pendingDeleteFile=files[selectedFile];
            string name=Path.GetFileNameWithoutExtension(pendingDeleteFile);
            ShowActionConfirm("editor.deleteTitle","editor.deleteMessage","ui.delete",ConfirmDeleteSelected,name);
        }
        void ConfirmDeleteSelected()
        {
            string file=pendingDeleteFile;
            pendingDeleteFile=null;
            if(!string.IsNullOrEmpty(file))controller.DeleteBlueprint(file);
        }
        void ShowActionConfirm(string title,string message,string yesLabel,Action action,params object[] args)
        {
            Starframe.App.GameAudio.Play(Starframe.App.GameSound.ConfirmWarning);
            confirmAction=action;
            confirmOpenedFrame=Time.frameCount;
            L10n.Bind(actionConfirmTitle,title);
            L10n.Bind(actionConfirmMessage,message,args);
            L10n.Bind(actionConfirmButtonLabel,yesLabel);
            if(assemblyLayout)
            {
                var image=(UnityEngine.UI.Image)actionConfirmButton.targetGraphic;
                Color confirmColor=yesLabel=="ui.delete"?Starframe.Presentation.UiTheme.Current.amber:Accent;
                image.color=confirmColor;actionConfirmButtonLabel.color=ContrastingText(confirmColor);
            }
            actionConfirmRoot.SetActive(true);
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);UiModalScope.Protect((RectTransform)modalRoot.transform,actionConfirmRoot.transform);
        }
        void RunConfirmAction()
        {
            Action action=confirmAction;
            CancelActionConfirm();
            action?.Invoke();
        }
        void CancelActionConfirm()
        {
            confirmAction=null;
            if(actionConfirmRoot)actionConfirmRoot.SetActive(false);
        }
        public void SetStatus(string message,bool error=false)
        {
            string key=L10n.LegacyKey(message);
            if(key!=null){SetStatusKey(key,error);return;}
            rawStatusMessage=message;rawStatusError=error;
            if(error)message=L10n.DomainMessage(message);
            ConfigureStatusLayout(error);
            L10n.Unbind(statusText);
            statusText.text=message;statusText.color=error?(assemblyLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.48f,.42f)):Ink;
            if(IsDialogOpen&&error){L10n.Unbind(modalError);modalError.text=message;}
        }
        public void SetStatusKey(string key,bool error=false,params object[] args)
        {
            rawStatusMessage=null;
            ConfigureStatusLayout(error);
            L10n.Bind(statusText,key,args);
            statusText.color=error?(assemblyLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.48f,.42f)):Ink;
            if(IsDialogOpen&&error)L10n.Bind(modalError,key,args);
        }
        void ConfigureStatusLayout(bool error)
        {
            if(!figmaRoot||!statusText)return;
            // The existing footer has room for two short warning lines without moving any controls.
            statusText.enableAutoSizing=error;statusText.fontSizeMin=14;statusText.fontSizeMax=18;statusText.fontSize=18;
            statusText.textWrappingMode=error?TextWrappingModes.Normal:TextWrappingModes.NoWrap;
            statusText.maxVisibleLines=error?2:int.MaxValue;
            statusText.overflowMode=error?TextOverflowModes.Truncate:TextOverflowModes.Ellipsis;
            statusText.alignment=TextAlignmentOptions.MidlineLeft;
        }
        void RefreshLocalizedContent()
        {
            placementReasonShown=false;lastPlacementFailure=null;
            if(rawStatusError&&rawStatusMessage!=null)SetStatus(rawStatusMessage,true);
            font=L10n.CurrentFont;
            if(nameField){nameField.fontAsset=font;L10n.ApplyUserContentFont(nameField.textComponent);}
            UpdateBlueprintIdentity();
            if(moduleSearchPlaceholder)moduleSearchPlaceholder.text=Copy("搜索组件或分类…","Search modules or category…");
            var libraryLabel=canvas?canvas.transform.Find("Toolbox/LoadBlueprintText"):null;if(libraryLabel)libraryLabel.GetComponent<TextMeshProUGUI>().text=Copy("蓝图库","Blueprint library");
            if(placeToolText)placeToolText.text=Copy("放置","Place");if(deleteToolText)deleteToolText.text=Copy("删除  X","Delete  X");
            var frameLabel=canvas?canvas.transform.Find("Header/FrameShipToolText"):null;if(frameLabel)frameLabel.GetComponent<TextMeshProUGUI>().text=Copy("聚焦  F","Frame ship  F");
            FilterCatalogue(false);
            if(!initialized)return;
            bool restorePerformance=showShipPerformance;
            SetShape(localizedShape);if(restorePerformance)ShowShipPerformance();RefreshFileRows();if(controller&&controller.Assembly)UpdateStats(controller.Assembly.Count,controller.Dirty);
            RefreshFigmaCopy();
        }
        public void SetFiles(string[] values){files=values??Array.Empty<string>();page=0;selectedFile=0;pendingDeleteFile=null;RefreshFileRows();}
        public void SelectFile(string file){int index=Array.IndexOf(files,file);if(index<0)return;selectedFile=index;page=index/6;RefreshFileRows();}
        void RefreshFileRows()
        {
            for(int i=0;i<6;i++)
            {
                int index=page*6+i;bool available=index<files.Length;
                fileButtons[i].gameObject.SetActive(available);fileLabels[i].gameObject.SetActive(available);
                fileLabels[i].text=available?Path.GetFileNameWithoutExtension(files[index]):string.Empty;
                L10n.ApplyUserContentFont(fileLabels[i]);
                ((UnityEngine.UI.Image)fileButtons[i].targetGraphic).color=available&&index==selectedFile?
                    (assemblyLayout?new Color(Starframe.Presentation.UiTheme.Current.accent.r,Starframe.Presentation.UiTheme.Current.accent.g,Starframe.Presentation.UiTheme.Current.accent.b,.36f):new Color(.12f,.26f,.4f,.65f)):
                    (assemblyLayout?new Color(.72f,.82f,.88f,.10f):new Color(.025f,.06f,.12f,.25f));
            }
            emptyFiles.gameObject.SetActive(files.Length==0);loadButton.interactable=files.Length>0;
            deleteButton.interactable=files.Length>0;
            previousButton.interactable=page>0;nextButton.interactable=(page+1)*6<files.Length;
            pageText.text=(page+1).ToString("D2")+" / "+Mathf.Max(1,(files.Length+5)/6).ToString("D2");
        }
        void Update()
        {
            if(!initialized)return;ResizeTarget();
            if(!PerformanceCacheIsCurrent()&&controller&&controller.Assembly)UpdateStats(controller.Assembly.Count,controller.Dirty);
            if(summaryLayoutDirty){summaryLayoutDirty=false;RefreshModuleInfoHeight();RefreshShipReadoutHeight();}
            var keys=UnityEngine.InputSystem.Keyboard.current;
            if(IsDialogOpen&&keys!=null)
            {
                if(keys.escapeKey.wasPressedThisFrame)
                {
                    if(actionConfirmRoot.activeSelf)CancelActionConfirm();
                    else CloseDialog();
                }
                else if(actionConfirmRoot.activeSelf){if(keys.enterKey.wasPressedThisFrame&&Time.frameCount>confirmOpenedFrame)RunConfirmAction();}
                else if(!savingDialog&&keys.enterKey.wasPressedThisFrame)ConfirmLoad();
            }
        }
        void ResizeTarget()
        {
            int w=Mathf.Clamp(Screen.width,1,4096),h=Mathf.Clamp(Screen.height,1,4096);
            if(sceneTexture && sceneTexture.width==w && sceneTexture.height==h)return;
            if(sceneTexture){worldCamera.targetTexture=null;sceneTexture.Release();Destroy(sceneTexture);}
            sceneTexture=new RenderTexture(w,h,24,RenderTextureFormat.ARGBHalf){name="WorkbenchScene",antiAliasing=1,useMipMap=true,autoGenerateMips=true,filterMode=FilterMode.Trilinear};
            sceneTexture.Create();worldCamera.targetTexture=sceneTexture;worldCamera.aspect=(float)w/h;
            background.texture=sceneTexture;foreach(var image in glass)if(image)image.texture=sceneTexture;
        }
        void OnDestroy()
        {
            Starframe.App.GameSettings.Changed-=OnUiSettingsChanged;
            if(presentationCamera)presentationCamera.enabled=false;
            if(worldCamera)worldCamera.targetTexture=null;
            if(sceneTexture){sceneTexture.Release();Destroy(sceneTexture);}
            foreach(var material in runtimeMaterials)if(material)Destroy(material);
            L10n.Changed-=RefreshLocalizedContent;
            if(performanceAssembly)performanceAssembly.Changed-=InvalidateAssemblyPerformance;
        }
    }
}
