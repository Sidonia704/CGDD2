// @role Validate and persist display, audio, language and input preferences.
// @calls GameInput, GameAudio, L10n

using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Starframe.Presentation;

namespace Starframe.App
{
    [Serializable]
    public sealed class GameSettingsData
    {
        public int version=1;
        public string localeCode="en";
        public int fullscreenMode=(int)FullScreenMode.FullScreenWindow;
        public int resolutionWidth=1920,resolutionHeight=1080,refreshRate=60;
        public int vSyncCount,targetFrameRate=-1,qualityLevel=1;
        public float fieldOfView=65,bloomIntensity=.65f;
        public bool bloomEnabled=true,reduceFlashes;
        public float masterVolume=.8f,musicVolume=.22f,sfxVolume=.6f,uiVolume=.6f;
        public bool muteWhenUnfocused=true;
        public float mouseSensitivity=1,aimSmoothing=1;
        public bool invertY;
        public GameBinding[] bindings=GameInput.DefaultBindings();

        public GameSettingsData Clone()=>JsonUtility.FromJson<GameSettingsData>(JsonUtility.ToJson(this));
        public void Validate()
        {
            version=1;
            localeCode=localeCode!=null&&localeCode.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh-Hans":"en";
            if(fullscreenMode!=(int)FullScreenMode.ExclusiveFullScreen&&fullscreenMode!=(int)FullScreenMode.FullScreenWindow&&fullscreenMode!=(int)FullScreenMode.Windowed)
                fullscreenMode=(int)FullScreenMode.FullScreenWindow;
            resolutionWidth=Mathf.Clamp(resolutionWidth,640,16384);resolutionHeight=Mathf.Clamp(resolutionHeight,480,8640);
            refreshRate=Mathf.Clamp(refreshRate,24,1000);vSyncCount=Mathf.Clamp(vSyncCount,0,2);
            targetFrameRate=targetFrameRate<0?-1:Mathf.Clamp(targetFrameRate,30,500);
            qualityLevel=Mathf.Max(0,qualityLevel);
            fieldOfView=Finite(fieldOfView,65,45,100);bloomIntensity=Finite(bloomIntensity,.65f,0,2);
            mouseSensitivity=Finite(mouseSensitivity,1,.2f,3);aimSmoothing=Finite(aimSmoothing,1,0,3);
            masterVolume=Finite(masterVolume,.8f,0,1);musicVolume=Finite(musicVolume,.22f,0,1);
            sfxVolume=Finite(sfxVolume,.6f,0,1);uiVolume=Finite(uiVolume,.6f,0,1);
            bindings=GameInput.Normalize(bindings);
        }
        static float Finite(float value,float fallback,float min,float max)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,min,max);
    }

    public static class GameSettings
    {
        static GameSettingsData current;
        static GameSettingsData defaults;
        static GameSettingsRuntime runtime;
        public static GameSettingsData Current {get{if(current==null)Load();return current;}}
        public static event Action Changed;
        public static string LastError {get;private set;}
        public static string FilePath=>Path.Combine(Application.persistentDataPath,"Settings","preferences.json");

        public static GameSettingsData Defaults()
        {
            if(defaults!=null)return defaults.Clone();
            var resolution=Screen.currentResolution;
            defaults=new GameSettingsData
            {
                localeCode=Application.systemLanguage==SystemLanguage.ChineseSimplified||Application.systemLanguage==SystemLanguage.ChineseTraditional||Application.systemLanguage==SystemLanguage.Chinese?"zh-Hans":"en",
                fullscreenMode=(int)Screen.fullScreenMode,
                resolutionWidth=Mathf.Max(640,resolution.width),resolutionHeight=Mathf.Max(480,resolution.height),
                refreshRate=Mathf.Max(24,(int)Math.Round(resolution.refreshRateRatio.value)),
                qualityLevel=QualitySettings.GetQualityLevel(),vSyncCount=QualitySettings.vSyncCount,
                targetFrameRate=Application.targetFrameRate>0?Application.targetFrameRate:-1
            };
            defaults.Validate();return defaults.Clone();
        }
        public static void Load()
        {
            current=GameSettingsStorage.ReadFallback(FilePath,Defaults(),out var error);
            LastError=error==null?null:"settings.loadFailed";
            if(error!=null)Debug.LogWarning("Using default preferences: "+error.Message);
            current.Validate();
        }
        public static bool Save()
        {
            try
            {
                GameSettingsStorage.Write(FilePath,Current);
                LastError=null;return true;
            }
            catch(Exception error) when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException)
            { LastError="settings.saveFailed";Debug.LogWarning("Could not save preferences: "+error.Message);return false; }
        }
        public static void SetCurrent(GameSettingsData data)
        {current=(data??Defaults()).Clone();current.Validate();}
        public static void Apply()
        {
            Current.Validate();
            int quality=Mathf.Clamp(Current.qualityLevel,0,Mathf.Max(0,QualitySettings.names.Length-1));
            QualitySettings.SetQualityLevel(quality,true);Current.qualityLevel=quality;
            QualitySettings.vSyncCount=Current.vSyncCount;Application.targetFrameRate=Current.targetFrameRate;
            if(!Application.isEditor)
                Screen.SetResolution(Current.resolutionWidth,Current.resolutionHeight,(FullScreenMode)Current.fullscreenMode,new RefreshRate{numerator=(uint)Current.refreshRate,denominator=1});
            L10n.SelectLocale(Current.localeCode);
            if(!runtime)
            {
                var go=new GameObject("Game Settings Runtime");UnityEngine.Object.DontDestroyOnLoad(go);
                runtime=go.AddComponent<GameSettingsRuntime>();
            }
            runtime.ApplyToScene();GameAudio.Apply();Changed?.Invoke();
        }
        public static void ResetDefaults(){SetCurrent(Defaults());Apply();Save();}
    }

    public static class GameSettingsStorage
    {
        /// <summary>Recover at the application boundary without changing the strict reader or source file.</summary>
        public static GameSettingsData ReadFallback(string path,GameSettingsData fallback,out Exception error)
        {
            error=null;
            try{return Read(path,fallback);}
            catch(Exception exception)when(exception is IOException||exception is InvalidDataException
                ||exception is UnauthorizedAccessException||exception is ArgumentException)
            {
                error=exception;
                var recovered=(fallback??new GameSettingsData()).Clone();recovered.Validate();return recovered;
            }
        }
        public static GameSettingsData Read(string path,GameSettingsData fallback)
        {
            if(!File.Exists(path))return (fallback??new GameSettingsData()).Clone();
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Preferences file is too large.");
            var value=JsonUtility.FromJson<GameSettingsData>(File.ReadAllText(path,Encoding.UTF8));
            if(value==null||value.version!=1)throw new InvalidDataException("Preferences version is unsupported.");
            value.Validate();return value;
        }
        public static void Write(string path,GameSettingsData data)
        {
            var snapshot=data.Clone();snapshot.Validate();
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                string directory=Path.GetDirectoryName(path);if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
                File.WriteAllText(temporary,JsonUtility.ToJson(snapshot,true),new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
            }
            finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
    }

    public sealed class GameSettingsRuntime:MonoBehaviour
    {
        void OnEnable(){SceneManager.sceneLoaded+=Loaded;}
        void OnDisable(){SceneManager.sceneLoaded-=Loaded;}
        void Loaded(Scene scene,LoadSceneMode mode){StartCoroutine(ApplyAfterStart());}
        System.Collections.IEnumerator ApplyAfterStart(){yield return null;ApplyToScene();}
        public void ApplyToScene()
        {
            foreach(var volume in FindObjectsByType<Volume>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var profile=volume.HasInstantiatedProfile()?volume.profile:volume.sharedProfile;
                if(!profile||!profile.TryGet<Bloom>(out _))continue;
                var binding=volume.GetComponent<SettingsPostProcessing>();
                if(!binding)binding=volume.gameObject.AddComponent<SettingsPostProcessing>();
                binding.Apply();
            }
        }
    }
    [RequireComponent(typeof(Volume))]
    public sealed class SettingsPostProcessing:MonoBehaviour
    {
        VolumeProfile owned;Bloom bloom;
        void Awake()
        {
            var volume=GetComponent<Volume>();
            var source=volume.HasInstantiatedProfile()?volume.profile:volume.sharedProfile;
            if(!source)return;
            owned=ScriptableObject.CreateInstance<VolumeProfile>();owned.name=source.name+" (User settings)";
            foreach(var component in source.components)owned.components.Add(Instantiate(component));
            volume.profile=owned;owned.TryGet(out bloom);Apply();
        }
        public void Apply()
        {
            if(!bloom)return;
            var settings=GameSettings.Current;
            bloom.active=settings.bloomEnabled;
            bloom.intensity.Override(settings.reduceFlashes?Mathf.Min(settings.bloomIntensity,.15f):settings.bloomIntensity);
        }
        void OnDestroy()
        {
            if(!owned)return;
            foreach(var component in owned.components)if(component)Destroy(component);
            Destroy(owned);
        }
    }
}
