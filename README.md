# SimChop (beta, version 0.8.3)
#### &copy; Dr. Russell Campbell, and Eli Landa
##### Vancouver Island University, Nanaimo, British Columbia, Canada

<img src="images/SimChopIcon.png" width="18%">

[![](https://img.shields.io/discord/849621464541429781.svg?label=discord&logo=discord)](https://discord.gg/N2yPtpFYpE)

| ![](images/Cloud_Sim.gif) | ![](images/Smoke_Effect.gif) |
|---|---|
| ![](images/Squid_tankBetterWater.gif) | ![](images/River_Demo.gif) |

<br>

## Volumetric/Particle System

SimChop is an open source project ([MIT License](LICENSE)) demonstrating a real-time volumetric/particle system in Unity 2020 LTS.

Specifications:

* most recent setup uses Unity version 2020.3.14f1 (but any 2020 LTS version should be fine)
* a GPU---so far, tested on NVIDIA:
    * GTX 1080TI
    * GeForce RTX 2060
    * GeForce RTX 3070 

We plan to continue development for the next Unity LTS release making use of DOTS to extend SimChop to allow for tens-of-thousands of shaded Unity Physics Sphere Colliders.

Imagine games with real-time flows/pools of *interactive* water, or manipulable fogs controlled by the player, fuzzy portals, highly dynamic force fields that react to their environment, etc.

The different use cases are quite diverse and we would be excited to see your adaptations of our system! Join the [SimChop Discord](https://discord.gg/N2yPtpFYpE) server where you can show off your shader skills, or get a bit of help with the configurations of SimChop.

<br>

<hr>

<br>

## SimChop Summary

Particles are managed with Sphere Colliders. Their positions are passed into shaders to produce volumetric Voronoi-like effects.

The system is still fairly early in development, but a few demo scenes are included in the project to help see some examples of configurations that are more stable.

The `InterleavingDemo` scene includes a most basic setup of game objects to have particles consistently flow throughout an area:

* Box Colliders on a Terrain for walls to hold particles inside
* another Box Collider `Sink` that teleports particles back to a `Source` when particles enter it <br> 
(this keeps particles flowing through an area to avoid them settling and then locking motion)
* a Player object with a child `InterleavedVolume` that generates "level-surface" geometry to display the shaded particles
* each particle is a prefab that includes:
    * a Rigidbody for Physics interaction
    * a Sphere Collider with a `radius` control and `PhysicMaterial`
    * a `MoveParticle` script that applies a force once per second <br>
    (adapt its formulae for applying forces as needed)


<br>
<hr>
<br>

## `InterleavedVolume` Controls

The Inspector for a game object that has the script `Simulation` added as a component (e.g., `InterleavedVolume` in the `InterleavingDemo` Scene) gives many controls.

Note that in the Editor, you will have higher frame rates when the Game View is docked in the same window as the Scene View.

The particle prefab Sphere Collider `radius` is typically set to be smaller than the `Radius` for shader display, but you can also set the display radius to be larger than the Sphere Collider radius. Best performance has particle display radius equal to or smaller than Sphere Collider radius. When the display radius is much larger than the Sphere Collider radius, the frame rate will be lower.

Debugging for when particles pop in and out of view (henceforth called "flicker") is now minimal and only rarely occurring near the corners of the volume, which will be debugged soon.

The system handles a lower number of particles fairly well. It is possible to set options so that 4096 particles can get about 30 fps on higher-end graphics cards, but this is mostly for matching radii and the frame rate will be lower the more particles you add.

The volume covering the camera depends on the aspect ratio of the window it displays inside&mdash;and the correct geometry to fit the camera view is generated at the start of play. Therefore, you may need to restart play to have the geometry/particles properly display if you need a different-sized window. This is mostly dealt with now, but you may see artefacts of geometry lines near the edge of the window if you resize it. It is recommended that the Game View be set to one of the preset constant aspect ratios, e.g.: `16:9 Aspect`, rather than a `Free Aspect` ratio.

<br>

<hr>

<br>

## `Levels` Material and `RipplingFog` Shader

The basic shader magic is done in `SimChop/RipplingFog.shader`.

It includes a `BinarySearch.cginc` that organizes the Texture3D lookups. The data structure follows the design of an octree, but no linking of cells needs to be passed in, since we order the particle positions based on Morton codes and then use binary search.

The volume that an octree covers tends to be restricted to an exact halving some finite number of times, but we scale between these discrete halvings with the setup and calculations in `Simulation.cs`.

The volume of interest in the scene that covers the particles to render have the positions mapped to a unit cube in the first octant (all x, y, z values are between 0 and 1).

The Morton codes then perform lookup by scaling a requested location in the unit cube up to a cube of sidelength some power of 2. The `precision` controls this scaling and is calculated to fit the desired radius and cover the volume in view as closely as possible. The same scaling has to be done in the shaders for the particles to be quickly found out of thousands for those that are closest.

The scan number is responsible for lookup within cells (similar to octree lookups) where the cells are larger and will fill with at most 78% density full of collider spheres (see the Sphere Packing Conjecture). This gives a strict upper bound on the number of particles within any cellular region.

 You may have more powerful graphics hardware and know how to use it. The system will still work if you edit a change to the `MAX_PARTICLES` constant in `Simulation.cs`. Although nothing crashed with testing 32768 particles, the fps with NVIDIA GeForce RTX 3070 was around 0.5. The configuration settings have limits in place to avoid extreme calculations needed to run the particle simulation that would result in frame rates that would be unreasonably low and unlikey to be useful. If you are familiar with the code and have more powerful hardware, there is nothing to stop you from adjusting the limits as you need.
 