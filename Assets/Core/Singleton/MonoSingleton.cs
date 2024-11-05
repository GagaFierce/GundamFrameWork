/**************************************************
 *
 * Copyright (c) 2024 [WangJian] , Licensed under the MIT License.  See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
using UnityEngine;
namespace Core.Singleton
{

    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T mInstance = null;
        private static bool mIsDestoy = false;

        public static T Instance
        {
            get
            {
                if (mInstance == null && !mIsDestoy)
                {
                    GameObject go = new GameObject(typeof(T).Name);
                    mInstance = go.AddComponent<T>();
                }

                return mInstance;
            }
        }


        private void Awake()
        {
            if (mInstance != null && mInstance != this)
            {
                Destroy(mInstance.gameObject);
            }

            mIsDestoy = false;
            mInstance = this as T;
            DontDestroyOnLoad(gameObject);
            Init();
        }

        private void OnDestroy()
        {
            Dispose();
            mInstance = null;
            mIsDestoy = true;
        }

        protected virtual void Init()
        {

        }


        public virtual void Dispose()
        {

        }

    }
}
