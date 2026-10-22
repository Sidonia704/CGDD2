using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Starframe.Construction;
using Starframe.Presentation;

namespace Starframe.App
{
    /// <summary>Transactional loadout page operating on the supplied preview assembly.</summary>
    public sealed class WeaponGroupScreen:MonoBehaviour
    {
        const int MaxGroups=8;
        static readonly Color Ink=new Color(.94f,.95f,.96f,1),Muted=new Color(.66f,.69f,.72f,1),Warning=new Color(.92f,.72f,.43f,1);
        static readonly Color[] GroupColors={new Color(.85f,.73f,.49f),new Color(.58f,.76f,.65f),new Color(.81f,.59f,.62f),new Color(.71f,.65f,.82f),new Color(.87f,.66f,.47f),new Color(.52f,.75f,.74f),new Color(.77f,.72f,.62f),new Color(.66f,.72f,.81f)};
        static readonly int BaseColor=Shader.PropertyToID("_BaseColor"),ColorProperty=Shader.PropertyToID("_Color"),Emission=Shader.PropertyToID("_EmissionColor");
        sealed class Highlight
        {
            public BlockView view;public MeshRenderer renderer;public MaterialPropertyBlock original,working;
        }
        MenuSurface surface;ShipAssembly assembly;Camera previewCamera;BlueprintData draft;
        Action<BlueprintData> onApply;Action onClose;
        readonly List<BlockView> guns=new List<BlockView>();
        readonly Dictionary<int,RectTransform> cards=new Dictionary<int,RectTransform>();
        readonly Dictionary<string,int> groupByPart=new Dictionary<string,int>(StringComparer.Ordinal);
        readonly List<Highlight> highlights=new List<Highlight>();
        readonly List<WeaponGroupConnections.Connection> links=new List<WeaponGroupConnections.Connection>();
        readonly List<RaycastResult> uiHits=new List<RaycastResult>();
        readonly Vector3[] cardCorners=new Vector3[4];
        WeaponGroupConnections connections;TextMeshProUGUI status;
        UnityEngine.UI.ScrollRect rosterScroll;float rosterPosition=1;
        UnityEngine.UI.Button applyButton;GameObject modal;
        string selectedPart,baseline;int selectedGroup,captureGroup=-1,captureFrame;
        bool open,ownsSuspension,previousSuspension,building;
        public bool HasUnsavedChanges=>draft!=null&&JsonUtility.ToJson(draft)!=baseline;
        public BlueprintData Draft=>draft;
        public bool IsCapturingBinding=>captureGroup>=0;

        public void Initialize(MenuSurface menu,ShipAssembly preview,Camera camera,BlueprintData blueprint,Action<BlueprintData> apply,Action close)
        {
            RestoreHighlights();RestoreSuspension();L10n.Changed-=LocaleChanged;
            surface=menu;assembly=preview;previewCamera=camera;onApply=apply;onClose=close;
            if(!surface||!assembly||!previewCamera||blueprint==null)throw new ArgumentException("Weapon grouping requires a menu, preview assembly, camera and blueprint.");
            draft=Clone(blueprint);WeaponGroups.Normalize(draft);baseline=JsonUtility.ToJson(draft);
            selectedPart=null;selectedGroup=0;captureGroup=-1;open=true;rosterPosition=1;rosterScroll=null;
            previousSuspension=GameInput.Suspended;ownsSuspension=true;GameInput.Suspended=true;
            guns.Clear();foreach(var view in assembly.Blocks)if(view&&WeaponGroups.IsWeapon(view.Record)&&draft.parts.Exists(p=>p.id==view.Record.id))guns.Add(view);
            guns.Sort((a,b)=>string.CompareOrdinal(a.Record.id,b.Record.id));
            foreach(var view in guns)if(view.Solid){
                var original=new MaterialPropertyBlock();view.Solid.GetPropertyBlock(original);
                highlights.Add(new Highlight{view=view,renderer=view.Solid,original=original,working=new MaterialPropertyBlock()});
            }
            L10n.Changed+=LocaleChanged;Build();RefreshHighlights();
        }

        static BlueprintData Clone(BlueprintData value)=>JsonUtility.FromJson<BlueprintData>(JsonUtility.ToJson(value));
        static string T(string key,params object[] args)=>L10n.T("ui.weaponGroups."+key,args);
        static Color GroupColor(int id)=>GroupColors[(int)((uint)id%(uint)GroupColors.Length)];
        string GroupName(WeaponGroupRecord group)=>group.name=="Group "+((long)group.id+1)?T("groupDefault",(long)group.id+1):group.name;
        int Number(string id)=>guns.FindIndex(view=>view.Record.id==id)+1;
        string WeaponName(string id)=>T("gun",Number(id).ToString("00"));
        int Count(int id)=>draft.weaponAssignments.FindAll(value=>value.groupId==id).Count;

        void Build()
        {
            if(!open||!surface)return;if(rosterScroll)rosterPosition=rosterScroll.verticalNormalizedPosition;
            building=true;captureGroup=-1;modal=null;surface.Clear();cards.Clear();groupByPart.Clear();foreach(var assignment in draft.weaponAssignments)groupByPart[assignment.partId]=assignment.groupId;
            var lineObject=new GameObject("WeaponGroupConnections",typeof(RectTransform),typeof(CanvasRenderer),typeof(WeaponGroupConnections));
            lineObject.layer=surface.Root.gameObject.layer;
            var lineRect=(RectTransform)lineObject.transform;lineRect.SetParent(surface.Root,false);lineRect.anchorMin=Vector2.zero;lineRect.anchorMax=Vector2.one;lineRect.offsetMin=lineRect.offsetMax=Vector2.zero;
            connections=lineObject.GetComponent<WeaponGroupConnections>();connections.raycastTarget=false;
            surface.Label("WeaponGroupsTitle",T("title"),64,44,1380,62,40,Ink);
            surface.Label("WeaponGroupsSubtitle",T("subtitle"),66,114,1360,32,19,Muted);
            var add=surface.Button("AddWeaponGroup",T("add"),1620,65,236,48,AddGroup);add.interactable=draft.weaponGroups.Count<MaxGroups;
            if(!add.interactable)surface.Label("GroupLimit",T("limit"),1620,119,236,24,13,Muted);
            float gap=14,width=Mathf.Min(340,(1792-gap*(draft.weaponGroups.Count-1))/draft.weaponGroups.Count);
            for(int i=0;i<draft.weaponGroups.Count;i++)BuildCard(draft.weaponGroups[i],64+i*(width+gap),180,width);
            surface.Label("WeaponSelection",selectedPart==null?T("pick"):T("selected",WeaponName(selectedPart)),64,455,1348,42,23,Ink);
            surface.Label("WeaponSelectionHint",T("selectionHint"),66,502,1340,30,16,Muted);
            if(guns.Count==0)surface.Label("NoWeapons",T("empty"),240,700,1060,80,26,Muted);
            BuildRoster();
            surface.Label("WeaponPreviewHelp",T("help"),66,949,1300,30,15,Muted);
            var footer=surface.Panel("WeaponGroupFooter",64,993,1792,70);Decorative(footer);
            surface.Button("WeaponGroupsBack",T("back"),80,1005,160,46,RequestClose);
            status=surface.Label("WeaponGroupStatus","",270,1010,1030,40,16,Muted);
            L10n.ApplyUserContentFont(status);
            applyButton=surface.Button("ApplyWeaponGroups",T("apply"),1598,1005,242,46,Apply,true);
            building=false;Canvas.ForceUpdateCanvases();if(rosterScroll)rosterScroll.verticalNormalizedPosition=rosterPosition;RefreshStatus();RefreshHighlights();
        }

        void BuildCard(WeaponGroupRecord group,float x,float y,float width)
        {
            int id=group.id;var card=surface.Panel("WeaponGroup_"+id,x,y,width,254);cards[id]=card;
            var click=card.gameObject.AddComponent<UnityEngine.UI.Button>();click.targetGraphic=card.GetComponent<UnityEngine.UI.Graphic>();click.onClick.AddListener(()=>ChooseGroup(id));
            if(click.targetGraphic)click.targetGraphic.raycastTarget=true;
            var chip=Rect("GroupColor",card,14,17,6,20);var chipImage=chip.gameObject.AddComponent<UnityEngine.UI.Image>();chipImage.color=GroupColor(id);chipImage.raycastTarget=false;
            Nest(surface.Label("GroupNumber_"+id,T("groupNumber",(long)id+1),0,0,1,1,12,Muted).rectTransform,card,29,14,width-110,25);
            if(id!=0)Nest((RectTransform)surface.Button("DeleteGroup_"+id,"×",0,0,44,26,()=>ConfirmDelete(id)).transform,card,width-58,12,44,28);
            var input=surface.Input("GroupName_"+id,GroupName(group),0,0,width-28,40,value=>{
                if(building)return;var current=WeaponGroups.FindGroup(draft,id);if(current==null)return;
                current.name=value;RefreshStatus();
            });input.characterLimit=WeaponGroups.MaxNameLength;input.lineType=TMP_InputField.LineType.SingleLine;
            L10n.ApplyUserContentFont(input.textComponent);
            Nest((RectTransform)input.transform,card,14,52,width-28,40);
            Nest((RectTransform)surface.Button("GroupBinding_"+id,BindingName(group.binding),0,0,width-28,40,()=>OpenBinding(id)).transform,card,14,104,width-28,40);
            string warning=BindingWarning(group);var note=surface.Label("GroupBindingWarning_"+id,string.IsNullOrEmpty(warning)?T("bindingNote"):warning,0,0,width-28,34,12,string.IsNullOrEmpty(warning)?Muted:Warning);
            note.textWrappingMode=TextWrappingModes.Normal;note.enableAutoSizing=true;note.fontSizeMin=10;note.fontSizeMax=12;Nest(note.rectTransform,card,14,151,width-28,38);
            L10n.ApplyUserContentFont(note);
            Nest(surface.Label("GroupWeaponCount_"+id,T("weapons",Count(id)),0,0,width-28,25,15,Ink).rectTransform,card,14,190,width-28,25);
            var assign=surface.Button("AssignGroup_"+id,T("assign"),0,0,width-28,30,()=>ChooseGroup(id));assign.interactable=selectedPart!=null;
            var label=assign.GetComponentInChildren<TMP_Text>();if(label)label.fontSize=13;
            Nest((RectTransform)assign.transform,card,14,219,width-28,27);
            if(selectedPart!=null&&WeaponGroups.GetGroupId(draft,selectedPart)==id){
                var selected=Rect("AssignedGroupMarker",card,0,0,3,254);var image=selected.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=GroupColor(id);image.raycastTarget=false;
            }
        }

        void BuildRoster()
        {
            var panel=surface.Panel("WeaponRoster",1452,455,404,517);Decorative(panel);
            surface.Label("WeaponRosterTitle",T("roster"),1474,474,360,34,21,Ink);
            surface.Label("WeaponRosterHint",T("rosterHint"),1474,514,360,48,14,Muted);
            var scroll=surface.Scroll("WeaponRosterScroll",1472,570,364,378,out var content);
            rosterScroll=scroll;
            scroll.horizontal=false;scroll.vertical=true;
            var layout=content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();if(!layout)layout=content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing=8;layout.padding=new RectOffset(0,5,0,0);layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var fitter=content.GetComponent<UnityEngine.UI.ContentSizeFitter>();if(!fitter)fitter=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var options=new List<string>();foreach(var group in draft.weaponGroups)options.Add(GroupName(group));
            foreach(var view in guns){
                string id=view.Record.id;var row=Rect("WeaponRow_"+id,content,0,0,356,64);var size=row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();size.preferredHeight=64;
                int groupId=WeaponGroups.GetGroupId(draft,id);
                var select=surface.Button("SelectWeapon_"+id,WeaponName(id)+"\n"+T("shortId",id.Substring(0,Math.Min(6,id.Length))),0,0,176,64,()=>SelectWeapon(id));
                Nest((RectTransform)select.transform,row,0,0,176,64);var text=select.GetComponentInChildren<TMP_Text>();if(text){text.fontSize=15;text.alignment=TextAlignmentOptions.MidlineLeft;text.margin=new Vector4(18,0,8,0);}
                var swatch=Rect("WeaponGroupColor_"+id,(RectTransform)select.transform,0,0,4,64);var image=swatch.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=id==selectedPart?Color.white:GroupColor(groupId);image.raycastTarget=false;
                int index=draft.weaponGroups.FindIndex(value=>value.id==groupId);
                var dropdown=surface.Dropdown("WeaponGroupChoice_"+id,options,index,0,0,151,44,value=>{
                    if(building||value<0||value>=draft.weaponGroups.Count)return;
                    WeaponGroups.Assign(draft,id,draft.weaponGroups[value].id);selectedPart=id;selectedGroup=WeaponGroups.GetGroupId(draft,id);Build();
                });Nest((RectTransform)dropdown.transform,row,184,10,151,44);
                L10n.ApplyUserContentFont(dropdown.captionText);L10n.ApplyUserContentFont(dropdown.itemText);
            }
        }

        public void SelectWeapon(string partId)
        {
            if(!open||captureGroup>=0||!guns.Exists(view=>view.Record.id==partId))return;
            selectedPart=partId;selectedGroup=WeaponGroups.GetGroupId(draft,partId);Build();
        }
        void ChooseGroup(int id)
        {
            if(modal||captureGroup>=0)return;selectedGroup=id;
            if(selectedPart!=null)WeaponGroups.Assign(draft,selectedPart,id);Build();
        }
        void AddGroup()
        {
            if(draft.weaponGroups.Count>=MaxGroups)return;
            var group=WeaponGroups.AddGroup(draft,null,WeaponInputBinding.MouseLeft());selectedGroup=group.id;Build();
        }
        void ConfirmDelete(int id)
        {
            ShowModal(T("deleteTitle"),T("deleteHint"),L10n.T("ui.delete"),()=>{WeaponGroups.RemoveGroup(draft,id);selectedGroup=0;Build();});
        }

        void RefreshStatus()
        {
            if(!status)return;bool dirty=HasUnsavedChanges;status.text=dirty?T("dirty"):T("saved");status.color=dirty?Warning:Muted;
            string warning=BindingWarning(WeaponGroups.FindGroup(draft,selectedGroup));if(!string.IsNullOrEmpty(warning)){status.text+="  ·  "+warning;status.color=Warning;}
            if(applyButton)applyButton.interactable=dirty;
        }
        public void Apply()
        {
            if(!open||captureGroup>=0||modal)return;
            WeaponGroups.Normalize(draft);var applied=Clone(draft);onApply?.Invoke(applied);baseline=JsonUtility.ToJson(draft);if(open&&surface)Build();
        }
        public void ReportSaveFailure()
        {
            if(status){status.text=L10n.T("app.storageError");status.color=Warning;}
            if(applyButton)applyButton.interactable=HasUnsavedChanges;
        }
        public void RequestClose()
        {
            if(!open)return;if(captureGroup>=0){CloseModal();return;}
            if(HasUnsavedChanges){ShowModal(T("discardTitle"),T("discardHint"),T("discard"),Close);return;}Close();
        }
        void Close()
        {
            open=false;captureGroup=-1;RestoreHighlights();RestoreSuspension();L10n.Changed-=LocaleChanged;onClose?.Invoke();
        }

        void OpenBinding(int id)
        {
            if(modal)return;captureGroup=id;captureFrame=Time.frameCount;
            var group=WeaponGroups.FindGroup(draft,id);var panel=ModalPanel(T("captureTitle"),T("captureHint"));
            Nest(surface.Label("CurrentWeaponBinding",BindingName(group.binding),0,0,1,1,25,Ink).rectTransform,panel,32,122,616,48);
            var warning=BindingWarning(group);var warningLabel=surface.Label("BindingCaptureWarning",warning,0,0,1,1,15,Warning);L10n.ApplyUserContentFont(warningLabel);Nest(warningLabel.rectTransform,panel,32,184,616,62);
            Nest((RectTransform)surface.Button("ClearWeaponGroupBinding",T("clearBinding"),0,0,260,44,()=>SetBinding(id,new WeaponInputBinding{source=WeaponInputSource.None})).transform,panel,32,278,280,46);
            Nest((RectTransform)surface.Button("CancelWeaponBinding",T("captureCancel"),0,0,260,44,CloseModal).transform,panel,368,278,280,46);
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
        }
        void SetBinding(int id,WeaponInputBinding binding)
        {
            var group=WeaponGroups.FindGroup(draft,id);if(group!=null)WeaponGroups.ConfigureGroup(draft,id,group.name,binding);
            captureGroup=-1;Build();
        }
        string BindingName(WeaponInputBinding binding)
        {
            if(binding==null)return T("unbound");
            switch(binding.source){
                case WeaponInputSource.MouseLeft:return T("mouseLeft");case WeaponInputSource.MouseRight:return T("mouseRight");case WeaponInputSource.MouseMiddle:return T("mouseMiddle");
                case WeaponInputSource.Keyboard:
                    if(Keyboard.current!=null&&binding.key!=Key.None){string name=Keyboard.current[binding.key].displayName;if(!string.IsNullOrEmpty(name))return name;}
                    return binding.key.ToString();default:return T("unbound");
            }
        }
        string BindingWarning(WeaponGroupRecord group)
        {
            if(group?.binding==null||group.binding.source==WeaponInputSource.None)return "";
            var shared=draft.weaponGroups.Find(value=>value.id!=group.id&&SameBinding(group.binding,value.binding));
            string conflict=shared!=null?T("conflict",GroupName(shared)):"";
            bool reserved=Reserved(group.binding);
            return conflict+(reserved?(conflict.Length>0?"\n":"")+T("reserved"):"");
        }
        static bool SameBinding(WeaponInputBinding a,WeaponInputBinding b)=>a!=null&&b!=null&&a.source==b.source&&(a.source!=WeaponInputSource.Keyboard||a.key==b.key);
        static bool IsGroupToggleKey(Key key)=>key==Key.Digit1||key==Key.Digit2||key==Key.Digit3||key==Key.Digit4||key==Key.Digit5||key==Key.Digit6||key==Key.Digit7||key==Key.Digit8;
        static bool Reserved(WeaponInputBinding binding)
        {
            if(binding!=null&&binding.source==WeaponInputSource.Keyboard&&IsGroupToggleKey(binding.key))return true;
            string path=BindingPath(binding);if(string.IsNullOrEmpty(path))return false;if(GameInput.IsReserved(path))return true;
            foreach(GameAction action in Enum.GetValues(typeof(GameAction))){var flight=GameInput.Get(action);if(flight!=null&&(GameInput.MatchesWeaponBinding(flight.path,binding)||GameInput.MatchesWeaponBinding(flight.alternatePath,binding)))return true;}
            return false;
        }
        static string BindingPath(WeaponInputBinding binding)
        {
            if(binding==null)return null;
            switch(binding.source){
                case WeaponInputSource.MouseLeft:return "<Mouse>/leftButton";case WeaponInputSource.MouseRight:return "<Mouse>/rightButton";case WeaponInputSource.MouseMiddle:return "<Mouse>/middleButton";
                case WeaponInputSource.Keyboard:return binding.key==Key.None?null:"<Keyboard>/"+(Keyboard.current!=null?Keyboard.current[binding.key].name:binding.key.ToString().ToLowerInvariant());
                default:return null;
            }
        }

        void Update()
        {
            if(!open)return;var keyboard=Keyboard.current;
            if(captureGroup>=0){
                if(Time.frameCount<=captureFrame)return;
                if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){CloseModal();return;}
                if(keyboard!=null)foreach(var key in keyboard.allKeys)if(key.wasPressedThisFrame){if(IsGroupToggleKey(key.keyCode))continue;SetBinding(captureGroup,WeaponInputBinding.Keyboard(key.keyCode));return;}
                var mouse=Mouse.current;if(mouse!=null){
                    // Capture controls remain clickable; clicking the dimmed area binds a mouse button.
                    if(mouse.leftButton.wasPressedThisFrame&&!OverModalButton()){SetBinding(captureGroup,WeaponInputBinding.MouseLeft());return;}
                    if(mouse.rightButton.wasPressedThisFrame){SetBinding(captureGroup,new WeaponInputBinding{source=WeaponInputSource.MouseRight});return;}
                    if(mouse.middleButton.wasPressedThisFrame){SetBinding(captureGroup,new WeaponInputBinding{source=WeaponInputSource.MouseMiddle});return;}
                }return;
            }
            if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame){
                if(modal){CloseModal();return;}
                foreach(var dropdown in surface.Root.GetComponentsInChildren<TMP_Dropdown>())if(dropdown.IsExpanded){dropdown.Hide();return;}
                var focused=FocusedInput();if(focused){focused.DeactivateInputField();if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);return;}
                RequestClose();return;
            }
            if(modal||Mouse.current==null||!Mouse.current.leftButton.wasPressedThisFrame||PointerOverUi())return;
            var editing=FocusedInput();if(editing){editing.DeactivateInputField();if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);}
            var position=Mouse.current.position.ReadValue();if(!PreviewPoint(position,out _))return;
            var ray=previewCamera.ScreenPointToRay(position);BlockView best=null;float distance=float.PositiveInfinity;
            foreach(var hit in Physics.RaycastAll(ray,float.PositiveInfinity,previewCamera.cullingMask&~(1<<2),QueryTriggerInteraction.Ignore)){
                var view=hit.collider.GetComponentInParent<BlockView>();if(!view||!view.transform.IsChildOf(assembly.transform)||hit.distance>=distance)continue;
                distance=hit.distance;best=view;
            }
            if(best&&WeaponGroups.IsWeapon(best.Record))SelectWeapon(best.Record.id);
        }

        void LateUpdate()
        {
            if(!open||!connections||!previewCamera||!surface)return;links.Clear();WeaponGroupConnections.Connection? selectedLink=null;
            foreach(var view in guns){
                if(!view)continue;int id=groupByPart.TryGetValue(view.Record.id,out int assigned)?assigned:0;if(!cards.TryGetValue(id,out var card)||!card)continue;
                var point=previewCamera.WorldToScreenPoint(view.transform.TransformPoint(BlockModules.LocalCenter(view.Record.shape)));
                if(point.z<=0||!PreviewPoint(point,out var local))continue;
                card.GetWorldCorners(cardCorners);var start=(Vector2)connections.rectTransform.InverseTransformPoint((cardCorners[0]+cardCorners[3])*.5f);
                var color=GroupColor(id);bool selected=view.Record.id==selectedPart;color.a=selected?.88f:.23f;
                var link=new WeaponGroupConnections.Connection{from=start,to=local,color=color,width=selected?2.6f:1.1f};
                if(selected)selectedLink=link;else if(links.Count<160)links.Add(link);
            }
            if(selectedLink.HasValue)links.Add(selectedLink.Value);connections.SetConnections(links);
        }

        bool PreviewPoint(Vector2 screen,out Vector2 local)
        {
            local=default;if(!surface)return false;var canvas=surface.Root.GetComponentInParent<Canvas>();var camera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(connections?connections.rectTransform:surface.Root,screen,camera,out local))return false;
            var rect=surface.Root.rect;float x=local.x-rect.xMin,y=rect.yMax-local.y;
            return x>=64&&x<=1430&&y>=535&&y<=940;
        }
        bool PointerOverUi()
        {
            if(!EventSystem.current||Mouse.current==null)return false;uiHits.Clear();var data=new PointerEventData(EventSystem.current){position=Mouse.current.position.ReadValue()};EventSystem.current.RaycastAll(data,uiHits);
            foreach(var hit in uiHits)if(hit.module is UnityEngine.UI.GraphicRaycaster)return true;return false;
        }
        bool OverModalButton()
        {
            if(!EventSystem.current||Mouse.current==null)return false;uiHits.Clear();var data=new PointerEventData(EventSystem.current){position=Mouse.current.position.ReadValue()};EventSystem.current.RaycastAll(data,uiHits);
            foreach(var hit in uiHits){
                if(!(hit.module is UnityEngine.UI.GraphicRaycaster)||!modal||!hit.gameObject.transform.IsChildOf(modal.transform))continue;
                return hit.gameObject.GetComponentInParent<UnityEngine.UI.Button>()!=null;
            }return false;
        }
        static TMP_InputField FocusedInput()
        {
            if(!EventSystem.current||!EventSystem.current.currentSelectedGameObject)return null;
            var field=EventSystem.current.currentSelectedGameObject.GetComponentInParent<TMP_InputField>();return field&&field.isFocused?field:null;
        }

        void RefreshHighlights()
        {
            foreach(var item in highlights){
                if(!item.renderer||!item.view)continue;item.renderer.SetPropertyBlock(item.original);item.renderer.GetPropertyBlock(item.working);var material=item.renderer.sharedMaterial;if(!material)continue;
                var tint=GroupColor(WeaponGroups.GetGroupId(draft,item.view.Record.id));bool selected=item.view.Record.id==selectedPart;
                if(material.HasProperty(BaseColor)){var value=item.original.HasProperty(BaseColor)?item.original.GetColor(BaseColor):material.GetColor(BaseColor);item.working.SetColor(BaseColor,Color.Lerp(value,tint,selected?.78f:.33f));}
                if(material.HasProperty(ColorProperty)){var value=item.original.HasProperty(ColorProperty)?item.original.GetColor(ColorProperty):material.GetColor(ColorProperty);item.working.SetColor(ColorProperty,Color.Lerp(value,tint,selected?.78f:.33f));}
                if(material.HasProperty(Emission))item.working.SetColor(Emission,tint*(selected?.65f:.08f));item.renderer.SetPropertyBlock(item.working);
            }
        }
        void RestoreHighlights(){foreach(var item in highlights)if(item.renderer)item.renderer.SetPropertyBlock(item.original);highlights.Clear();}
        void RestoreSuspension(){if(ownsSuspension){GameInput.Suspended=previousSuspension;ownsSuspension=false;}}
        void LocaleChanged(){if(open)Build();}
        void OnDisable(){open=false;captureGroup=-1;RestoreHighlights();RestoreSuspension();L10n.Changed-=LocaleChanged;}
        void OnDestroy(){RestoreHighlights();RestoreSuspension();L10n.Changed-=LocaleChanged;}

        RectTransform ModalPanel(string title,string hint)
        {
            modal=new GameObject("WeaponGroupModal",typeof(RectTransform),typeof(UnityEngine.UI.Image));modal.layer=surface.Root.gameObject.layer;var blocker=(RectTransform)modal.transform;blocker.SetParent(surface.Root,false);blocker.anchorMin=Vector2.zero;blocker.anchorMax=Vector2.one;blocker.offsetMin=blocker.offsetMax=Vector2.zero;
            var image=modal.GetComponent<UnityEngine.UI.Image>();image.color=new Color(.025f,.03f,.035f,.64f);image.raycastTarget=true;
            var panel=surface.Panel("WeaponGroupDialog",620,330,680,360,true);Nest(panel,blocker,620,330,680,360);
            Nest(surface.Label("WeaponGroupDialogTitle",title,0,0,1,1,28,Ink).rectTransform,panel,32,27,616,43);
            var message=surface.Label("WeaponGroupDialogHint",hint,0,0,1,1,17,Muted);message.textWrappingMode=TextWrappingModes.Normal;Nest(message.rectTransform,panel,32,83,616,75);
            return panel;
        }
        void ShowModal(string title,string hint,string action,Action confirm)
        {
            if(modal)return;var panel=ModalPanel(title,hint);
            Nest((RectTransform)surface.Button("KeepEditingWeaponGroups",T("keepEditing"),0,0,280,48,CloseModal).transform,panel,32,278,280,48);
            Nest((RectTransform)surface.Button("ConfirmWeaponGroupAction",action,0,0,280,48,()=>{CloseModal();confirm();},true).transform,panel,368,278,280,48);
        }
        void CloseModal(){captureGroup=-1;if(modal){modal.SetActive(false);Destroy(modal);modal=null;}}
        static void Decorative(RectTransform panel){foreach(var graphic in panel.GetComponents<UnityEngine.UI.Graphic>())graphic.raycastTarget=false;}
        static RectTransform Rect(string name,RectTransform parent,float x,float y,float width,float height)
        {
            var result=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;result.gameObject.layer=parent.gameObject.layer;Nest(result,parent,x,y,width,height);return result;
        }
        static void Nest(RectTransform rect,RectTransform parent,float x,float y,float width,float height)
        {
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);
        }
    }
}
