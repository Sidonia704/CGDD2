using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Starframe.Flight;
using Starframe.Presentation;

namespace Starframe.App
{
    public sealed class MenuSurface:MonoBehaviour
    {
        public RectTransform Root{get;private set;}
        public Canvas Canvas{get;private set;}
        public Camera DisplayCamera{get;private set;}
        Camera worldCamera;RenderTexture scene;
        bool ownsScene=true;
        public event Action<Exception> OperationFailed;
        readonly List<Material> materials=new List<Material>();
        readonly List<RawImage> backgrounds=new List<RawImage>();
        public void Initialize(Camera world)
        {
            worldCamera=world;world.cullingMask&=~(1<<5);
            var cameraObject=new GameObject("Menu Presentation Camera");cameraObject.transform.SetParent(transform,false);
            DisplayCamera=cameraObject.AddComponent<Camera>();DisplayCamera.orthographic=true;DisplayCamera.orthographicSize=5.4f;
            DisplayCamera.cullingMask=1<<5;DisplayCamera.depth=20;DisplayCamera.clearFlags=CameraClearFlags.SolidColor;DisplayCamera.backgroundColor=Color.black;DisplayCamera.nearClipPlane=.1f;DisplayCamera.farClipPlane=10;
            cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing=false;
            var go=new GameObject("GameMenuCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.layer=5;go.transform.SetParent(transform,false);
            Canvas=go.GetComponent<Canvas>();Canvas.renderMode=RenderMode.ScreenSpaceCamera;Canvas.worldCamera=DisplayCamera;Canvas.planeDistance=1;
            var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var backdrop=Rect((RectTransform)go.transform,"WorldView",0,0,1920,1080).gameObject.AddComponent<RawImage>();backdrop.raycastTarget=false;backgrounds.Add(backdrop);
            Root=Rect((RectTransform)go.transform,"Page",0,0,1920,1080);
            if(!FindFirstObjectByType<EventSystem>()){
                var events=new GameObject("MenuEventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));events.transform.SetParent(transform,false);events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            Resize();
        }
        public void InitializeOverlay(Canvas owner,Camera world)
        {
            ownsScene=false;worldCamera=world;Canvas=owner;DisplayCamera=owner.worldCamera;scene=world.targetTexture;
            Root=GetComponent<RectTransform>();if(!Root)throw new InvalidOperationException("Overlay requires a RectTransform.");
            Root.SetParent(owner.transform,false);Root.anchorMin=Vector2.zero;Root.anchorMax=Vector2.one;Root.offsetMin=Root.offsetMax=Vector2.zero;Root.SetAsLastSibling();
            var blocker=gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=new Color(0,0,0,.55f);blocker.raycastTarget=true;
        }
        public void Clear()
        {
            for(int i=Root.childCount-1;i>=0;i--){var child=Root.GetChild(i).gameObject;child.SetActive(false);Destroy(child);}
            foreach(var value in materials)if(value)Destroy(value);materials.Clear();
            backgrounds.RemoveAll(x=>!x||x.name!="WorldView");
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
        }
        public static RectTransform Rect(RectTransform parent,string name,float x,float y,float w,float h)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;var r=(RectTransform)go.transform;r.SetParent(parent,false);
            r.anchorMin=new Vector2(x/1920f,1-(y+h)/1080f);r.anchorMax=new Vector2((x+w)/1920f,1-y/1080f);r.offsetMin=r.offsetMax=Vector2.zero;return r;
        }
        static RectTransform Local(RectTransform parent,string name,Vector2 min,Vector2 max,Vector2 lower,Vector2 upper)
        {
            var go=new GameObject(name,typeof(RectTransform));go.layer=5;var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=min;r.anchorMax=max;r.offsetMin=lower;r.offsetMax=upper;return r;
        }
        public TextMeshProUGUI Label(string name,string text,float x,float y,float w,float h,float size=16,Color? color=null)
        {
            var label=Rect(Root,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();ConfigureText(label,text,size,color??GlassUiPalette.Ink);return label;
        }
        static void ConfigureText(TextMeshProUGUI label,string text,float size,Color color)
        {
            label.font=L10n.CurrentFont;label.text=text;label.fontSize=size;label.color=color;label.raycastTarget=false;
            L10n.ApplyUserContentFont(label);
            label.textWrappingMode=TextWrappingModes.Normal;label.overflowMode=TextOverflowModes.Ellipsis;label.enableAutoSizing=false;label.characterWidthAdjustment=0;
        }
        public RectTransform Panel(string name,float x,float y,float w,float h,bool dialog=false)
        {
            var rect=Rect(Root,name,x,y,w,h);var image=rect.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
            var mat=new Material(Shader.Find("Starframe/UI/BackdropBlur"));GlassUiPalette.Apply(mat,dialog?GlassUiPalette.Dialog:GlassUiPalette.Glass);
            var uv=new UnityEngine.Rect(x/1920f,1-(y+h)/1080f,w/1920f,h/1080f);mat.SetVector("_PanelUVRect",new Vector4(uv.x,uv.y,uv.width,uv.height));
            image.uvRect=uv;image.material=mat;image.texture=scene;materials.Add(mat);backgrounds.Add(image);return rect;
        }
        public UnityEngine.UI.Button Button(string name,string text,float x,float y,float w,float h,Action action,bool primary=false)
        {
            var rect=Rect(Root,name,x,y,w,h);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=primary?GlassUiPalette.Accent:GlassUiPalette.Button;
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.navigation=new Navigation{mode=Navigation.Mode.Automatic};
            var colors=button.colors;colors.highlightedColor=new Color(.86f,.98f,.97f,1);colors.pressedColor=new Color(.65f,.80f,.8f,1);colors.disabledColor=new Color(.5f,.52f,.55f,.4f);button.colors=colors;
            var label=Local(rect,name+"Label",Vector2.zero,Vector2.one,new Vector2(14,4),new Vector2(-14,-4)).gameObject.AddComponent<TextMeshProUGUI>();ConfigureText(label,text,16,primary?new Color(.045f,.075f,.08f,1):GlassUiPalette.Ink);label.alignment=TextAlignmentOptions.Center;label.textWrappingMode=TextWrappingModes.NoWrap;
            button.onClick.AddListener(()=>{GameAudio.PlayUiClick();try{action?.Invoke();}catch(Exception error)when(error is System.IO.IOException||error is System.IO.InvalidDataException||error is UnauthorizedAccessException){if(OperationFailed!=null)OperationFailed(error);else Debug.LogWarning(error.Message);}});return button;
        }
        public TMP_InputField Input(string name,string value,float x,float y,float w,float h,Action<string> changed)
        {
            var rect=Rect(Root,name,x,y,w,h);var bg=rect.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=GlassUiPalette.Button;
            var field=rect.gameObject.AddComponent<TMP_InputField>();var viewport=Local(rect,"Viewport",Vector2.zero,Vector2.one,new Vector2(12,5),new Vector2(-12,-5));viewport.gameObject.AddComponent<RectMask2D>();
            var text=Local(viewport,"Value",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero).gameObject.AddComponent<TextMeshProUGUI>();ConfigureText(text,value,16,GlassUiPalette.Ink);text.alignment=TextAlignmentOptions.MidlineLeft;text.textWrappingMode=TextWrappingModes.NoWrap;
            field.targetGraphic=bg;field.textViewport=viewport;field.textComponent=text;field.fontAsset=L10n.CurrentFont;field.characterLimit=64;field.lineType=TMP_InputField.LineType.SingleLine;field.text=value;
            field.customCaretColor=true;field.caretColor=GlassUiPalette.Accent;field.selectionColor=new Color(.4f,.7f,.7f,.3f);field.onValueChanged.AddListener(v=>changed?.Invoke(v));return field;
        }
        public ScrollRect Scroll(string name,float x,float y,float w,float h,out RectTransform content)
        {
            var root=Rect(Root,name,x,y,w,h);var scroll=root.gameObject.AddComponent<ScrollRect>();
            var viewport=Local(root,"Viewport",Vector2.zero,Vector2.one,Vector2.zero,new Vector2(-20,0));
            var maskImage=viewport.gameObject.AddComponent<UnityEngine.UI.Image>();maskImage.color=Color.clear;maskImage.canvasRenderer.cullTransparentMesh=false;viewport.gameObject.AddComponent<RectMask2D>();
            content=Local(viewport,"Content",new Vector2(0,1),Vector2.one,Vector2.zero,Vector2.zero);content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
            var rail=Local(root,"Scrollbar",new Vector2(1,0),Vector2.one,new Vector2(-16,0),Vector2.zero);var hit=rail.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=new Color(1,1,1,.025f);
            var handle=Local(rail,"Handle",Vector2.zero,Vector2.one,new Vector2(5,0),new Vector2(-5,0));var handleImage=handle.gameObject.AddComponent<UnityEngine.UI.Image>();handleImage.color=new Color(.75f,.85f,.85f,.5f);
            var bar=rail.gameObject.AddComponent<Scrollbar>();bar.handleRect=handle;bar.targetGraphic=handleImage;bar.direction=Scrollbar.Direction.BottomToTop;
            scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.verticalScrollbar=bar;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=38;scroll.inertia=true;scroll.decelerationRate=.09f;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }
        public TMP_Dropdown Dropdown(string name,List<string> options,int selected,float x,float y,float w,float h,Action<int> changed)
        {
            var rect=Rect(Root,name,x,y,w,h);var bg=rect.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=GlassUiPalette.Button;var dropdown=rect.gameObject.AddComponent<TMP_Dropdown>();dropdown.targetGraphic=bg;
            var caption=Local(rect,"Caption",Vector2.zero,Vector2.one,new Vector2(14,5),new Vector2(-34,-5)).gameObject.AddComponent<TextMeshProUGUI>();ConfigureText(caption,"",16,GlassUiPalette.Ink);caption.alignment=TextAlignmentOptions.MidlineLeft;caption.textWrappingMode=TextWrappingModes.NoWrap;dropdown.captionText=caption;
            var arrow=Local(rect,"Arrow",new Vector2(1,0),Vector2.one,new Vector2(-29,5),new Vector2(-7,-5)).gameObject.AddComponent<MenuDropdownArrow>();arrow.color=GlassUiPalette.Muted;arrow.raycastTarget=false;
            var template=Local(rect,"Template",Vector2.zero,new Vector2(1,0),new Vector2(0,-300),Vector2.zero);var panel=template.gameObject.AddComponent<UnityEngine.UI.Image>();panel.color=new Color(.025f,.04f,.05f,.985f);
            var scroll=template.gameObject.AddComponent<ScrollRect>();var viewport=Local(template,"Viewport",Vector2.zero,Vector2.one,new Vector2(4,4),new Vector2(-4,-4));viewport.gameObject.AddComponent<RectMask2D>();
            var content=Local(viewport,"Content",new Vector2(0,1),Vector2.one,Vector2.zero,Vector2.zero);content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,44);
            var item=Local(content,"Item",new Vector2(0,1),Vector2.one,new Vector2(0,-44),Vector2.zero);var itemBg=item.gameObject.AddComponent<UnityEngine.UI.Image>();itemBg.color=new Color(.3f,.5f,.5f,.4f);var toggle=item.gameObject.AddComponent<UnityEngine.UI.Toggle>();toggle.targetGraphic=itemBg;
            var check=Local(item,"Selected",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero).gameObject.AddComponent<UnityEngine.UI.Image>();check.color=new Color(.5f,.8f,.8f,.22f);toggle.graphic=check;
            var itemLabel=Local(item,"Label",Vector2.zero,Vector2.one,new Vector2(12,3),new Vector2(-12,-3)).gameObject.AddComponent<TextMeshProUGUI>();ConfigureText(itemLabel,"",16,GlassUiPalette.Ink);itemLabel.alignment=TextAlignmentOptions.MidlineLeft;itemLabel.textWrappingMode=TextWrappingModes.NoWrap;
            dropdown.template=template;dropdown.itemText=itemLabel;scroll.content=content;scroll.viewport=viewport;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
            template.gameObject.SetActive(false);dropdown.AddOptions(options);dropdown.value=selected;dropdown.RefreshShownValue();dropdown.onValueChanged.AddListener(i=>changed?.Invoke(i));return dropdown;
        }
        public UnityEngine.UI.Slider Slider(string name,float min,float max,float value,float x,float y,float w,float h,Action<float> changed)
        {
            var rect=Rect(Root,name,x,y,w,h);var hit=rect.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=Color.clear;hit.canvasRenderer.cullTransparentMesh=false;var rail=Local(rect,"Track",new Vector2(0,.5f),new Vector2(1,.5f),new Vector2(0,-3),new Vector2(0,3));rail.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.55f,.65f,.65f,.18f);
            var fill=Local(rail,"Fill",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);fill.gameObject.AddComponent<UnityEngine.UI.Image>().color=GlassUiPalette.Accent;
            var handle=Local(rect,"Handle",new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(-6,-10),new Vector2(6,10));var image=handle.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=GlassUiPalette.Ink;
            var slider=rect.gameObject.AddComponent<UnityEngine.UI.Slider>();slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=image;slider.minValue=min;slider.maxValue=max;slider.value=value;slider.onValueChanged.AddListener(v=>changed?.Invoke(v));return slider;
        }
        public UnityEngine.UI.Toggle Toggle(string name,string text,bool value,float x,float y,float w,float h,Action<bool> changed)
        {
            var rect=Rect(Root,name,x,y,w,h);var hit=rect.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=Color.clear;hit.canvasRenderer.cullTransparentMesh=false;var square=Local(rect,"Box",new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(0,-12),new Vector2(24,12));var bg=square.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=GlassUiPalette.Button;
            var check=Local(square,"Check",Vector2.zero,Vector2.one,new Vector2(5,5),new Vector2(-5,-5));var image=check.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=GlassUiPalette.Accent;
            var label=Local(rect,"Label",Vector2.zero,Vector2.one,new Vector2(38,0),Vector2.zero).gameObject.AddComponent<TextMeshProUGUI>();ConfigureText(label,text,16,GlassUiPalette.Ink);label.alignment=TextAlignmentOptions.MidlineLeft;
            var toggle=rect.gameObject.AddComponent<UnityEngine.UI.Toggle>();toggle.targetGraphic=bg;toggle.graphic=image;toggle.isOn=value;toggle.onValueChanged.AddListener(v=>changed?.Invoke(v));return toggle;
        }
        void Update()=>Resize();
        void Resize()
        {
            if(!ownsScene){if(worldCamera&&scene!=worldCamera.targetTexture){scene=worldCamera.targetTexture;foreach(var image in backgrounds)if(image)image.texture=scene;}return;}
            if(!worldCamera)return;int width=Mathf.Max(1,Screen.width),height=Mathf.Max(1,Screen.height);if(scene&&scene.width==width&&scene.height==height)return;
            if(scene){worldCamera.targetTexture=null;scene.Release();Destroy(scene);}scene=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf){name="GameMenuView",useMipMap=true,autoGenerateMips=true,filterMode=FilterMode.Trilinear};scene.Create();worldCamera.targetTexture=scene;worldCamera.aspect=(float)width/height;
            foreach(var image in backgrounds)if(image)image.texture=scene;
        }
        void OnDestroy()
        {
            if(ownsScene){if(worldCamera&&worldCamera.targetTexture==scene)worldCamera.targetTexture=null;if(scene){scene.Release();Destroy(scene);}}foreach(var material in materials)if(material)Destroy(material);
        }
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuDropdownArrow:UnityEngine.UI.Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var center=rectTransform.rect.center;var tint=(Color32)color;
            vh.AddVert(center+new Vector2(-5,2),tint,Vector2.zero);vh.AddVert(center+new Vector2(0,-3),tint,Vector2.zero);vh.AddVert(center+new Vector2(5,2),tint,Vector2.zero);vh.AddTriangle(0,1,2);
        }
    }
}
