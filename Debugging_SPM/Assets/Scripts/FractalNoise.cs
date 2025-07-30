using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public class FractalNoise : MonoBehaviour
{
    private const float Gain = 0.5f;
    private const float Lacunarity = 2.0f;
    
    /// <summary>
    /// Calculates the fractal brownian motion (fBM) for the given position.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="seed">The random seed to generate the noise map from.</param>
    /// <param name="frequency">Determines the amount of large and small values.</param> 
    /// <param name="amplitude">Determines the size of the difference between values.</param>
    /// <param name="octaves">Determines the amount of iterations, and therefore the amount of detail in the generated noise.</param>
    /// <param name="offset">The offset for scrolling over the noise.</param>
    /// <returns>The normalized value of the summed up noise at the given position.</returns>
    public static float CalculateNoise(float x, float y, uint seed, float frequency, int octaves, float2 offset)
    {
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed);
        NativeArray<float2> octaveOffsets = new NativeArray<float2>(octaves, Allocator.Temp);
        
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = random.NextFloat(-1000f, 1000f) + offset.x;
            float offsetY = random.NextFloat(-1000f, 1000f) + offset.y;
            octaveOffsets[i] = new float2(offsetX, offsetY);
        }
        
        float amplitude = 1;
        float noise = 0;
        float maxNoise = 0;
        
        for (int i = 0; i < octaves; i++)
        {
            // Add to the sum of the total noise by calculating perlin noise.
            noise += PerlinNoise.CalculateNoise(x * frequency + octaveOffsets[i].x, y * frequency + octaveOffsets[i].y) * amplitude;
            maxNoise += amplitude;
            
            // Multiply amplitude and frequency, making for changes in the resulting noise for each octave step in the loop.
            // This gives us larger patterns, as well as smaller fine details.
            amplitude *= Gain;
            frequency *= Lacunarity;
        }

        octaveOffsets.Dispose();

        // Return the total summed up noise value, normalized to between 0 and 1.
        return noise / maxNoise;
    }
}
