/**************************************************
 *
 * Copyright (c) 2024 [WangJian] , Licensed under the MIT License.  See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
using System;
namespace Core.Singleton
{
    public abstract class Singleton<T> where T : class, new()
    {
        private static T m_instance;
        public static T Instance
        {
            get
            {
                if (Singleton<T>.m_instance == null)
                {
                    Singleton<T>.m_instance = Activator.CreateInstance<T>();
                    if (Singleton<T>.m_instance != null)
                    {
                        (Singleton<T>.m_instance as Singleton<T>).Init();
                    }
                }

                return Singleton<T>.m_instance;
            }
        }

        public static void Release()
        {
            if (Singleton<T>.m_instance != null)
            {
                Singleton<T>.m_instance = (T)((object)null);
            }
        }

        public virtual void Init()
        {

        }

        public abstract void Dispose();

    }
}
