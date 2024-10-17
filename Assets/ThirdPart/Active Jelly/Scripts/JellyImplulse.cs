using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JellyImplulse : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnCollisionEnter(Collision collision)
    {
        var magnitude = 500;
        // calculate force vector
        var force = collision.gameObject.GetComponent<Rigidbody>().velocity;
        // normalize force vector to get direction only and trim magnitude
        force.Normalize();
        collision.gameObject.GetComponent<Rigidbody>().AddForce(force * magnitude);
    }
}
