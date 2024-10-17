/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.ResLoad LuaManager
 * 
 *****************************************************************************/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Core.ResLoad
{
    public interface LuaManager
    {
        public void InitLuaEnv();
        public void Launcher();
        public XLua.LuaTable GeLuaTable(string luafile);
        public void DisposeLuaEnv();
    }


    public class LuaManagerImpl : Singleton.MonoSingleton<LuaManagerImpl>, LuaManager
    {
        XLua.LuaEnv mLuaEnv = null;
        //public delegate string LoadResFunc(string filename);
        //LoadResFunc mLoadResFunc;
        Func<string, string> mLoadResFunc;

        Dictionary<string, string> mLuaBufferCache = new Dictionary<string, string>();
        Dictionary<string, string> mLuaFilePath = new Dictionary<string, string>();
        protected override void Init()
        {
            //LoadSpriteAtlasMapping();
            LoadLuaFiles();
            InitLuaEnv();
        }

        public void InitLuaEnv()
        {
            if (null == mLuaEnv)
            {
                mLuaEnv = new XLua.LuaEnv();
                mLuaEnv.AddLoader(CustomLoader);
            }
        }

        public void SetLoadResFunc(Func<string, string> loadResFunc)
        {
            mLoadResFunc = loadResFunc;
        }

        byte[] CustomLoader(ref string fileName)
        {
            string buffer = GetLuaBuffer(fileName);
            return Encoding.UTF8.GetBytes(buffer);
        }

        public void Launcher()
        {
            mLuaEnv?.DoString("require 'launcher'");
        }

        public XLua.LuaTable GeLuaTable(string luafilepath)
        {
            string luafilename = Path.GetFileNameWithoutExtension(luafilepath);
            string luabuffer = GetLuaBuffer(luafilename);
            if (!string.IsNullOrEmpty(luabuffer))
            {
                object[] objs = mLuaEnv.DoString(luabuffer, luafilename);
                if (objs.Length > 0)
                {
                    XLua.LuaTable luaTable = objs[0] as XLua.LuaTable;
                    return luaTable;
                }
            }
            else
            {
                Debug.LogError($"Load Lua File failed, fileName={luafilename}");
            }
            return null;
        }

        public void ClickedButton_Global(object obj, object param)
        {
            if (null != mLuaEnv)
            {
                Action<object, object> func = mLuaEnv.Global.Get<Action<object, object>>("clickedbutton_global");
                func?.Invoke(obj, param);
            }
        }

        public void DisposeLuaEnv()
        {
            mLuaEnv?.Dispose();
            mLuaEnv = null;
        }

        string GetLuaBuffer(string luafilename)
        {
            if (mLuaBufferCache.TryGetValue(luafilename, out string buffer))
            {
                return buffer;
            }

            // when we need to debug on device, we can copy the lua file to this directory
            string customLuaPath = string.Format("{0}/_lua/{1}.lua", Application.persistentDataPath, luafilename);
            if (File.Exists(customLuaPath))
            {
                buffer = File.ReadAllText(customLuaPath);
                return buffer;
            }

            // TODO 支持require带目录的lua文件
            // TODO UIPageImplForLua上不要使用Assets开始的路径，设备上不能用
            // TODO 把lua文件挪到Assets之外

            //buffer = mLoadResFunc?.Invoke(luafilename);
            //if (!string.IsNullOrEmpty(buffer))
            //{
            //    mLuaBufferCache[luafilename] = buffer;
            //}

            
            if (mLuaFilePath.TryGetValue(luafilename, out string filepath))
            {
#if UNITY_EDITOR
                buffer = File.ReadAllText(filepath);
#else
                string luaSrcDir = "Assets/Resources/Lua/";
                string luafile = "LuaPack/" + filepath.Substring(luaSrcDir.Length, filepath.Length - luaSrcDir.Length);
                TextAsset luaTextAsset = Resources.Load<TextAsset>(luafile);
                buffer = luaTextAsset.text;
                Resources.UnloadAsset(luaTextAsset);
#endif
                mLuaBufferCache[luafilename] = buffer;
            }

            return buffer;
        }


        // 映射表容器类
        [System.Serializable]
        public class FilesMappingContainer
        {
            public List<FilesMapping> mappings;
        }

        // 单个 Sprite 到图集的映射关系类
        [System.Serializable]
        public class FilesMapping
        {
            public string name;
            public string path;
        }

        private void LoadLuaFiles()
        {
            string path = "luafilesIndex";
            TextAsset mappingJson = Resources.Load<TextAsset>(path);
            if (mappingJson != null)
            {
                FilesMappingContainer mapping = JsonUtility.FromJson<FilesMappingContainer>(mappingJson.text);
                foreach (var v in mapping.mappings)
                {
                    mLuaFilePath[v.name] = v.path;
                }
            }
            else
            {
                Debug.LogError($"Error loading atlas mapping from {path}");
            }
            Resources.UnloadAsset(mappingJson);
        }

        void Update()
        {
            if (null != mLuaEnv)
            {
                System.Action func = mLuaEnv.Global.Get<System.Action>("update");
                func?.Invoke();
            }

        }
    }
}

