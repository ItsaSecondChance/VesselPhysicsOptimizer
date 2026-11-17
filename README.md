# Vessel Physics Optimizer (VPO)

Target game: Kerbal Space Program **1.12.5.3190**.

## Current milestone: connection-state visualization

This first build is deliberately a diagnostic prototype. It does **not** merge Unity rigidbodies or change the physics simulation yet.

- Purple: parts in a connected component containing more than one part.
- Green: parts that were previously in a multi-part connected component but are now separate.
- Unmodified: parts that have never been in a multi-part component.
- F9: enable/disable debug coloring.
- F10: show/hide the diagnostic window.

The plugin scans loaded vessels every 0.5 seconds and uses the part parent/child attachment graph. The color overlay uses renderer property blocks and restores the original property blocks when debug coloring is disabled or the plugin unloads.

## Install

Install the contents of the release ZIP into the KSP directory. The ZIP contains a `GameData` folder.

## Important limitations

This is not the physics optimizer implementation yet. It only visualizes attachment-connected components. Actual physics-body aggregation, safe joint handling, force/torque transfer, collision behavior, and split/rebuild logic require a separate implementation and in-game testing.
