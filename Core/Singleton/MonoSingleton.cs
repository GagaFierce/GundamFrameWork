/**************************************************
 *
 * Copyright (c) 2024 WangJian 
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/

using UnityEngine;

namespace WFrameWork.Core.Singleton
{

    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T mInstance = null;
        private static bool mIsDestroy = false;

        public static T Instance
        {
            get
            {
                if (mInstance == null && !mIsDestroy)
                {
                    GameObject go = new GameObject(typeof(T).Name);
                    mInstance = go.AddComponent<T>();
                }

                return mInstance;
            }
        }


        private void Awake()
        {
            // A valid instance already exists: this component is a duplicate, so destroy the
            // newcomer (this), not the established instance that other code may already reference.
            if (mInstance != null && mInstance != this)
            {
                Destroy(gameObject);
                return;
            }

            mIsDestroy = false;
            mInstance = this as T;
            DontDestroyOnLoad(gameObject);
            Init();
        }

        private void OnDestroy()
        {
            // Only clear statics when the authoritative instance is being destroyed. A duplicate
            // destroyed in Awake must not null out the real instance or flip the destroyed flag.
            if (mInstance != this)
            {
                return;
            }

            Dispose();
            mInstance = null;
            mIsDestroy = true;
        }

        protected virtual void Init()
        {

        }


        public virtual void Dispose()
        {

        }

    }
}
