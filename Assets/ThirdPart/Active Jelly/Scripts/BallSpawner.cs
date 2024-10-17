using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    public GameObject ball;
    float time = 2f;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        time -= Time.deltaTime;
        if (time <= 0)
        {
            GameObject ballGameobject = Instantiate(ball, gameObject.transform.position, gameObject.transform.rotation);
            Destroy(ballGameobject, 4f);
            time = 2f;
        }
    }
}
