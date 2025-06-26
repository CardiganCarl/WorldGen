using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[RequireComponent(typeof(CapsuleCollider))]
public class Player : MonoBehaviour
{
    public float movementSpeed = 10.0f;
    void Update()
    {
        Vector3 input =
            Input.GetAxisRaw("Horizontal") * Vector3.right +
            Input.GetAxisRaw("Vertical") * Vector3.forward;

        // Don't move if there aren't any inputs.
        if (input != Vector3.zero)
        {
            transform.position += input.normalized * movementSpeed * Time.deltaTime;

            ResolveCollisions();
        }
    }

    void ResolveCollisions()
    {
            // Resolve collisions
            List<Collider> colliders = Physics.OverlapCapsule(
                transform.position + GetComponent<CapsuleCollider>().height * 0.5f * Vector3.up,
                transform.position - GetComponent<CapsuleCollider>().height * 0.5f * Vector3.up,
                GetComponent<CapsuleCollider>().radius)
                .Where(c => c.transform != transform)
                .ToList();

            if (colliders.Count > 0)
            {
                if (Physics.ComputePenetration(
                    GetComponent<CapsuleCollider>(),
                    transform.position,
                    transform.rotation,
                    colliders[0],
                    colliders[0].transform.position,
                    colliders[0].transform.rotation,
                    out Vector3 direction,
                    out float distance))
                {
                    transform.position += direction * distance;
                }
            }
    }
}
