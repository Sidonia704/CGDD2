using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
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
        TMP_FontAsset font,osFallback;
        UnityEngine.UI.RawImage background;
        readonly List<UnityEngine.UI.RawImage> glass=new List<UnityEngine.UI.RawImage>();
        readonly List<Material> runtimeMaterials=new List<Material>();
        readonly Dictionary<BlockShape,UnityEngine.UI.Button> shapeButtons=new Dictionary<BlockShape,UnityEngine.UI.Button>();
        readonly UnityEngine.UI.Button[] fileButtons=new UnityEngine.UI.Button[6];
        [SerializeField] Sprite[] moduleIcons,moduleSelectedIcons;
        [SerializeField] Sprite rectangleIcon,triangleIcon;
        readonly Dictionary<BlockShape,UnityEngine.UI.Image> shapeIcons=new Dictionary<BlockShape,UnityEngine.UI.Image>();
        readonly TextMeshProUGUI[] fileLabels=new TextMeshProUGUI[6];
        TextMeshProUGUI moduleStatsText;
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
        int confirmOpenedFrame;
        string blueprintName="装配样例";
        static readonly Color Ink=Palette.Ink,Muted=Palette.Muted,Accent=Palette.Accent;
        public TMP_FontAsset Font=>font;
        public Starframe.Flight.ModuleDatabase Database=>flightTuning?flightTuning.database:null;
        public bool IsDialogOpen=>modalRoot && modalRoot.activeSelf;
        public bool EditingName=>IsDialogOpen || (nameField && nameField.isFocused);
        public string BlueprintName {get=>IsDialogOpen&&savingDialog?nameField.text:blueprintName;set{blueprintName=value;}}
        public void Initialize(ShipEditorController editor,Camera camera)
        {
            controller=editor;worldCamera=camera;
            font=Instantiate(TMP_Settings.defaultFontAsset);
            font.name="Workbench Latin";
            osFallback=TMP_FontAsset.CreateFontAsset("Microsoft YaHei","Regular",48);
            if(osFallback!=null)font.fallbackFontAssetTable=new List<TMP_FontAsset>{osFallback};
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
            Glass(header,"HeaderGlass",37,36,476,72,Palette.Glass);
            Glass(toolbox,"ModuleLibraryGlass",1515,10,376,778,Palette.Glass);
            Glass(toolbox,"ModuleInfoGlass",1515,798,376,204,Palette.Glass);
            Glass(toolbox,"SaveButtonGlass",1515,1012,183,56,Palette.Glass);
            Glass(toolbox,"LoadButtonGlass",1708,1012,183,56,Palette.Glass);
            Label(header,"Title","装配编辑器",60,55,333,34,21,Ink);
            headerMode=Label(header,"ModeBadge","放置",430,53,56,28,16,Accent);

            BuildModuleLibrary(toolbox);

            Label(toolbox,"InfoTitle","模块信息",1532,809,315,29,19,Ink);
            Line(toolbox,1532,842,319,2);
            moduleStatsText=Label(toolbox,"ModuleStatistics","",1540,853,320,138,14,Color.white);
            moduleStatsText.textWrappingMode=TextWrappingModes.Normal;
            Button(toolbox,"SaveBlueprint","保存蓝图",1515,1012,183,56,OpenSaveDialog);
            Button(toolbox,"LoadBlueprint","载入蓝图",1708,1012,183,56,OpenLoadDialog);
            countText=Label(header,"Count","0005  部件",402,83,91,17,12,Muted,TextAlignmentOptions.Right);
            var hidden=FullGroup(root,"EditorData");hidden.gameObject.SetActive(false);
            modeText=Label(hidden,"Mode","",0,0,10,10,12,Ink);shapeText=Label(hidden,"Shape","",0,0,10,10,12,Ink);
            coordinateText=Label(hidden,"Coordinates","",0,0,10,10,12,Ink);
            rotationText=Label(hidden,"Rotation","",0,0,10,10,12,Ink);idText=Label(hidden,"ID","",0,0,10,10,12,Ink);
            BuildDialogs(root);
            Glass(footer,"TestButtonGlass",37,877,170,56,Palette.Glass);
            Button(footer,"EnterFlightRange","测试",37,877,170,56,controller.EnterFlightRange);
            statusText=Label(footer,"Status","请选择体块。",37,955,1120,26,18,Ink);
            Label(footer,"MouseHelp","中键 旋转   ·   Shift + 中键 平移   ·   滚轮 缩放",37,1000,1130,21,15,Muted);
            Label(footer,"BuildHelp","左键 放置   ·   X 删除模式   ·   R 旋转体块   ·   F 居中   ·   Shift 加速",37,1029,1250,21,15,Muted);
            initialized=true;ResizeTarget();Canvas.ForceUpdateCanvases();
            foreach(var child in go.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
        }
        void BuildModuleLibrary(RectTransform toolbox)
        {
            var scrollRoot=MakeRect(toolbox,"ModuleLibraryScroll",1536,24,350,744);
            var scroll=scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
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
            string[] labels={"船体","推进","核心","电源","护盾","武器"};
            for(int g=0;g<order.Length;g++)
            {
                if(!groups.TryGetValue(order[g],out var values)||values.Count==0)continue;
                values.Sort((a,b)=>ShapeOrder(a).CompareTo(ShapeOrder(b)));
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
            tmp.font=font;tmp.text=text;tmp.fontSize=size;tmp.color=color;tmp.alignment=alignment;
            tmp.enableAutoSizing=false;tmp.textWrappingMode=TextWrappingModes.NoWrap;tmp.overflowMode=size>=20?TextOverflowModes.Overflow:TextOverflowModes.Ellipsis;
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
            var material=new Material(backdropBlurShader);material.SetColor("_Tint",tint);material.SetFloat("_BlurRadius",18);
            material.SetVector("_PanelUVRect",new Vector4(x/1920,(1080-y-h)/1080,w/1920,h/1080));
            image.material=material;image.uvRect=new Rect(x/1920,(1080-y-h)/1080,w/1920,h/1080);
            image.raycastTarget=true;glass.Add(image);runtimeMaterials.Add(material);
        }
        UnityEngine.UI.Button Button(RectTransform parent,string name,string text,float x,float y,float w,float h,Action action)
        {
            var rt=MakeRect(parent,name,x,y,w,h);
            var image=rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            bool iconOnly=name.EndsWith("Slot",StringComparison.Ordinal)||name.StartsWith("HullSlot",StringComparison.Ordinal)||name.StartsWith("ThrusterSlot",StringComparison.Ordinal);
            image.color=iconOnly?Color.clear:Palette.Button;
            if(iconOnly)image.canvasRenderer.cullTransparentMesh=false;
            var button=rt.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
            var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.85f,.95f,1,1);
            colors.pressedColor=new Color(.55f,.8f,1,1);colors.selectedColor=Color.white;colors.disabledColor=new Color(.4f,.45f,.5f,.45f);
            button.colors=colors;button.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            button.onClick.AddListener(()=>action());
            if(iconOnly)button.transition=UnityEngine.UI.Selectable.Transition.None;
            if(text.Length>0)Label(parent,name+"Text",text,x+5,y+4,w-10,h-8,16,Ink,TextAlignmentOptions.Center);
            return button;
        }
        TMP_InputField NameInput(RectTransform parent,float x,float y,float w,float h)
        {
            var rt=MakeRect(parent,"BlueprintName",x,y,w,h);
            var image=rt.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.02f,.07f,.14f,.6f);
            var field=rt.gameObject.AddComponent<TMP_InputField>();
            var viewport=new GameObject("TextViewport",typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.gameObject.layer=5;viewport.SetParent(rt,false);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=new Vector2(12,4);viewport.offsetMax=new Vector2(-12,-4);
            var textObject=new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI));
            textObject.layer=5;var textRect=(RectTransform)textObject.transform;textRect.SetParent(viewport,false);textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=textRect.offsetMax=Vector2.zero;
            var text=textObject.GetComponent<TextMeshProUGUI>();text.font=font;text.fontSize=17;text.color=Ink;text.alignment=TextAlignmentOptions.MidlineLeft;text.raycastTarget=false;text.textWrappingMode=TextWrappingModes.NoWrap;
            field.targetGraphic=image;field.textViewport=viewport;field.textComponent=text;field.fontAsset=font;
            field.characterLimit=64;field.lineType=TMP_InputField.LineType.SingleLine;field.text=string.Empty;
            field.placeholder=Label(parent,"NamePlaceholder","请输入蓝图名称",x+12,y+8,w-24,h-16,17,Muted);
            field.caretColor=Accent;field.customCaretColor=true;field.selectionColor=new Color(.3f,.6f,.85f,.35f);
            return field;
        }

        public bool WorkspaceContains(Vector2 screen)
        {
            if(IsDialogOpen)return false;
            float x=screen.x/Mathf.Max(1,Screen.width),y=1-screen.y/Mathf.Max(1,Screen.height);
            if(x<0 || x>=1515f/1920 || y<0 || y>1)return false;
            if(x>=37f/1920&&x<=207f/1920&&y>=877f/1080&&y<=933f/1080)return false;
            return !(x>=37f/1920 && x<=513f/1920 && y>=36f/1080 && y<=108f/1080);
        }

        public void SetShape(BlockShape shape)
        {
            var definition=GetDefinition(shape);
            string displayName=definition?definition.displayName:null;
            if(string.IsNullOrEmpty(displayName))displayName=ShipEditorController.ShapeName(shape);
            shapeText.text=displayName;
            var stats=flightTuning?flightTuning.Get(shape):null;
            float mass=definition?definition.massKg:stats!=null?stats.massKg:0;
            float hp=definition?definition.maxHp:stats!=null?stats.maxHp:0;
            float thrust=definition?definition.thrustNewtons:stats!=null?stats.thrustNewtons:0;
            moduleStatsText.text=displayName+"\n\n质量  "+mass.ToString("0")+" kg\n血量  "+hp.ToString("0");
            if(thrust>0)moduleStatsText.text+="\n推力  "+(thrust/1000).ToString("0.#")+" kN";
            var weapon=definition?definition.weapon:null;
            if(weapon)
            {
                float rate=weapon.shotsPerSecond,damage=weapon.damage,speed=weapon.projectileSpeed;
                if(rate>0)moduleStatsText.text+="\n射速  "+rate.ToString("0.##")+" 发/秒";
                float draw=weapon.sustainedPowerWatts;
                if(draw>0)moduleStatsText.text+="\n功耗  "+draw.ToString("0.##")+" 电量/s";
                if(damage>0)moduleStatsText.text+="\n伤害  "+damage.ToString("0.##");
                if(speed>0)moduleStatsText.text+="\n弹速  "+speed.ToString("0.##")+" m/s";
            }
            if(definition&&definition.category==Starframe.Flight.ModuleCategory.Power)
            {
                moduleStatsText.text+="\n电容量  "+definition.storageJoules.ToString("0")+" 电量\n恢复  "+definition.generationWatts.ToString("0")+" 电量/s";
            }
            if(definition&&definition.shieldCapacity>0)
                moduleStatsText.text+=$"\n护盾  {definition.shieldCapacity:0} · 半径 {definition.shieldRadius:0.#} m\n恢复  {definition.shieldRecoveryPerSecond:0}/s · 延迟 {definition.shieldRecoveryDelay:0.#}s\n恢复耗电  {definition.shieldPowerWatts:0}/s";
            foreach(var pair in shapeIcons)
            {
                bool active=pair.Key==shape;var iconDefinition=GetDefinition(pair.Key);
                var sprite=iconDefinition?(active?iconDefinition.selectedIcon:iconDefinition.icon):null;
                int index=(int)pair.Key;
                if(!sprite&&moduleIcons!=null&&index>=0&&index<moduleIcons.Length)sprite=active&&moduleSelectedIcons!=null&&index<moduleSelectedIcons.Length?moduleSelectedIcons[index]:moduleIcons[index];
                if(sprite)pair.Value.sprite=sprite;
                pair.Value.color=Color.white;
            }
        }
        public void SetMode(bool delete)
        {
            modeText.text=delete?"删除":"放置";headerMode.text=delete?"删除":"放置";
            modeText.color=headerMode.color=delete?new Color(1,.44f,.4f):Accent;
        }
        public void UpdateStats(int count,bool dirty){countText.text=count.ToString("D4")+"  部件"+(dirty?" *":"");}
        public void SetRotation(int degrees){rotationText.text=degrees+"°   /   R";}
        public void SetHover(BlockRecord part)
        {
            if(part==null){coordinateText.text="—";idText.text="ID  —";return;}
            var c=part.position;coordinateText.text=c.x.ToString("0.##")+" , "+c.y.ToString("0.##")+" , "+c.z.ToString("0.##");
            idText.text=string.IsNullOrEmpty(part.id)?"PREVIEW  /  "+ShipEditorController.ShapeName(part.shape):"ID  "+part.id.Substring(0,Math.Min(12,part.id.Length));
        }

        void BuildDialogs(RectTransform root)
        {
            var modal=FullGroup(root,"BlueprintModal");modalRoot=modal.gameObject;
            var dim=MakeRect(modal,"ModalBlocker",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();
            dim.color=new Color(0,0,0,.46f);dim.raycastTarget=true;
            var save=FullGroup(modal,"SaveDialog");saveDialog=save.gameObject;
            Glass(save,"SaveGlass",635,345,650,360,Palette.Glass);
            Label(save,"SaveTitle","保存蓝图",670,377,500,42,24,Ink);
            Button(save,"CloseSave","×",1220,367,36,36,CloseDialog);
            Label(save,"NameHint","输入蓝图名称；同名时可确认覆盖。",670,423,575,26,16,Muted);
            nameField=NameInput(save,670,465,580,50);nameField.onSubmit.AddListener(_=>ConfirmSave());
            Button(save,"CancelSave","取消",670,633,160,43,CloseDialog);
            Button(save,"ConfirmSave","保存",1000,633,250,43,ConfirmSave);
            var load=FullGroup(modal,"LoadDialog");loadDialog=load.gameObject;
            Glass(load,"LoadGlass",615,255,690,620,Palette.Glass);
            Label(load,"LoadTitle","载入蓝图",650,285,500,42,24,Ink);
            Button(load,"CloseLoad","×",1240,278,36,36,CloseDialog);
            Label(load,"LoadHint","选择蓝图；可设置多种靶机及各自数量",650,335,575,27,16,Muted);
            for(int i=0;i<6;i++)
            {
                int row=i;float y=382+i*45;
                fileButtons[i]=Button(load,"BlueprintChoice"+i,"",650,y,620,39,()=>{selectedFile=page*6+row;RefreshFileRows();});
                fileLabels[i]=Label(load,"ChoiceLabel"+i,"",663,y+7,594,27,17,Ink);
            }
            emptyFiles=Label(load,"EmptyBlueprints","尚未保存任何蓝图",650,454,620,62,20,Muted,TextAlignmentOptions.Center);
            previousButton=Button(load,"PreviousPage","‹",650,675,42,32,()=>{page=Mathf.Max(0,page-1);RefreshFileRows();});
            pageText=Label(load,"Page","01 / 01",706,680,172,25,14,Muted,TextAlignmentOptions.Center);
            nextButton=Button(load,"NextPage","›",892,675,42,32,()=>{page=Mathf.Min(Mathf.Max(0,(files.Length-1)/6),page+1);RefreshFileRows();});
            targetCountText=Label(load,"TargetCount","",650,717,390,32,16,Ink);
            targetMinus=Button(load,"TargetMinus","−",1120,711,60,38,()=>ChangeTargetCount(-1));
            targetPlus=Button(load,"TargetPlus","+",1190,711,80,38,()=>ChangeTargetCount(1));
            Button(load,"CancelLoad","取消",650,810,125,43,CloseDialog);
            deleteButton=Button(load,"DeleteBlueprint","删除",785,810,115,43,RequestDeleteSelected);
            targetButton=Button(load,"SetTargetBlueprint","设为靶机",910,810,175,43,ToggleTarget);
            targetButtonText=load.Find("SetTargetBlueprintText").GetComponent<TextMeshProUGUI>();
            loadButton=Button(load,"ConfirmLoad","载入",1095,810,175,43,ConfirmLoad);
            // Shared message layer remains above either dialog.
            modalError=Label(modal,"DialogError","",670,537,580,54,16,new Color(1,.53f,.47f));
            modalError.textWrappingMode=TextWrappingModes.Normal;

            var confirm=FullGroup(modal,"ActionConfirm");
            actionConfirmRoot=confirm.gameObject;
            var confirmDim=MakeRect(confirm,"ActionConfirmDim",0,0,1920,1080).gameObject.AddComponent<UnityEngine.UI.Image>();
            confirmDim.color=new Color(0,0,0,.42f);confirmDim.raycastTarget=true;
            Glass(confirm,"ActionConfirmGlass",690,410,540,260,Palette.Glass);
            actionConfirmTitle=Label(confirm,"ActionConfirmTitle","",725,442,470,38,23,Ink);
            actionConfirmMessage=Label(confirm,"ActionConfirmMessage","",725,493,470,54,16,Muted);
            actionConfirmMessage.textWrappingMode=TextWrappingModes.Normal;
            actionConfirmButton=Button(confirm,"ActionConfirmYes","确认",725,590,210,44,RunConfirmAction);
            actionConfirmButtonLabel=confirm.Find("ActionConfirmYesText").GetComponent<TextMeshProUGUI>();
            Button(confirm,"ActionConfirmCancel","取消",985,590,210,44,CancelActionConfirm);
            actionConfirmRoot.SetActive(false);
            modalRoot.SetActive(false);
        }
        public void OpenSaveDialog()
        {
            savingDialog=true;modalRoot.SetActive(true);saveDialog.SetActive(true);loadDialog.SetActive(false);
            CancelActionConfirm();pendingDeleteFile=null;
            modalError.text=string.Empty;SetErrorPosition(true);nameField.text=string.Empty;
            nameField.Select();nameField.ActivateInputField();
        }
        public void OpenLoadDialog()
        {
            savingDialog=false;modalRoot.SetActive(true);saveDialog.SetActive(false);loadDialog.SetActive(true);
            CancelActionConfirm();pendingDeleteFile=null;
            modalError.text=string.Empty;SetErrorPosition(false);
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
            if(string.IsNullOrWhiteSpace(name)){modalError.text="请输入蓝图名称。";return;}
            try
            {
                if(controller.BlueprintExists(name))
                {
                    modalError.text=string.Empty;
                    ShowActionConfirm("覆盖蓝图？","同名蓝图“"+name+"”已存在，是否覆盖？","覆盖",()=>ConfirmOverwrite(name));
                    return;
                }
            }
            catch(Exception e){modalError.text="检查蓝图名称失败："+e.Message;return;}
            if(controller.SaveBlueprint()){blueprintName=name;CloseDialog();}
        }
        void ConfirmOverwrite(string name)
        {
            if(!IsDialogOpen||!savingDialog||name!=nameField.text.Trim())return;
            if(controller.SaveBlueprint(true)){blueprintName=name;CloseDialog();}
        }
        void ConfirmLoad()
        {
            if(files.Length==0||selectedFile<0||selectedFile>=files.Length)return;
            if(controller.LoadBlueprint(files[selectedFile]))CloseDialog();
        }
        void ToggleTarget()
        {
            if(files.Length==0)return;var file=files[selectedFile];int count=controller.TargetCount(file);
            if(controller.SetTargetCount(file,count>0?0:1)){modalError.text=string.Empty;RefreshFileRows();}
        }
        void ChangeTargetCount(int delta)
        {
            if(files.Length==0)return;var file=files[selectedFile];int count=controller.TargetCount(file);
            if(controller.SetTargetCount(file,Mathf.Max(0,count+delta))){modalError.text=string.Empty;RefreshFileRows();}
        }
        void RequestDeleteSelected()
        {
            if(files.Length==0||selectedFile<0||selectedFile>=files.Length)return;
            pendingDeleteFile=files[selectedFile];
            string name=Path.GetFileNameWithoutExtension(pendingDeleteFile);
            ShowActionConfirm("删除蓝图？","确定删除内部蓝图“"+name+"”吗？","删除",ConfirmDeleteSelected);
        }
        void ConfirmDeleteSelected()
        {
            string file=pendingDeleteFile;
            pendingDeleteFile=null;
            if(!string.IsNullOrEmpty(file))controller.DeleteBlueprint(file);
        }
        void ShowActionConfirm(string title,string message,string yesLabel,Action action)
        {
            confirmAction=action;
            confirmOpenedFrame=Time.frameCount;
            actionConfirmTitle.text=title;
            actionConfirmMessage.text=message;
            actionConfirmButtonLabel.text=yesLabel;
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
            statusText.text=message;statusText.color=error?new Color(1,.48f,.42f):Ink;
            if(IsDialogOpen&&error)modalError.text=message;
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
                fileLabels[i].text=available?Path.GetFileNameWithoutExtension(files[index])+(targetCount>0?"   · 靶机 × "+targetCount:""):string.Empty;
                ((UnityEngine.UI.Image)fileButtons[i].targetGraphic).color=available&&index==selectedFile?
                    new Color(.12f,.26f,.4f,.65f):new Color(.025f,.06f,.12f,.25f);
            }
            emptyFiles.gameObject.SetActive(files.Length==0);loadButton.interactable=files.Length>0;
            deleteButton.interactable=files.Length>0;
            if(targetButton){int count=files.Length>0?controller.TargetCount(files[selectedFile]):0;targetButton.interactable=targetPlus.interactable=files.Length>0;targetMinus.interactable=files.Length>0&&count>0;targetButtonText.text=count>0?"取消靶机":"设为靶机";targetCountText.text=files.Length>0?"当前蓝图的靶机数量："+count:"请先保存一个带核心的蓝图";}
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
            if(osFallback)Destroy(osFallback);
            // The primary clone shares the project's static atlas and material.
            // Detach those references before TMP's destructor disposes runtime resources.
            if(font){font.atlasTextures=Array.Empty<Texture2D>();font.material=null;Destroy(font);}
        }
    }
}
