# Advanced Terrain Erosion Filter (C# / Unity / Burst)

A Burst-compatible C# implementation of a fast, procedural terrain erosion filter, 
based on a chain of shader work from the ShaderToy community.

This is a **CPU Based** erosion filter that can be used to generate realistic looking
mountain erosion effects on your CPU-based terrain generation.

This is based on the work of Rune Skovbo Johansen, and a long list of works by other
people (read THIRD_PARTY_NOTICES.md for more info).

For an in-depth and awesome explanation of the technique, read/watch this first:

👉 https://blog.runevision.com/2026/03/fast-and-gorgeous-erosion-filter.html

## What this is

- A translation of a shader-based erosion technique into C#
- Designed for:
    - Unity
    - Burst / Jobs
    - CPU-side generation
- Supports both 2d and 3d sampling for spherical terrains
  - 3d sampling uses a projected cubemap with blended edges
- Outputs:
    - Height
    - Ridge map (useful for drainage, texturing, masks, etc.)

## Installation

### Copy + Paste

The code is all in a single file (`AdvancedTerrainErosion.cs`), so you can 
just copy it into your project.

### Unity Package Manager

1. Open Package Manager
2. Click "Add package from git URL"
3. Paste in: `https://github.com/lpmitchell/.git?path=/package`

## Single/Double Precision

By default the code will use 32 bit floating point precision.

If you're doing something like planetary scale terrain, you will likely need to use 64 bit
(double) precision. You can do that by setting the
`ADVANCED_TERRAIN_EROSION_DOUBLE_PRECISION` compiler define.

This **will change the public API** of the file to expect doubles (double2, double3, etc.)
for ALL parameters an inputs, and it the output samples will become doubles also.

To do this in Unity:
- Edit -> Project Settings
- Select **Player** from the left menu
- Scroll down to **Script Compilation**
- Add `ADVANCED_TERRAIN_EROSION_DOUBLE_PRECISION` define to the **Scripting Define Symbols** list


## Usage

```csharp
var config = new TerrainErosionConfig
{
    Terrain = new TerrainConfig
    {
        Frequency = 3.0f,
        Amplitude = 0.125f,
        Octaves = 3,
        Gain = 0.1f,
        Lacunarity = 2.0f,
        TerrainHeightOffset = new float2(-0.65f, 0.0f)
    },
    
    Erosion = new ErosionConfig
    {
        Scale = 0.15,
        Strength = 0.22,
        GullyWeight = 0.5f,
        Detail = 1.5f,
        Rounding = new float4(0.1f, 0.0f, 0.1f, 2.0f),
        Onset = new float4(1.25f, 1.25f, 2.8f, 1.5f),
        AssumedSlope = new float2(0.7f, 1.0f),
        CellScale = 0.7f,
        Octaves = 5,
        Gain = 0.5f,
        Lacunarity = 2.0f,
        Normalization = 0.5f
    },
    
    // Used only for 3d sampling:
    Cubemap = new CubemapConfig
    {
        FaceFrequency = 150.0f,
        FaceBlendWidth = 0.02f
    }
};

// 2D sampling
var sample = AdvancedTerrainErosion.Sample(new float2(x, y), config);

// 3D spherical sampling (no need to normalize the vector)
var sample3D = AdvancedTerrainErosion.Sample(new float3(x, y, z), config);

float height = sample.Height;
float ridge = sample.RidgeWeight;
```

If you need to override parameters per-pixel, there are signatures of the Sample method
that take all of the sensible runtime per-pixel variable parameters.

# FAQ

## Why isn't this a GPU shader?

CPU based terrain generation can be much easier to use than GPU based, and avoids
GPU readback for things such as collider generation, scatter placement, etc.

But importantly for me, the game that I am creating uses CPU based generation
and therefore I made this!

## What makes it useful

- Very fast compared to simulation-based erosion
- Highly controllable via parameters
- Produces believable gullies, ridges, and flow patterns
- Works well layered on top of existing terrain noise

