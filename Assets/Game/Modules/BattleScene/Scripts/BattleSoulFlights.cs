using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleSoulFlights : IDisposable
    {
        [Serializable]
        internal sealed class Art
        {
            public BattleHudPreview.SpineEffect drop, arrival;
            public TextAsset dropParticles, arrivalParticles;
        }

        private const float FlightSeconds = .56f;
        private const float SourceScale = 1.5f / 2.24f;
        private readonly RectTransform layer;
        private readonly Canvas canvas;
        private readonly Art art;
        private readonly IReadOnlyDictionary<string, Texture2D> textures;
        private readonly List<Flight> pool = new List<Flight>();

        internal BattleSoulFlights(RectTransform parent, Canvas canvas, Art art, IReadOnlyDictionary<string, Texture2D> textures)
        {
            this.canvas = canvas;
            this.art = art;
            this.textures = textures;
            layer = (RectTransform)new GameObject("Soul Flights", typeof(RectTransform)).transform;
            layer.SetParent(parent, false);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
        }

        internal void Play(Vector2 screenOrigin, Vector3 destination)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screenOrigin, canvas.worldCamera, out var start))
                return;
            var flight = pool.Find(item => !item.Root.gameObject.activeSelf);
            if (flight == null)
            {
                flight = new Flight(layer, art, textures, canvas);
                pool.Add(flight);
            }
            flight.Start(Rect.PointToNormalized(layer.rect, start),
                Rect.PointToNormalized(layer.rect, layer.InverseTransformPoint(destination)));
        }

        internal void Tick(float delta)
        {
            foreach (var flight in pool)
                if (flight.Root.gameObject.activeSelf) flight.Tick(delta, layer.rect);
        }

        internal void Clear()
        {
            foreach (var flight in pool) flight.Stop();
        }

        public void Dispose()
        {
            foreach (var flight in pool) flight.Dispose();
            pool.Clear();
            layer.gameObject.SetActive(false);
            Object.Destroy(layer.gameObject);
        }

        private sealed class Flight : IDisposable
        {
            internal readonly Transform Root;
            private readonly ISkeletonPlayer drop, arrival;
            private readonly BattleHudParticles trail, burst;
            private float elapsed;
            private Vector2 start, end;

            internal Flight(RectTransform layer, Art art, IReadOnlyDictionary<string, Texture2D> textures, Canvas canvas)
            {
                Root = new GameObject("Earned Soul").transform;
                Root.SetParent(layer, false);
                try
                {
                    int order = canvas.sortingOrder + 2;
                    drop = art.drop.Create(Root, "Drop", SourceScale * .6f, order);
                    arrival = art.arrival.Create(Root, "Arrival", SourceScale, order);
                    trail = new BattleHudParticles(art.dropParticles, textures, Root, Vector2.zero, SourceScale, order + 1);
                    burst = new BattleHudParticles(art.arrivalParticles, textures, Root, Vector2.zero, SourceScale, order + 1);
                    foreach (var renderer in Root.GetComponentsInChildren<MeshRenderer>(true))
                        renderer.sortingLayerID = canvas.sortingLayerID;
                    Stop();
                }
                catch { Dispose(); throw; }
            }

            internal void Start(Vector2 origin, Vector2 destination)
            {
                start = origin;
                end = destination;
                elapsed = 0f;
                Root.gameObject.SetActive(true);
                Root.localPosition = Rect.NormalizedToPoint(((RectTransform)Root.parent).rect, start);
                drop.Root.SetActive(true);
                drop.Play("intro", false);
                arrival.Root.SetActive(false);
                trail.Play();
                Tick(0f, ((RectTransform)Root.parent).rect);
            }

            internal void Tick(float delta, Rect bounds)
            {
                float previous = elapsed;
                elapsed += delta;
                float t = Mathf.Clamp01(elapsed / FlightSeconds);
                float travel = t * t * (3f - 2f * t);
                Vector3 previousPosition = Root.position;
                Root.localPosition = Rect.NormalizedToPoint(bounds, Vector2.Lerp(start, end, travel)) +
                    Vector2.up * (Mathf.Sin(t * Mathf.PI) * bounds.height * .045f);
                if (previous < FlightSeconds)
                {
                    float step = Mathf.Min(delta, FlightSeconds - previous);
                    if (drop.CurrentAnimation == "intro")
                    {
                        float intro = Mathf.Min(step, Mathf.Max(0f, drop.Duration - drop.Time));
                        drop.Tick(intro);
                        step -= intro;
                        if (drop.Time >= drop.Duration) drop.Play("loop", true);
                    }
                    drop.Tick(step);
                    trail.TickMoving(Mathf.Min(delta, FlightSeconds - previous), Root.position - previousPosition);
                }
                if (t < 1f) return;
                if (previous < FlightSeconds)
                {
                    drop.Root.SetActive(false);
                    trail.StopEmission();
                    arrival.Root.SetActive(true);
                    arrival.Play("animation", false);
                    burst.Play();
                }
                float afterArrival = elapsed - Mathf.Max(previous, FlightSeconds);
                trail.Tick(afterArrival);
                float spineStep = Mathf.Min(afterArrival, Mathf.Max(0f, arrival.Duration - arrival.Time));
                if (spineStep > 0f) arrival.Tick(spineStep);
                burst.Tick(spineStep);
                if (arrival.Time < arrival.Duration) return;
                arrival.Root.SetActive(false);
                burst.StopEmission();
                burst.Tick(afterArrival - spineStep);
                if (!burst.HasLiveParticles && !trail.HasLiveParticles) Stop();
            }

            internal void Stop()
            {
                trail.Stop();
                burst.Stop();
                Root.gameObject.SetActive(false);
            }

            public void Dispose()
            {
                trail?.Dispose();
                burst?.Dispose();
                drop?.Dispose();
                arrival?.Dispose();
                Root.gameObject.SetActive(false);
                Object.Destroy(Root.gameObject);
            }
        }
    }
}
