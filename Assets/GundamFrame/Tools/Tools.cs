using UnityEngine;

namespace GundamFrame.Tools
{
    public static class Tools
    {
        public static T LoadResourcesMethod<T>(string path) where T : Object
        {
            return Resources.Load<T>(path);
        }
    }
}
