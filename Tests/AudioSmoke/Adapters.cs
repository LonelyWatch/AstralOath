using AstralOath.Client.Core;
namespace UnityEngine { public class Object {} public sealed class TextAsset : Object { public byte[] bytes; public TextAsset(byte[] data) => bytes=data; } }
namespace AOT { [AttributeUsage(AttributeTargets.Method)] public sealed class MonoPInvokeCallbackAttribute : Attribute { public MonoPInvokeCallbackAttribute(Type type) {} } }
namespace AstralOath.Client.Core { public interface IAssetService { Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object; } }
sealed class FileAssets : IAssetService {
 readonly string root; public int Loaded,Released;
 public FileAssets(string root) => this.root=Path.GetFullPath(root);
 public Task<T> LoadAsync<T>(string address,BindingScope scope) where T:UnityEngine.Object {
  scope.Token.ThrowIfCancellationRequested();
  string path=Path.GetFullPath(Path.Combine(root,address));
  if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Source escaped root");
  var value=new UnityEngine.TextAsset(File.ReadAllBytes(path));Loaded++;scope.Add(()=>Released++);
  return Task.FromResult((T)(UnityEngine.Object)value);
 }
}
