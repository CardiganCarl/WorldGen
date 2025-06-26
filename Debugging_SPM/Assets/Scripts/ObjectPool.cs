using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    public static ObjectPool instance;
    public List<GameObject> objects;
    public GameObject enemy;
    public int amount;

    private bool initialized;

    private void Awake()
    {
        instance = this;
    }

    private void InitializePool()
    {
        objects = new List<GameObject>();
        GameObject tmp;
        for (int i = 0; i < amount; i++)
        {
            tmp = Instantiate(enemy);
            tmp.SetActive(false);
            objects.Add(tmp);
        }
        
        initialized = true;
    }

    public GameObject GetPooledObject()
    {
        if (!initialized)
        {
            InitializePool();
        }

        for (int i = 0; i < amount; i++)
        {
            if (!objects[i].activeInHierarchy)
            {
                return objects[i];
            }
        }
        return null;
    }
}
