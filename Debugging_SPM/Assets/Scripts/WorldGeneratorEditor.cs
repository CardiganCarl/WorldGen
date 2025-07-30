using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldGenerator))]
public class WorldGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WorldGenerator worldGenerator = (WorldGenerator)target;

        if (DrawDefaultInspector())
        {
            if (worldGenerator.autoUpdate)
            {
                worldGenerator.GenerateTerrain();
            }
        }

        if (GUILayout.Button("Generate Terrain"))
        {
            worldGenerator.GenerateTerrain();
        }
    }
}
