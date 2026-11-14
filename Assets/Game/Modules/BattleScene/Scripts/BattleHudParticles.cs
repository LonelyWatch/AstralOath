using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    // The HUD owns the clock, the anchor and the borrowed textures.
    internal sealed class BattleHudParticles : IDisposable
    {
#pragma warning disable 0649 // Fields are populated by JsonUtility from the source particle document.
        [Serializable]
        private sealed class Definition
        {
            public EmitterData[] emitters;
        }

        [Serializable]
        private sealed class EmitterData
        {
            public string name, textureFileName;
            public string[] colorBlends;
            public bool disable;
            public int emitterType, burningType, repeat, blendFuncSource, blendFuncDestination;
            public float maxParticles, startDelay, startDelayVar, interval, duration, emissionRate, scale;
            public float angle, angleVariance, speed, speedVariance, gravityx, gravityy;
            public float sourcePositionx, sourcePositiony, sourcePositionVariancex, sourcePositionVariancey;
            public float radialAcceleration, radialAccelVariance, tangentialAcceleration, tangentialAccelVariance;
            public float maxRadius, maxRadiusVariance, minRadius, minRadiusVariance, rotatePerSecond, rotatePerSecondVariance;
            public float particleLifespan, particleLifespanVariance, rotationStart, rotationStartVariance, rotationDir, rotationDirVariance;
            public float startParticleSize, startParticleSizeVariance, finishParticleSize, finishParticleSizeVariance;
            public float startColorRed, startColorGreen, startColorBlue, startColorAlpha;
            public float startColorVarianceRed, startColorVarianceGreen, startColorVarianceBlue, startColorVarianceAlpha;
            public float finishColorRed, finishColorGreen, finishColorBlue, finishColorAlpha;
            public float finishColorVarianceRed, finishColorVarianceGreen, finishColorVarianceBlue, finishColorVarianceAlpha;
        }
#pragma warning restore 0649

        private struct Particle
        {
            public Vector2 Origin, Position, Velocity;
            public int ColorRamp;
            public Color StartColor, EndColor;
            public float Age, Life, StartSize, EndSize, Rotation, Spin;
            public float RadialAcceleration, TangentialAcceleration, Angle, AngularSpeed, StartRadius, EndRadius;
        }

        private sealed class Emitter : IDisposable
        {
            private static readonly Vector2[] Corners =
            {
                new Vector2(-1, -1), new Vector2(1, -1), Vector2.one, new Vector2(-1, 1)
            };

            private readonly EmitterData data;
            private readonly GameObject root;
            private readonly Mesh mesh;
            private readonly Material material;
            private readonly (float Time, Color Color)[][] ramps;
            private readonly List<Particle> particles;
            private readonly List<Vector3> vertices;
            private readonly List<Vector2> uvs;
            private readonly List<Vector2> darkTint;
            private readonly List<Color> colors;
            private readonly List<int> triangles;
            private readonly int capacity;
            private readonly int repetitions;
            private double delay;
            private int nextBurst;

            public Emitter(EmitterData data, Texture2D texture, Shader shader, Transform parent,
                Vector2 offset, float scale, int sortingOrder, bool premultiplied)
            {
                this.data = data;
                if (data.emitterType < 0 || data.emitterType > 1 || data.burningType < 0 || data.burningType > 1 ||
                    !float.IsFinite(data.maxParticles) || data.maxParticles < 1 || data.maxParticles > 4096 ||
                    data.maxParticles != (int)data.maxParticles || data.repeat < 0 || data.repeat > 32 ||
                    !float.IsFinite(data.emissionRate) || data.emissionRate < 0 ||
                    !float.IsFinite(data.scale) || data.scale <= 0 ||
                    !float.IsFinite(data.duration) || !float.IsFinite(data.interval) || data.interval < 0 ||
                    !float.IsFinite(data.startDelay) || !float.IsFinite(data.startDelayVar))
                    throw new FormatException($"Unsupported HUD particle emitter '{data.name}'.");

                // The skeleton shader premultiplies straight texture samples. With the original
                // SRC_ALPHA/ONE additive blend the equivalent factors are therefore ONE/ONE.
                if (!((data.blendFuncSource == 770 && data.blendFuncDestination == 1) ||
                    (data.blendFuncSource == 1 && data.blendFuncDestination == 771)))
                    throw new FormatException($"Unsupported HUD particle blend in '{data.name}'.");
                ramps = ReadRamps(data.colorBlends);
                capacity = (int)data.maxParticles;
                repetitions = data.interval > 0 ? Math.Max(1, data.repeat) : 1;
                particles = new List<Particle>(capacity);
                vertices = new List<Vector3>(capacity * 4);
                uvs = new List<Vector2>(capacity * 4);
                darkTint = new List<Vector2>(capacity * 4);
                colors = new List<Color>(capacity * 4);
                triangles = new List<int>(capacity * 6);

                root = new GameObject("HUD particle " + data.name, typeof(MeshFilter), typeof(MeshRenderer));
                root.SetActive(false);
                root.layer = parent.gameObject.layer;
                root.transform.SetParent(parent, false);
                root.transform.localPosition = offset * scale;
                root.transform.localScale = Vector3.one * (data.scale * scale);
                mesh = new Mesh { name = "HUD particle mesh" };
                mesh.MarkDynamic();
                root.GetComponent<MeshFilter>().sharedMesh = mesh;
                material = new Material(shader) { name = "Source particle material", mainTexture = texture };
                material.SetFloat("_PremultipliedAlpha", premultiplied ? 1f : 0f);
                material.SetFloat("_PmaSrgb", premultiplied && texture.isDataSRGB &&
                    QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)(data.blendFuncDestination == 771 ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
                var renderer = root.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = sortingOrder;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            public void Play(System.Random random)
            {
                particles.Clear();
                mesh.Clear();
                delay = Math.Max(0, Sample(random, data.startDelay, data.startDelayVar));
                nextBurst = 0;
                root.SetActive(true);
            }

            public void Stop()
            {
                if (root)
                    root.SetActive(false);
                particles.Clear();
                mesh.Clear();
            }

            public void Tick(double previous, double time, System.Random random)
            {
                float delta = (float)(time - previous);
                for (int index = particles.Count - 1; index >= 0; index--)
                {
                    Particle particle = particles[index];
                    if (Advance(ref particle, delta))
                        particles[index] = particle;
                    else
                        particles.RemoveAt(index);
                }

                if (data.burningType == 1)
                {
                    while (nextBurst < repetitions && delay + nextBurst * data.interval <= time)
                    {
                        float age = (float)(time - delay - nextBurst * data.interval);
                        for (int count = 0; count < capacity && particles.Count < capacity; count++)
                            Spawn(random, age);
                        nextBurst++;
                    }
                }
                else if (data.emissionRate > 0 && particles.Count < capacity)
                {
                    // Count crossed emission times so a long frame cannot extend a finite emitter.
                    for (int repetition = 0; repetition < repetitions && particles.Count < capacity; repetition++)
                    {
                        double start = delay + repetition * data.interval;
                        double end = data.duration < 0 ? time : Math.Min(time, start + data.duration);
                        double first = Math.Max(1, Math.Floor((previous - start) * data.emissionRate) + 1);
                        double last = Math.Floor((end - start) * data.emissionRate);
                        double maxLife = Math.Max(0.01, data.particleLifespan + Math.Abs(data.particleLifespanVariance));
                        first = Math.Max(first, Math.Ceiling((time - maxLife - start) * data.emissionRate));
                        for (double emission = first; emission <= last && particles.Count < capacity; emission++)
                            Spawn(random, (float)(time - start - emission / data.emissionRate));
                    }
                }

                Draw();
            }

            private void Spawn(System.Random random, float age)
            {
                float startSize = Math.Max(0, Sample(random, data.startParticleSize, data.startParticleSizeVariance));
                float startRadius = Sample(random, data.maxRadius, data.maxRadiusVariance);
                float angle = Sample(random, data.angle, data.angleVariance) * Mathf.Deg2Rad;
                Vector2 origin = new Vector2(
                    Sample(random, data.sourcePositionx, data.sourcePositionVariancex),
                    Sample(random, data.sourcePositiony, data.sourcePositionVariancey));
                var particle = new Particle
                {
                    Origin = origin,
                    Position = origin,
                    Velocity = Direction(angle) * Sample(random, data.speed, data.speedVariance),
                    RadialAcceleration = Sample(random, data.radialAcceleration, data.radialAccelVariance),
                    TangentialAcceleration = Sample(random, data.tangentialAcceleration, data.tangentialAccelVariance),
                    Angle = angle,
                    AngularSpeed = Sample(random, data.rotatePerSecond, data.rotatePerSecondVariance) * Mathf.Deg2Rad,
                    StartRadius = startRadius,
                    EndRadius = data.minRadius == -1 ? startRadius : Sample(random, data.minRadius, data.minRadiusVariance),
                    Life = Math.Max(0.01f, Sample(random, data.particleLifespan, data.particleLifespanVariance)),
                    StartSize = startSize,
                    EndSize = data.finishParticleSize == -1 ? startSize :
                        Math.Max(0, Sample(random, data.finishParticleSize, data.finishParticleSizeVariance)),
                    Rotation = Sample(random, data.rotationStart, data.rotationStartVariance) * -Mathf.Deg2Rad,
                    Spin = Sample(random, data.rotationDir, data.rotationDirVariance) * -Mathf.Deg2Rad,
                    StartColor = RandomColor(random, new Color(data.startColorRed, data.startColorGreen, data.startColorBlue, data.startColorAlpha),
                        new Color(data.startColorVarianceRed, data.startColorVarianceGreen, data.startColorVarianceBlue, data.startColorVarianceAlpha)),
                    EndColor = RandomColor(random, new Color(data.finishColorRed, data.finishColorGreen, data.finishColorBlue, data.finishColorAlpha),
                        new Color(data.finishColorVarianceRed, data.finishColorVarianceGreen, data.finishColorVarianceBlue, data.finishColorVarianceAlpha))
                };
                // Each source string is a complete 0..1 curve, not a continuation of the preceding curve.
                // Choose once per particle. Single-ramp assets consume no additional random samples.
                particle.ColorRamp = ramps.Length > 1 ? random.Next(ramps.Length) : 0;
                if (Advance(ref particle, Math.Max(0, age)))
                    particles.Add(particle);
            }

            private bool Advance(ref Particle particle, float delta)
            {
                particle.Age += delta;
                if (particle.Age >= particle.Life)
                    return false;
                if (data.emitterType == 1)
                {
                    float angle = particle.Angle + particle.AngularSpeed * particle.Age;
                    float radius = Mathf.Lerp(particle.StartRadius, particle.EndRadius, particle.Age / particle.Life);
                    particle.Position = particle.Origin - Direction(angle) * radius;
                }
                else
                {
                    Vector2 radial = particle.Position.normalized;
                    Vector2 acceleration = new Vector2(data.gravityx, data.gravityy) +
                        radial * particle.RadialAcceleration +
                        new Vector2(-radial.y, radial.x) * particle.TangentialAcceleration;
                    particle.Position += (particle.Velocity + 0.5f * acceleration * delta) * delta;
                    particle.Velocity += acceleration * delta;
                }
                return true;
            }

            private void Draw()
            {
                if (particles.Count == 0 && mesh.vertexCount == 0)
                    return;
                vertices.Clear();
                uvs.Clear();
                darkTint.Clear();
                colors.Clear();
                triangles.Clear();
                foreach (Particle particle in particles)
                {
                    float t = particle.Age / particle.Life;
                    float halfSize = Mathf.Lerp(particle.StartSize, particle.EndSize, t) * 0.5f;
                    Vector2 axis = Direction(particle.Rotation + particle.Spin * particle.Age) * halfSize;
                    Vector2 perpendicular = new Vector2(-axis.y, axis.x);
                    Color color = ramps.Length == 0 ? Color.Lerp(particle.StartColor, particle.EndColor, t)
                        : RampColor(ramps[particle.ColorRamp], t);
                    int index = vertices.Count;
                    foreach (Vector2 corner in Corners)
                    {
                        vertices.Add(particle.Position + axis * corner.x + perpendicular * corner.y);
                        colors.Add(color);
                        uvs.Add((corner + Vector2.one) * 0.5f);
                        darkTint.Add(Vector2.zero);
                    }
                    triangles.Add(index);
                    triangles.Add(index + 2);
                    triangles.Add(index + 1);
                    triangles.Add(index);
                    triangles.Add(index + 3);
                    triangles.Add(index + 2);
                }
                mesh.Clear();
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uvs);
                mesh.SetUVs(1, darkTint);
                mesh.SetTriangles(triangles, 0);
            }

            private static Color RampColor((float Time, Color Color)[] ramp, float time)
            {
                if (time <= ramp[0].Time)
                    return ramp[0].Color;
                for (int index = 1; index < ramp.Length; index++)
                {
                    if (time <= ramp[index].Time)
                        return Color.Lerp(ramp[index - 1].Color, ramp[index].Color,
                            Mathf.InverseLerp(ramp[index - 1].Time, ramp[index].Time, time));
                }
                return ramp[ramp.Length - 1].Color;
            }

            public void Dispose()
            {
                if (root)
                {
                    root.SetActive(false);
                    Destroy(root);
                }
                Destroy(mesh);
                Destroy(material);
            }
        }

        private readonly List<Emitter> emitters = new List<Emitter>();
        private System.Random random;
        private double time;
        private bool playing, disposed;

        public BattleHudParticles(TextAsset definition, IReadOnlyDictionary<string, Texture2D> textures, Transform parent,
            Vector2 offset, float scale, int sortingOrder) : this(definition, textures, parent, offset, scale, sortingOrder, false) { }

        public BattleHudParticles(TextAsset definition, IReadOnlyDictionary<string, Texture2D> textures, Transform parent,
            Vector2 offset, float scale, int sortingOrder, bool premultiplied)
        {
            if (!definition)
                throw new ArgumentNullException(nameof(definition));
            if (textures == null)
                throw new ArgumentNullException(nameof(textures));
            if (!parent)
                throw new ArgumentNullException(nameof(parent));
            if (!float.IsFinite(scale) || scale <= 0)
                throw new ArgumentOutOfRangeException(nameof(scale));

            var document = JsonUtility.FromJson<Definition>(definition.text);
            if (document?.emitters == null)
                throw new FormatException($"Missing HUD particle emitters in '{definition.name}'.");
            var shader = Resources.Load<Shader>("AstralOathSkeleton");
            if (!shader)
                throw new InvalidOperationException("Missing HUD particle shader AstralOathSkeleton.");
            try
            {
                foreach (var emitter in document.emitters)
                {
                    if (emitter == null)
                        throw new FormatException($"Invalid HUD particle emitter in '{definition.name}'.");
                    if (emitter.disable)
                        continue;
                    if (string.IsNullOrWhiteSpace(emitter.textureFileName) ||
                        (!textures.TryGetValue(emitter.textureFileName, out var texture) &&
                         !textures.TryGetValue(Path.GetFileName(emitter.textureFileName), out texture) &&
                         !textures.TryGetValue(Path.GetFileNameWithoutExtension(emitter.textureFileName), out texture)) || !texture)
                        throw new FormatException($"Missing HUD particle texture '{emitter.textureFileName}' in '{definition.name}'.");
                    emitters.Add(new Emitter(emitter, texture, shader, parent, offset, scale, sortingOrder, premultiplied));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Play()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(BattleHudParticles));
            time = 0;
            random = new System.Random(1);
            playing = true;
            foreach (var emitter in emitters)
                emitter.Play(random);
            Tick(0);
        }

        public void Tick(float delta)
        {
            if (!playing)
                return;
            if (!float.IsFinite(delta) || delta < 0)
                throw new ArgumentOutOfRangeException(nameof(delta));
            double previous = time;
            time += delta;
            foreach (var emitter in emitters)
                emitter.Tick(previous, time, random);
        }

        public void Stop()
        {
            playing = false;
            foreach (var emitter in emitters)
                emitter.Stop();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            playing = false;
            foreach (var emitter in emitters)
                emitter.Dispose();
            emitters.Clear();
        }

        private static (float Time, Color Color)[][] ReadRamps(string[] groups)
        {
            if (groups == null || groups.Length == 0)
                return Array.Empty<(float Time, Color Color)[]>();
            var result = new (float Time, Color Color)[groups.Length][];
            for (int index = 0; index < groups.Length; index++)
                result[index] = ReadRamp(new[] { groups[index] });
            return result;
        }
        private static (float Time, Color Color)[] ReadRamp(string[] groups)
        {
            if (groups == null || groups.Length == 0)
                return Array.Empty<(float, Color)>();
            var keys = new List<(float, Color)>();
            float previous = -1;
            foreach (string group in groups)
            {
                foreach (string row in group.Split(';'))
                {
                    string[] columns = row.Split(',');
                    if (columns.Length != 5)
                        throw new FormatException($"Invalid HUD particle color key '{row}'.");
                    float time = ReadFloat(columns[0]);
                    if (time < 0 || time > 1 || time <= previous)
                        throw new FormatException($"Unordered HUD particle color key '{row}'.");
                    // Source particle colorBlends uses time, alpha, red, green, blue.
                    // Other source color fields may use RGBA; do not generalize this order beyond this schema.
                    keys.Add((time, new Color(ReadFloat(columns[2]), ReadFloat(columns[3]), ReadFloat(columns[4]), ReadFloat(columns[1]))));
                    previous = time;
                }
            }
            return keys.ToArray();
        }

        private static float ReadFloat(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) || !float.IsFinite(number))
                throw new FormatException($"Invalid HUD particle value '{value}'.");
            return number;
        }

        private static Vector2 Direction(float radians) => new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

        private static float Sample(System.Random random, float value, float variance) =>
            value + ((float)random.NextDouble() * 2 - 1) * variance;

        private static Color RandomColor(System.Random random, Color color, Color variance) =>
            new Color(Mathf.Clamp01(Sample(random, color.r, variance.r)), Mathf.Clamp01(Sample(random, color.g, variance.g)),
                Mathf.Clamp01(Sample(random, color.b, variance.b)), Mathf.Clamp01(Sample(random, color.a, variance.a)));

        private static void Destroy(Object resource)
        {
            if (Application.isPlaying)
                Object.Destroy(resource);
            else
                Object.DestroyImmediate(resource);
        }
    }
}
