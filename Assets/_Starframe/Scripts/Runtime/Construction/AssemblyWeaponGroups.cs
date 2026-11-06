// @role Commit live weapon assignments only on drag release, then clear transient selection; group cards inspect membership.
// @calls ShipEditorUI, ShipAssembly, WeaponGroups, WeaponGroupConnections

using System;
using System.Collections.Generic;
using Starframe.App;
using Starframe.Flight;
using Starframe.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Starframe.Construction
{
    public sealed class AssemblyWeaponGroups:MonoBehaviour
    {
        ShipEditorController editor;ShipEditorUI ui;RectTransform root,cardsRoot;WeaponGroupConnections connections;BlueprintData data;
        readonly Dictionary<int,RectTransform> cards=new Dictionary<int,RectTransform>();
        readonly HashSet<string> selected=new HashSet<string>(StringComparer.Ordinal);
        readonly List<BlockView> weapons=new List<BlockView>();
        readonly List<WeaponGroupConnections.Connection> links=new List<WeaponGroupConnections.Connection>();
        readonly Vector3[] corners=new Vector3[4];
        string lastPart;float lastClick;int selectedGroup;bool dragging,dragMoved;Vector2 dragStart;
        WorkbenchOrbit.ViewState assemblyView;
        static Color Lime=>new Color32(185,255,3,255);
        public bool IsOpen{get;private set;}
        public void Initialize(ShipEditorController controller,ShipEditorUI view){editor=controller;ui=view;}
        public void Open()
        {
            if(IsOpen)return;IsOpen=true;data=editor.Assembly.Export(ui.BlueprintName);WeaponGroups.Normalize(data,ui.Database);selected.Clear();selectedGroup=0;dragging=false;
            assemblyView=editor.Orbit.Capture();editor.Orbit.SetView(assemblyView.pivot-editor.Orbit.Camera.transform.right*(assemblyView.size*.15f),assemblyView.yaw,assemblyView.pitch,assemblyView.size*1.08f);
            root=Full("AssemblyWeaponGroups",ui.CanvasRoot);
            var lineRoot=Full("WeaponGroupConnections",root);connections=lineRoot.gameObject.AddComponent<WeaponGroupConnections>();connections.raycastTarget=false;
            cardsRoot=Full("GroupCards",root);RefreshWeapons();BuildCards();
        }
        public void Close()
        {
            if(!IsOpen)return;IsOpen=false;dragging=false;selected.Clear();if(root){root.gameObject.SetActive(false);Destroy(root.gameObject);}editor.Orbit.Restore(assemblyView);
        }
        void RefreshWeapons(){weapons.Clear();foreach(var view in editor.Assembly.Blocks)if(view&&WeaponGroups.IsWeapon(view.Record,ui.Database))weapons.Add(view);}
        void BuildCards()
        {
            if(!cardsRoot||!ui)return;
            foreach(Transform child in cardsRoot){child.gameObject.SetActive(false);Destroy(child.gameObject);}cards.Clear();
            ui.FText(cardsRoot,"WeaponGroupsHeading",L10n.IsEnglish?"WEAPON GROUPS":"武器分组",552.75f,32.25f,550,47,37.5f,Color.white,true);
            for(int i=0;i<data.weaponGroups.Count;i++){
                var group=data.weaponGroups[i];int id=group.id;float x=554.25f+(i%6)*150,y=95.25f+(i/6)*156;
                var card=ui.FBox(cardsRoot,"WeaponGroup_"+id,x,y,130.5f,144);cards[id]=card.rectTransform;
                var select=card.gameObject.AddComponent<Button>();select.targetGraphic=card;select.onClick.AddListener(()=>InspectGroup(id));
                if(id==selectedGroup){var outline=card.gameObject.AddComponent<Outline>();outline.effectColor=Lime;outline.effectDistance=new Vector2(2,-2);}
                ui.FBox(cardsRoot,"GroupChip_"+id,x+17.25f,y+16.5f,8.25f,18.75f,Color.white,false);
                ui.FText(cardsRoot,"GroupName_"+id,"GROUP "+(id+1),x+35.25f,y+18,86,21,16.5f,Color.white,true);
                int count=data.weaponAssignments.FindAll(a=>a.groupId==id).Count;
                ui.FText(cardsRoot,"Assigned_"+id,(L10n.IsEnglish?"Assigned: ":"已分配: ")+count,x+17.25f,y+46.5f,112,21,15,Lime,true);
                bool right=group.binding!=null&&group.binding.source==WeaponInputSource.MouseRight;
                var binding=ui.FButton(cardsRoot,"GroupFireBinding_"+id,"",x+16.5f,y+91.5f,48.75f,22.5f,()=>SwitchBinding(id),15,Color.white,new Color32(56,62,80,255));
                var knob=ui.FBox(cardsRoot,"BindingKnob_"+id,x+18+(right?22.5f:0),y+93,21.75f,19.5f,Lime,false);
                string fire=group.binding?.source==WeaponInputSource.Keyboard?group.binding.key.ToString():right?"MOUSE RIGHT":"MOUSE LEFT";
                ui.FText(cardsRoot,"BindingLabel_"+id,fire,x+18,y+119.25f,112,21,16.5f,Color.white,true);
                if(id!=WeaponGroups.DefaultGroupId){
                    var remove=ui.FButton(cardsRoot,"DeleteGroup_"+id,"-",x+102,y+3,27,27,()=>Remove(id),19.5f,Color.white,new Color32(44,51,70,255));
                    var label=remove.GetComponentInChildren<TextMeshProUGUI>();if(label)label.gameObject.SetActive(false);
                    // Draw the minus itself so its visibility never depends on font glyph coverage or baseline.
                    var bar=new GameObject("Minus",typeof(RectTransform),typeof(CanvasRenderer),typeof(UnityEngine.UI.Image));bar.layer=remove.gameObject.layer;bar.transform.SetParent(remove.transform,false);
                    var barRect=(RectTransform)bar.transform;barRect.anchorMin=barRect.anchorMax=barRect.pivot=new Vector2(.5f,.5f);barRect.anchoredPosition=Vector2.zero;barRect.sizeDelta=new Vector2(11,2);
                    var barImage=bar.GetComponent<UnityEngine.UI.Image>();barImage.color=Color.white;barImage.raycastTarget=false;
                    remove.transform.SetAsLastSibling();
                }
            }
            float addX=554.25f+(data.weaponGroups.Count%6)*150;
            if(data.weaponGroups.Count<6)ui.FButton(cardsRoot,"AddWeaponGroup","+",addX,96.75f,30,29.25f,Add,22.5f);
            if(weapons.Count==0)ui.FText(cardsRoot,"NoWeapons",L10n.IsEnglish?"Install weapons to create a fire group.":"安装武器后即可分组。",560,270,700,40,24,Color.white);
        }
        void Commit(){selected.Clear();dragging=dragMoved=false;WeaponGroups.Normalize(data,ui?ui.Database:null);if(editor&&editor.Assembly)editor.Assembly.UpdateWeaponGroups(data);BuildCards();}
        void Add(){if(data.weaponGroups.Count>=6)return;var group=WeaponGroups.AddGroup(data,binding:new WeaponInputBinding{source=data.weaponGroups.Count%2==0?WeaponInputSource.MouseLeft:WeaponInputSource.MouseRight},database:ui.Database);selectedGroup=group.id;Commit();}
        void Remove(int id){if(WeaponGroups.RemoveGroup(data,id,ui.Database)){selectedGroup=0;Commit();}}
        void SwitchBinding(int id){var group=WeaponGroups.FindGroup(data,id);if(group==null)return;group.binding=new WeaponInputBinding{source=group.binding?.source==WeaponInputSource.MouseLeft?WeaponInputSource.MouseRight:WeaponInputSource.MouseLeft};Commit();}
        void InspectGroup(int id){if(data==null||WeaponGroups.FindGroup(data,id)==null)return;CancelDrag();selectedGroup=id;BuildCards();}
        void Assign(int id){if(selected.Count==0)return;foreach(var part in selected)WeaponGroups.Assign(data,part,id,ui?ui.Database:null);selectedGroup=id;Commit();}
        void ReleasePointer(int destination)
        {
            bool assign=dragging&&dragMoved&&destination>=0&&data!=null&&WeaponGroups.FindGroup(data,destination)!=null;
            if(assign&&selected.Count>0){Assign(destination);return;}
            // Releasing anywhere ends this gesture, including a click without a drag or an invalid drop.
            CancelDrag();BuildCards();
        }
        public bool CancelDrag(){bool active=dragging||selected.Count>0;dragging=dragMoved=false;selected.Clear();return active;}
        void Update()
        {
            if(!IsOpen)return;if(ui.IsDialogOpen||ui.HasEditorFocus){CancelDrag();return;}var mouse=Mouse.current;var keys=Keyboard.current;if(mouse==null){CancelDrag();return;}
            if(keys!=null&&(keys.leftShiftKey.isPressed||keys.rightShiftKey.isPressed)){CancelDrag();return;}
            var point=mouse.position.ReadValue();
            if(dragging){
                dragMoved|=(point-dragStart).sqrMagnitude>25;
                if(mouse.leftButton.wasReleasedThisFrame)ReleasePointer(GroupUnder(point));
                return;
            }
            if(!mouse.leftButton.wasPressedThisFrame)return;
            int cardId=GroupUnder(point);if(cardId>=0)return;
            if(!ui.WorkspaceContains(point))return;
            var ray=editor.Orbit.Camera.ScreenPointToRay(point);BlockView found=null;
            // Prefer the visible surface; hidden markers remain selectable in crowded assemblies.
            if(Physics.Raycast(ray,out var hit,1000,1,QueryTriggerInteraction.Ignore))found=hit.collider.GetComponentInParent<BlockView>();
            if(!found||!WeaponGroups.IsWeapon(found.Record,ui.Database)){
                float best=18*Screen.height/1080f;found=null;
                foreach(var weapon in weapons){if(!weapon)continue;var screen=editor.Orbit.Camera.WorldToScreenPoint(weapon.transform.TransformPoint(BlockModules.LocalCenter(weapon.Record.shape)));float distance=Vector2.Distance(point,screen);if(screen.z>0&&distance<best){best=distance;found=weapon;}}
            }
            if(!found||!WeaponGroups.IsWeapon(found.Record,ui.Database))return;
            bool family=lastPart==found.Record.id&&Time.unscaledTime-lastClick<.35f;
            selected.Clear();if(family){int type=Family(found.Record.shape);foreach(var weapon in weapons)if(weapon&&Family(weapon.Record.shape)==type)selected.Add(weapon.Record.id);}else selected.Add(found.Record.id);
            lastPart=found.Record.id;lastClick=Time.unscaledTime;selectedGroup=WeaponGroups.GetGroupId(data,found.Record.id);dragging=true;dragMoved=false;dragStart=point;BuildCards();
        }
        int Family(BlockShape shape)
        {
            if(shape==BlockShape.Gun1||shape==BlockShape.Gun2||shape==BlockShape.Gun3)return 100;
            var definition=ui.Database?ui.Database.Get(shape):null;if(definition&&definition.weapon&&definition.weapon.delivery==WeaponDelivery.GuidedMissile)return 101;
            if(shape==BlockShape.Laser||shape==BlockShape.Laser2||shape==BlockShape.Laser3)return 102;
            if(shape==BlockShape.Railgun1||shape==BlockShape.Railgun2||shape==BlockShape.Railgun3)return 103;
            if(shape==BlockShape.Sword1||shape==BlockShape.Sword2)return 104;return (int)shape;
        }
        int GroupUnder(Vector2 point)
        {foreach(var pair in cards)if(pair.Value&&RectTransformUtility.RectangleContainsScreenPoint(pair.Value,point,ui.CanvasCamera))return pair.Key;return -1;}
        bool Local(Vector2 point,out Vector2 local)=>RectTransformUtility.ScreenPointToLocalPointInRectangle(connections.rectTransform,point,ui.CanvasCamera,out local);
        void LateUpdate()
        {
            if(!IsOpen||!connections)return;links.Clear();var camera=editor.Orbit.Camera;var mouse=Mouse.current;
            foreach(var weapon in weapons){
                if(!weapon)continue;var world=weapon.transform.TransformPoint(BlockModules.LocalCenter(weapon.Record.shape));var point=camera.WorldToScreenPoint(world);if(point.z<=0||point.x<0||point.x>Screen.width||point.y<0||point.y>Screen.height||!Local(point,out var local))continue;
                var ray=camera.ScreenPointToRay(point);bool front=true;
                if(Physics.Raycast(ray,out var hit,point.z+10,1,QueryTriggerInteraction.Ignore)){var view=hit.collider.GetComponentInParent<BlockView>();front=view==weapon;}
                Color color=front?Lime:(Color)new Color32(110,153,0,255);bool chosen=selected.Contains(weapon.Record.id);int id=WeaponGroups.GetGroupId(data,weapon.Record.id);
                if(id==selectedGroup&&cards.TryGetValue(id,out var card)){
                    card.GetWorldCorners(corners);var from=(Vector2)connections.rectTransform.InverseTransformPoint((corners[0]+corners[3])*.5f);
                    links.Add(new WeaponGroupConnections.Connection{from=from,to=local,color=new Color(color.r,color.g,color.b,chosen?1:.65f),width=chosen?2:1.5f});
                }
                links.Add(new WeaponGroupConnections.Connection{from=local,to=local,color=color,width=chosen?10:8.6f,markerOnly=true});
                if(dragging&&mouse!=null&&chosen&&Local(mouse.position.ReadValue(),out var cursor))links.Add(new WeaponGroupConnections.Connection{from=local,to=cursor,color=Lime,width=2});
            }
            connections.SetConnections(links);
        }
        static RectTransform Full(string name,RectTransform parent)
        {var r=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;r.gameObject.layer=5;r.SetParent(parent,false);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r;}
        void OnDestroy(){if(IsOpen)Close();}
        void OnApplicationFocus(bool focused){if(!focused)CancelDrag();}
    }
}
