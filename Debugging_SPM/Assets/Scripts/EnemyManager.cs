using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Profiling;

[BurstCompile]
public class EnemyManager : MonoBehaviour
{
    public float movementSpeed = 5.0f;
    public float rotationSpeed = 4.0f;
    public float sight = 400.0f;
    public float visionAngle = 70.0f;
    public float enemyActionDistance = 50.0f;

    public GameObject enemyPrefab;

    [Range(3, 21)]
    public int maxTraces = 7;

    public bool debugDraw = false;

    private HashSet<Transform> enemies;
    private Transform playerTransform;

    private Transform[] enemyTransforms;
    private TransformAccessArray enemiesAccessArray;

    private NativeArray<RaycastCommand> raycastCommands;
    private NativeArray<RaycastHit> raycastHits;

    private float enemyHeight;
    private float enemyRadius;

    //private JobHandle jobHandle;
    //private QueryParameters queryParameters;

    private Camera mainCamera;

    private Vector3[] movementVectors;

    private Dictionary<int, int> tracesPerEnemy;

    private void Awake()
    {
        raycastCommands = new NativeArray<RaycastCommand>(maxTraces * 500, Allocator.Persistent);
        raycastHits = new NativeArray<RaycastHit>(maxTraces * 500, Allocator.Persistent);
        enemyTransforms = new Transform[500];
        mainCamera = Camera.main;
        
        movementVectors = new Vector3[500];

        tracesPerEnemy = new Dictionary<int, int>();
        
        enemiesAccessArray = new TransformAccessArray(enemyTransforms);
    }

    private void Start()
    {
        GameObject[] enemyObjects = GameObject.FindGameObjectsWithTag("Enemy");
        enemies = enemyObjects.Select(obj => obj.transform).ToHashSet();
        // Need to fix this.
        enemyTransforms = enemies.ToArray();

        playerTransform = GameObject.Find("Player").GetComponent<Transform>();

        enemyHeight = enemyPrefab.GetComponent<CapsuleCollider>().height;
        enemyRadius = enemyPrefab.GetComponent<CapsuleCollider>().radius;
    }

    void Update()
    {
        // Add raycasts to batch for each enemy.
        for (int i = 0; i < 500; i++)
        {
            ScheduleRaycasts(enemyTransforms[i], i);
            // CalculateMovementVector(enemyTransforms[i], i);
        }
        
        // Create a job from all the raycasts.
        JobHandle raycastJobHandle = RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, 1);
        raycastJobHandle.Complete();

        // Apply movement based on the raycast commands for each enemy.
        Transform t;
        for (int i = 0; i < 500; i++)
        {
            t = enemyTransforms[i];
            movementVectors[i] = CalculateMovement(t, i);
            Vector3 movementVector = movementVectors[i];
            // Vector3 movementVector = CalculateMovementVector(t, i);
            if (movementVector != Vector3.zero)
            {
                // Apply movement.
                t.forward = Vector3.Lerp(t.forward, movementVector.normalized, Time.deltaTime * rotationSpeed);
                t.position += t.forward * movementSpeed * Time.deltaTime;
                
                ResolveCollisions(t);
            }

            // ResolveCollisions(t);
        }
        // var job = new CheckCollisionsJob
        // {
        //     EnemyHeight = enemyHeight,
        //     EnemyRadius = enemyRadius
        // };
        //     
        // JobHandle jobHandle = job.Schedule(enemiesAccessArray);
        // jobHandle.Complete();
    }

    // Add raycasts to batch job for this frame.
    public void ScheduleRaycasts(Transform t, int enemyIndex)
    {
        Vector2 screenPosition = mainCamera.ScreenToWorldPoint(t.position);
        int traces = maxTraces;

        // The enemy is inactive and in range. Set it as active.
        if (Vector3.Distance(playerTransform.position, t.position) <= enemyActionDistance && !t.gameObject.activeSelf)
        {
            t.gameObject.SetActive(true);
        }
        // Enemies far enough away don't move at all.
        else if (Vector3.Distance(playerTransform.position, t.position) > enemyActionDistance)
        {
            t.gameObject.SetActive(false);
            tracesPerEnemy[enemyIndex] = 0;
            return;
        }
        // Enemy is outside of screen space, fewer traces needed.
        else if (screenPosition.x < 0 || screenPosition.y < 0 || screenPosition.x > mainCamera.pixelWidth || screenPosition.y > mainCamera.pixelHeight)
        {
            traces = Mathf.Clamp(traces / 2, 3, maxTraces);
        }
        
        // Add the amount of traces this enemy will have this frame.
        tracesPerEnemy[enemyIndex] = traces;

        // Go through all raycasts for this enemy.
        for (int i = 0; i < traces; i++)
        {
            int index = enemyIndex * maxTraces + i;
            
            float stepAngle = (visionAngle * 2.0f) / (traces - 1);
            float angle = (90.0f + visionAngle - (i * stepAngle)) * Mathf.Deg2Rad;
            Vector3 direction = t.TransformDirection(new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)));
            
            // Create a raycast command and add it to be processed later.
            raycastCommands[index] = new RaycastCommand(t.position, direction, QueryParameters.Default);
        }
    }

    // Calculate movement direction for this enemy.
    public Vector3 CalculateMovement(Transform t, int index)
    {
        Vector3 movementVector = Vector3.zero;
        
        // Loop through completed raycasts for this enemy.
        for (int i = index * maxTraces; i < index * maxTraces + tracesPerEnemy[index]; i++)
        {
            RaycastHit hit = raycastHits[i];
            Vector3 direction = raycastCommands[i].direction;
            
            if (hit.collider)
            {
                movementVector += direction * hit.distance;

                if (debugDraw)
                {
                    Debug.DrawLine(t.position, t.position + direction * hit.distance);
                    
                    Vector3 Perp = Vector3.Cross(direction, Vector3.up);
                    Debug.DrawLine(hit.point + Perp, hit.point - Perp, Color.red);
                }

                if (hit.collider.gameObject.CompareTag("Player"))
                {
                    movementVector = direction;
                    return movementVector;
                }
            }
            else
            {
                movementVector += direction * sight;
                
                if (debugDraw)
                {
                    Debug.DrawLine(transform.position, transform.position + direction * sight);
                }
            }
        }
        return movementVector;
    }

    public Vector3 CalculateMovementVector(Transform t, int enemyIndex)
    {
        Vector3 movementVector = Vector3.zero;
        
        Vector2 screenPosition = mainCamera.ScreenToWorldPoint(t.position);
        int traces = maxTraces;

        // The enemy is inactive and in range. Set it as active.
        if (Vector3.Distance(playerTransform.position, t.position) <= enemyActionDistance && !t.gameObject.activeSelf)
        {
            t.gameObject.SetActive(true);
        }
        // Enemies far enough away don't move at all.
        else if (Vector3.Distance(playerTransform.position, t.position) > enemyActionDistance)
        {
            t.gameObject.SetActive(false);
            return movementVector;   
        }
        // Enemy is outside of screen space, less traces needed.
        else if (screenPosition.x < 0 || screenPosition.y < 0 || screenPosition.x > mainCamera.pixelWidth || screenPosition.y > mainCamera.pixelHeight)
        {
            traces = Mathf.Clamp(traces / 2, 3, maxTraces);
        }

        // Go through all raycasts from last frame.
        for (int i = 0; i < traces; i++)
        {
            int index = enemyIndex * 21 + i;
            
            float stepAngle = (visionAngle * 2.0f) / (traces - 1);
            float angle = (90.0f + visionAngle - (i * stepAngle)) * Mathf.Deg2Rad;
            Vector3 direction = t.TransformDirection(new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)));
            
            raycastCommands[enemyIndex] = new RaycastCommand(t.position, direction, QueryParameters.Default);
            
            if (Physics.Raycast(t.position, direction, out RaycastHit hitInfo, sight))
            {
                movementVector += direction * hitInfo.distance;
            
                if (debugDraw)
                {
                    Debug.DrawLine(t.position, t.position + direction * hitInfo.distance);
            
                    Vector3 Perp = Vector3.Cross(direction, Vector3.up);
                    Debug.DrawLine(hitInfo.point + Perp, hitInfo.point - Perp, Color.red);
                }
            
                if (hitInfo.transform.gameObject.CompareTag("Player"))
                {
                    movementVector = direction;
                    break;
                }
            }
            else
            {
                movementVector += direction * sight;
            
                if (debugDraw)
                {
                    Debug.DrawLine(transform.position, transform.position + direction * sight);
                }
            }

            // ControlEnemyJob job = new ControlEnemyJob()
            // {
            //     Traces = traces,
            //     VisionAngle = visionAngle,
            //     Sight = sight,
            //     DebugDraw = debugDraw
            // };
            
            // JobHandle jobHandle = job.Schedule(accessArray);
            // jobHandle.Complete();
            
            // raycastCommands[index] = new RaycastCommand(t.position, direction, sight, LayerMask.GetMask("Default"));
        }
        
        // JobHandle raycastJobHandle = RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, 1);
        // raycastJobHandle.Complete();
        //
        // if (raycastHits.Length > 0)
        // {
        //     for (int i = 0; i < raycastHits.Length; i++)
        //     {
        //         RaycastHit hit = raycastHits[i];
        //         
        //         movementVector += raycastCommands[i].direction * hit.distance;
        //     
        //         if (debugDraw)
        //         {
        //             Debug.DrawLine(t.position, t.position + raycastCommands[i].direction * hit.distance);
        //     
        //             Vector3 Perp = Vector3.Cross(raycastCommands[i].direction, Vector3.up);
        //             Debug.DrawLine(hit.point + Perp, hit.point - Perp, Color.red);
        //         }
        //     
        //         if (hit.collider && hit.collider.gameObject.CompareTag("Player"))
        //         {
        //             movementVector = raycastCommands[i].direction;
        //             break;
        //         }
        //     }
        // }
        
        return movementVector;
    }

    public void ResolveCollisions(Transform t)
    {
        List<Collider> colliders = Physics.OverlapCapsule(
            t.position + enemyHeight * 0.5f * Vector3.up,
            t.position - enemyHeight * 0.5f * Vector3.up,
            enemyRadius)
            .Where(c => c.transform != t)
            .ToList();

        if (colliders.Count > 0)
        {
            if (Physics.ComputePenetration(
                t.gameObject.GetComponent<CapsuleCollider>(),
                t.position,
                t.rotation,
                colliders[0],
                colliders[0].transform.position,
                colliders[0].transform.rotation,
                out Vector3 direction,
                out float distance))
            {
                t.position += direction * distance;
            }
        }

        //CapsuleCollider enemyCollisions = transform.GetComponent<CapsuleCollider>();
        //if (!enemyCollisions) return;

        //Collider[] colliders = Physics.OverlapCapsule(
        //    transform.position + enemyHeight * 0.5f * Vector3.up,
        //    transform.position - enemyHeight * 0.5f * Vector3.up,
        //    enemyRadius);

        //foreach (Collider collider in colliders)
        //{
        //    if (collider.transform != transform)
        //    {
        //        if (Physics.ComputePenetration(
        //            enemyCollisions,
        //            transform.position,
        //            transform.rotation,
        //            collider,
        //            collider.transform.position,
        //            collider.transform.rotation,
        //            out Vector3 direction,
        //            out float distance))
        //        {
        //            transform.position += direction * distance;
        //        }
        //    }
        //}
    }
}

[BurstCompile]
struct CheckCollisionsJob : IJobParallelForTransform
{
    [ReadOnly] public float EnemyHeight;
    [ReadOnly] public float EnemyRadius;
    public void Execute(int index, TransformAccess transform)
    {
        // List<Collider> colliders = Physics.OverlapCapsule(
        //         transform.position + EnemyHeight * 0.5f * Vector3.up,
        //         transform.position - EnemyHeight * 0.5f * Vector3.up,
        //         EnemyRadius)
        //     .Where(c => c.transform != transform)
        //     .ToList();
        //
        // if (colliders.Count > 0)
        // {
        //     if (Physics.ComputePenetration(
        //             transform.gameObject.GetComponent<CapsuleCollider>(),
        //             transform.position,
        //             transform.rotation,
        //             colliders[0],
        //             colliders[0].transform.position,
        //             colliders[0].transform.rotation,
        //             out Vector3 direction,
        //             out float distance))
        //     {
        //         transform.position += direction * distance;
        //     }
        // }
    }
}
