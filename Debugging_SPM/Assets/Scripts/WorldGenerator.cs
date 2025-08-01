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
    [Header("World")]
    public int width = 200;
    public int height = 200;
    public int chunks = 4;
    public float percentageBlocks = 0.35f;
	public int subDivisions = 200;
    
    [Header("Noise")]
    public float frequency = 1.0f;
    public float amplitude = 0.5f;
    public int octaves = 4;
    public uint seed = 1;
    public Vector2 offset;
    public bool autoUpdate;

    [Header("Materials")]
    public Material planeMaterial;
    public Material obstacleMaterial;
    public Gradient planeGradient;

    [Header("AI")]
    public GameObject pawnDirectoryInstance;
    public GameObject AIPrefab;
    public int numAI = 500;

    // GameObject walls;
    Mesh wallMesh;
    
    // private Transform[] wallTransforms;
    private NativeArray<float> samples;
    private NativeArray<float3> positions;
    private NativeArray<float3> scales;
    
    private int chunkSize;
    private int previousWidth;
    private int previousHeight;
    private int previousSubDivisions;
    
    // Cached values.
    private GameObject plane;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private Dictionary<(int, int), int[]> computedTriangles = new();
    
    void Awake()
    {
        PlaceAI();
    }

    [ContextMenu("Generate Terrain")]
    public void GenerateTerrain()
    { 
        // Log execution time.
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        if (!plane || width != previousWidth || height != previousHeight || subDivisions != previousSubDivisions)
        {
            DestroyWorld();
            plane = CreatePlane();
            previousWidth = width;
            previousHeight = height;
            previousSubDivisions = subDivisions;
            mesh = new Mesh();
            meshFilter = plane.GetComponent<MeshFilter>();
        }

		int xAmount = width * subDivisions;
		int yAmount = height * subDivisions;
        
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
        };
        JobHandle handle = generateHeightJob.Schedule(points.Length, 64);
        handle.Complete();
        
        Vector3[] vertices = new Vector3[points.Length];
        Vector2[] uvs = new Vector2[points.Length];
        Color[] colors = new Color[points.Length];
        
        for (int i = 0; i < vertices.Length; i++)
        { 
            vertices[i] = points[i];
            
            // Set vertex color equivalent to the normalized height value of the vertex.
            colors[i] = planeGradient.Evaluate(points[i].y / amplitude);
            
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
        stopwatch.Stop();
        Debug.LogFormat("[WorldGenerator::GenerateTerrain] Execution time: {0}ms", stopwatch.ElapsedMilliseconds);
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
        
        [ReadOnly] public int SubDivisions;
        
        public void Execute(int index)
        {
            float x = index % (XAmount + 1);
            float y = index / (XAmount + 1);
            float u = x / XAmount;
            float v = y / YAmount;
            
            Points[index] = new float3(x / SubDivisions, FractalNoise.CalculateNoise(u, v, Seed, Frequency, Octaves, Offset) * Amplitude, y / SubDivisions);
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
        // NOTE: Planes are scaled 10x.
        plane.transform.localScale = new Vector3(width, 10.0f, height) * 0.1f;
        // NOTE: Origo of a plane is in the middle. Move lower left corner to zero.
        plane.transform.position = new Vector3(width * 0.5f - 0.5f, 0.0f, height * 0.5f - 0.5f);
        plane.GetComponent<Renderer>().material = planeMaterial;
        plane.transform.SetParent(transform);
        
        return plane;
    }

    [ContextMenu("Generate New World")]
    public void BuildWorld()
    {
        // Log execution time.
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        
        // walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(temp);
        samples = new NativeArray<float>(height * width, Allocator.Persistent);
        positions = new NativeArray<float3>(height * width, Allocator.Persistent);
        scales = new NativeArray<float3>(height * width, Allocator.Persistent);

        chunkSize = width / chunks;

        DestroyWorld();
        CreatePlane();
        
        List<CombineInstance> meshes = new List<CombineInstance>();

        // Create obstacles/geometry.
        GenerateWallsJob job = new GenerateWallsJob
        {
            Samples = samples,
            Positions = positions,
            Scales = scales,
            Width = width,
            Height = height,
            Amplitude = amplitude,
            PercentageBlocks = percentageBlocks,
        };
        JobHandle handle = job.Schedule(width * height, 64);
        handle.Complete();
        
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                // Add a new mesh to the chunk.
                if (samples[i * width + j] < percentageBlocks)
                {
                    CombineInstance ci = new CombineInstance();
                    ci.mesh = wallMesh;
                    Matrix4x4 matrix = Matrix4x4.TRS(positions[i * width + j], Quaternion.identity, scales[i * width + j]);
                    ci.transform = matrix;
                    meshes.Add(ci);
                }

                // Create new chunk by combining meshes.
                if (j == i && (j + 1) % chunkSize == 0 && (i + 1) % chunkSize == 0)
                {
                    Mesh mesh = new Mesh();
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.CombineMeshes(meshes.ToArray());
                    meshes.Clear();
                    
                    GameObject walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    walls.GetComponent<MeshFilter>().mesh = mesh;
                    
                    walls.isStatic = true;
                    walls.GetComponent<Renderer>().material = obstacleMaterial;
                    
                    walls.transform.SetParent(transform);
                    walls.transform.position = Vector3.zero;
                    
                    DestroyImmediate(walls.GetComponent<BoxCollider>());
                    walls.AddComponent<MeshCollider>();
                }
            }
        }
        
        positions.Dispose();
        scales.Dispose();
        samples.Dispose();

        // Logging 
        stopwatch.Stop();
        Debug.LogFormat("[WorldGenerator::BuildWorld] Execution time: {0}ms", stopwatch.ElapsedMilliseconds);
    }

    [BurstCompile]
    struct GenerateWallsJob : IJobParallelFor
    {
        [ReadOnly] private float x;
        [ReadOnly] private float y;
        public NativeArray<float> Samples;
        public NativeArray<float3> Positions;
        public NativeArray<float3> Scales;
        [ReadOnly] public float Width;
        [ReadOnly] public float Height;
        [ReadOnly] public float Amplitude;
        [ReadOnly] public float PercentageBlocks;
        public void Execute(int index)
        {
            x = index % Width;
            y = index / Width;
            
            float sample = PerlinNoise.CalculateNoise((float)x / Width * Amplitude, (float)y / Height * Amplitude);
            Samples[index] = sample;
            if (sample < PercentageBlocks)
            {
                // NOTE: Larger noise gives higher blocks.
                float obstacleHeight = 3.0f - sample * 2.0f;

                Positions[index] = new Vector3(x, obstacleHeight * 0.5f, y);
                Scales[index] = new Vector3(1.0f, obstacleHeight, 1.0f);
            }
        }
    }

    void PlaceAI()
    {
        // Log execution time.
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        int placed = 0;

        float placementChance = numAI / ((1.0f - percentageBlocks) * ((width - 2) * (height - 2)));

        while (placed < numAI)
        {
            int x = Random.Range(0, width);
            int y = Random.Range(0, height);

            Vector3 samplePos = new Vector3(x, AIPrefab.GetComponent<CapsuleCollider>().height * 0.5f, y);
            Vector3 point1 = samplePos + AIPrefab.GetComponent<CapsuleCollider>().height * 0.5f * Vector3.up;
            Vector3 point2 = samplePos - AIPrefab.GetComponent<CapsuleCollider>().height * 0.5f * Vector3.up;
            float radius = AIPrefab.GetComponent<CapsuleCollider>().radius;

            Collider[] colliders = Physics.OverlapCapsule(point1, point1, radius);
            if (colliders.Length == 0)
            {
                Quaternion rotation = Quaternion.AngleAxis(Random.value * 360.0f, Vector3.up);

                // Gets the objects to place from the object pool.
                GameObject pawn = ObjectPool.instance.GetPooledObject();
                if (pawn != null)
                {
                    pawn.transform.position = samplePos;
                    pawn.transform.rotation = rotation;
                    pawn.transform.SetParent(pawnDirectoryInstance.transform);
                    pawn.SetActive(true);
                    placed++;
                }
            }
        }
        
        // Logging 
        stopwatch.Stop();
        Debug.LogFormat("[WorldGenerator::PlaceAI] Execution time: {0}ms", stopwatch.ElapsedMilliseconds);
    }
}