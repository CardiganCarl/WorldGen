using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    void Awake()
    {
        PlaceAI();
    }

    [ContextMenu("Generate New World")]
    public void BuildWorld()
    {
        walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallMesh = walls.GetComponent<MeshFilter>().sharedMesh;

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
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sample = 0.0f;
                // NOTE: Add a solid border and generate inside with perlin noise.
                if (x > 0 && y > 0 && x < width - 1 && y < height - 1)
                {
                    sample = Mathf.PerlinNoise((float)x / width * noiseScale, (float)y / height * noiseScale);
                }

                if (sample < percentageBlocks)
                {
                    // NOTE: Larger noice gives higher blocks.
                    float obstacleHeight = 3.0f - sample * 2.0f;

                    walls.transform.position = new Vector3(x, obstacleHeight * 0.5f, y);
                    walls.transform.localScale = new Vector3(1.0f, obstacleHeight, 1.0f);

                    walls.isStatic = true;

                    // Sets the values for one of the meshes.
                    CombineInstance instance = new CombineInstance();
                    instance.mesh = wallMesh;
                    instance.transform = walls.transform.localToWorldMatrix;
                    meshes.Add(instance);
                }
            }
        }

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
