// @role Present the parts catalogue, blueprint dialogs and construction feedback.
// @calls ShipEditorController, FlightTuning, L10n, GlassUiPalette

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
    public sealed class ShipEditorUI : MonoBehaviour
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
        TextMeshProUGUI moduleNameText,blueprintIdentityText,rotationReadoutText;
        TextMeshProUGUI targetCountText,targetButtonText;
        UnityEngine.UI.Button targetButton,targetMinus,targetPlus;
        TextMeshProUGUI countText,modeText,shapeText,coordinateText,rotationText,idText,statusText,headerMode,pageText;
        TMP_InputField nameField;
        UnityEngine.UI.Button loadButton,previousButton,nextButton,deleteButton,actionConfirmButton;
        TextMeshProUGUI actionConfirmButtonLabel;
        string[] files=Array.Empty<string>();
        int page,selectedFile;
        bool initialized;
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
        public bool EditingName=>IsDialogOpen || (nameField && nameField.isFocused);
        public string BlueprintName {get=>IsDialogOpen&&savingDialog?nameField.text:blueprintName??L10n.T("editor.blueprintDefault");set{blueprintName=value;UpdateBlueprintIdentity();}}
        public void Initialize(ShipEditorController editor,Camera camera)
        {
            controller=editor;worldCamera=camera;
            assemblyLayout=Starframe.Presentation.UiTheme.Current.assemblyLayout;
            font=L10n.CurrentFont;
            L10n.Changed+=RefreshLocalizedContent;
            var go=new GameObject("ConstructionCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
            go.layer=5;go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();
            if(!presentationCamera)throw new InvalidOperationException("Presentation camera is not assigned.");
            presentationCamera.enabled=true;canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=presentationCamera;canvas.planeDistance=1;
            worldCamera.cullingMask&=~(1<<5);
            var scaler=go.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
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
            Glass(header,"HeaderGlass",assemblyLayout?32:37,assemblyLayout?32:36,assemblyLayout?520:476,assemblyLayout?88:72,Palette.Glass);
            Glass(toolbox,"ModuleLibraryGlass",assemblyLayout?1504:1515,assemblyLayout?32:10,assemblyLayout?384:376,assemblyLayout?680:778,Palette.Glass);
            Glass(toolbox,"ModuleInfoGlass",assemblyLayout?1504:1515,assemblyLayout?728:798,assemblyLayout?384:376,assemblyLayout?240:204,Palette.Glass);
            Glass(toolbox,"SaveButtonGlass",assemblyLayout?1504:1515,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,Palette.Glass);
            Glass(toolbox,"LoadButtonGlass",assemblyLayout?1704:1708,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,Palette.Glass);
            Label(header,"Title","editor.title",assemblyLayout?56:60,assemblyLayout?44:55,333,34,assemblyLayout?23:21,Ink);
            if(assemblyLayout)
            {
                Label(header,"BlueprintCaption","editor.blueprintCaption",56,84,72,19,12,Muted);
                blueprintIdentityText=Label(header,"BlueprintIdentity",BlueprintName,128,81,280,22,15,Ink);
                blueprintIdentityText.overflowMode=TextOverflowModes.Ellipsis;
            }
            headerMode=Label(header,"ModeBadge","editor.mode.place",assemblyLayout?464:430,assemblyLayout?48:53,56,28,16,Accent);

            BuildModuleLibrary(toolbox);

            Label(toolbox,"InfoTitle","editor.moduleInfo",assemblyLayout?1528:1532,assemblyLayout?744:809,315,29,19,Ink);
            Line(toolbox,assemblyLayout?1528:1532,assemblyLayout?780:842,319,2);
            if(assemblyLayout)
            {
                moduleNameText=Label(toolbox,"ModuleName","editor.selectModule",1528,792,330,25,17,Ink);
                moduleStatsText=Label(toolbox,"ModuleStatistics","",1528,818,330,120,14,Ink,TextAlignmentOptions.TopLeft);
            }
            else moduleStatsText=Label(toolbox,"ModuleStatistics","",1540,853,320,138,14,Color.white);
            moduleStatsText.textWrappingMode=TextWrappingModes.Normal;
            Button(toolbox,"SaveBlueprint","editor.saveBlueprint",assemblyLayout?1504:1515,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,OpenSaveDialog);
            Button(toolbox,"LoadBlueprint","editor.loadBlueprint",assemblyLayout?1704:1708,assemblyLayout?984:1012,assemblyLayout?184:183,assemblyLayout?64:56,OpenLoadDialog);
            countText=Label(header,"Count",L10n.T("editor.parts",0,""),assemblyLayout?448:402,assemblyLayout?88:83,91,17,12,Muted,TextAlignmentOptions.Right);
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
            initialized=true;ResizeTarget();Canvas.ForceUpdateCanvases();
            moduleLibraryScroll.StopMovement();moduleLibraryScroll.verticalNormalizedPosition=1;
            foreach(var child in go.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
        }
        void BuildModuleLibrary(RectTransform toolbox)
        {
            var scrollRoot=MakeRect(toolbox,"ModuleLibraryScroll",assemblyLayout?1524:1536,assemblyLayout?56:24,assemblyLayout?344:350,assemblyLayout?632:744);
            var scroll=scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            moduleLibraryScroll=scroll;
            scroll.horizontal=false;scroll.vertical=true;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity=10;scroll.inertia=true;scroll.decelerationRate=.135f;
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
            string[] order={"Hull","Propulsion","Core","Power","Shield","Weapon"};
            string[] labels={"category.Hull","category.Propulsion","category.Core","category.Power","category.Shield","category.Weapon"};
            for(int g=0;g<order.Length;g++)
            {
                if(!groups.TryGetValue(order[g],out var values)||values.Count==0)continue;
                values.Sort((a,b)=>ShapeOrder(a).CompareTo(ShapeOrder(b)));
                if(assemblyLayout)
                {
                    BuildRefinedCategory(content,order[g],labels[g],values);
                    continue;
                }
                var section=LocalRect(content,order[g]+"Category",0,0,0,144);
                var item=section.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();item.minHeight=item.preferredHeight=144;
                var title=Label(section,order[g]+"Title",labels[g],0,0,260,35,21,Ink);PlaceLocal(title.rectTransform,12,0,260,35);
                var divider=LocalRect(section,"Divider",8,40,304,2).gameObject.AddComponent<UnityEngine.UI.Image>();divider.color=new Color(.9f,.94f,.97f,.24f);divider.raycastTarget=false;
                for(int i=0;i<values.Count;i++)
                {
                    var shape=values[i];float iconWidth=IconWidth(shape);float x=12+i*62;
                    var button=Button(section,shape+"Slot","",0,0,iconWidth,52,()=>controller.SelectShape(shape));
                    PlaceLocal((RectTransform)button.transform,x,56,iconWidth,52);
                    var image=LocalRect((RectTransform)button.transform,shape+"Icon",0,0,iconWidth,52).gameObject.AddComponent<UnityEngine.UI.Image>();
                    image.preserveAspect=true;image.raycastTarget=true;image.color=Color.white;
                    image.rectTransform.anchorMin=Vector2.zero;image.rectTransform.anchorMax=Vector2.one;
                    image.rectTransform.offsetMin=Vector2.zero;image.rectTransform.offsetMax=Vector2.zero;
                    button.targetGraphic=image;
                    int index=(int)shape;
                    if(moduleIcons!=null&&index>=0&&index<moduleIcons.Length)image.sprite=moduleIcons[index];
                    var definition=GetDefinition(shape);
                    if(definition&&definition.icon)image.sprite=definition.icon;
                    shapeButtons.Add(shape,button);shapeIcons.Add(shape,image);
                }
            }
        }
        void BuildRefinedCategory(RectTransform content,string category,string title,List<BlockShape> values)
        {
            const float left=10,right=318,gap=8,top=34,rowHeight=70;
            var placements=new List<Vector3>(values.Count);
            float x=left;int row=0;
            foreach(var shape in values)
            {
                float width=IconWidth(shape);
                if(x>left&&x+width>right){row++;x=left;}
                placements.Add(new Vector3(x,row,width));
                x+=width+gap;
            }
            int rows=values.Count==0?0:row+1;
            float height=Mathf.Max(112,top+rows*rowHeight+4);
            var section=LocalRect(content,category+"Category",0,0,0,height);
            var item=section.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            item.minHeight=item.preferredHeight=height;
            LocalLabel(section,category+"Title",title,10,0,230,26,16,Ink);
            LocalLabel(section,category+"Count",values.Count.ToString("00"),284,3,34,20,12,Muted,TextAlignmentOptions.Right);
            var divider=LocalRect(section,"Divider",10,29,308,1).gameObject.AddComponent<UnityEngine.UI.Image>();
            divider.color=new Color(.9f,.94f,.97f,.24f);divider.raycastTarget=false;
            for(int i=0;i<values.Count;i++)
            {
                var shape=values[i];var place=placements[i];float shapeWidth=place.z;
                var button=LocalButton(section,shape+"Slot",place.x,top+place.y*rowHeight,shapeWidth,48,()=>controller.SelectShape(shape));
                var plate=(UnityEngine.UI.Image)button.targetGraphic;
                plate.color=Color.clear;plate.canvasRenderer.cullTransparentMesh=false;
                plate.raycastTarget=true;
                var image=LocalRect((RectTransform)button.transform,shape+"Icon",0,0,shapeWidth,48).gameObject.AddComponent<UnityEngine.UI.Image>();
                image.preserveAspect=true;image.raycastTarget=false;image.color=Color.white;
                int index=(int)shape;
                if(moduleIcons!=null&&index>=0&&index<moduleIcons.Length)image.sprite=moduleIcons[index];
                var definition=GetDefinition(shape);
                if(definition&&definition.icon)image.sprite=definition.icon;
                LocalLabel(section,shape+"Name",ShapeShortName(shape),place.x,top+place.y*rowHeight+49,shapeWidth,16,12,Muted,TextAlignmentOptions.Center);
                var mark=LocalRect(section,shape+"SelectedMark",place.x+(shapeWidth-16)/2,top+place.y*rowHeight+69,16,2).gameObject.AddComponent<UnityEngine.UI.Image>();
                mark.color=Accent;mark.raycastTarget=false;mark.gameObject.SetActive(false);shapeSelectionMarks.Add(shape,mark);
                shapeButtons.Add(shape,button);shapeIcons.Add(shape,image);
            }
        }
        static string ShapeShortName(BlockShape shape)=>"module.short."+shape;
        TextMeshProUGUI LocalLabel(RectTransform parent,string name,string text,float x,float y,float width,float height,float size,Color color,TextAlignmentOptions alignment=TextAlignmentOptions.Left)
        {
            var tmp=LocalRect(parent,name,x,y,width,height).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font=font;tmp.fontSize=size;tmp.color=color;tmp.alignment=alignment;
            if(L10n.IsKey(text))L10n.Bind(tmp,text);else{tmp.text=text;L10n.ApplyFont(tmp);}
            tmp.enableAutoSizing=false;tmp.textWrappingMode=TextWrappingModes.NoWrap;tmp.overflowMode=TextOverflowModes.Ellipsis;
            tmp.raycastTarget=false;tmp.margin=Vector4.zero;return tmp;
        }
        UnityEngine.UI.Button LocalButton(RectTransform parent,string name,float x,float y,float width,float height,Action action)
        {
            var rect=LocalRect(parent,name,x,y,width,height);
            var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.72f,.82f,.88f,.10f);
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.85f,.95f,1,1);
            colors.pressedColor=new Color(.55f,.8f,1,1);colors.selectedColor=Color.white;colors.disabledColor=new Color(.4f,.45f,.5f,.45f);
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
        static int ShapeOrder(BlockShape shape)=>shape==BlockShape.Cube?0:shape==BlockShape.Wedge?1:shape==BlockShape.Slab?2:shape==BlockShape.Pyramid?3:shape==BlockShape.Thruster1?0:shape==BlockShape.Thruster2?1:0;
        static float IconWidth(BlockShape shape)=>shape==BlockShape.Thruster2?96:shape==BlockShape.Laser?144:48;
        string CategoryFor(BlockShape shape)
        {
            var definition=GetDefinition(shape);
            if(definition)
            {
                string value=definition.category.ToString();
                if(value=="Hull"||value=="Propulsion"||value=="Weapon"||value=="Core"||value=="Power"||value=="Shield")return value;
            }
            if(shape==BlockShape.Core)return "Core";
            if(shape==BlockShape.Shield)return "Shield";
            if(shape.ToString()=="Power")return "Power";
            if(BlockModules.IsThruster(shape))return "Propulsion";
            if(shape.ToString().IndexOf("Laser",StringComparison.OrdinalIgnoreCase)>=0)return "Weapon";
            return "Hull";
        }
        Starframe.Flight.ModuleDefinition GetDefinition(BlockShape shape)=>flightTuning?flightTuning.GetDefinition(shape):null;
        string BuildRefinedModuleSummary(Starframe.Flight.ModuleDefinition definition,Starframe.Flight.ModuleStats stats,float mass,float hp)
        {
            var entries=new List<string>{L10n.T("module.stat.mass",mass),L10n.T("module.stat.hp",hp)};
            float thrust=definition?definition.thrustNewtons:stats!=null?stats.thrustNewtons:0;
            if(thrust>0)entries.Add(L10n.T("module.stat.thrust",thrust/1000));
            if(definition&&definition.category==Starframe.Flight.ModuleCategory.Power)
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
            var weapon=definition?definition.weapon:null;
            if(weapon)
            {
                if(weapon.shotsPerSecond>0)entries.Add(L10n.T("module.stat.rate",weapon.shotsPerSecond));
                if(weapon.damage>0)entries.Add(L10n.T("module.stat.damage",weapon.damage));
                if(weapon.sustainedPowerWatts>0)entries.Add(L10n.T("module.stat.power",weapon.sustainedPowerWatts));
                if(weapon.projectileSpeed>0)entries.Add(L10n.T("module.stat.projectile",weapon.projectileSpeed));
            }
            var rows=new List<string>();
            for(int i=0;i<entries.Count;i+=2)
                rows.Add(i+1<entries.Count?entries[i]+"  ·  "+entries[i+1]:entries[i]);
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
            tmp.font=font;tmp.fontSize=size;tmp.color=color;tmp.alignment=alignment;
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
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.85f,.95f,1,1);
            colors.pressedColor=new Color(.55f,.8f,1,1);colors.selectedColor=Color.white;colors.disabledColor=new Color(.4f,.45f,.5f,.45f);
            button.colors=colors;button.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            button.onClick.AddListener(()=>{Starframe.App.GameAudio.Click();action();});
            if(iconOnly)button.transition=UnityEngine.UI.Selectable.Transition.None;
            if(text.Length>0)
            {
                var buttonLabel=Label(parent,name+"Text",text,x+5,y+4,w-10,h-8,16,Ink,TextAlignmentOptions.Center);
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
            field.placeholder=Label(parent,"NamePlaceholder","editor.namePlaceholder",x+12,y+8,w-24,h-16,17,Muted);
            field.caretColor=Accent;field.customCaretColor=true;field.selectionColor=assemblyLayout?new Color(Accent.r,Accent.g,Accent.b,.35f):new Color(.3f,.6f,.85f,.35f);
            return field;
        }

        public bool WorkspaceContains(Vector2 screen)
        {
            if(IsDialogOpen)return false;
            float x=screen.x/Mathf.Max(1,Screen.width),y=1-screen.y/Mathf.Max(1,Screen.height);
            if(assemblyLayout)
            {
                float px=x*1920f,py=y*1080f;
                if(px<0||px>=1920||py<0||py>=1080)return false;
                if(px>=1504)return false;
                if(px>=628&&px<=1472&&py>=32&&py<=80)return false;
                if(px>=32&&px<=552&&py>=32&&py<=120)return false;
                if(px>=32&&px<=176&&py>=936&&py<=992)return false;
                if(px>=192&&px<=1016&&py>=936&&py<=992)return false;
                if(px>=32&&px<=1132&&py>=1008&&py<=1048)return false;
                return true;
            }
            if(x<0 || x>=1515f/1920 || y<0 || y>1)return false;
            if(x>=37f/1920&&x<=207f/1920&&y>=877f/1080&&y<=933f/1080)return false;
            return !(x>=37f/1920 && x<=513f/1920 && y>=36f/1080 && y<=108f/1080);
        }

        public void SetShape(BlockShape shape)
        {
            localizedShape=shape;
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
                    mark.color=Accent;mark.gameObject.SetActive(active);
                }
            }
        }
        public void SetMode(bool delete)
        {
            L10n.Bind(modeText,delete?"editor.mode.delete":"editor.mode.place");L10n.Bind(headerMode,delete?"editor.mode.delete":"editor.mode.place");
            modeText.color=headerMode.color=assemblyLayout?
                (delete?Starframe.Presentation.UiTheme.Current.danger:Starframe.Presentation.UiTheme.Current.accent):
                (delete?new Color(1,.44f,.4f):Accent);
        }
        public void UpdateStats(int count,bool dirty){L10n.Bind(countText,"editor.parts",count,dirty?" *":"");}
        public void SetRotation(int degrees)
        {
            rotationText.text=degrees+"°   /   R";
            if(rotationReadoutText)L10n.Bind(rotationReadoutText,"editor.rotation",degrees);
        }
        public void SetHover(BlockRecord part)
        {
            if(part==null){coordinateText.text="—";idText.text="ID  —";return;}
            var c=part.position;coordinateText.text=c.x.ToString("0.##")+" , "+c.y.ToString("0.##")+" , "+c.z.ToString("0.##");
            idText.text=string.IsNullOrEmpty(part.id)?L10n.T("editor.preview",ShipEditorController.ShapeName(part.shape)):"ID  "+part.id.Substring(0,Math.Min(12,part.id.Length));
        }

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
            Label(load,"LoadHint","editor.loadHint",650,335,575,27,16,Muted);
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
            targetCountText=Label(load,"TargetCount","",650,717,390,32,16,Ink);
            targetMinus=Button(load,"TargetMinus","−",1120,711,60,38,()=>ChangeTargetCount(-1));
            targetPlus=Button(load,"TargetPlus","+",1190,711,80,38,()=>ChangeTargetCount(1));
            Button(load,"CancelLoad","ui.cancel",650,810,125,43,CloseDialog);
            deleteButton=Button(load,"DeleteBlueprint","ui.delete",785,810,115,43,RequestDeleteSelected);
            targetButton=Button(load,"SetTargetBlueprint","editor.targetAdd",910,810,175,43,ToggleTarget);
            targetButtonText=load.Find("SetTargetBlueprintText").GetComponent<TextMeshProUGUI>();
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
            L10n.Unbind(modalError);modalError.text=string.Empty;SetErrorPosition(true);nameField.text=string.Empty;
            nameField.Select();nameField.ActivateInputField();
        }
        public void OpenLoadDialog()
        {
            savingDialog=false;modalRoot.SetActive(true);saveDialog.SetActive(false);loadDialog.SetActive(true);
            CancelActionConfirm();pendingDeleteFile=null;
            L10n.Unbind(modalError);modalError.text=string.Empty;SetErrorPosition(false);
            controller.RefreshFiles();RefreshFileRows();
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
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
            if(files.Length==0||selectedFile<0||selectedFile>=files.Length)return;
            if(controller.LoadBlueprint(files[selectedFile]))CloseDialog();
        }
        void ToggleTarget()
        {
            if(files.Length==0)return;var file=files[selectedFile];int count=controller.TargetCount(file);
            if(controller.SetTargetCount(file,count>0?0:1)){L10n.Unbind(modalError);modalError.text=string.Empty;RefreshFileRows();}
        }
        void ChangeTargetCount(int delta)
        {
            if(files.Length==0)return;var file=files[selectedFile];int count=controller.TargetCount(file);
            if(controller.SetTargetCount(file,Mathf.Max(0,count+delta))){L10n.Unbind(modalError);modalError.text=string.Empty;RefreshFileRows();}
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
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
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
            L10n.Unbind(statusText);
            statusText.text=message;statusText.color=error?(assemblyLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.48f,.42f)):Ink;
            if(IsDialogOpen&&error){L10n.Unbind(modalError);modalError.text=message;}
        }
        public void SetStatusKey(string key,bool error=false,params object[] args)
        {
            L10n.Bind(statusText,key,args);
            statusText.color=error?(assemblyLayout?Starframe.Presentation.UiTheme.Current.danger:new Color(1,.48f,.42f)):Ink;
            if(IsDialogOpen&&error)L10n.Bind(modalError,key,args);
        }
        void RefreshLocalizedContent()
        {
            font=L10n.CurrentFont;
            if(nameField){nameField.fontAsset=font;L10n.ApplyUserContentFont(nameField.textComponent);}
            UpdateBlueprintIdentity();
            if(!initialized)return;
            SetShape(localizedShape);RefreshFileRows();
        }
        public void SetFiles(string[] values){files=values??Array.Empty<string>();page=0;selectedFile=0;pendingDeleteFile=null;RefreshFileRows();}
        public void SelectFile(string file){int index=Array.IndexOf(files,file);if(index<0)return;selectedFile=index;page=index/6;RefreshFileRows();}
        void RefreshFileRows()
        {
            for(int i=0;i<6;i++)
            {
                int index=page*6+i;bool available=index<files.Length;
                fileButtons[i].gameObject.SetActive(available);fileLabels[i].gameObject.SetActive(available);
                int targetCount=available?controller.TargetCount(files[index]):0;
                fileLabels[i].text=available?Path.GetFileNameWithoutExtension(files[index])+(targetCount>0?L10n.T("editor.fileTargetSuffix",targetCount):""):string.Empty;
                L10n.ApplyUserContentFont(fileLabels[i]);
                ((UnityEngine.UI.Image)fileButtons[i].targetGraphic).color=available&&index==selectedFile?
                    (assemblyLayout?new Color(Starframe.Presentation.UiTheme.Current.accent.r,Starframe.Presentation.UiTheme.Current.accent.g,Starframe.Presentation.UiTheme.Current.accent.b,.36f):new Color(.12f,.26f,.4f,.65f)):
                    (assemblyLayout?new Color(.72f,.82f,.88f,.10f):new Color(.025f,.06f,.12f,.25f));
            }
            emptyFiles.gameObject.SetActive(files.Length==0);loadButton.interactable=files.Length>0;
            deleteButton.interactable=files.Length>0;
            if(targetButton){int count=files.Length>0?controller.TargetCount(files[selectedFile]):0;targetButton.interactable=targetPlus.interactable=files.Length>0;targetMinus.interactable=files.Length>0&&count>0;L10n.Bind(targetButtonText,count>0?"editor.targetRemove":"editor.targetAdd");if(files.Length>0)L10n.Bind(targetCountText,"editor.targetCount",count);else L10n.Bind(targetCountText,"editor.targetNeedsBlueprint");}
            previousButton.interactable=page>0;nextButton.interactable=(page+1)*6<files.Length;
            pageText.text=(page+1).ToString("D2")+" / "+Mathf.Max(1,(files.Length+5)/6).ToString("D2");
        }
        void Update()
        {
            if(!initialized)return;ResizeTarget();
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
            if(presentationCamera)presentationCamera.enabled=false;
            if(worldCamera)worldCamera.targetTexture=null;
            if(sceneTexture){sceneTexture.Release();Destroy(sceneTexture);}
            foreach(var material in runtimeMaterials)if(material)Destroy(material);
            L10n.Changed-=RefreshLocalizedContent;
        }
    }
}
