using UnityEngine;
using System.Linq;
public static class Audio116TestTick
{
    public static object Run(){bool old=Application.runInBackground;Application.runInBackground=true;return new{old,frame=Time.frameCount,time=Time.time,realtime=Time.realtimeSinceStartup,playing=Application.isPlaying,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,objects=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(x=>x&&x.GetType().Name.Contains("Test")).Select(x=>new{name=x.name,type=x.GetType().FullName}).ToArray()};}
}
