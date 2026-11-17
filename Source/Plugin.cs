using System;
using System.Collections.Generic;
using UnityEngine;

namespace VesselPhysicsOptimizer
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class Plugin : MonoBehaviour
    {
        private const float ScanInterval = 0.5f;
        private static readonly Color GroupedColor = new Color(0.68f, 0.28f, 0.92f, 1f);
        private static readonly Color RestoredColor = new Color(0.25f, 0.88f, 0.38f, 1f);

        private readonly HashSet<uint> previouslyGrouped = new HashSet<uint>();
        private readonly Dictionary<uint, Part> highlightedParts = new Dictionary<uint, Part>();
        private readonly Dictionary<uint, int> appliedStates = new Dictionary<uint, int>();

        private bool debugColorsEnabled = true;
        private bool diagnosticsVisible;
        private float nextScanTime;
        private int lastPartCount;
        private int lastGroupedCount;
        private int lastRestoredCount;
        private int lastComponentCount;

        private const int StateGrouped = 1;
        private const int StateRestored = 2;

        private void Start()
        {
            Debug.Log("[VPO] Visualization milestone loaded. Physics aggregation is not active.");
            ScanVessels();
            nextScanTime = Time.unscaledTime + ScanInterval;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                debugColorsEnabled = !debugColorsEnabled;
                if (!debugColorsEnabled)
                    RestoreAllHighlights();

                Debug.Log("[VPO] Debug colors " + (debugColorsEnabled ? "enabled." : "disabled."));
                ScanVessels();
            }

            if (Input.GetKeyDown(KeyCode.F10))
                diagnosticsVisible = !diagnosticsVisible;

            if (Time.unscaledTime >= nextScanTime)
            {
                nextScanTime = Time.unscaledTime + ScanInterval;
                ScanVessels();
            }
        }

        private static Dictionary<uint, Part> CollectLoadedParts()
        {
            Dictionary<uint, Part> result = new Dictionary<uint, Part>();

            if (FlightGlobals.Vessels == null)
                return result;

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || !vessel.loaded || vessel.parts == null)
                    continue;

                foreach (Part part in vessel.parts)
                {
                    if (part != null)
                        result[part.flightID] = part;
                }
            }

            return result;
        }

        private static List<List<uint>> FindComponents(Dictionary<uint, Part> parts)
        {
            List<List<uint>> components = new List<List<uint>>();
            HashSet<uint> visited = new HashSet<uint>();
            Queue<uint> queue = new Queue<uint>();

            foreach (KeyValuePair<uint, Part> entry in parts)
            {
                if (!visited.Add(entry.Key))
                    continue;

                List<uint> component = new List<uint>();
                queue.Enqueue(entry.Key);

                while (queue.Count > 0)
                {
                    uint currentId = queue.Dequeue();
                    component.Add(currentId);

                    Part current;
                    if (!parts.TryGetValue(currentId, out current) || current == null)
                        continue;

                    if (current.parent != null &&
                        parts.ContainsKey(current.parent.flightID) &&
                        visited.Add(current.parent.flightID))
                    {
                        queue.Enqueue(current.parent.flightID);
                    }

                    if (current.children == null)
                        continue;

                    foreach (Part child in current.children)
                    {
                        if (child != null &&
                            parts.ContainsKey(child.flightID) &&
                            visited.Add(child.flightID))
                        {
                            queue.Enqueue(child.flightID);
                        }
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private void ScanVessels()
        {
            try
            {
                Dictionary<uint, Part> parts = CollectLoadedParts();
                List<List<uint>> components = FindComponents(parts);
                HashSet<uint> groupedNow = new HashSet<uint>();

                lastPartCount = parts.Count;
                lastComponentCount = components.Count;
                lastGroupedCount = 0;
                lastRestoredCount = 0;

                foreach (List<uint> component in components)
                {
                    if (component.Count <= 1)
                        continue;

                    foreach (uint id in component)
                    {
                        groupedNow.Add(id);
                        previouslyGrouped.Add(id);
                    }
                }

                lastGroupedCount = groupedNow.Count;

                if (!debugColorsEnabled)
                {
                    RestoreAllHighlights();
                    return;
                }

                List<uint> stale = new List<uint>();
                foreach (uint id in highlightedParts.Keys)
                {
                    if (!parts.ContainsKey(id))
                        stale.Add(id);
                }

                foreach (uint id in stale)
                    RestoreHighlight(id);

                foreach (KeyValuePair<uint, Part> entry in parts)
                {
                    uint id = entry.Key;

                    if (groupedNow.Contains(id))
                    {
                        ApplyHighlight(id, entry.Value, StateGrouped, GroupedColor);
                    }
                    else if (previouslyGrouped.Contains(id))
                    {
                        lastRestoredCount++;
                        ApplyHighlight(id, entry.Value, StateRestored, RestoredColor);
                    }
                    else if (highlightedParts.ContainsKey(id))
                    {
                        RestoreHighlight(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPO] Connection scan failed: " + ex);
            }
        }

        private void ApplyHighlight(uint id, Part part, int state, Color color)
        {
            int oldState;
            if (appliedStates.TryGetValue(id, out oldState) &&
                oldState == state &&
                highlightedParts.ContainsKey(id))
            {
                return;
            }

            try
            {
                part.SetHighlightType(Part.HighlightType.AlwaysOn);
                part.SetHighlightColor(color);
                part.SetHighlight(true, false);
                highlightedParts[id] = part;
                appliedStates[id] = state;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VPO] Could not highlight part " + id + ": " + ex.Message);
            }
        }

        private void RestoreHighlight(uint id)
        {
            Part part;
            if (highlightedParts.TryGetValue(id, out part) && part != null)
            {
                try
                {
                    part.SetHighlightDefault();
                    part.SetHighlight(false, false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[VPO] Could not restore part " + id + ": " + ex.Message);
                }
            }

            highlightedParts.Remove(id);
            appliedStates.Remove(id);
        }

        private void RestoreAllHighlights()
        {
            List<uint> ids = new List<uint>(highlightedParts.Keys);
            foreach (uint id in ids)
                RestoreHighlight(id);
        }

        private void OnGUI()
        {
            if (!diagnosticsVisible)
                return;

            GUILayout.BeginArea(new Rect(12f, 12f, 330f, 155f), GUI.skin.box);
            GUILayout.Label("Vessel Physics Optimizer 0.1.0");
            GUILayout.Label("Milestone 1: visualization only");
            GUILayout.Label("Loaded parts: " + lastPartCount);
            GUILayout.Label("Connected components: " + lastComponentCount);
            GUILayout.Label("Grouped parts: " + lastGroupedCount);
            GUILayout.Label("Restored singletons: " + lastRestoredCount);
            GUILayout.Label("Debug colors: " + (debugColorsEnabled ? "ON (F9)" : "OFF (F9)"));
            GUILayout.Label("Diagnostics: F10");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            RestoreAllHighlights();
        }
    }
}
