using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Starframe.Flight;
public static class RangeLightResponse
{
    public static string Start()
    {
        var atmosphere=UnityEngine.Object.FindFirstObjectByType<RangeAtmosphere>();if(!atmosphere||!Application.isPlaying)throw new InvalidOperationException("Live atmosphere required.");
        var so=new SerializedObject(atmosphere);var light=(Light)so.FindProperty("starLight").objectReferenceValue;var settings=(RangeAtmosphereTuning)so.FindProperty("tuning").objectReferenceValue;
        settings.skyIntensity=.09f; // Native asset tuning requested by the user; persisted only through the dedicated Save entry.
        var camera=UnityEngine.Object.FindFirstObjectByType<RangeHUD>().WorldCamera;var follow=camera.GetComponent<FlightChaseCamera>();
        var rotation=light.transform.rotation;var color=light.color;float intensity=light.intensity;
        var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;bool followEnabled=follow.enabled;
        var beforeDirection=(Vector3)Shader.GetGlobalVector("_StarframeRangeSkyLight");var beforeWarm=Shader.GetGlobalColor("_StarframeRangeSkyWarm");float beforeBelt=Shader.GetGlobalVector("_StarframeRangeSkyState").z;
        light.transform.rotation=Quaternion.AngleAxis(70,Vector3.up)*rotation;light.color=new Color(.88f,.68f,.48f);light.intensity=1.3f;
        int stage=0,last=-1,frames=0;double deadline=EditorApplication.timeSinceStartup+30;Vector3 changedDirection=Vector3.zero;Color changedWarm=Color.black;float highBelt=0;
        string dir=Path.GetFullPath("Documentation/RangeAtmosphere-2026-11-02");
        EditorApplication.CallbackFunction update=null;
        Action restore=()=>{EditorApplication.update-=update;if(light){light.transform.rotation=rotation;light.color=color;light.intensity=intensity;}if(camera){camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);follow.enabled=followEnabled;}};
        update=()=>{
            try
            {
                if(!Application.isPlaying||EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Lighting response probe timeout.");
                if(last==Time.frameCount)return;last=Time.frameCount;if(++frames<50)return;
                if(stage==0)
                {
                    changedDirection=Shader.GetGlobalVector("_StarframeRangeSkyLight");changedWarm=Shader.GetGlobalColor("_StarframeRangeSkyWarm");
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir,"light-response-warm.png"));stage=1;frames=0;return;
                }
                if(stage==1){follow.enabled=false;camera.transform.position=cameraPosition+Vector3.up*1500;stage=2;frames=0;return;}
                highBelt=Shader.GetGlobalVector("_StarframeRangeSkyState").z;
                float angle=Vector3.Angle(beforeDirection,changedDirection),difference=Mathf.Abs(beforeWarm.r-changedWarm.r)+Mathf.Abs(beforeWarm.g-changedWarm.g)+Mathf.Abs(beforeWarm.b-changedWarm.b);
                if(angle<20||difference<.05f||highBelt>=beforeBelt-.05f)throw new InvalidOperationException("Light/height response failed: "+angle+" / "+difference+" / "+beforeBelt+" -> "+highBelt);
                File.WriteAllText(Path.Combine(dir,"light-response.json"),FormattableString.Invariant($"{{\"passed\":true,\"globalLightDirectionChangeDegrees\":{angle},\"globalWarmColorDifference\":{difference},\"beltProximityBefore\":{beforeBelt},\"beltProximityAfter1500mCameraRise\":{highBelt},\"temporarySceneLightRestored\":true}}"));restore();
            }
            catch(Exception error){File.WriteAllText(Path.Combine(dir,"light-response-error.txt"),error.ToString());restore();Debug.LogException(error);}
        };
        EditorApplication.update+=update;return "Live star-color/direction and belt-height response queued; temporary light and camera changes are restored automatically.";
    }
    public static string Save()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play first.");
        var tuning=AssetDatabase.LoadAssetAtPath<RangeAtmosphereTuning>("Assets/_Starframe/Settings/Rendering/RangeAtmosphereTuning.asset");tuning.skyIntensity=.09f;EditorUtility.SetDirty(tuning);AssetDatabase.SaveAssets();return "Saved sky gradient intensity .09";
    }
}
