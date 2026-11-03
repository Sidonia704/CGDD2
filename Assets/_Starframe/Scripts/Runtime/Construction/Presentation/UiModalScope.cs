// @role Keep keyboard navigation inside the top modal and restore underlying navigation when it closes.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Starframe.Presentation
{
    public sealed class UiModalScope:MonoBehaviour
    {
        readonly Dictionary<Selectable,Navigation> previous=new Dictionary<Selectable,Navigation>();
        RectTransform root;Transform boundary;GameObject previousFocus;bool configured;
        public static void Protect(RectTransform root,Transform boundary)
        {
            var scope=boundary.GetComponent<UiModalScope>();if(!scope)scope=boundary.gameObject.AddComponent<UiModalScope>();
            scope.root=root;scope.boundary=boundary;scope.configured=true;scope.Capture();
        }
        bool Inside(Transform target)
        {
            if(!target||!root||!boundary||!target.IsChildOf(root))return false;
            while(target.parent!=root&&target.parent)target=target.parent;
            return target.GetSiblingIndex()>=boundary.GetSiblingIndex();
        }
        void Capture()
        {
            if(!configured||!root)return;
            previousFocus=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            foreach(var control in root.GetComponentsInChildren<Selectable>())
            {
                if(Inside(control.transform)||previous.ContainsKey(control))continue;
                previous.Add(control,control.navigation);var navigation=control.navigation;navigation.mode=Navigation.Mode.None;control.navigation=navigation;
            }
            FocusFirst();
        }
        void FocusFirst()
        {
            if(!EventSystem.current||!root)return;
            foreach(var control in root.GetComponentsInChildren<Selectable>())
                if(control.IsInteractable()&&Inside(control.transform)){EventSystem.current.SetSelectedGameObject(control.gameObject);return;}
            EventSystem.current.SetSelectedGameObject(null);
        }
        void LateUpdate()
        {
            if(!configured||!EventSystem.current)return;
            var selected=EventSystem.current.currentSelectedGameObject;
            if(selected&&!Inside(selected.transform))FocusFirst();
        }
        void OnEnable(){if(configured)Capture();}
        void OnDisable()
        {
            foreach(var entry in previous)if(entry.Key)entry.Key.navigation=entry.Value;previous.Clear();
            if(EventSystem.current&&previousFocus&&previousFocus.activeInHierarchy)EventSystem.current.SetSelectedGameObject(previousFocus);
        }
    }
}
