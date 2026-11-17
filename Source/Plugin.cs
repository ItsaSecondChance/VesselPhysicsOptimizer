using System;
using System.Collections.Generic;
using UnityEngine;

namespace VesselPhysicsOptimizer
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Plugin : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;
        private const string ColorProperty = "_Color";

        private readonly HashSet<uint> previouslyGrouped = new HashSet<uint>();
        private readonly Dictionary<uint, Part> knownParts = new Dictionary<uint, Part>();
        private readonly Dictionary<Renderer, MaterialPropertyBlock> originalBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

        private float nextScan;
        private bool debugColors = true;
        private bool windowVisible = true;
        private Rect windowRect = new Rect(20f, 80f, 310f, 150f);
        private int groupedPartCount;
        private int restoredPartCount;
        private int scannedVesselCount;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                debugColors = !debugColors;
                if (!debugColors) RestoreAllRenderers();
                else ScanAndColor();
            }

            if (Input.GetKeyDown(KeyCode.F10))
                windowVisible = !windowVisible;

            if (debugColors && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                ScanAndColor();
            }
        }

        private void OnDestroy()
        {
            RestoreAllRenderers();
        }

        private void ScanAndColor()
        {
            var currentGroups = new HashSet<uint>();
            var currentParts = new Dictionary<uint, Part>();
            groupedPartCount = 0;
            restoredPartCount = 0;
            scannedVesselCount = 0;

            if (FlightGlobals.Vessels != null)
            {
                foreach (Vessel vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || !vessel.loaded || vessel.parts == null)
                        continue;

                    scannedVesselCount++;
                    var vesselParts = new List<Part>();
                    var vesselSet = new HashSet<Part>();

                    foreach (Part part in vessel.parts)
                    {
                        if (part == null) continue;
                        vesselParts.Add(part);
                        vesselSet.Add(part);
                        currentParts[part.flightID] = part;
                        knownParts[part.flightID] = part;
                    }

                    var visited = new HashSet<Part>();
                    foreach (Part start in vesselParts)
                    {
                        if (visited.Contains(start)) continue;

                        var component = CollectConnectedComponent(start, vesselSet, visited);
                        if (component.Count > 1)
                        {
                            foreach (Part part in component)
                            {
                                currentGroups.Add(part.flightID);
                                previouslyGrouped.Add(part.flightID);
                                groupedPartCount++;
                            }
                        }
                    }
                }
            }

            // Keep references to former group members so detached parts can still be shown green
            // even after they move to another loaded vessel.
            foreach (KeyValuePair<uint, Part> entry in knownParts)
            {
                Part part = entry.Value;
                if (part == null || part.gameObject == null) continue;

                if (currentGroups.Contains(entry.Key))
                {
                    ApplyColor(part, new Color(0.62f, 0.20f, 0.92f, 1f));
                }
                else if (previouslyGrouped.Contains(entry.Key))
                {
                    ApplyColor(part, new Color(0.20f, 0.90f, 0.30f, 1f));
                    restoredPartCount++;
                }
                else
                {
                    RestorePartRenderers(part);
                }
            }
        }

        private static List<Part> CollectConnectedComponent(
            Part start, HashSet<Part> vesselParts, HashSet<Part> visited)
        {
            var result = new List<Part>();
            var queue = new Queue<Part>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                Part part = queue.Dequeue();
                result.Add(part);

                if (part.parent != null && vesselParts.Contains(part.parent) && visited.Add(part.parent))
                    queue.Enqueue(part.parent);

                if (part.children != null)
                {
                    foreach (Part child in part.children)
                    {
                        if (child != null && vesselParts.Contains(child) && visited.Add(child))
                            queue.Enqueue(child);
                    }
                }
            }
            return result;
        }

        private void ApplyColor(Part part, Color color)
        {
            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                MaterialPropertyBlock block;
                if (!originalBlocks.TryGetValue(renderer, out block))
                {
                    block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    var saved = new MaterialPropertyBlock();
                    saved.SetColor("_VPO_DUMMY_", Color.clear);
                    // Preserve the original property block by copying it through a renderer read
                    // into a fresh block before the first VPO override.
                    saved = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(saved);
                    originalBlocks[renderer] = saved;
                }

                var updated = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(updated);
                updated.SetColor(ColorProperty, color);
                updated.SetColor("_MainColor", color);
                renderer.SetPropertyBlock(updated);
            }
        }

        private void RestorePartRenderers(Part part)
        {
            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                MaterialPropertyBlock original;
                if (originalBlocks.TryGetValue(renderer, out original))
                {
                    renderer.SetPropertyBlock(original);
                    originalBlocks.Remove(renderer);
                }
            }
        }

        private void RestoreAllRenderers()
        {
            foreach (KeyValuePair<Renderer, MaterialPropertyBlock> entry in originalBlocks)
            {
                if (entry.Key != null)
                    entry.Key.SetPropertyBlock(entry.Value);
            }
            originalBlocks.Clear();
        }

        private void OnGUI()
        {
            if (!windowVisible) return;
            windowRect = GUILayout.Window(
                731204, windowRect, DrawWindow, "Vessel Physics Optimizer — Debug");
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label("Build 0.1.0 — visualization prototype");
            GUILayout.Label("Loaded vessels scanned: " + scannedVesselCount);
            GUILayout.Label("Grouped parts (purple): " + groupedPartCount);
            GUILayout.Label("Restored parts (green): " + restoredPartCount);
            GUILayout.Label("Debug colors: " + (debugColors ? "ON (F9)" : "OFF (F9)"));
            GUILayout.Label("Toggle this window: F10");
            GUI.DragWindow();
        }
    }
}
