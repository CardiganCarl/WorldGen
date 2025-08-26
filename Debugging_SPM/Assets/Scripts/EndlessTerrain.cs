using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class EndlessTerrain : MonoBehaviour
{
    public const float viewDistance = 300;
    public Transform viewer;
    public static Vector2 viewerPosition;
    
    private int chunkSize = 100;
    private int visibleChunks;
    private WorldGenerator worldGenerator;
    
    Dictionary<Vector2, TerrainChunk> chunks = new();
    List<TerrainChunk> previouslyVisibleChunks = new();
    
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
    }

    void UpdateVisibleChunks()
    {
        foreach (TerrainChunk t in previouslyVisibleChunks)
        {
            t.SetVisible(false);
        }
        previouslyVisibleChunks.Clear();
        
        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

        for (int yOffset = -visibleChunks; yOffset <= visibleChunks; yOffset++)
        {
            for (int xOffset = -visibleChunks; xOffset <= visibleChunks; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);

                if (chunks.ContainsKey(viewedChunkCoord))
                {
                    chunks[viewedChunkCoord].UpdateChunk();
                    if (chunks[viewedChunkCoord].IsVisible())
                    {
                        previouslyVisibleChunks.Add(chunks[viewedChunkCoord]);
                    }
                }
                else
                {
                    chunks.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, transform, worldGenerator));
                }
            }
        }
    }

    public class TerrainChunk
    {
        private GameObject meshObject;
        private Vector2 position;
        private Bounds bounds;
        
        public TerrainChunk(Vector2 coord, int size, Transform parent, WorldGenerator generator)
        {
            position = coord * size;
            bounds = new Bounds(position, Vector2.one * size);
            Vector3 positionV3 = new Vector3(position.x, 0, position.y);
            
            // meshObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            meshObject = generator.GenerateTerrain(position);
            
            meshObject.transform.position = positionV3;
            meshObject.transform.localScale = Vector3.one;
            meshObject.transform.parent = parent;
            
            SetVisible(false);
        }

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
