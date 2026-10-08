using System.Collections.Generic;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Records frame times for a while and reports the hitches, with how much of each frame this plugin used.
    /// Started by the "frames" DevBridge command; it removes itself when done.
    /// </summary>
    internal class FrameProbe : MonoBehaviour
    {
        /// <summary>Milliseconds this plugin spent in the current frame (scans, loads). Reset every frame.</summary>
        internal static float PluginMsThisFrame;

        private struct Frame
        {
            public float Time, Ms, PluginMs;
            public bool Gc;   // a garbage collection ran since the previous frame
        }

        private int lastGc = System.GC.CollectionCount(0);
        private long lastHeap = System.GC.GetTotalMemory(false), allocated;

        private readonly List<Frame> frames = new List<Frame>();
        private float until;

        internal static void Run(float seconds)
        {
            var go = new GameObject("GSOHDTextures.FrameProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameProbe>().until = Time.unscaledTime + seconds;
        }

        // Runs late so the plugin's own Update for this frame is already counted.
        private void LateUpdate()
        {
            int gc = System.GC.CollectionCount(0);
            long heap = System.GC.GetTotalMemory(false);
            if (gc == lastGc && heap > lastHeap) allocated += heap - lastHeap;   // growth between collections
            lastHeap = heap;
            frames.Add(new Frame { Time = Time.unscaledTime, Ms = Time.unscaledDeltaTime * 1000f, PluginMs = PluginMsThisFrame, Gc = gc != lastGc });
            lastGc = gc;
            PluginMsThisFrame = 0f;
            if (Time.unscaledTime < until) return;
            Report();
            Destroy(gameObject);
        }

        private void Report()
        {
            if (frames.Count < 2) return;
            frames.RemoveAt(0);   // the first delta includes the bridge command itself
            var ms = new List<float>();
            float total = 0f, plugin = 0f;
            foreach (var f in frames) { ms.Add(f.Ms); total += f.Ms; plugin += f.PluginMs; }
            ms.Sort();
            float P(float q) => ms[Mathf.Min(ms.Count - 1, (int)(q * ms.Count))];
            int over25 = 0, over50 = 0, gcs = 0;
            foreach (var f in frames) if (f.Gc) gcs++;
            foreach (var m in ms) { if (m > 25f) over25++; if (m > 50f) over50++; }
            Plugin.Log.LogInfo($"[dev] frames: {frames.Count} in {total / 1000f:F1} s, avg {total / frames.Count:F1} ms ({frames.Count * 1000f / total:F0} fps), " +
                $"p50 {P(0.5f):F1}, p95 {P(0.95f):F1}, p99 {P(0.99f):F1}, max {ms[ms.Count - 1]:F1} ms; >25 ms: {over25}, >50 ms: {over50}; plugin total {plugin:F0} ms; garbage collections {gcs}, allocating about {allocated / 1048576f / (total / 1000f):F1} MB/s");

            var worst = new List<Frame>(frames);
            worst.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            for (int i = 0; i < worst.Count && i < 8; i++)
                Plugin.Log.LogInfo($"[dev]   spike at {worst[i].Time:F2}s: {worst[i].Ms:F1} ms, plugin {worst[i].PluginMs:F1} ms{(worst[i].Gc ? ", GC" : "")}");
        }
    }
}
