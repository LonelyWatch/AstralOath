using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Bootstrap
{
    // Explicit command-line acceptance hook; absent from normal interactive launches.
    public sealed class CharacterLabPlayerProbe : MonoBehaviour
    {
        private readonly List<string> errors = new List<string>();
        private void Awake() => Application.logMessageReceived += RecordLog;
        private void OnDestroy() => Application.logMessageReceived -= RecordLog;
        private void RecordLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
                (type == LogType.Warning && message.StartsWith("GameText missing glyph ", StringComparison.Ordinal))) errors.Add(message);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRequestedProbe()
        {
            if (!Environment.GetCommandLineArgs().Contains("-astral-lab-smoke")) return;
            Application.runInBackground = true;
            new GameObject("CharacterLab Acceptance Probe").AddComponent<CharacterLabPlayerProbe>();
        }

        private async void Start()
        {
            try
            {
                var bootstrap = FindObjectOfType<CharacterLabBootstrap>();
                if (!bootstrap) throw new InvalidOperationException("CharacterLab scene is not active.");
                await Until(() => bootstrap.IsReady || !string.IsNullOrEmpty(bootstrap.StartupError), 90);
                if (!bootstrap.IsReady) throw new InvalidOperationException(bootstrap.StartupError);
                var map = FindObjectsOfType<BindingMap>().Single();
                map.Validate();
                await RequireVisibleMesh();
                if (!bootstrap.IsAudioAvailable) throw new InvalidOperationException(bootstrap.AudioDiagnostic);
                await Capture("battle");
                foreach (int character in new[] { 0, 1, 2 })
                {
                    await ClickRow(map, "characters", character);
                    await RequireVisibleMesh();
                    await ClickRow(map, "voices", 0);
                    await Task.Delay(1000);
                    Click(map, "stop");
                    for (int skill = 0; skill < 3; skill++)
                    {
                        await ClickRow(map, "skills", skill);
                        await Task.Delay(3500);
                        if (!bootstrap.IsReady || !bootstrap.IsAudioAvailable)
                            throw new InvalidOperationException(bootstrap.StartupError ?? bootstrap.AudioDiagnostic);
                        Click(map, "stop");
                    }
                }
                await ClickRow(map, "characters", 0);
                Click(map, "pause"); await Task.Delay(100);
                Click(map, "play"); await Task.Delay(100);
                Click(map, "portrait"); await Task.Delay(1500);
                await RequireVisibleMesh();
                await Capture("portrait");
                if (Environment.GetCommandLineArgs().Contains("-astral-lab-quit-open"))
                {
                    if (errors.Count > 0) throw new InvalidOperationException("Player logged unexpected errors before quit: " + string.Join(" | ", errors.Take(8)));
                    // This requests shutdown; only the final process log can validate quit cleanup.
                    Debug.Log("ASTRAL_CHARACTERLAB_REQUESTED_QUIT_OPEN");
                    Application.Quit(0);
                    return;
                }
                Click(map, "stop");
                Click(map, "close");
                await Until(() => !map, 10);
                Destroy(bootstrap.gameObject);
                await Task.Delay(250);
                if (FindObjectsOfType<MeshFilter>().Any(f => f.sharedMesh && f.sharedMesh.vertexCount > 0))
                    throw new InvalidOperationException("Closing the lab retained skeleton meshes.");
                if (errors.Count > 0) throw new InvalidOperationException("Player logged unexpected errors: " + string.Join(" | ", errors.Take(8)));
                Debug.Log("ASTRAL_CHARACTERLAB_PLAYER_SMOKE_PASSED startup,binding,mesh,three-characters,nine-skill-events,voices,battle,portrait,pause,stop,close");
                Application.Quit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("ASTRAL_CHARACTERLAB_PLAYER_SMOKE_FAILED " + error);
                Application.Quit(2);
            }
        }

        private static async Task Until(Func<bool> ready, int seconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!ready())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("CharacterLab player probe timed out.");
                await Task.Delay(50);
            }
        }
        private static Task RequireVisibleMesh() => Until(() => FindObjectsOfType<MeshRenderer>().Any(r =>
            r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<MeshFilter>() is MeshFilter f &&
            f.sharedMesh && f.sharedMesh.vertexCount > 0), 15);
        private static void Click(BindingMap map, string key) => map.Require<GameButton>(key).GetComponent<Button>().onClick.Invoke();

        private static async Task ClickRow(BindingMap map, string key, int index)
        {
            var rows = map.Require<RectTransform>(key).GetComponentsInChildren<Button>(false);
            if (index >= rows.Length) throw new InvalidOperationException("Missing probe row: " + key);
            var button = rows[index];
            button.onClick.Invoke();
            await Until(() => !button || button.interactable, 40);
            await Task.Delay(150);
        }

        private static async Task Capture(string suffix)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-astral-lab-capture");
            if (index < 0 || index + 1 >= args.Length) return;
            string directory = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, suffix + ".png");
            if (File.Exists(file)) File.Delete(file);
            ScreenCapture.CaptureScreenshot(file);
            byte[] bytes=null;
            await Until(() => TryReadCompletedPng(file,out bytes), 10);
            var capture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if(!ImageConversion.LoadImage(capture,bytes,false))
                    throw new InvalidDataException("Completed screenshot could not be decoded: "+file);
                ValidateCapturePixels(capture.GetPixels32());
                Debug.Log("ASTRAL_CHARACTERLAB_CAPTURE_VALID "+suffix+" "+capture.width+"x"+capture.height);
            }
            finally { Destroy(capture); }
        }
        public static void ValidateCapturePixels(Color32[] pixels)
        {
            if(pixels==null||pixels.Length==0)throw new InvalidDataException("Screenshot contains no pixels.");
            int first=pixels[0].r|(pixels[0].g<<8)|(pixels[0].b<<16);
            for(int i=1;i<pixels.Length;i++)
                if((pixels[i].r|(pixels[i].g<<8)|(pixels[i].b<<16))!=first)return;
            throw new InvalidDataException(first==0?"Screenshot is entirely black.":"Screenshot contains only one RGB color.");
        }
        private static bool TryReadCompletedPng(string file,out byte[] bytes)
        {
            bytes=null;
            try
            {
                if(!File.Exists(file))return false;
                var candidate=File.ReadAllBytes(file);
                byte[] header={137,80,78,71,13,10,26,10};
                byte[] end={0,0,0,0,73,69,78,68,174,66,96,130};
                if(candidate.Length<header.Length+end.Length)return false;
                for(int i=0;i<header.Length;i++)if(candidate[i]!=header[i])return false;
                for(int i=0;i<end.Length;i++)if(candidate[candidate.Length-end.Length+i]!=end[i])return false;
                bytes=candidate;return true;
            }
            catch(IOException){return false;}
        }
    }
}
