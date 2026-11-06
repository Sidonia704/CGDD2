// @role Assign weapons to named groups and edit their input bindings.
// @calls WeaponGroups, ShipAssembly, WeaponGroupConnections, GameInput

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Starframe.Construction;
using Starframe.Presentation;
using Starframe.Flight;

namespace Starframe.App
{
    /// <summary>Transactional loadout page operating on the supplied preview assembly.</summary>
    public sealed class WeaponGroupScreen:MonoBehaviour
    {
        const int MaxGroups=6;
        static Color Ink=>GlassUiPalette.Ink;static Color Muted=>GlassUiPalette.Muted;static Color Warning=>UiTheme.Current.amber;
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
        float previewTop=535;
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
        static Color GroupColor(int id)=>Color.Lerp(GlassUiPalette.Accent,GroupColors[(int)((uint)id%(uint)GroupColors.Length)],.32f);
        static string Copy(string zh,string en)=>L10n.IsEnglish?en:zh;
        string GroupName(WeaponGroupRecord group)=>"GROUP "+((long)group.id+1);
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
            surface.Label("WeaponGroupsTitle",T("title"),64,44,1380,54,32,Ink);
            surface.Label("WeaponGroupsSubtitle",Copy("选择武器并分配分组。数字键切换分组开关，开火键负责发射。","Assign each weapon to a group. Number keys toggle groups; fire bindings launch their weapons."),66,106,1360,42,16,Muted);
            var add=surface.Button("AddWeaponGroup",T("add"),1620,65,236,48,AddGroup);add.interactable=draft.weaponGroups.Count<MaxGroups;
            if(!add.interactable)surface.Label("GroupLimit",T("limit"),1620,119,236,24,13,Muted);
            bool twoRows=draft.weaponGroups.Count>4;int columns=Mathf.Min(4,draft.weaponGroups.Count);float gap=16,width=Mathf.Min(880,(1792-gap*(columns-1))/columns);
            float cardsLeft=64+(1792-(columns*width+(columns-1)*gap))*.5f;
            for(int i=0;i<draft.weaponGroups.Count;i++)BuildCard(draft.weaponGroups[i],cardsLeft+(i%columns)*(width+gap),168+(i/columns)*170,width,twoRows);
            float selectionY=twoRows?516:455;previewTop=twoRows?594:535;
            surface.Label("WeaponSelection",selectedPart==null?T("pick"):T("selected",WeaponName(selectedPart)),64,selectionY,1348,38,21,Ink);
            surface.Label("WeaponSelectionHint",Copy("点击卡片查看分组；选中武器后使用“分配”按钮。仅显示当前选择的连线。","Select a card to inspect its group. Select a weapon, then use Assign. Only active selection links are shown."),66,selectionY+42,1340,36,15,Muted);
            if(guns.Count==0)surface.Label("NoWeapons",T("empty"),240,700,1060,80,26,Muted);
            BuildRoster();
            surface.Label("WeaponPreviewHelp",T("help"),66,949,1300,30,13,Muted);
            var footer=surface.Panel("WeaponGroupFooter",64,993,1792,70);Decorative(footer);
            surface.Button("WeaponGroupsBack",T("back"),80,1005,160,46,RequestClose);
            status=surface.Label("WeaponGroupStatus","",270,1010,1030,40,16,Muted);
            L10n.ApplyUserContentFont(status);
            applyButton=surface.Button("ApplyWeaponGroups",T("apply"),1598,1005,242,46,Apply,true);
            building=false;Canvas.ForceUpdateCanvases();if(rosterScroll)rosterScroll.verticalNormalizedPosition=rosterPosition;RefreshStatus();RefreshHighlights();
        }

        void BuildCard(WeaponGroupRecord group,float x,float y,float width,bool compact)
        {
            int id=group.id;float height=compact?154:254;var card=surface.Panel("WeaponGroup_"+id,x,y,width,height);cards[id]=card;
            var click=card.gameObject.AddComponent<UnityEngine.UI.Button>();click.targetGraphic=card.GetComponent<UnityEngine.UI.Graphic>();click.onClick.AddListener(()=>ChooseGroup(id));
            if(click.targetGraphic)click.targetGraphic.raycastTarget=true;
            var chip=Rect("GroupColor",card,18,17,5,18);var chipImage=chip.gameObject.AddComponent<UnityEngine.UI.Image>();chipImage.color=GroupColor(id);chipImage.raycastTarget=false;
            string toggle=Copy($"数字键 {id+1} · 启用 / 停用",$"KEY {id+1} · ENABLE / DISABLE");
            Nest(surface.Label("GroupNumber_"+id,T("groupNumber",(long)id+1),0,0,1,1,13,Muted).rectTransform,card,33,13,112,26);
            Nest(surface.Label("GroupToggle_"+id,toggle,0,0,1,1,12,Muted).rectTransform,card,140,13,width-198,26);
            if(id!=0)Nest((RectTransform)surface.Button("DeleteGroup_"+id,"×",0,0,34,30,()=>ConfirmDelete(id)).transform,card,width-49,10,34,30);
            float nameWidth=compact?(width-54)*.56f:width-36;
            var input=surface.Input("GroupName_"+id,GroupName(group),0,0,nameWidth,42,value=>{
                if(building)return;var current=WeaponGroups.FindGroup(draft,id);if(current==null)return;
                current.name=value;RefreshStatus();
            });input.readOnly=true;input.characterLimit=WeaponGroups.MaxNameLength;input.lineType=TMP_InputField.LineType.SingleLine;
            L10n.ApplyUserContentFont(input.textComponent);Nest((RectTransform)input.transform,card,18,48,nameWidth,42);
            float bindingX=compact?nameWidth+30:18,bindingY=compact?48:104,bindingWidth=compact?width-bindingX-18:width-36;
            Nest((RectTransform)surface.Button("GroupBinding_"+id,Copy("开火  ","FIRE  ")+BindingName(group.binding),0,0,bindingWidth,42,()=>OpenBinding(id)).transform,card,bindingX,bindingY,bindingWidth,42);
            string warning=BindingWarning(group);
            if(!compact){
                var note=surface.Label("GroupBindingWarning_"+id,string.IsNullOrEmpty(warning)?Copy("共用开火键的已启用分组会同时射击。","Enabled groups with the same fire binding shoot together."):warning,0,0,width-36,38,14,string.IsNullOrEmpty(warning)?Muted:Warning);
                note.textWrappingMode=TextWrappingModes.Normal;Nest(note.rectTransform,card,18,155,width-36,38);L10n.ApplyUserContentFont(note);
            }
            float footerY=compact?103:205;
            Nest(surface.Label("GroupWeaponCount_"+id,T("weapons",Count(id)),0,0,width*.5f-26,32,15,selectedGroup==id?Ink:Muted).rectTransform,card,18,footerY,width*.5f-26,32);
            var assign=surface.Button("AssignGroup_"+id,Copy("分配","Assign"),0,0,width*.5f-18,34,()=>AssignToGroup(id));assign.interactable=selectedPart!=null;
            Nest((RectTransform)assign.transform,card,width*.5f,footerY,width*.5f-18,34);
            if(selectedGroup==id){var selected=Rect("AssignedGroupMarker",card,18,height-5,width-36,2);var image=selected.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=GroupColor(id);image.raycastTarget=false;}
        }

        void BuildRoster()
        {
            float top=draft.weaponGroups.Count>4?516:455;
            var panel=surface.Panel("WeaponRoster",1452,top,404,972-top);Decorative(panel);
            surface.Label("WeaponRosterTitle",T("roster"),1474,top+19,360,34,19,Ink);
            surface.Label("WeaponRosterHint",Copy("逐件选择与分配，所有更改在应用后保存。","Select and assign each weapon. Apply to save your loadout."),1474,top+59,360,48,15,Muted);
            var scroll=surface.Scroll("WeaponRosterScroll",1472,top+115,364,948-top-115,out var content);
            rosterScroll=scroll;
            scroll.horizontal=false;scroll.vertical=true;
            var layout=content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();if(!layout)layout=content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing=8;layout.padding=new RectOffset(0,5,0,0);layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var fitter=content.GetComponent<UnityEngine.UI.ContentSizeFitter>();if(!fitter)fitter=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            var options=new List<string>();foreach(var group in draft.weaponGroups)options.Add(GroupName(group));
            foreach(var view in guns){
                string id=view.Record.id;var row=Rect("WeaponRow_"+id,content,0,0,356,64);var size=row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();size.preferredHeight=64;
                int groupId=WeaponGroups.GetGroupId(draft,id);
                var select=surface.Button("SelectWeapon_"+id,L10n.T("module."+view.Record.shape)+"\n"+WeaponName(id),0,0,176,64,()=>SelectWeapon(id));
                Nest((RectTransform)select.transform,row,0,0,176,64);var text=select.GetComponentInChildren<TMP_Text>();if(text){UiStyle.ApplyText(text,14);text.textWrappingMode=TextWrappingModes.Normal;text.alignment=TextAlignmentOptions.MidlineLeft;text.margin=new Vector4(8,0,0,0);}
                var swatch=Rect("WeaponGroupColor_"+id,(RectTransform)select.transform,0,0,4,64);var image=swatch.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=id==selectedPart?Color.white:GroupColor(groupId);image.raycastTarget=false;
                int index=draft.weaponGroups.FindIndex(value=>value.id==groupId);
                var dropdown=surface.Dropdown("WeaponGroupChoice_"+id,options,index,0,0,151,44,value=>{
                    if(building||value<0||value>=draft.weaponGroups.Count)return;
                    WeaponGroups.Assign(draft,id,draft.weaponGroups[value].id);selectedPart=null;selectedGroup=WeaponGroups.GetGroupId(draft,id);Build();
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
            if(modal||captureGroup>=0)return;selectedGroup=id;selectedPart=null;Build();
        }
        void AssignToGroup(int id)
        {
            if(modal||captureGroup>=0||selectedPart==null)return;WeaponGroups.Assign(draft,selectedPart,id);selectedPart=null;selectedGroup=id;Build();
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
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);UiModalScope.Protect(surface.Root,modal.transform);
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
                // Keep the heading/instruction band clear. The selected card's marker is its source port;
                // resume its connector inside the 3D area instead of crossing the text.
                var bounds=connections.rectTransform.rect;
                start.x=Mathf.Clamp(start.x,bounds.xMin+bounds.width*(80f/1920),bounds.xMin+bounds.width*(1406f/1920));
                start.y=bounds.yMax-bounds.height*((previewTop+10)/1080);
                var color=GroupColor(id);bool selected=view.Record.id==selectedPart;if(selectedPart!=null?!selected:id!=selectedGroup)continue;color.a=selected?.9f:.48f;
                var link=new WeaponGroupConnections.Connection{from=start,to=local,color=color,width=selected?2.2f:1.4f};
                if(selected)selectedLink=link;else if(links.Count<96)links.Add(link);
            }
            if(selectedLink.HasValue)links.Add(selectedLink.Value);connections.SetConnections(links);
        }

        bool PreviewPoint(Vector2 screen,out Vector2 local)
        {
            local=default;if(!surface)return false;var canvas=surface.Root.GetComponentInParent<Canvas>();var camera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(connections?connections.rectTransform:surface.Root,screen,camera,out local))return false;
            var rect=surface.Root.rect;float x=(local.x-rect.xMin)/Mathf.Max(1,rect.width)*1920,y=(rect.yMax-local.y)/Mathf.Max(1,rect.height)*1080;
            return x>=64&&x<=1430&&y>=previewTop&&y<=940;
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
                var tint=GroupColor(WeaponGroups.GetGroupId(draft,item.view.Record.id));bool selected=item.view.Record.id==selectedPart;bool active=selectedPart!=null?selected:WeaponGroups.GetGroupId(draft,item.view.Record.id)==selectedGroup;if(!active)continue;
                if(material.HasProperty(BaseColor)){var value=item.original.HasProperty(BaseColor)?item.original.GetColor(BaseColor):material.GetColor(BaseColor);item.working.SetColor(BaseColor,Color.Lerp(value,tint,selected?.68f:active?.30f:0f));}
                if(material.HasProperty(ColorProperty)){var value=item.original.HasProperty(ColorProperty)?item.original.GetColor(ColorProperty):material.GetColor(ColorProperty);item.working.SetColor(ColorProperty,Color.Lerp(value,tint,selected?.68f:active?.30f:0f));}
                if(material.HasProperty(Emission))item.working.SetColor(Emission,tint*(selected?.42f:active?.08f:0f));item.renderer.SetPropertyBlock(item.working);
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
            UiModalScope.Protect(surface.Root,modal.transform);
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
