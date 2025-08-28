using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.VisualScripting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class EndlessTerrain : MonoBehaviour
{
    public const float viewDistance = 600;
    public Transform viewer;
    public int maxChunksPerFrame = 2;
    
    private static Vector2 viewerPosition;
    private const int chunkSize = 100;
    private int visibleChunks;
    private WorldGenerator worldGenerator;
    
    Dictionary<Vector2, TerrainChunk> chunks = new();
    List<TerrainChunk> previouslyVisibleChunks = new();
    
    private ConcurrentQueue<MeshInfo> meshInfoToProcess = new();
    private List<Vector2> positionsBeingCalculated = new();
    
    // Start is called before the first frame update
    void Start()
    {
        visibleChunks = Mathf.RoundToInt(viewDistance / chunkSize);
        worldGenerator = GetComponent<WorldGenerator>();
    }

    void Update()
    {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z);
        UpdateVisibleChunks();
        ProcessMeshes();
    }

    private void ProcessMeshes()
    {
        UnityEngine.Profiling.Profiler.BeginSample("LolXD");
        
        int count = meshInfoToProcess.Count;
        int processedChunks = 0;
        
        for (int i = 0; i < count && processedChunks < maxChunksPerFrame; i++)
        {
            if (meshInfoToProcess.TryDequeue(out MeshInfo meshInfo))
            {
                if (meshInfo.vertexPosHandle.IsCompleted)
                {
                    // Take a meshInfo from the queue and process it into a terrain object.
                    GameObject terrain = worldGenerator.GenerateTerrain(meshInfo);
                
                    Vector2 coord = new Vector2(terrain.transform.position.x, terrain.transform.position.z) / chunkSize;
                
                    // Create the chunk and add it to the current chunks.
                    TerrainChunk chunk = new TerrainChunk(coord, chunkSize, terrain);
                    chunks.Add(coord, chunk);
                
                    positionsBeingCalculated.Remove(coord);
                    processedChunks++;
                }
                else
                {
                    // Place the chunk at the end of the queue to check next frame.
                    meshInfoToProcess.Enqueue(meshInfo);
                }
            }
        }
        
        UnityEngine.Profiling.Profiler.EndSample();
    }

    void UpdateVisibleChunks()
    {
        // Hide all chunks.
        foreach (TerrainChunk t in previouslyVisibleChunks)
        {
            t.SetVisible(false);
        }
        previouslyVisibleChunks.Clear();
        
        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

        // Loop over all visible chunks.
        for (int yOffset = -visibleChunks; yOffset <= visibleChunks; yOffset++)
        {
            for (int xOffset = -visibleChunks; xOffset <= visibleChunks; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                if (chunks.ContainsKey(viewedChunkCoord))
                {
                    // Update visibility.
                    chunks[viewedChunkCoord].UpdateChunk();
                    if (chunks[viewedChunkCoord].IsVisible())
                    {
                        previouslyVisibleChunks.Add(chunks[viewedChunkCoord]);
                    }
                }
                else
                {
                    if (!positionsBeingCalculated.Contains(viewedChunkCoord))
                    {
                        // Create a new chunk.
                        meshInfoToProcess.Enqueue(worldGenerator.GenerateMeshInfo(viewedChunkCoord * chunkSize));
                        positionsBeingCalculated.Add(viewedChunkCoord);
                    }
                }
            }
        }
    }

    public class TerrainChunk
    {
        private GameObject meshObject;
        private Vector2 position;
        private Bounds bounds;
        
        public TerrainChunk(Vector2 coord, int size, GameObject terrain)
        {
            position = coord * size;
            bounds = new Bounds(position, Vector2.one * size);
            meshObject = terrain;
            
            SetVisible(false);
        }

        // Update visibility.
        public void UpdateChunk()
        {
            float viewerDistance = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
            bool visible = viewerDistance <= viewDistance;
            SetVisible(visible);
        }

        public void SetVisible(bool visible)
        {
            meshObject.SetActive(visible);
        }

        public bool IsVisible()
        {
            return meshObject.activeSelf;
        }
    }
}
