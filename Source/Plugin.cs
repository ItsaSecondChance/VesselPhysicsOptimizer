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
        private readonly HashSet<uint> releasedFromGroup = new HashSet<uint>();
        private readonly HashSet<uint> impactsHandledThisStep = new HashSet<uint>();
        private readonly Dictionary<uint, Part> managedParts = new Dictionary<uint, Part>();
        private readonly Dictionary<uint, List<PartCollisionRelay>> collisionRelays =
            new Dictionary<uint, List<PartCollisionRelay>>();
        private readonly Dictionary<uint, Part> highlightedParts = new Dictionary<uint, Part>();
        private readonly Dictionary<uint, int> appliedStates = new Dictionary<uint, int>();

        private bool aggregationEnabled;
        private bool debugColorsEnabled = true;
        private bool diagnosticsVisible;
        private bool eventsSubscribed;
        private float nextScanTime;

        private int lastPartCount;
        private int lastGroupedCount;
        private int lastRestoredCount;
        private int lastComponentCount;
        private int lastCandidateCount;
        private int lastSkippedCount;
        private int collisionPromotions;

        private const int StateGrouped = 1;
        private const int StateRestored = 2;

        private void Start()
        {
            SubscribeEvents();
            Debug.Log("[VPO] Physics aggregation loaded OFF. F8 toggles aggregation, F9 colors, F10 diagnostics.");
            ScanVessels();
            nextScanTime = Time.unscaledTime + ScanInterval;
        }

        private void SubscribeEvents()
        {
            if (eventsSubscribed)
                return;

            GameEvents.onVesselWasModified.Add(OnVesselModified);
            GameEvents.onVesselPartCountChanged.Add(OnVesselModified);
            GameEvents.onVesselGoOffRails.Add(OnVesselModified);
            GameEvents.onVesselLoaded.Add(OnVesselModified);
            GameEvents.onVesselGoOnRails.Add(OnVesselGoingOnRails);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);

            GameEvents.onPartDeCouple.Add(OnPartAboutToSeparate);
            GameEvents.onPartDeCoupleComplete.Add(OnPartSeparationComplete);
            GameEvents.onPartDeCoupleNewVesselComplete.Add(OnVesselsSeparated);
            GameEvents.onPartUndock.Add(OnPartAboutToSeparate);
            GameEvents.onPartUndockComplete.Add(OnPartSeparationComplete);
            GameEvents.onPartDie.Add(OnPartDied);

            eventsSubscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!eventsSubscribed)
                return;

            GameEvents.onVesselWasModified.Remove(OnVesselModified);
            GameEvents.onVesselPartCountChanged.Remove(OnVesselModified);
            GameEvents.onVesselGoOffRails.Remove(OnVesselModified);
            GameEvents.onVesselLoaded.Remove(OnVesselModified);
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoingOnRails);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);

            GameEvents.onPartDeCouple.Remove(OnPartAboutToSeparate);
            GameEvents.onPartDeCoupleComplete.Remove(OnPartSeparationComplete);
            GameEvents.onPartDeCoupleNewVesselComplete.Remove(OnVesselsSeparated);
            GameEvents.onPartUndock.Remove(OnPartAboutToSeparate);
            GameEvents.onPartUndockComplete.Remove(OnPartSeparationComplete);
            GameEvents.onPartDie.Remove(OnPartDied);

            eventsSubscribed = false;
        }

        private void OnVesselModified(Vessel vessel)
        {
            ScheduleScan();
        }

        private void OnPartAboutToSeparate(Part part)
        {
            // Restore the whole vessel before stock decoupling/undocking changes its tree.
            // This avoids relying on whether the event supplies the released or retained side.
            if (part != null && part.vessel != null)
                RestoreManagedPartsForVessel(part.vessel);

            ScheduleScan();
        }

        private void OnPartSeparationComplete(Part part)
        {
            ScheduleScan();
        }

        private void OnVesselsSeparated(Vessel oldVessel, Vessel newVessel)
        {
            ScheduleScan();
        }

        private void OnPartDied(Part part)
        {
            if (part == null)
                return;

            uint id = FindManagedKeyForPart(part);
            RemoveManagedTracking(id);
            previouslyGrouped.Remove(id);
            releasedFromGroup.Remove(id);
            RestoreHighlight(id);
            ScheduleScan();
        }

        private void OnVesselGoingOnRails(Vessel vessel)
        {
            if (vessel == null)
                return;

            RestoreManagedPartsForVessel(vessel);
            ScheduleScan();
        }

        private void OnVesselWillDestroy(Vessel vessel)
        {
            if (vessel == null)
                return;

            RestoreManagedPartsForVessel(vessel);
        }

        private void ScheduleScan()
        {
            nextScanTime = 0f;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                aggregationEnabled = !aggregationEnabled;

                if (!aggregationEnabled)
                {
                    RestoreAllManagedParts();
                    releasedFromGroup.Clear();
                }

                Debug.Log("[VPO] Physics aggregation " +
                    (aggregationEnabled ? "enabled." : "disabled; restoring managed parts."));
                ScanVessels();
            }

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

        private void FixedUpdate()
        {
            impactsHandledThisStep.Clear();
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

        private static uint FindComponentRoot(List<uint> component, Dictionary<uint, Part> parts)
        {
            HashSet<uint> ids = new HashSet<uint>(component);

            foreach (uint id in component)
            {
                Part part;
                if (parts.TryGetValue(id, out part) &&
                    part != null &&
                    (part.parent == null || !ids.Contains(part.parent.flightID)))
                {
                    return id;
                }
            }

            return component.Count > 0 ? component[0] : 0u;
        }

        private static int GetHierarchyDepth(Part part)
        {
            int depth = 0;
            HashSet<uint> visited = new HashSet<uint>();
            Part current = part;

            while (current != null && current.parent != null && visited.Add(current.flightID))
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        private static bool RequiresIndependentBody(Part part)
        {
            if (part == null || part.Modules == null)
                return false;

            // Autostruts and parts with separately moving modules must retain their own body.
            if (part.autoStrutMode != Part.AutoStrutMode.Off || part.servoRb != null)
                return true;

            foreach (PartModule module in part.Modules)
            {
                if (module == null)
                    continue;

                string name = module.GetType().Name.ToLowerInvariant();

                if ((name.Contains("wheel") && !name.Contains("reactionwheel")) ||
                    name.Contains("landinggear") ||
                    name.Contains("servo") ||
                    name.Contains("robot") ||
                    name.Contains("dockingnode") ||
                    name.Contains("grapple") ||
                    name.Contains("ladder") ||
                    name.Contains("deployable") ||
                    name.Contains("controlsurface") ||
                    name.Contains("airbrake") ||
                    name.Contains("gimbal") ||
                    name.Contains("launchclamp"))
                {
                    return true;
                }
            }

            return false;
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

                if (aggregationEnabled)
                    ReconcilePhysicsGroups(parts, components);
                else
                    RestoreAllManagedParts();

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
                Debug.LogError("[VPO] Vessel scan failed: " + ex);
            }
        }

        private void ReconcilePhysicsGroups(
            Dictionary<uint, Part> parts,
            List<List<uint>> components)
        {
            HashSet<uint> desired = new HashSet<uint>();
            int skipped = 0;

            foreach (List<uint> component in components)
            {
                if (component.Count <= 1)
                    continue;

                uint rootId = FindComponentRoot(component, parts);
                Part root;
                if (!parts.TryGetValue(rootId, out root) ||
                    root == null ||
                    root.physicalSignificance != Part.PhysicalSignificance.FULL)
                {
                    skipped += component.Count;
                    continue;
                }

                foreach (uint id in component)
                {
                    if (id == rootId || releasedFromGroup.Contains(id))
                        continue;

                    Part part;
                    if (!parts.TryGetValue(id, out part) || part == null)
                        continue;

                    bool managed = managedParts.ContainsKey(id);
                    if (part.physicalSignificance != Part.PhysicalSignificance.FULL && !managed)
                        continue;

                    if (RequiresIndependentBody(part))
                    {
                        skipped++;
                        continue;
                    }

                    desired.Add(id);
                }
            }

            lastCandidateCount = desired.Count;
            lastSkippedCount = skipped;

            // Restore no-longer-eligible parts parent-first, so every child can find
            // a physical parent while KSP recreates its native joints and rigidbodies.
            List<Part> restore = new List<Part>();
            List<uint> managedIds = new List<uint>(managedParts.Keys);

            foreach (uint id in managedIds)
            {
                Part part;
                if (!parts.TryGetValue(id, out part) || part == null)
                {
                    RemoveManagedTracking(id);
                    continue;
                }

                if (!desired.Contains(id))
                    restore.Add(part);
            }

            restore.Sort(delegate(Part a, Part b)
            {
                return GetHierarchyDepth(a).CompareTo(GetHierarchyDepth(b));
            });

            foreach (Part part in restore)
                PromoteManagedPart(part, false);

            // Demote leaves first. This keeps each parent physical until its children
            // have been converted through KSP's own physicsless-part implementation.
            List<Part> demote = new List<Part>();
            foreach (uint id in desired)
            {
                Part part;
                if (!parts.TryGetValue(id, out part) || part == null)
                    continue;

                if (managedParts.ContainsKey(id))
                {
                    if (part.physicalSignificance == Part.PhysicalSignificance.NONE)
                        EnsureCollisionRelays(part);
                    else
                    {
                        RemoveManagedTracking(id);
                        releasedFromGroup.Add(id);
                    }

                    continue;
                }

                if (part.physicalSignificance == Part.PhysicalSignificance.FULL)
                    demote.Add(part);
            }

            demote.Sort(delegate(Part a, Part b)
            {
                return GetHierarchyDepth(b).CompareTo(GetHierarchyDepth(a));
            });

            foreach (Part part in demote)
            {
                uint id = part.flightID;

                try
                {
                    part.DemoteToPhysicslessPart();

                    if (part.physicalSignificance == Part.PhysicalSignificance.NONE)
                    {
                        managedParts[id] = part;
                        EnsureCollisionRelays(part);
                    }
                    else
                    {
                        Debug.LogWarning("[VPO] KSP did not demote part " + id + " to physicsless state.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[VPO] Could not demote part " + id + ": " + ex.Message);
                }
            }
        }

        private void EnsureCollisionRelays(Part part)
        {
            if (part == null)
                return;

            uint id = part.flightID;
            Collider[] colliders = part.GetPartColliders();
            HashSet<Collider> ownedColliders = new HashSet<Collider>();
            HashSet<GameObject> targets = new HashSet<GameObject>();

            if (colliders != null)
            {
                foreach (Collider collider in colliders)
                {
                    if (collider == null)
                        continue;

                    ownedColliders.Add(collider);
                    targets.Add(collider.gameObject);

                    Rigidbody attachedBody = collider.attachedRigidbody;
                    if (attachedBody != null)
                        targets.Add(attachedBody.gameObject);
                }
            }

            if (ownedColliders.Count == 0)
            {
                Debug.LogWarning("[VPO] No colliders found for physicsless child " + id + "; restoring it.");
                PromoteManagedPart(part, false);
                return;
            }

            List<PartCollisionRelay> relays;
            if (!collisionRelays.TryGetValue(id, out relays))
            {
                relays = new List<PartCollisionRelay>();
                collisionRelays[id] = relays;
            }

            foreach (GameObject target in targets)
            {
                if (target == null)
                    continue;

                PartCollisionRelay relay = null;
                PartCollisionRelay[] existing = target.GetComponents<PartCollisionRelay>();

                foreach (PartCollisionRelay candidate in existing)
                {
                    if (candidate != null && candidate.IsBound && candidate.OwnerFlightId == id)
                    {
                        relay = candidate;
                        break;
                    }
                }

                if (relay == null)
                    relay = target.AddComponent<PartCollisionRelay>();

                relay.Initialize(this, part, ownedColliders);

                if (!relays.Contains(relay))
                    relays.Add(relay);
            }
        }

        internal void RouteManagedPartImpact(Part part, Collision collision)
        {
            if (!aggregationEnabled || part == null || collision == null)
                return;

            uint id = FindManagedKeyForPart(part);
            Part tracked;
            if (!managedParts.TryGetValue(id, out tracked) || tracked == null ||
                !object.ReferenceEquals(tracked, part))
            {
                return;
            }

            if (!impactsHandledThisStep.Add(id))
                return;

            if (part.physicalSignificance != Part.PhysicalSignificance.NONE)
                return;

            try
            {
                part.PromoteToPhysicalPart();

                if (part.physicalSignificance != Part.PhysicalSignificance.FULL)
                    return;

                managedParts.Remove(id);
                releasedFromGroup.Add(part.flightID);
                RemoveCollisionRelays(id);
                collisionPromotions++;

                // Re-enter KSP's collision gate after promotion. It checks collider ownership
                // and vessel collision-ignore state before invoking the native damage handler.
                part.OnCollisionEnter(collision);
                ScheduleScan();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPO] Collision handoff failed for part " + id + ": " + ex);
            }
        }

        private bool PromoteManagedPart(Part part, bool markReleased)
        {
            if (part == null)
                return false;

            uint id = FindManagedKeyForPart(part);
            Part tracked;
            if (!managedParts.TryGetValue(id, out tracked))
                return false;

            if (tracked != null)
                part = tracked;

            try
            {
                if (part.physicalSignificance != Part.PhysicalSignificance.FULL)
                    part.PromoteToPhysicalPart();

                if (part.physicalSignificance != Part.PhysicalSignificance.FULL)
                    return false;

                managedParts.Remove(id);
                RemoveCollisionRelays(id);

                if (markReleased)
                    releasedFromGroup.Add(part.flightID);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VPO] Could not restore part " + id + ": " + ex.Message);
                return false;
            }
        }

        private uint FindManagedKeyForPart(Part part)
        {
            if (part == null)
                return 0u;

            foreach (KeyValuePair<uint, Part> entry in managedParts)
            {
                if (object.ReferenceEquals(entry.Value, part))
                    return entry.Key;
            }

            return part.flightID;
        }

        private void RestoreManagedPartsForVessel(Vessel vessel)
        {
            List<Part> restore = new List<Part>();

            foreach (KeyValuePair<uint, Part> entry in managedParts)
            {
                Part part = entry.Value;
                if (part != null && part.vessel == vessel)
                    restore.Add(part);
            }

            restore.Sort(delegate(Part a, Part b)
            {
                return GetHierarchyDepth(a).CompareTo(GetHierarchyDepth(b));
            });

            foreach (Part part in restore)
                PromoteManagedPart(part, false);
        }

        private void RestoreAllManagedParts()
        {
            List<Part> restore = new List<Part>();

            foreach (KeyValuePair<uint, Part> entry in managedParts)
            {
                if (entry.Value != null)
                    restore.Add(entry.Value);
            }

            restore.Sort(delegate(Part a, Part b)
            {
                return GetHierarchyDepth(a).CompareTo(GetHierarchyDepth(b));
            });

            foreach (Part part in restore)
                PromoteManagedPart(part, false);
        }

        private void RemoveManagedTracking(uint id)
        {
            managedParts.Remove(id);
            RemoveCollisionRelays(id);
        }

        private void RemoveCollisionRelays(uint id)
        {
            List<PartCollisionRelay> relays;
            if (!collisionRelays.TryGetValue(id, out relays))
                return;

            foreach (PartCollisionRelay relay in relays)
            {
                if (relay == null)
                    continue;

                relay.Unbind();
                Destroy(relay);
            }

            collisionRelays.Remove(id);
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

            GUILayout.BeginArea(new Rect(12f, 12f, 390f, 245f), GUI.skin.box);
            GUILayout.Label("Vessel Physics Optimizer 0.2.0");
            GUILayout.Label("Aggregation: " + (aggregationEnabled ? "ON (F8)" : "OFF (F8)"));
            GUILayout.Label("Loaded parts: " + lastPartCount);
            GUILayout.Label("Connected components: " + lastComponentCount);
            GUILayout.Label("Grouped parts: " + lastGroupedCount);
            GUILayout.Label("Aggregation candidates: " + lastCandidateCount);
            GUILayout.Label("Skipped special parts: " + lastSkippedCount);
            GUILayout.Label("VPO physicsless children: " + managedParts.Count);
            GUILayout.Label("Collision-promoted children: " + collisionPromotions);
            GUILayout.Label("Restored singletons: " + lastRestoredCount);
            GUILayout.Label("Debug colors: " + (debugColorsEnabled ? "ON (F9)" : "OFF (F9)"));
            GUILayout.Label("Diagnostics: F10");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            RestoreAllManagedParts();
            RestoreAllHighlights();
            releasedFromGroup.Clear();
            impactsHandledThisStep.Clear();
        }
    }

    [DefaultExecutionOrder(10000)]
    public sealed class PartCollisionRelay : MonoBehaviour
    {
        private Plugin owner;
        private Part ownerPart;
        private uint ownerFlightId = uint.MaxValue;
        private HashSet<Collider> ownedColliders;

        internal bool IsBound
        {
            get { return owner != null && ownerPart != null; }
        }

        internal uint OwnerFlightId
        {
            get { return ownerFlightId; }
        }

        internal void Initialize(Plugin plugin, Part part, HashSet<Collider> colliders)
        {
            owner = plugin;
            ownerPart = part;
            ownerFlightId = part != null ? part.flightID : uint.MaxValue;
            ownedColliders = colliders;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsBound || collision == null || ownedColliders == null)
                return;

            ContactPoint[] contacts = collision.contacts;
            for (int i = 0; i < contacts.Length; i++)
            {
                if (ownedColliders.Contains(contacts[i].thisCollider) ||
                    ownedColliders.Contains(contacts[i].otherCollider))
                {
                    owner.RouteManagedPartImpact(ownerPart, collision);
                    return;
                }
            }
        }

        internal void Unbind()
        {
            owner = null;
            ownerPart = null;
            ownerFlightId = uint.MaxValue;
            ownedColliders = null;
        }
    }
}