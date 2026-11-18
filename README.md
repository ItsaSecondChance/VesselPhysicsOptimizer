# Vessel Physics Optimizer (VPO)

Target: Kerbal Space Program 1.12.5.3190.

## Experimental physics aggregation (0.2.0)

This build adds an opt-in experiment using KSP's native part physics transitions:

- The physical vessel root remains the main rigidbody.
- Eligible attached descendants are converted through KSP's `Part.DemoteToPhysicslessPart()`.
- Each physicsless child keeps a collider relay. When that child's collider is struck, VPO promotes that exact part through KSP's `Part.PromoteToPhysicalPart()` and passes the collision through that part's native `Part.OnCollisionEnter()` callback. KSP checks collider ownership and collision-ignore state before running its native damage and crash-tolerance routine.
- Before decoupling or undocking, VPO restores the managed parts on that vessel. KSP then performs the stock separation, and VPO scans the resulting vessels again.
- VPO does not replace KSP's mass or collision-damage calculations with its own parallel system.

**This is experimental and has not been validated in a live game session. Keep it disabled until testing.** Enable it with F8 only on a backed-up test save. Press F8 again to disable aggregation and restore parts managed by VPO.

### Controls

- F8: toggle experimental physics aggregation (OFF by default)
- F9: toggle part-group debug colors
- F10: toggle diagnostics

Purple marks connected multi-part groups. Green marks parts that were previously grouped but are now singletons after a separation.

### Conservative exclusions

VPO leaves parts physical when their modules indicate independent movement or special joints, including wheels, landing gear, robotics/servos, docking nodes, grapples, ladders, deployables, control surfaces, airbrakes, gimbals, launch clamps, and autostrutted parts. Stock physicsless parts that VPO did not demote are left untouched.

## Install through CKAN

Add a custom metadata repository in CKAN:

- Name: Vessel Physics Optimizer
- URL: https://github.com/ItsaSecondChance/VesselPhysicsOptimizer/archive/main.tar.gz

Do not use "Import downloaded mods" for the metadata repository.

## Build

GitHub Actions compiles against KSP 1.12.5 reference assemblies, packages only `GameData/VesselPhysicsOptimizer`, and uploads a workflow artifact. A GitHub release is published only from the main branch after successful packaging. After publishing the main-branch release, the workflow updates the CKAN metadata from the actual ZIP, including its exact size and SHA-256. The 0.2.0 metadata is marked as testing until the physics behavior has been validated in-game.