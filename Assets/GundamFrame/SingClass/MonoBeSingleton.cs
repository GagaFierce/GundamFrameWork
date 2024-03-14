using UnityEngine;

namespace GundamFrame
{
    public abstract class MonoBeSingleton<T> : MonoBehaviour where T : MonoBeSingleton<T>
    {
        private static T instance;

        /// <summary>
        /// The static reference to the instance.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<T>();
                    if (instance == null)
                    {
                        GameObject singletonObject = new GameObject(typeof(T).Name);
                        instance = singletonObject.AddComponent<T>();
                    }
                }

                return instance;
            }
        }

        /// <summary>
        /// Gets whether an instance of this singleton exists.
        /// </summary>
        public static bool InstanceExists => instance != null;

        /// <summary>
        /// Gets the instance of this singleton, and returns true if it is not null.
        /// Prefer this whenever you would otherwise use InstanceExists and Instance together.
        /// </summary>
        public static bool TryGetInstance(out T result)
        {
            result = instance;

            return result != null;
        }

        /// <summary>
        /// Awake method to associate singleton with instance.
        /// </summary>
        protected virtual void Awake()
        {
            if (instance != null)
            {
                Debug.LogWarningFormat("Trying to create a second instance of {0}", typeof(T));
                Destroy(gameObject);
            }
            else
            {
                instance = (T)this;
            }

            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// OnDestroy method to clear singleton association.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}