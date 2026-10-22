// @role Create construction-bay floor markings and orientation references.

using TMPro;
using UnityEngine;
namespace Starframe.Construction
{
    public sealed class WorkbenchEnvironment : MonoBehaviour
    {
        [SerializeField] Material lineMaterial;
        bool ready;
        public void Initialize(TMP_FontAsset font)
        {
            if(ready)return;ready=true;
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
            {
                Line("Corner",new Vector3(x*6,.023f,z*5.4f),new Vector3(x*6,.023f,z*6),.023f);
                Line("Corner",new Vector3(x*5.4f,.023f,z*6),new Vector3(x*6,.023f,z*6),.023f);
                for(int i=0;i<7;i++)
                {
                    float a=x*(5.35f-i*.11f);
                    Line("Calibration",new Vector3(a,.025f,z*5.6f),new Vector3(a,.025f,z*5.68f),.014f);
                }
            }
            Word(font,"0  /  ORIGIN",new Vector3(-1.8f,.026f,-1.35f),new Vector3(90,0,0),.012f);
            Word(font,"X  /  01",new Vector3(5.4f,.026f,0),new Vector3(90,90,0),.018f);
            Word(font,"Z  /  02",new Vector3(0,.026f,5.4f),new Vector3(90,0,0),.018f);
        }
        void Word(TMP_FontAsset font,string value,Vector3 position,Vector3 rotation,float scale)
        {
            var go=new GameObject("SurfaceLabel_"+value,typeof(TextMeshPro));
            go.transform.SetParent(transform,false);go.transform.position=position;go.transform.eulerAngles=rotation;go.transform.localScale=Vector3.one*scale;
            var text=go.GetComponent<TextMeshPro>();text.font=font;text.text=value;text.fontSize=36;text.color=new Color(.33f,.44f,.46f,1);
            text.alignment=TextAlignmentOptions.Center;text.textWrappingMode=TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta=new Vector2(250,60);
        }
        void Line(string name,Vector3 a,Vector3 b,float width)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.positionCount=2;
            line.SetPositions(new[]{a,b});line.startWidth=line.endWidth=width;line.sharedMaterial=lineMaterial;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
        }
    }
}