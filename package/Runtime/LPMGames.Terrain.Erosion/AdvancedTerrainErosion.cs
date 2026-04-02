/*
=====================================================================================

See README.md and THIRD_PARTY_NOTICES.md for information.

== The MIT License ==

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, including 
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions: The above copyright notice and this permission notice shall be included in all copies 
or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES 
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION 
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

== MPL 2.0 License ==

This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at https://mozilla.org/MPL/2.0/.
=====================================================================================
*/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Mathematics;

// This compiler define allows you to change between single and double precision, but
// note that it also changes the public API. Be careful to ensure that your code isn't
// casting at the API call site for the Sample methods!
#if ADVANCED_TERRAIN_EROSION_DOUBLE_PRECISION
using real = System.Double;
using vec2 = Unity.Mathematics.double2;
using vec3 = Unity.Mathematics.double3;
using vec4 = Unity.Mathematics.double4;
#else
using real = System.Single;
using vec2 = Unity.Mathematics.float2;
using vec3 = Unity.Mathematics.float3;
using vec4 = Unity.Mathematics.float4;
#endif

namespace LPMGames.Terrain.Erosion
{
    /// <summary>
    /// Contains the height and ridge map sample for a single point on the terrain.
    /// </summary>
    public struct ErosionSample
    {
        public readonly real Height;
        public readonly real RidgeWeight;

        public ErosionSample(real height, real ridgeWeight)
        {
            Height = height;
            RidgeWeight = ridgeWeight;
        }

        public static ErosionSample operator /(ErosionSample sample, real value) => 
            new(sample.Height / value, sample.RidgeWeight / value);

        public static ErosionSample operator +(ErosionSample sample, ErosionSample other) =>
            new(sample.Height + other.Height, sample.RidgeWeight + other.RidgeWeight);

        public static ErosionSample operator *(ErosionSample sample, real value) =>
            new(sample.Height * value, sample.RidgeWeight * value);
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct TerrainErosionConfig
    {
        public TerrainConfig Terrain;
        public ErosionConfig Erosion;
        public CubemapConfig Cubemap;
    }
    
    [Serializable]
    public struct CubemapConfig
    {
        public real FaceFrequency;
        public real FaceBlendWidth;
    }

    [Serializable]
    public struct ErosionConfig
    {
        public real Scale;
        public real Strength;
        public real GullyWeight;
        public real Detail;
        public vec4 Rounding;
        public vec4 Onset;
        public vec2 AssumedSlope;
        public real CellScale;
        public int Octaves;
        public real Gain;
        public real Lacunarity;
        public real Normalization;
    }
    
    [Serializable]
    public struct TerrainConfig
    {
        public int Octaves;
        public real Gain;
        public real Lacunarity;
        public real Amplitude;
        public real Frequency;
        
        public vec2 TerrainHeightOffset;
    }
    
    [SuppressMessage("ReSharper", "RedundantCast")]
    public struct AdvancedTerrainErosion
    {
        #region Precision helper constants
        
        // These following constants are really just helpers so that you can easily choose between
        // single or double precision values without having to put too many (real) casts in the code below
        private const real R0 = (real)0.0;
        private const real R1 = (real)1.0;
        private const real Tau = (real)math.TAU_DBL;
        private const real TerrainEpsilon = (real)1e-10;
        private static readonly vec2 HashIrrationals = new((real)(1.0 / math.PI_DBL), (real)math.exp(-1.0));
        private static readonly vec2 HashSeedScale = new((real)0.06711056, (real)0.00583715);
        
        #endregion

        #region Public API
        
        /// <summary>
        /// Gets the height and ridge map for any given point by projecting the direction to 2d cubemap faces and blending.
        /// </summary>
        /// <returns>
        /// An <see cref="ErosionSample"/> that contains the computed height and ridgemap at the given direction using the specified configuration and seed.
        /// </returns>
        public static ErosionSample Sample(vec3 direction, in TerrainErosionConfig config, int seed = 1337)
        {
            return Sample(
                direction, in config,
                config.Erosion.Strength, config.Erosion.GullyWeight, config.Erosion.Detail,
                config.Erosion.Rounding, config.Erosion.Onset, config.Erosion.AssumedSlope,
                seed);
        }
        
        /// <summary>
        /// Gets the height and ridge map for any given point by projecting the direction to 2d cubemap faces and blending.
        /// </summary>
        /// <returns>
        /// An <see cref="ErosionSample"/> that contains the computed height and ridgemap at the given direction using the specified configuration and seed.
        /// </returns>
        public static ErosionSample Sample(
            vec3 direction, in TerrainErosionConfig config,
            
            // Per-pixel variable values, these will override the values in config:
            real erosionStrength, real erosionGullyWeight, real erosionDetail, 
            vec4 erosionRounding, vec4 erosionOnset, vec2 erosionAssumedSlope,
            
            int seed = 1337)
        {
            if (math.lengthsq(direction) <= TerrainEpsilon) return default;

            direction = math.normalize(direction);

            var absDirection = math.abs(direction);
            var maxAbs = math.cmax(absDirection);
            var blendWidth = math.max(config.Cubemap.FaceBlendWidth, TerrainEpsilon);
            var blendStart = maxAbs - blendWidth;

            var weightX = math.smoothstep(blendStart, maxAbs, absDirection.x);
            var weightY = math.smoothstep(blendStart, maxAbs, absDirection.y);
            var weightZ = math.smoothstep(blendStart, maxAbs, absDirection.z);

            var weightedSample = new ErosionSample();
            var weightSum = R0;

            if (weightX > TerrainEpsilon)
                AccumulateFace(direction, direction.x >= R0 ? CubeFace.Right : CubeFace.Left, weightX, in config, erosionStrength, erosionGullyWeight, erosionDetail, erosionRounding, erosionOnset, erosionAssumedSlope, ref weightedSample, ref weightSum, seed);
            if (weightY > TerrainEpsilon)
                AccumulateFace(direction, direction.y >= R0 ? CubeFace.Top : CubeFace.Bottom, weightY, in config, erosionStrength, erosionGullyWeight, erosionDetail, erosionRounding, erosionOnset, erosionAssumedSlope, ref weightedSample, ref weightSum, seed);
            if (weightZ > TerrainEpsilon)
                AccumulateFace(direction, direction.z >= R0 ? CubeFace.Front : CubeFace.Back, weightZ, in config, erosionStrength, erosionGullyWeight, erosionDetail, erosionRounding, erosionOnset, erosionAssumedSlope, ref weightedSample, ref weightSum, seed);

            return weightSum > TerrainEpsilon ? weightedSample / weightSum : default;
        }

        /// <summary>
        /// Gets the height and ridge map for any given point by projecting the direction to 2d cubemap faces and blending.
        /// </summary>
        /// <returns>
        /// An <see cref="ErosionSample"/> that contains the computed height and ridgemap at the given direction using the specified configuration and seed.
        /// </returns>
        public static ErosionSample Sample(real x, real y, real z, in TerrainErosionConfig config, int seed = 1337) => Sample(new vec3(x, y, z), in config, seed);

        /// <summary>
        /// Gets the height and ridge map for any given point at a 2d coordinate
        /// </summary>
        /// <returns>
        /// An <see cref="ErosionSample"/> that contains the computed height and ridgemap at the given direction using the specified configuration and seed.
        /// </returns>
        public static ErosionSample Sample(
            vec2 p,
            in TerrainErosionConfig config,
            
            // Per-pixel variable values, these will override the values in config:
            real erosionStrength, real erosionGullyWeight, real erosionDetail, 
            vec4 erosionRounding, vec4 erosionOnset, vec2 erosionAssumedSlope,
            
            int seed = 1337)
        {
            return Sample(
                p, config.Erosion.Scale, 
                
                erosionStrength, erosionGullyWeight, erosionDetail, 
                erosionRounding, erosionOnset, erosionAssumedSlope,
                
                config.Erosion.CellScale, config.Erosion.Normalization, config.Erosion.Octaves, 
                config.Erosion.Lacunarity, config.Erosion.Gain,
                
                config.Terrain.TerrainHeightOffset, config.Terrain.Frequency, config.Terrain.Amplitude,
                config.Terrain.Octaves, config.Terrain.Lacunarity, config.Terrain.Gain,
                
                seed
            );
        }
        
        /// <summary>
        /// Gets the height and ridge map for any given point at a 2d coordinate
        /// </summary>
        /// <returns>
        /// An <see cref="ErosionSample"/> that contains the computed height and ridgemap at the given direction using the specified configuration and seed.
        /// </returns>
        public static ErosionSample Sample(
            vec2 p,
            in TerrainErosionConfig config,
            int seed = 1337)
        {
            return Sample(
                p, config.Erosion.Scale, 
                
                config.Erosion.Strength, config.Erosion.GullyWeight, config.Erosion.Detail, 
                config.Erosion.Rounding, config.Erosion.Onset, config.Erosion.AssumedSlope, 
                
                config.Erosion.CellScale, config.Erosion.Normalization, config.Erosion.Octaves, 
                config.Erosion.Lacunarity, config.Erosion.Gain,
                
                config.Terrain.TerrainHeightOffset, config.Terrain.Frequency, config.Terrain.Amplitude,
                config.Terrain.Octaves, config.Terrain.Lacunarity, config.Terrain.Gain,
                
                seed
            );
        }
        
        /// <summary>
        /// Gets the height and ridge map for any given point at a 2d coordinate
        /// </summary>
        /// <returns>
        /// An <see cref="ErosionSample"/> that contains the computed height and ridgemap at the given direction using the specified configuration and seed.
        /// </returns>
        public static ErosionSample Sample(
            vec2 p, real erosionScale, 
            
            // Erosion per-pixel variable:
            real erosionStrength, real erosionGullyWeight, real erosionDetail, 
            vec4 erosionRounding, vec4 erosionOnset, vec2 erosionAssumedSlope, 
            
            // Erosion nonvariable:
            real erosionCellScale, real erosionNormalization, int erosionOctaves, 
            real erosionLacunarity, real erosionGain, 
            
            // Terrain:
            vec2 terrainHeightOffset, real heightFrequency, real heightAmplitude, 
            int heightOctaves, real heightLacunarity, real heightGain, 
            
            // Seed:
            int seed = 1337)
        {
            // ------------------------------------------------------------------------
            // Heightmap implementation.
            // ------------------------------------------------------------------------
            
            // Calculate the FBM terrain height and derivatives and store them in n.
            // The heights are in the [-1, 1] range.
            var n = FractalNoise(p, heightFrequency, heightOctaves, heightLacunarity, heightGain, seed);
            n *= heightAmplitude;
            
            // Define the erosion fade target based on the altitude of the pre-eroded terrain.
            // The fade target should strive to be -1 at valleys and 1 at peaks, but overshooting is ok.
            var fadeTarget = math.clamp(n.x / (heightAmplitude * (real)0.6), -R1, R1);
            
            // Change terrain heights from [-1, 1] range to [0, 1] range.
            n = n * (real)0.5 + new vec3((real)0.5, 0, 0);
            
            // Store erosion in h (x : height delta, yz : slope delta, w : magnitude).
            // The output ridge map is -1 on creases and 1 on ridges.
            // The output debug value can be set to various values inside the erosion function.
            var h = ErosionFilter(
                p, n, fadeTarget,
                erosionStrength, erosionGullyWeight, erosionDetail,
                erosionRounding, erosionOnset, erosionAssumedSlope,
                erosionScale, erosionOctaves, erosionLacunarity,
                erosionGain, erosionCellScale, erosionNormalization,
                seed,
                out var ridgeMap);
            
            var offset = math.lerp(terrainHeightOffset.x, -fadeTarget, terrainHeightOffset.y) * h.w;
            var eroded = n.x + h.x + offset;

            return new ErosionSample(eroded, ridgeMap);
        }
        
        #endregion

        #region Core algorithms
        
        /// <summary>
        /// Generates a raw erosion sample at a given 2d point.
        /// </summary>
        /// <returns>
        /// A <c>float4</c> or <c>double4</c> vector containing the height delta (x), slope delta (yz), and
        /// magnitude (w).
        /// </returns>
        /// <remarks>
        /// Advanced Terrain Erosion Filter copyright (c) 2025 Rune Skovbo Johansen
        /// Converted to burstable C# by Luke Mitchell, 2026
        /// This Source Code Form is subject to the terms of the Mozilla Public
        /// License, v. 2.0. If a copy of the MPL was not distributed with this
        /// file, You can obtain one at https://mozilla.org/MPL/2.0/.
        /// </remarks>
        private static vec4 ErosionFilter(
            // Input parameters that vary per pixel.
            vec2 p, vec3 heightAndSlope, real fadeTarget,
            // Stylistic parameters that may vary per pixel.
            real strength, real gullyWeight, real detail, vec4 rounding, vec4 onset, vec2 assumedSlope,
            // Scale related parameters that do not support variation per pixel.
            real scale, int octaves, real lacunarity,
            // Other parameters.
            real gain, real cellScale, real normalization, int seed,
            // Output parameters.
            out real ridgeMap
        )
        {
            strength *= scale;
            fadeTarget = math.clamp(fadeTarget, -R1, R1);

            var inputHeightAndSlope = heightAndSlope;
            var freq = R1 / (scale * cellScale);
            var slopeLength = math.max(math.length(heightAndSlope.yz), TerrainEpsilon);
            var magnitude = R0;
            var roundingMult = R1;
            

            var roundingForInput = math.lerp(rounding.y, rounding.x, Clamp01(fadeTarget + (real)0.5)) * rounding.z;
            // The combined accumulating mask, based first on initial slope, and later on slope of each octave too.
            var combiMask = EaseOut(SmoothStart(slopeLength * onset.x, roundingForInput * onset.x));

            // Initialize the ridgeMap fadeTarget and mask.
            var ridgeMapCombiMask = EaseOut(slopeLength * onset.z);
            var ridgeMapFadeTarget = fadeTarget;

            // Deteriming the strength of the initial slope used for gully directions
            // based on the specified mix of the actual slope and an assumed slope.
            var gullySlope = math.lerp(heightAndSlope.yz, heightAndSlope.yz / slopeLength * assumedSlope.x, assumedSlope.y);
            
            for (var i = 0; i < octaves; i++)
            {
                // Calculate and add gullies to the height and slope.
                var phacelle = PhacelleNoise(p * freq, math.normalizesafe(gullySlope), cellScale, (real)0.25, normalization, seed);
                
                // Multiply with freq since p was multiplied with freq.
                // Negate since we use slope directions that point down.
                phacelle.zw *= -freq;
                
                // Amount of slope as value from 0 to 1.
                var sloping = math.abs(phacelle.y);
                
                // Add non-masked, normalized slope to gullySlope, for use by subsequent octaves.
                // It's normalized to use the steepest part of the sine wave everywhere.
                gullySlope += math.sign(phacelle.y) * phacelle.zw * strength * gullyWeight;
                
                // Gullies has height offset (from -1 to 1) in x and derivative in yz.
                var gullies = new vec3(phacelle.x, phacelle.y * phacelle.zw);

                // Fade gullies towards fadeTarget based on combiMask.
                // vec3 fadedGullies = mix(vec3(fadeTarget, 0.0, 0.0), gullies * gullyWeight, combiMask);
                var fadedGullies = math.lerp(new vec3(fadeTarget, R0, R0), gullies * gullyWeight, combiMask);
                
                // Apply height offset and derivative (slope) according to strength of current octave.
                heightAndSlope += fadedGullies * strength;
                magnitude += strength;

                // Update fadeTarget to include the new octave.
                fadeTarget = fadedGullies.x;

                // Update the mask to include the new octave.
                var roundingForOctave = math.lerp(rounding.y, rounding.x, Clamp01(phacelle.x + (real)0.5)) * roundingMult;
                var newMask = EaseOut(SmoothStart(sloping * onset.y, roundingForOctave * onset.y));
                combiMask = PowInv(combiMask, detail) * newMask;

                // Update the ridgeMap fadeTarget and mask.
                ridgeMapFadeTarget = math.lerp(ridgeMapFadeTarget, gullies.x, ridgeMapCombiMask);
                var newRidgeMapMask = EaseOut(sloping * onset.w);
                ridgeMapCombiMask *= newRidgeMapMask;

                // Prepare the next octave.
                strength *= gain;
                freq *= lacunarity;
                roundingMult *= rounding.w;
            }

            ridgeMap = ridgeMapFadeTarget * (R1 - ridgeMapCombiMask);

            var heightAndSlopeDelta = heightAndSlope - inputHeightAndSlope;
            return new vec4(heightAndSlopeDelta, magnitude);
        }
        
        /// <summary>
        /// The Simple Phacelle Noise function produces a stripe pattern aligned with the input vector.
        /// The name Phacelle is a portmanteau of phase and cell, since the function produces a phase by
        /// interpolating cosine and sine waves from multiple cells.
        /// </summary>
        /// <param name="p">The input point being evaluated.</param>
        /// <param name="normDir">The direction of the stripes at this point. It must be a normalized vector.</param>
        /// <param name="freq">
        /// The frequency of the stripes within each cell. It's best to keep it close to 1.0, as high values
        /// will produce distortions and other artifacts.
        /// </param>
        /// <param name="offset">The phase offset of the stripes, where 1.0 is a full cycle.</param>
        /// <param name="normalization">
        /// The degree of normalization applied, between 0 and 1. With e.g. a value of 0.4, raw output with 
        /// a magnitude below 0.6 won't get fully normalized to a magnitude of 1.0.
        /// </param>
        /// <param name="seed">The random seed used for computation.</param>
        /// <returns>
        /// A vector containing the normalized cosine and sine waves, as well as the direction vector, 
        /// which can be multiplied onto the sine to get the derivatives of the cosine.
        /// </returns>
        /// <remarks>
        /// Phacelle Noise function copyright (c) 2025 Rune Skovbo Johansen.
        /// Converted to burstable C# by Luke Mitchell, 2026
        /// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. 
        /// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
        /// </remarks>
        public static vec4 PhacelleNoise(vec2 p, vec2 normDir, real freq, real offset, real normalization, int seed)
        {
            // Get a vector orthogonal to the input direction, with a
            // magnitude proportional to the frequency of the stripes.
            var sideDir = normDir.yx * new vec2(-R1, R1) * freq * Tau;
            offset *= Tau;

            // Iterate over 4x4 cells, calculating a stripe pattern for each and blending between them.
            var pInt = math.floor(p);
            var pFrac = math.frac(p);
            vec2 phaseDir = R0;
            var weightSum = R0;

            for (var i = -1; i <= 2; i++)
            {
                for (var j = -1; j <= 2; j++)
                {
                    var gridOffset = new vec2(i, j);

                    // Calculate a cell point by starting off with a point in the integer grid.
                    var gridPoint = pInt + gridOffset;

                    // Calculate a random offset for the cell point between -0.5 and 0.5 on each axis.
                    var randomOffset = Hash2(gridPoint, seed) * (real)0.5;

                    // The final cell point (we don't store it) is the gridPoint plus the randomOffset.
                    // Calculate a vector representing the input point relative to this cell point:
                    // p - (gridPoint + randomOffset)
                    // = (pFrac + pInt) - ((pInt + gridOffset) + randomOffset)
                    // = pFrac + pInt - pInt - gridOffset - randomOffset
                    // = pFrac - gridOffset - randomOffset
                    var vectorFromCellPoint = pFrac - gridOffset - randomOffset;

                    // Bell-shaped weight function which is 1 at dist 0 and nearly 0 at dist 1.5.
                    // Due to the random offsets of up to 0.5, the closest a cell point not in the 4x4
                    // grid can be to the current point p is 1.5 units away.
                    var sqrDist = math.dot(vectorFromCellPoint, vectorFromCellPoint);
                    var weight = math.exp(-sqrDist * (real)2);

                    // Subtract 0.01111 to make the function actually 0 at distance 1.5, which avoids
                    // some (very subtle) grid line artefacts.
                    weight = math.max(R0, weight - (real)0.01111);

                    // Keep track of the total sum of weights.
                    weightSum += weight;

                    // The waveInput is a gradient which increases in value along sideDir. Its rate of
                    // change is the freq times tau, due to the multiplier pre-applied to sideDir.
                    var waveInput = math.dot(vectorFromCellPoint, sideDir) + offset;

                    // Add this cell's cosine and sine wave contributions to the interpolated value.
                    phaseDir += new vec2(math.cos(waveInput), math.sin(waveInput)) * weight;
                }
            }

            // Get the raw interpolated value.
            var interpolated = phaseDir / weightSum;

            // Interpret the value as a vector whose length represents the magnitude of both waves.
            var magnitude = math.sqrt(math.dot(interpolated, interpolated));

            // Apply a lower threshold to show small magnitudes we're going to fully normalize.
            magnitude = math.max(R1 - normalization, magnitude);

            // Return a vector containing the normalized cosine and sine waves, as well as the direction
            // vector, which can be multiplied onto the sine to get the derivatives of the cosine.
            return new vec4(interpolated / magnitude, sideDir);
        }

        #endregion
        
        #region Terrain height
        
        // Used for the height map - this could be swapped out with any noise function which efficiently
        // produces both height and 'slope' derivatives.
        private static vec3 FractalNoise(vec2 p, real freq, int octaves, real lacunarity, real gain, int seed)
        {
            var n = new vec3();
            var nf = freq;
            var na = R1;

            for (var i = 0; i < octaves; i++)
            {
                var octave = Noised(seed, p * nf);
                n.x += octave.x * na;
                n.yz += octave.yz * (na * nf);

                na *= gain;
                nf *= lacunarity;
            }

            return n;
        }

        // Returns gradient noise (in x) and its derivatives (in yz).
        // From https://www.shadertoy.com/view/XdXBRH
        private static vec3 Noised(int seed, vec2 p)
        {
            var i = math.floor(p);
            var f = math.frac(p);

            
            var u = f * f * f * (f * (f * (real)6 - (real)15) + (real)10);
            var du = (real)30 * f * f * (f * (f - (real)2) + R1);

            var ga = Hash2( i + new vec2(R0, R0), seed);
            var gb = Hash2( i + new vec2(R1, R0), seed);
            var gc = Hash2( i + new vec2(R0, R1), seed);
            var gd = Hash2( i + new vec2(R1, R1), seed);

            var va = math.dot(ga, f - new vec2(R0, R0));
            var vb = math.dot(gb, f - new vec2(R1, R0));
            var vc = math.dot(gc, f - new vec2(R0, R1));
            var vd = math.dot(gd, f - new vec2(R1, R1));

            return new vec3(
                va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd),
                ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) +
                du * (u.yx * (va - vb - vc + vd) + new vec2(vb, vc) - va)
            );
        }
        
        #endregion
        
        #region Common shader-style helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static real PowInv(real t, real power)
        {
            return R1 - math.pow(R1 - Clamp01(t), power);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static real EaseOut(real t)
        {
            var v = R1 - Clamp01(t);
            return R1 - v * v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static real SmoothStart(real t, real smoothing)
        {
            if (t >= smoothing)
                return t - (real)0.5 * smoothing;
            return (real)0.5 * t * t / smoothing;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static real Clamp01(real value)
        {
            return math.clamp(value, R0, R1);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static vec2 Hash2(vec2 x, int seed)
        {
            var k = HashIrrationals;
            var seedOffset = seed * HashSeedScale;
            x = (x + seedOffset) * k + new vec2(k.y, k.x);
            var t = math.frac(x.x * x.y * (x.x + x.y));
            var inner = (real)16 * k * t;
            return -R1 + (real)2 * math.frac(inner);
        }
        
        #endregion
        
        #region Cubemap helpers
        
        // Converts a 3d direction to a 2d cubemap face, then does a weighted sample.
        private static void AccumulateFace(
            vec3 direction,
            CubeFace face,
            real weight,
            in TerrainErosionConfig config,
            
            // Per-pixel variable values, these will override the values in config:
            real erosionStrength, real erosionGullyWeight, real erosionDetail, 
            vec4 erosionRounding, vec4 erosionOnset, vec2 erosionAssumedSlope,

            ref ErosionSample weightedSample,
            ref real weightSum,
            int seed)
        {
            var uv = DirectionToFaceUv(direction, face);
            var p = uv * config.Cubemap.FaceFrequency;
            
            weightedSample += Sample(
                p,
                config.Erosion.Scale, 
                
                erosionStrength, erosionGullyWeight, erosionDetail, 
                erosionRounding, erosionOnset, erosionAssumedSlope, 
                
                config.Erosion.CellScale, config.Erosion.Normalization, config.Erosion.Octaves, 
                config.Erosion.Lacunarity, config.Erosion.Gain,
                
                config.Terrain.TerrainHeightOffset, config.Terrain.Frequency, config.Terrain.Amplitude,
                config.Terrain.Octaves, config.Terrain.Lacunarity, config.Terrain.Gain,
                
                seed
            ) * weight;
            
            weightSum += weight;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static vec2 DirectionToFaceUv(vec3 direction, CubeFace face)
        {
            var uv = face switch
            {
                CubeFace.Right => new vec2(
                    -direction.z / math.max(math.abs(direction.x), TerrainEpsilon),
                     direction.y / math.max(math.abs(direction.x), TerrainEpsilon)
                ),
                CubeFace.Left => new vec2(
                    direction.z / math.max(math.abs(direction.x), TerrainEpsilon),
                    direction.y / math.max(math.abs(direction.x), TerrainEpsilon)
                ),
                CubeFace.Top => new vec2(
                     direction.x / math.max(math.abs(direction.y), TerrainEpsilon),
                    -direction.z / math.max(math.abs(direction.y), TerrainEpsilon)
                ),
                CubeFace.Bottom => new vec2(
                    direction.x / math.max(math.abs(direction.y), TerrainEpsilon),
                    direction.z / math.max(math.abs(direction.y), TerrainEpsilon)
                ),
                CubeFace.Front => new vec2(
                    direction.x / math.max(math.abs(direction.z), TerrainEpsilon),
                    direction.y / math.max(math.abs(direction.z), TerrainEpsilon)
                ),
                _ => new vec2(
                    -direction.x / math.max(math.abs(direction.z), TerrainEpsilon),
                     direction.y / math.max(math.abs(direction.z), TerrainEpsilon)
                )
            };

            return uv * (real)0.5 + (real)0.5;
        }

        private enum CubeFace
        {
            Right = 0, Left = 1,
            Top = 2, Bottom = 3,
            Front = 4, Back = 5,
        }
        
        #endregion
    }
}
