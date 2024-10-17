
using UnityEngine;

public class Launch : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        var confLevels = Config.ConfLevels.Get(1);
        Debug.Log(confLevels.id);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
