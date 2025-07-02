using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Random = UnityEngine.Random;

[BurstCompile]
public class WorldGenerator : MonoBehaviour
{
    [Header("World")]
    public int width = 200;
    public int height = 200;
    public float percentageBlocks = 0.35f;
    public float noiseScale = 15.0f;

    [Header("Materials")]
    public Material planeMaterial;
    public Material obstacleMaterial;

    [Header("AI")]
    public GameObject pawnDirectoryInstance;
    public GameObject AIPrefab;
    public int numAI = 500;

    GameObject walls;
    Mesh wallMesh;
    
    // private Transform[] wallTransforms;
    private NativeArray<float> samples;
    private NativeArray<float3> positions;
    private NativeArray<float3> scales;
    
    void Awake()
    {
        PlaceAI();
    }

    [ContextMenu("Generate New World")]
    public void BuildWorld()
    {
        walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallMesh = walls.GetComponent<MeshFilter>().sharedMesh;
        samples = new NativeArray<float>(height * width, Allocator.Persistent);
        positions = new NativeArray<float3>(height * width, Allocator.Persistent);
        scales = new NativeArray<float3>(height * width, Allocator.Persistent);
        
        // Log execution time.
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        // NOTE: Clean up previous world so we don't double up.
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }

        // Create ground plane.
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        // NOTE: Planes are scaled 10x.
        plane.transform.localScale = new Vector3(width, 10.0f, height) * 0.1f;
        // NOTE: Origo of a plane is in the middle. Move lower left corner to zero.
        plane.transform.position = new Vector3(width * 0.5f - 0.5f, 0.0f, height * 0.5f - 0.5f);
        plane.GetComponent<Renderer>().material = planeMaterial;
        plane.transform.SetParent(transform);

        Mesh mesh = new Mesh();
        List<CombineInstance> meshes = new List<CombineInstance>();

        // Create obstacles/geometry.
        GenerateWallsJob job = new GenerateWallsJob
        {
            Samples = samples,
            Positions = positions,
            Scales = scales,
            Width = width,
            Height = height,
            NoiseScale = noiseScale,
            PercentageBlocks = percentageBlocks,
        };
        JobHandle handle = job.Schedule(width * height, 64);
        handle.Complete();
        
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                if (samples[i * width + j] >= percentageBlocks)
                {
                    continue;
                }
                
                CombineInstance ci = new CombineInstance();
                ci.mesh = wallMesh;
                Matrix4x4 matrix = Matrix4x4.TRS(positions[i * width + j], Quaternion.identity, scales[i * width + j]);
                ci.transform = matrix;
                meshes.Add(ci);
            }
        }
        
        positions.Dispose();
        scales.Dispose();
        samples.Dispose();

        // Combine all meshes into one.
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.CombineMeshes(meshes.ToArray());
        walls.GetComponent<MeshFilter>().mesh = mesh;

        // Set all walls to static.
        walls.isStatic = true;
        walls.GetComponent<Renderer>().material = obstacleMaterial;

        walls.transform.SetParent(transform);
        walls.transform.position = Vector3.zero;

        // Add mesh collider.
        DestroyImmediate(walls.GetComponent<BoxCollider>());
        walls.AddComponent<MeshCollider>();
        
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
        [ReadOnly] public float NoiseScale;
        [ReadOnly] public float PercentageBlocks;
        public void Execute(int index)
        {
            x = index % Width;
            y = index / Width;
            
            float sample = PerlinNoise.CalculateNoise((float)x / Width * NoiseScale, (float)y / Height * NoiseScale);
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