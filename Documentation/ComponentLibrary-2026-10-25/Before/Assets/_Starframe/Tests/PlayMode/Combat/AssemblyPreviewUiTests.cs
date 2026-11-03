using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Starframe.Tests
{
    public sealed class AssemblyPreviewUiTests
    {
        [UnityTest]public IEnumerator LibraryWheelAndScrollbarWorkAndShieldsStayNonPhysical()
        {
            yield return SceneManager.LoadSceneAsync(FlightSession.EditorScene);yield return null;
            var editor=Object.FindFirstObjectByType<ShipEditorController>();
            var blueprint=FlightRangeBootstrap.DefaultShip();blueprint.parts.Add(new BlockRecord{id="ui-shield",shape=BlockShape.Shield,position=new Vector3(-1,1.5f,0),rotation=Quaternion.identity});
            editor.Assembly.Import(blueprint);editor.SelectShape(BlockShape.Shield);
            yield return null;Canvas.ForceUpdateCanvases();
            var scroll=GameObject.Find("ModuleLibraryScroll").GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.That(scroll.content.rect.height,Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.viewport.GetComponent<UnityEngine.UI.Mask>(),Is.Not.Null);
            scroll.verticalNormalizedPosition=1;scroll.StopMovement();Canvas.ForceUpdateCanvases();
            float top=scroll.content.anchoredPosition.y;
            var wheel=new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-6)};
            ExecuteEvents.ExecuteHierarchy(GameObject.Find("CubeIcon"),wheel,ExecuteEvents.scrollHandler);
            yield return null;Canvas.ForceUpdateCanvases();
            Assert.That(scroll.content.anchoredPosition.y,Is.GreaterThan(top+1));
            scroll.verticalScrollbar.value=0;yield return null;Canvas.ForceUpdateCanvases();
            Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(0).Within(.001f));
            var corners=new Vector3[4];GameObject.Find("LaserSlot").GetComponent<RectTransform>().GetWorldCorners(corners);
            foreach(var corner in corners)Assert.That(scroll.viewport.rect.Contains(scroll.viewport.InverseTransformPoint(corner)),Is.True,"The last category must be reachable without expanding the panel.");
            var preview=Object.FindFirstObjectByType<AssemblyShieldPreview>();Assert.That(preview.SurfaceCount,Is.EqualTo(1));
            var surface=GameObject.Find("AssemblyShieldRange");Assert.That(surface.GetComponent<Collider>(),Is.Null);
            Assert.That(surface.GetComponent<MeshRenderer>().bounds.size.x,Is.EqualTo(16).Within(.01f));
            var placement=(MeshRenderer)typeof(ShipEditorController).GetField("shieldPreview",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
            Assert.That(placement,Is.Not.Null);Assert.That(placement.transform.localScale.x,Is.EqualTo(16));Assert.That(placement.gameObject.layer,Is.EqualTo(2));
            Assert.That(editor.Assembly.Remove("ui-shield"),Is.True);Assert.That(preview.SurfaceCount,Is.Zero);
            yield return null;
        }
    }
}
