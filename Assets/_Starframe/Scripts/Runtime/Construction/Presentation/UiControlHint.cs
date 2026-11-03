// @role Reveal contextual control help on pointer hover or keyboard focus without intercepting clicks.
// @calls MenuSurface
using UnityEngine;
using UnityEngine.EventSystems;
using Starframe.App;

namespace Starframe.Presentation
{
    public sealed class UiControlHint:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler,IPointerDownHandler
    {
        public MenuSurface surface;public string message;
        public void OnPointerEnter(PointerEventData e){if(surface)surface.ScheduleHint(this,message,e.position);}
        public void OnPointerExit(PointerEventData e){if(surface)surface.HideHint(this);}
        public void OnPointerDown(PointerEventData e){if(surface)surface.HideHint(this);}
        public void OnSelect(BaseEventData e)
        {
            if(surface)surface.ScheduleHint(this,message,RectTransformUtility.WorldToScreenPoint(surface.DisplayCamera,transform.position));
        }
        public void OnDeselect(BaseEventData e){if(surface)surface.HideHint(this);}
        void OnDisable(){if(surface)surface.HideHint(this);}
    }
}
