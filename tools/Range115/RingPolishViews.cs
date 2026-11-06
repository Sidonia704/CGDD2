using UnityEngine;
using Starframe.Flight;

public static class RingPolishViews
{
    static Camera camera;
    static Vector3 position;
    static Quaternion rotation;
    static bool ortho, chaseEnabled;
    static float size,far;
    static FlightChaseCamera chase;
    static Canvas[] canvases;
    static bool[] canvasEnabled;
    public static string Top()
    {
        camera=Camera.main;position=camera.transform.position;rotation=camera.transform.rotation;
        ortho=camera.orthographic;size=camera.orthographicSize;far=camera.farClipPlane;
        chase=camera.GetComponent<FlightChaseCamera>();chaseEnabled=chase&&chase.enabled;if(chase)chase.enabled=false;
        camera.orthographic=true;camera.orthographicSize=27000;camera.farClipPlane=60000;
        camera.transform.SetPositionAndRotation(new Vector3(0,32000,0),Quaternion.LookRotation(Vector3.down,Vector3.forward));
        canvases=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);canvasEnabled=new bool[canvases.Length];
        for(int i=0;i<canvases.Length;i++){canvasEnabled[i]=canvases[i].enabled;canvases[i].enabled=false;}
        return "Full world-anchored three-band top view.";
    }
    public static string Restore()
    {
        if(camera){camera.transform.SetPositionAndRotation(position,rotation);camera.orthographic=ortho;camera.orthographicSize=size;camera.farClipPlane=far;}
        if(chase)chase.enabled=chaseEnabled;
        if(canvases!=null)for(int i=0;i<canvases.Length;i++)if(canvases[i])canvases[i].enabled=canvasEnabled[i];
        return "Runtime camera and HUD restored.";
    }
}
