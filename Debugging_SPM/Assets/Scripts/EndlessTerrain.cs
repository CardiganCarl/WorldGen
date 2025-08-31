using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.VisualScripting;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Update = UnityEngine.PlayerLoop.Update;

public class EndlessTerrain : MonoBehaviour
{
    public Transform viewer;
    
    private static float viewDistance;
    private static Vector2 viewerPosition;
    private const int chunkSize = 100;
    private int visibleChunks;
    private WorldGenerator worldGenerator;
    
    Dictionary<Vector2, TerrainChunk> chunks = new();
    List<TerrainChunk> previouslyVisibleChunks = new();
    
    [SerializeField]
    private LODInfo[] lodDistances;
    
    // Start is called before the first frame update
    void Start()
    {
        viewDistance = lodDistances[^1].visibleThreshold;
        visibleChunks = Mathf.RoundToInt(viewDistance / chunkSize);
        worldGenerator = GetComponent<WorldGenerator>();
    }

    void Update()
    {
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z);
        UpdateVisibleChunks();
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
                    TerrainChunk chunk = new TerrainChunk(viewedChunkCoord, chunkSize, lodDistances, worldGenerator);
                    chunks.Add(viewedChunkCoord, chunk);
                }
            }
        }
    }

    public class TerrainChunk
    {
        private const int maxChunksPerFrame = 2;
        
        private GameObject currentTerrainObject;
        private GameObject lastTerrainObject;
        private Vector2 position;
        private Bounds bounds;
        private GameObject[] terrainObjects;
        private ConcurrentQueue<MeshInfo> meshInfoToProcess = new();
        
        public bool calculatingTerrain { get; private set; }
        private WorldGenerator generator;

        private int currentLOD = -1;
        
        // TODO: Change how this works. Weird to have two instances of lodDistances.
        private LODInfo[] lodDistances;
        
        public TerrainChunk(Vector2 coord, int size, LODInfo[] lodDistances, WorldGenerator generator)
        {
            position = coord * size;
            bounds = new Bounds(position, Vector2.one * size);
            terrainObjects = new GameObject[lodDistances.Length];
            this.generator = generator;
            this.lodDistances = lodDistances;
            
            SetVisible(false);
        }

        // Update visibility.
        public void UpdateChunk()
        {
            bool done = true;
            foreach (GameObject t in terrainObjects)
            {
                if (!t)
                {
                    done = false;
                }

                calculatingTerrain = done;
            }

            if (!calculatingTerrain)
            {
                GenerateTerrain();
            }
            
            float viewerDistance = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
            bool visible = viewerDistance <= viewDistance;
            
            SetVisible(visible);

            if (visible)
            {
                // Check which LOD we should display based on distance to player.
                int lod = lodDistances.Length - 1;
                for (int i = 0; i < lodDistances.Length; i++)
                {
                    if (viewerDistance <= lodDistances[i].visibleThreshold)
                    {
                        lod = i;
                        break;
                    }
                }

                // Mesh doesn't yet exist, so generate it in background.
                if (!terrainObjects[lod])
                { 
                    meshInfoToProcess.Enqueue(generator.GenerateMeshInfo(position, lodDistances[lod].LOD));
                    return;
                }
                currentTerrainObject = terrainObjects[lod];
                
                int highestAvailableLOD = lodDistances.Length - 1;
                for (int i = 0; i <= lod; i++)
                {
                    if (terrainObjects[i])
                    {
                        highestAvailableLOD = i;
                        break;
                    }
                }

                // Display the currently highest available LOD.
                if (currentLOD != highestAvailableLOD)
                {
                    currentLOD = highestAvailableLOD;
                    currentTerrainObject = terrainObjects[currentLOD];
                }
            }
        }
        
        private void GenerateTerrain()
        {
            int count = meshInfoToProcess.Count;
            int processedChunks = 0;
        
            for (int i = 0; i < count && processedChunks < maxChunksPerFrame; i++)
            {
                if (meshInfoToProcess.TryDequeue(out MeshInfo meshInfo))
                {
                    if (meshInfo.vertexPosHandle.IsCompleted)
                    {
                        // Take a meshInfo from the queue and process it into a terrain object.
                        GameObject terrain = generator.GenerateTerrain(meshInfo);
                        terrain.SetActive(false);

                        int lod = 0;
                        
                        for (int j = 0; j < lodDistances.Length; j++)
                        {
                            if (meshInfo.LOD == lodDistances[j].LOD)
                            {
                                lod = j;
                            }
                        }
                        
                        terrainObjects[lod] = terrain;
                        processedChunks++;
                    }
                    else
                    {
                        // Place the chunk at the end of the queue to check next frame.
                        meshInfoToProcess.Enqueue(meshInfo);
                    }
                }
            }
        }

        public void SetVisible(bool visible)
        {
            if (!currentTerrainObject)
            {
                return;
            }
            
            if (currentTerrainObject != lastTerrainObject)
            {
                if (lastTerrainObject)
                {
                    lastTerrainObject.SetActive(false);
                }
                
                lastTerrainObject = currentTerrainObject;
            }
            currentTerrainObject.SetActive(visible);
        }

        public bool IsVisible()
        {
            if (!currentTerrainObject)
            {
                return false;
            }
            return currentTerrainObject.activeSelf;
        }
    }

    [System.Serializable]
    public struct LODInfo
    {
        public int LOD;
        public float visibleThreshold;
    }
}
