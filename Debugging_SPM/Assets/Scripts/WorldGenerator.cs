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
    public bool autoUpdate;

    [Header("Materials")] 
    public Material textureShaderMaterial;
    public Material vertexColorMaterial;
    public Gradient planeGradient;
    
    private int previousWidth;
    private int previousHeight;
    private float previousSubDivisions;
    private int previousLevelOfDetail;
    
    // Cached values.
    private GameObject plane;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private Dictionary<(int, int), int[]> computedTriangles = new();

    [ContextMenu("Generate Terrain")]
    public void GenerateTerrain()
    {
        System.Diagnostics.Stopwatch stopwatch = null;
        // Log execution time.
        if (logPerformance)
        {
            stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
        }
        
        if (!plane || width != previousWidth || height != previousHeight || subDivisions != previousSubDivisions || levelOfDetail != previousLevelOfDetail)
        {
            DestroyWorld();
            plane = CreatePlane();
            previousWidth = width;
            previousHeight = height;
            previousSubDivisions = subDivisions;
            previousLevelOfDetail = levelOfDetail;
            mesh = new Mesh();
            meshFilter = plane.GetComponent<MeshFilter>();
        }

        int xAmount = (int)(width * subDivisions) / levelOfDetail;
        int yAmount = (int)(height * subDivisions) / levelOfDetail;
        
        NativeArray<float3> points = new NativeArray<float3>((xAmount + 1) * (yAmount + 1), Allocator.TempJob);
        
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
            vertices[i].y *= meshHeightCurve.Evaluate(points[i].y / amplitude);

            plane.GetComponent<MeshRenderer>().sharedMaterial = vertexColorMaterial;
            
            // Assign the color for the vertex depending on which draw mode is selected.
            if (drawMode == DrawMode.Gradients)
            {
                colors[i] = planeGradient.Evaluate(points[i].y / amplitude);
            }
            else if (drawMode == DrawMode.Regions)
            {
                for (int j = 0; j < regions.Length; j++)
                {
                    if (points[i].y / amplitude < regions[j].height)
                    {
                        colors[i] = regions[j].color;
                        break;
                    }
                }
            }
            else if (drawMode == DrawMode.Greyscale)
            {
                colors[i] = Color.Lerp(Color.black, Color.white, points[i].y / amplitude);
            }
			else if (drawMode == DrawMode.Textures)
            {
                plane.GetComponent<MeshRenderer>().sharedMaterial = textureShaderMaterial;
                
                // Sends the vertex height to the shader as a color value.
                colors[i] = new Color(points[i].y / amplitude, 0, 0);
            }

            // Apply fake AO to darken areas with big height changes.
            // TODO: Replace with a better approximation (sweep-based hemisphere sampling).
            float avgNeighborHeight = GetNeighboringHeight(vertices, i, xAmount, yAmount);
            float delta = Mathf.Abs(avgNeighborHeight - points[i].y) * aoScale / amplitude;
            
            float ao = 1.0f - Mathf.Clamp01(delta * aoScale / amplitude);
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
    }

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
            
            Points[index] = new float3(x / SubDivisions * LevelOfDetail, FractalNoise.CalculateNoise(u, v, Seed, Frequency, Octaves, Offset) * Amplitude, y / SubDivisions * LevelOfDetail);
        }
    }

    private float GetNeighboringHeight(Vector3[] vertices, int index, int xAmount, int yAmount)
    {
        int count = 0;
        float sum = 0.0f;
        
        int xIndex = index % (xAmount + 1);
        int yIndex = index / (xAmount + 1);
        
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0)
                    continue;
                int dx = x + xIndex;
                int dy = y + yIndex;
                
                if (dx < 0 || dx > xAmount || dy < 0 || dy > yAmount)
                    continue;
                
                int neighborIndex = dy * (xAmount + 1) + dx;
                sum += vertices[neighborIndex].y;
                
                count++;
            }
        }

        if (count > 0)
        {
            sum /= count;
            return sum;
        }
        else
        {
            Debug.Log("Wrong?");
            return 0.0f;
        }
    }

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
        
        plane.transform.localScale = new Vector3(width, 10.0f, height) * 0.1f;
        
        plane.transform.position = new Vector3(width * 0.5f - 0.5f, 0.0f, height * 0.5f - 0.5f);
        plane.GetComponent<Renderer>().material = vertexColorMaterial;
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