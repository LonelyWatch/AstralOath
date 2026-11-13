using System.Text;
using System.Text.Json;
using AstralOath.Client.Core;
namespace UnityEngine
{
    public class Object { public string name; public static implicit operator bool(Object value) => value != null; }
    public sealed class TextAsset : Object
    {
        public byte[] bytes;
        public string text => Encoding.UTF8.GetString(bytes);
        public TextAsset(byte[] data, string name) { bytes = data; this.name = name; }
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value,
            new JsonSerializerOptions { IncludeFields = true });
    }
    public static class Debug { public static void LogWarning(string value) => Console.WriteLine(value); }
}
namespace UnityEngine.Scripting { [AttributeUsage(AttributeTargets.Class)] public sealed class PreserveAttribute : Attribute {} }
namespace AOT { [AttributeUsage(AttributeTargets.Method)] public sealed class MonoPInvokeCallbackAttribute : Attribute { public MonoPInvokeCallbackAttribute(Type type) {} } }
namespace AstralOath.Client.Core { public interface IAssetService { Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object; } }

namespace Unity.Profiling
{
    public sealed class ProfilerMarker
    {
        public ProfilerMarker(string name) { }
        public Scope Auto() => default;
        public readonly struct Scope : System.IDisposable { public void Dispose() { } }
    }
}
