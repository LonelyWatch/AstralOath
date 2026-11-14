using System.Text.Json;
using AstralOath.ThirdParty.Spine21;

if (args.Length != 3) throw new ArgumentException("Usage: UiTouchPulse <Spine JSON> <atlas> <output JSON>");
using var atlasReader = File.OpenText(args[1]);
using var skeletonReader = File.OpenText(args[0]);
var atlas = new Atlas(atlasReader, "", new AtlasMetadataOnly());
var data = new SkeletonJson(atlas) { Scale = .15f }.ReadSkeletonData(skeletonReader);
var skeleton = new Skeleton(data);
var animation = data.FindAnimation("animation");
var frames = new List<Layer[]>();
const float playbackSpeed = 1.3f;
const float displayFrameRate = 60;
for (int frame = 0; frame * playbackSpeed / displayFrameRate < animation.Duration; frame++)
{
    skeleton.SetToSetupPose();
    animation.Apply(skeleton, -1, frame * playbackSpeed / displayFrameRate, false, null);
    skeleton.UpdateWorldTransform();
    var layers = new List<Layer>();
    foreach (var slot in skeleton.DrawOrder)
    {
        if (slot.Attachment is not SkinnedMeshAttachment mesh) throw new InvalidDataException("Weighted mesh expected.");
        var xy = new float[mesh.UVs.Length];
        mesh.ComputeWorldVertices(slot, xy);
        layers.Add(new Layer(xy, mesh.UVs, mesh.Triangles, new[] { slot.R * mesh.R, slot.G * mesh.G, slot.B * mesh.B, slot.A * mesh.A }, slot.Data.AdditiveBlending));
    }
    frames.Add(layers.ToArray());
}
using var output = File.Create(args[2]);
JsonSerializer.Serialize(output, frames);

sealed record Layer(float[] Positions, float[] UVs, int[] Triangles, float[] Color, bool Additive);
sealed class AtlasMetadataOnly : TextureLoader
{
    public void Load(AtlasPage page, string path) { }
    public void Unload(object texture) { }
}
