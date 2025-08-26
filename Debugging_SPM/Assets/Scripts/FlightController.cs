using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlightController : MonoBehaviour
{
    public float speed;
    public float turnRate;
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        ApplyMovement();
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
}
