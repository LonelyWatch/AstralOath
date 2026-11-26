using System.Text.Json;
using AstralOath.ThirdParty.Spine21;

if (args.Length != 3) throw new ArgumentException("Usage: EquipmentResultEffect skeleton.json atlas output.json");
using var atlasReader = File.OpenText(args[1]);
using var skeletonReader = File.OpenText(args[0]);
var atlas = new Atlas(atlasReader, "", new NoTextures());
var data = new SkeletonJson(atlas).ReadSkeletonData(skeletonReader);
var skeleton = new Skeleton(data);
var animation = data.FindAnimation("animation") ?? throw new InvalidDataException("Missing source animation.");
var frames = new List<Layer[]>();
const int fps = 30;
for (int frame = 0; frame <= MathF.Ceiling(animation.Duration * fps); frame++)
{
    skeleton.SetToSetupPose();
    animation.Apply(skeleton, -1, frame / (float)fps, false, null);
    skeleton.UpdateWorldTransform();
    var layers = new List<Layer>();
    foreach (var slot in skeleton.DrawOrder)
    {
        if (slot.Attachment is null) continue;
        float[] positions, uvs; int[] triangles; float r, g, b, a;
        if (slot.Attachment is MeshAttachment mesh)
        {
            positions = new float[mesh.UVs.Length]; mesh.ComputeWorldVertices(slot, positions);
            uvs = mesh.UVs; triangles = mesh.Triangles; r = mesh.R; g = mesh.G; b = mesh.B; a = mesh.A;
        }
        else if (slot.Attachment is SkinnedMeshAttachment weighted)
        {
            positions = new float[weighted.UVs.Length]; weighted.ComputeWorldVertices(slot, positions);
            uvs = weighted.UVs; triangles = weighted.Triangles; r = weighted.R; g = weighted.G; b = weighted.B; a = weighted.A;
        }
        else if (slot.Attachment is RegionAttachment region)
        {
            positions = new float[8]; region.ComputeWorldVertices(slot.Bone, positions);
            uvs = region.UVs; triangles = new[] { 0, 1, 2, 2, 3, 0 }; r = region.R; g = region.G; b = region.B; a = region.A;
        }
        else throw new InvalidDataException("Unsupported source attachment: " + slot.Attachment.GetType());
        layers.Add(new Layer(positions, uvs, triangles,
            new[] { slot.R * r, slot.G * g, slot.B * b, slot.A * a }, slot.Data.AdditiveBlending));
    }
    frames.Add(layers.ToArray());
}
using var output = File.Create(args[2]);
JsonSerializer.Serialize(output, new { fps, duration = animation.Duration, frames });

sealed record Layer(float[] Positions, float[] UVs, int[] Triangles, float[] Color, bool Additive);
sealed class NoTextures : TextureLoader
{
    public void Load(AtlasPage page, string path) { }
    public void Unload(object texture) { }
}
