using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

[BurstCompile]
public struct PerlinNoise
{
    // Implementation based on article by Raouf Touti.
    // Calculates perlin noise for the point at the given coordinates.
    // Returns a value between 0 and 1.
    public static float CalculateNoise(float x, float y)
    {
        // Create a new NativeArray with permutation values.
        var p = new NativeArray<int>(512, Allocator.Temp);
        for (int i = 0; i < 256; i++)
        {
            p[256 + i] = p[i] = Permutations[i];
        }
        
        var X = (int)math.floor(x) & 255;
        var Y = (int)math.floor(y) & 255;
        x -= math.floor(x);
        y -= math.floor(y);
        
        // Calculate vector values from the given point to each corner. 
        var topRight = new float2(x - 1.0f, y - 1.0f);
        var topLeft = new float2(x, y - 1.0f);
        var bottomRight = new float2(x - 1.0f, y);
        var bottomLeft = new float2(x, y);

        // Calculate dot products from the coordinate to each corner.
        var dotTopRight = math.dot(topRight, GetConstantVector(p[p[X + 1] + Y + 1]));
        var dotTopLeft = math.dot(topLeft, GetConstantVector(p[p[X] + Y + 1]));
        var dotBottomRight = math.dot(bottomRight, GetConstantVector(p[p[X + 1] + Y]));
        var dotBottomLeft = math.dot(bottomLeft, GetConstantVector(p[p[X] + Y]));

        // Calculate weights for linear interpolation.
        var u = Fade(x);
        var v = Fade(y);
        
        // Get the perlin noise by linearly interpolating between all four corners.
        var value = math.lerp(math.lerp(dotBottomLeft, dotBottomRight, u), math.lerp(dotTopLeft, dotTopRight, u), v);

        p.Dispose();
        
        return (value / 0.70710678f + 1f) * 0.5f;
    }

    // Easing function for interpolation. Makes nearby locations have similar values of noise.
    private static float Fade(float t)
    {
        return ((6 * t - 15) * t + 10) * t * t * t;
    }

    // Returns the constant vector for the given value from the permutation table.
    private static float2 GetConstantVector(int v)
    {
        int h = v & 3;
        if (h == 0)
        {
            return new float2(1, 1);
        }
        else if (h == 1)
        {
            return new float2(-1, 1);
        }
        else if (h == 2)
        {
            return new float2(-1, -1);
        }
        else
        {
            return new float2(1, -1);
        }
    }
    
    // Precalculated permutations taken from Perlin's implementation:
    // https://cs.nyu.edu/~perlin/noise/
    private static readonly int[] Permutations =
    {
        151,160,137,91,90,15,
        131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,
        8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,
        197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,
        56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,
        27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,
        105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,
        216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,
        116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,
        250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,
        59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,
        248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,
        129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,
        104,218,246,97,228,251,34,242,193,238,210,144,12,191,
        179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,
        181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,
        254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,
        78,66,215,61,156,180
    };
}