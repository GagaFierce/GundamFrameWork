
using System;
using System.Collections.Generic;
namespace Config{
    public class ConfigFactory{
        private static ConfigFactory sInstance = null;
        public static ConfigFactory Instance{
            get{
                if (sInstance == null){
                    sInstance = new ConfigFactory();
                    sInstance.Init();
                }
                return sInstance;
            }
        }
        
                public void Init(bool clearCache = false){
                    ConfLevels.Init(clearCache);
                }
        
    }
}
        
