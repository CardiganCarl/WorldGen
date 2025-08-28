using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlightController : MonoBehaviour
{
    [SerializeField]
    private float speed;
    [SerializeField]
    private float horizontalTurnRate;
    [SerializeField]
    private float verticalTurnRate;
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
        transform.position = newPosition;
        
        if (Input.GetAxis("Vertical") != 0)
        {
            // newRotation.x += Input.GetAxis("Vertical") * speed * Time.deltaTime;
            transform.Rotate(Input.GetAxis("Vertical") * verticalTurnRate * Time.deltaTime, 0, 0);
        }

        if (Input.GetAxis("Horizontal") != 0)
        {
            // newRotation.z -= Input.GetAxis("Horizontal") * turnRate * Time.deltaTime;
            transform.Rotate(0, 0, -Input.GetAxis("Horizontal") * horizontalTurnRate * Time.deltaTime);
        }
        
        // transform.rotation = Quaternion.Euler(newRotation);
    }

    private void RotatePropeller()
    {
        propeller.transform.Rotate(propellerRotation * Time.deltaTime, 0, 0);
    }
}
