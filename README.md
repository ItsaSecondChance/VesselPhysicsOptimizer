# Vessel Physics Optimizer (VPO)

Target: Kerbal Space Program 1.12.5.3190.

## Current milestone: connection visualization

This initial test build visualizes connected part groups. It does not merge rigidbodies or improve physics performance yet.

- Purple: parts in a currently connected multi-part component.
- Green: parts that were previously grouped but are now singletons after a separation.
- F9: toggle debug colors.
- F10: toggle the diagnostics window.

## Install through CKAN

Add a custom metadata repository in CKAN:

- Name: Vessel Physics Optimizer
- URL: https://github.com/ItsaSecondChance/VesselPhysicsOptimizer/archive/main.tar.gz

Do not use "Import downloaded mods" for the metadata repository.

## Build

GitHub Actions compiles the plugin against KSP 1.12.5 reference assemblies using KSPBuildTools, packages only GameData/VesselPhysicsOptimizer, and attaches the resulting ZIP to the v0.1.0 GitHub release.
