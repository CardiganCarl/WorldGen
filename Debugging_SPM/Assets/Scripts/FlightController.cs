using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlightController : MonoBehaviour
{
    [SerializeField]
    private float speed;
    [SerializeField]
    private float turnRate;
    [SerializeField]
    private float propellerRotation;
    [SerializeField]
    private GameObject propeller;

    // Update is called once per frame
    void Update()
    {
        ApplyMovement();
        RotatePropeller();
    }

    private void ApplyMovement()
    {
        Vector3 newPosition = transform.position;
        newPosition += transform.forward * speed * Time.deltaTime;
        
        Vector3 newRotation = transform.rotation.eulerAngles;
        
        if (Input.GetAxis("Vertical") != 0)
        {
            newPosition.y += Input.GetAxis("Vertical") * speed * Time.deltaTime;
        }

        if (Input.GetAxis("Horizontal") != 0)
        {
            newRotation.y += Input.GetAxis("Horizontal") * turnRate * Time.deltaTime;
        }
        
        transform.position = newPosition;
        transform.rotation = Quaternion.Euler(newRotation);
    }

    private void RotatePropeller()
    {
        propeller.transform.Rotate(propellerRotation * Time.deltaTime, 0, 0);
    }
}
