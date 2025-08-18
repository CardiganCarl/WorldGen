using System;
using System.Collections;
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
    [Range(1, 6)]
    public int levelOfDetail;
    public bool logPerformance;
    public bool autoUpdate;

    [Header("Appearance")] 
    public DrawMode drawMode;
    public float aoScale = 2;
    public TerrainType[] regions;
    
    [Header("Noise")]
    public float frequency = 1.0f;
    public float amplitude = 0.5f;
	public AnimationCurve meshHeightCurve;
    public int octaves = 4;
    public uint seed = 1;
    public Vector2 offset;

    [Header("Materials")] 
    public Material textureShaderMaterial;
    public Material vertexColorMaterial;
    public Gradient planeGradient;
    
    private int previousWidth;
    private int previousHeight;
    private float previousSubDivisions;
    private int previousLevelOfDetail;
    
    // Cached values.
    // private GameObject terrain;
    // private Mesh mesh;
    // private MeshFilter meshFilter;
    private Dictionary<(int, int), int[]> computedTriangles = new();

    // Generates a procedural terrain mesh based on fractal brownian motion.
    [ContextMenu("Generate Terrain")]
    public GameObject GenerateTerrain(Vector2 offset)
    {
        // Log execution time.
        System.Diagnostics.Stopwatch stopwatch = null;
        if (logPerformance)
        {
            stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
        }
        
        // The plane does not exist, or the amount of vertices in it has changed.
        // if (!terrain || width != previousWidth || height != previousHeight || subDivisions != previousSubDivisions || levelOfDetail != previousLevelOfDetail)
        // {
        //     DestroyWorld();
        //     terrain = CreatePlane();
        //     previousWidth = width;
        //     previousHeight = height;
        //     previousSubDivisions = subDivisions;
        //     previousLevelOfDetail = levelOfDetail;
        //     mesh = new Mesh();
        //     meshFilter = terrain.GetComponent<MeshFilter>();
        // }
        
        // GameObject terrain = GameObject.CreatePrimitive(PrimitiveType.Plane);
        GameObject terrain = CreatePlane();
        MeshFilter meshFilter = terrain.GetComponent<MeshFilter>();
        Mesh mesh = meshFilter.mesh;

        // Get the amount of vertices.
        int xAmount = (int)(width * subDivisions) / levelOfDetail;
        int yAmount = (int)(height * subDivisions) / levelOfDetail;
        
        NativeArray<float3> points = new NativeArray<float3>((xAmount + 1) * (yAmount + 1), Allocator.TempJob);
        
        // Calculate the position for each vertex on the mesh.
        GenerateHeightJob generateHeightJob = new GenerateHeightJob()
        {
            Points = points,
            Seed = seed,
            Amplitude = amplitude,
            Frequency = frequency,
            Octaves = octaves,
            Offset = offset,
            XAmount = xAmount,
            YAmount = yAmount,
            SubDivisions = subDivisions,
            LevelOfDetail = levelOfDetail,
        };
        JobHandle handle = generateHeightJob.Schedule(points.Length, 64);
        handle.Complete();
        
        Vector3[] vertices = new Vector3[points.Length];
        Vector2[] uvs = new Vector2[points.Length];
        Color[] colors = new Color[points.Length];
        
        // TODO: Make this into a job.
        for (int i = 0; i < vertices.Length; i++)
        { 
            vertices[i] = points[i];
            
            // Multiply the height with the curve value at that point.
            // This makes water for example be flat on the mesh, but mountains more pronounced.
            vertices[i].y *= meshHeightCurve.Evaluate(GetNormalizedHeight(points[i].y));

            terrain.GetComponent<MeshRenderer>().sharedMaterial = vertexColorMaterial;
            
            // Assign vertex color depending on which draw mode is selected.
            if (drawMode == DrawMode.Gradients)
            {
                colors[i] = planeGradient.Evaluate(GetNormalizedHeight(points[i].y));
            }
            else if (drawMode == DrawMode.Regions)
            {
                for (int j = 0; j < regions.Length; j++)
                {
                    if (GetNormalizedHeight(points[i].y) < regions[j].height)
                    {
                        colors[i] = regions[j].color;
                        break;
                    }
                }
            }
            else if (drawMode == DrawMode.Greyscale)
            {
                colors[i] = Color.Lerp(Color.black, Color.white, GetNormalizedHeight(points[i].y));
            }
			else if (drawMode == DrawMode.Textures)
            {
                terrain.GetComponent<MeshRenderer>().sharedMaterial = textureShaderMaterial;
                
                // Sends the vertex height to the shader as a color value.
                colors[i] = new Color(GetNormalizedHeight(points[i].y), 0, 0);
            }
            
            // Apply fake AO to darken areas with big height changes.
            // TODO: Replace with a better approximation (sweep-based hemisphere sampling).
            float avgNeighborHeight = GetNeighboringHeight(vertices, i, xAmount, yAmount);
            float ao = CalculateAO(points[i].y, avgNeighborHeight);
            colors[i] *= ao;
            
            // Calculate uvs.
            float u = (i % (xAmount + 1)) / (float)xAmount;
            float v = (i / (xAmount + 1)) / (float)yAmount;
            uvs[i] = new Vector2(u, v);
        }

        // Compute triangles if not done for this width and height already.
        if (!computedTriangles.ContainsKey((xAmount, yAmount)))
        {
            computedTriangles.Add((xAmount, yAmount), CalculateTriangles(xAmount, yAmount));
        }
        int[] triangles = computedTriangles[(xAmount, yAmount)];
        
        // Assign new mesh values.
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.colors = colors;
        mesh.uv = uvs;
        
        meshFilter.mesh = mesh;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        
        points.Dispose();
        
        // Logging
        if (stopwatch != null)
        {
            stopwatch.Stop();
            Debug.LogFormat("[WorldGenerator::GenerateTerrain] Execution time: {0}ms", stopwatch.ElapsedMilliseconds);
        }
        
        return terrain;
    }

    // Job to generate vertex positions for each point on the mesh.
    [BurstCompile]
    struct GenerateHeightJob : IJobParallelFor
    {
        public NativeArray<float3> Points;
        [ReadOnly] public uint Seed;
        [ReadOnly] public float Amplitude;
        [ReadOnly] public float Frequency;
        [ReadOnly] public int Octaves;
        [ReadOnly] public float2 Offset;
        [ReadOnly] public int XAmount;
        [ReadOnly] public int YAmount;
        [ReadOnly] public float SubDivisions;
        [ReadOnly] public int LevelOfDetail;
        
        public void Execute(int index)
        {
            float x = index % (XAmount + 1);
            float y = index / (XAmount + 1);
            float u = x / XAmount;
            float v = y / YAmount;
            
            // Assign vertex position with calculated fBM height.
            Points[index] = new float3(x / SubDivisions * LevelOfDetail, FractalNoise.CalculateNoise(u, v, Seed, Frequency, Octaves, Offset) * Amplitude, y / SubDivisions * LevelOfDetail);
        }
    }

    // Returns the height value normalized to between 0 and 1.
    private float GetNormalizedHeight(float h)
    {
        return h / amplitude;
    }

    // Calculate the strength of AO for the given height and neighboring height.
    private float CalculateAO(float vertexHeight, float neighborHeight)
    {
        float delta = Mathf.Abs(neighborHeight - vertexHeight) * aoScale / amplitude;
        float ao = 1.0f - Mathf.Clamp01(delta * aoScale / amplitude);
        return ao;
    }

    // Get the average height of the points neighbors.
    private float GetNeighboringHeight(Vector3[] vertices, int index, int xAmount, int yAmount)
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

    [ContextMenu("Destroy World")]
    public void DestroyWorld()
    {
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }
    }

    private GameObject CreatePlane()
    {
        // Create ground plane.
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        
        // Divide by 10 to remove the original scale of the plane.
        plane.transform.localScale = new Vector3(width, 1, height) / 10f;
        
        plane.transform.position = new Vector3(width * 0.5f - 0.5f, 0.0f, height * 0.5f - 0.5f);
        plane.transform.SetParent(transform);
        
        return plane;
    }
}

[Serializable]
public struct TerrainType
{
    public string name;
    public float height;
    public Color color;
}