using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.VisualScripting;
using Random = UnityEngine.Random;

[BurstCompile]
public class WorldGenerator : MonoBehaviour
{
    public enum DrawMode
    {
        Regions,
        Gradients,
        Greyscale,
		Textures
    };
    
    [Header("World")]
    public int width = 200;
    public int height = 200;
    public float subDivisions = 1;
    public bool logPerformance;
    public bool autoUpdate;

    [Header("Appearance")] 
    public DrawMode drawMode;
    public bool applyAO;
    public float aoScale = 2;
    public TerrainType[] regions;
    
    [Header("Noise")]
    public float frequency = 1.0f;
    public float amplitude = 0.5f;
	public AnimationCurve meshHeightCurve;
    public int octaves = 4;
    public uint seed = 1;
    
    [Header("Materials")] 
    public Material textureShaderMaterial;
    public Material vertexColorMaterial;
    public Gradient planeGradient;
    
    // Cached values.
    private Dictionary<(int, int), int[]> computedTriangles = new();
    
    public MeshInfo GenerateMeshInfo(Vector2 posOffset, int levelOfDetail)
    {
        MeshInfo meshInfo = new MeshInfo();
        
        // Get the amount of vertices.
        int xAmount = (int)(width * subDivisions) / levelOfDetail;
        int yAmount = (int)(height * subDivisions) / levelOfDetail;
        
        NativeArray<float3> nativeVertices = new NativeArray<float3>((xAmount + 1) * (yAmount + 1), Allocator.Persistent);
        NativeArray<float2> nativeUVs = new NativeArray<float2>((xAmount + 1) * (yAmount + 1), Allocator.Persistent);
        
        // Calculate the position for each vertex on the mesh.
        CalculateVertexPosition vertexPositionJob = new CalculateVertexPosition()
        {
            Vertices = nativeVertices,
            UVs = nativeUVs,
            Seed = seed,
            Amplitude = amplitude,
            Frequency = frequency,
            Octaves = octaves,
            Offset = posOffset,
            XAmount = xAmount,
            YAmount = yAmount,
            SubDivisions = subDivisions,
            LevelOfDetail = levelOfDetail,
        };
        meshInfo.vertexPosHandle = vertexPositionJob.Schedule(nativeVertices.Length, 64);
        meshInfo.vertices = nativeVertices;
        meshInfo.uvs = nativeUVs;
        meshInfo.offset = posOffset;
        meshInfo.LOD = levelOfDetail;
        
        return meshInfo;
    }

    // Job to generate vertex positions for each point on the mesh.
    [BurstCompile]
    private struct CalculateVertexPosition : IJobParallelFor
    {
        public NativeArray<float3> Vertices;
        public NativeArray<float2> UVs;
        [ReadOnly] public uint Seed;
        [ReadOnly] public float Amplitude;
        [ReadOnly] public float Frequency;
        [ReadOnly] public int Octaves;
        [ReadOnly] public float2 Offset;
        [ReadOnly] public int XAmount;
        [ReadOnly] public int YAmount;
        [ReadOnly] public float SubDivisions;
        [ReadOnly] public int LevelOfDetail;
        [ReadOnly] public float2 Position;
        
        public void Execute(int index)
        {
            float x = index % (XAmount + 1);
            float y = index / (XAmount + 1);
            float u = x / XAmount;
            float v = y / YAmount;
            
            float posX = x / SubDivisions * LevelOfDetail;
            float posY = y / SubDivisions * LevelOfDetail;
            
            // Assign vertex position with calculated fBM height.
            Vertices[index] = new float3(posX, FractalNoise.CalculateNoise(posX, posY, Seed, Frequency, Octaves, Offset) * Amplitude, posY);
            
            UVs[index] = new float2(u, v);
        }
    }

    public GameObject GenerateTerrain(MeshInfo meshInfo)
    {
        // Log execution time.
        System.Diagnostics.Stopwatch stopwatch = null;
        if (logPerformance)
        {
            stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
        }
        
        // Check that mesh info generation has been completed before generating the mesh.
        meshInfo.vertexPosHandle.Complete();

        // Get the amount of vertices.
        int xAmount = (int)(width * subDivisions) / meshInfo.LOD;
        int yAmount = (int)(height * subDivisions) / meshInfo.LOD;
        int containerLength = (xAmount + 1) * (yAmount + 1);
        
        Vector3[] vertices = new Vector3[containerLength];
        Vector2[] uvs = new Vector2[containerLength];
        Color[] colors = new Color[containerLength];

        for (int i = 0; i < vertices.Length; i++)
        {
            // Copy over vertex info from native to managed array.
            vertices[i] = meshInfo.vertices[i];
            uvs[i] = meshInfo.uvs[i];
            
            // Multiply the height with the curve value at that point.
            // This makes water for example be flat on the mesh, but mountains more pronounced.
            vertices[i].y *= meshHeightCurve.Evaluate(GetNormalizedHeight(meshInfo.vertices[i].y));
        }
        
        // Dispose the temporary native arrays.
        meshInfo.vertices.Dispose();
        meshInfo.uvs.Dispose();
        
        // Create game object and components.
        GameObject terrain = new GameObject("Terrain, LOD: " + meshInfo.LOD)
        {
            transform =
            {
                position = new Vector3(meshInfo.offset.x, 0, meshInfo.offset.y),
                localScale = Vector3.one,
                parent = transform
            }
        };
        MeshFilter meshFilter = terrain.AddComponent<MeshFilter>();
        terrain.AddComponent<MeshRenderer>();
        Mesh mesh = meshFilter.mesh;
        
        if (drawMode == DrawMode.Textures)
        {
            terrain.GetComponent<Renderer>().sharedMaterial = textureShaderMaterial;
        }
        else
        {
            terrain.GetComponent<Renderer>().sharedMaterial = vertexColorMaterial;
        }
        
        // Apply per-vertex colors.
        for (int i = 0; i < vertices.Length; i++)
        {
            colors[i] = CalculateColor(vertices[i].y);
            
            // Apply fake AO to darken areas with big height changes.
            // TODO: Replace with a better approximation (sweep-based hemisphere sampling).
            if (applyAO)
            {
                float avgNeighborHeight = GetNeighboringHeight(vertices, i, xAmount, yAmount);
                float ao = CalculateAOAmount(vertices[i].y, avgNeighborHeight, aoScale, amplitude);
                colors[i] *= ao;
            }
        }
        
        // Compute triangles if not done for this width and height already.
        if (!computedTriangles.ContainsKey((xAmount, yAmount)))
        {
            computedTriangles.Add((xAmount, yAmount), CalculateTriangles(xAmount, yAmount));
        }
        int[] triangles = computedTriangles[(xAmount, yAmount)];
        
        // Assign new mesh values.
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        
        meshFilter.mesh = mesh;
        mesh.RecalculateNormals();
        
        // Recalculating tangents is only needed if the shader uses normals, which we currently don't.
        // mesh.RecalculateTangents();
        
        // Logging
        if (stopwatch != null)
        {
            stopwatch.Stop();
            Debug.LogFormat("[WorldGenerator::GenerateTerrain] Execution time: {0}ms", stopwatch.ElapsedMilliseconds);
        }

        return terrain;
    }

    private Color CalculateColor(float h)
    {
        // Assign vertex color depending on which draw mode is selected.
        if (drawMode == DrawMode.Gradients)
        {
            return planeGradient.Evaluate(GetNormalizedHeight(h));
        }
        else if (drawMode == DrawMode.Regions)
        {
            for (int j = 0; j < regions.Length; j++)
            {
                if (GetNormalizedHeight(h) < regions[j].height)
                {
                    return regions[j].color;
                }
            }
        }
        else if (drawMode == DrawMode.Greyscale)
        {
            return Color.Lerp(Color.black, Color.white, GetNormalizedHeight(h));
        }
        return new Color(GetNormalizedHeight(h), 0, 0);
    }

    // Returns the height value normalized to between 0 and 1.
    private float GetNormalizedHeight(float h)
    {
        return h / amplitude;
    }

    // Calculate the strength of AO for the given height and neighboring height.
    private static float CalculateAOAmount(float vertexHeight, float neighborHeight, float aoScale, float amplitude)
    {
        float delta = Mathf.Abs(neighborHeight - vertexHeight) * aoScale / amplitude;
        float ao = 1.0f - Mathf.Clamp01(delta * aoScale / amplitude);
        return ao;
    }

    // Get the average height of the points neighbors.
    private static float GetNeighboringHeight(Vector3[] vertices, int index, int xAmount, int yAmount)
    {
        int count = 0;
        float sum = 0.0f;
        
        int xIndex = index % (xAmount + 1);
        int yIndex = index / (xAmount + 1);
        
        // Loop over all adjacent vertices.
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                // Skip the middle vertex.
                if (x == 0 && y == 0)
                    continue;
                
                int dx = x + xIndex;
                int dy = y + yIndex;
                
                // The vertex doesn't exist.
                if (dx < 0 || dx > xAmount || dy < 0 || dy > yAmount)
                    continue;
                
                // Add up the height of the neighbor.
                int neighborIndex = dy * (xAmount + 1) + dx;
                sum += vertices[neighborIndex].y;
                
                count++;
            }
        }

        // Return average height.
        if (count > 0)
        {
            sum /= count;
            return sum;
        }
        return 0.0f;
    }

    // Calculates triangles for the given amount of vertices.
    private int[] CalculateTriangles(int xAmount, int yAmount)
    {
        int[] triangles = new int[xAmount * yAmount * 6];
        
        int vert = 0;
        int tris = 0;
        
        for (int y = 0; y < yAmount; y++)
        {
            for (int x = 0; x < xAmount; x++)
            {
                triangles[tris + 0] = vert;
                triangles[tris + 1] = vert + xAmount + 1;
                triangles[tris + 2] = vert + 1;
                triangles[tris + 3] = vert + 1;
                triangles[tris + 4] = vert + xAmount + 1;
                triangles[tris + 5] = vert + xAmount + 2;

                vert++;
                tris += 6;
            }

            vert++;
        }
        
        return triangles;
    }
}

[Serializable]
public struct TerrainType
{
    public string name;
    public float height;
    public Color color;
}

public struct MeshInfo
{
    public NativeArray<float3> vertices;
    public NativeArray<float2> uvs;
    public JobHandle vertexPosHandle;
    public Vector2 offset;
    public int LOD;
}