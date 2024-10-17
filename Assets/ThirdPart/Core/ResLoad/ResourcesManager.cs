/*****************************************************************************
 * 
 * author       : Wangjian
 * create date  : 2024 10 17
 * description  : ThirdPart.Core.ResLoad ResourcesManager ResourcesManagerImpl
 * 
 *****************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Singleton;
using UnityEngine;
using UnityEngine.U2D;

namespace ThirdPart.Core.ResLoad
{

    public partial interface ResourcesManager
    {
        public enum eResType
        {
            ERT_GAMEOBJECT,
            ERT_TEXTURE,
            ERT_SPRITE,
            ERT_MATERIAL,
            ERT_TEXT,
            ERT_SPRITEATLAS,
            ERT_AUDIOCLIP,
        };
        void LoadResources(string resname, Action<string, UnityEngine.Object> callback, eResType type, bool bAsync = false);
        void CreateGameObject(string resname, Action<string, UnityEngine.Object> callback, bool bAsync = false);
        GameObject CreateGameObject(string resname, GameObject parent = null);
        Texture2D CreateTexture(string resname);
        Sprite CreateSprite(string resname);
        Sprite CreateSpriteAtlas(string resname, string spritename);
        Material CreateMaterial(string resname);
        Material CreateMaterialInstance(string resname);
        //Dictionary<string, string> SpriteToAtlasMappings { get; }
        AudioClip CreateAudio(string resname);
        TextAsset CreateText(string resname);
        string GetText(string resname);

        void UnloadUnusedResources();
        void UnloadResources(UnityEngine.Object goTemplate);
    }

    //==========================================================================================================================

    public class ResourcesManagerImpl : MonoSingleton<ResourcesManagerImpl>, ResourcesManager
    {
        //private static ResourcesManager mSingleton = null;
        //public static ResourcesManager Instance
        //{
        //    get
        //    {
        //        if (mSingleton == null)
        //        {
        //            GameObject go = new GameObject("ResourcesManager");
        //            mSingleton = go.AddComponent<ResourcesManagerImpl>();
        //            DontDestroyOnLoad(go);
        //            //go.hideFlags = HideFlags.HideInHierarchy;
        //        }
        //        return mSingleton;
        //    }
        //}

        //private void Awake()
        //{
        //    //LoadSpriteAtlasMapping();
        //}

        //public void OnDestroy()
        //{
        //    //mSingleton = null;
        //}

        struct LoadResInfo
        {
            public ResourceRequest resReq;
            public string resname;
            public ResourcesManager.eResType restype;
            public Action<string, UnityEngine.Object> callback;
        }

        struct LoadedResInfo
        {
            public string resname;
            public UnityEngine.Object gotemplate;
            public ResourcesManager.eResType restype;
            public float durationtime;
        }

        Dictionary<string, LoadedResInfo> mLoadedAssetList = new Dictionary<string, LoadedResInfo>();
        Dictionary<string, LoadedResInfo> mUnloadWaitingAssetList = new Dictionary<string, LoadedResInfo>();
        List<LoadResInfo> mLoadingList = new List<LoadResInfo>();
        float mDurationTimeMax = 2.0f;


        void OnEnable()
        {
            //SpriteAtlasManager.atlasRequested += RequstAtlas;
        }

        void OnDisable()
        {
            //SpriteAtlasManager.atlasRequested -= RequstAtlas;
        }

        //void RequstAtlas(string atlasname, Action<SpriteAtlas> callback)
        //{
        //    string resname = "";
        //    foreach (var v in SpriteToAtlasMappings)
        //    {
        //        if (v.Value.EndsWith(atlasname))
        //        {
        //            resname = v.Value;
        //            break;
        //        }
        //    }

        //    SpriteAtlas spriteAtlas = null;
        //    LoadResources(resname, (n, g) => { spriteAtlas = g as SpriteAtlas; }, ResourcesManager.eResType.ERT_SPRITEATLAS, false);
        //    if (spriteAtlas)
        //    {
        //        callback(spriteAtlas);
        //    }
        //}

        void AddLoadedRes(string resname, UnityEngine.Object goTemplate, ResourcesManager.eResType restype)
        {
            if(!goTemplate)
                Debug.LogError($"ResourcesManager - the resources not fount | resname = {resname}");

            if (restype == ResourcesManager.eResType.ERT_GAMEOBJECT || restype ==ResourcesManager.eResType.ERT_SPRITEATLAS)
            {
                if (null != goTemplate && null != resname && resname != "")
                {
                    if (mLoadedAssetList.ContainsKey(resname))
                        Debug.LogError($"ResourcesManager - the resources with the same name in the cache | resname = {resname}");

                    LoadedResInfo info = new LoadedResInfo();
                    info.resname = resname;
                    info.gotemplate = goTemplate;
                    info.restype = restype;
                    info.durationtime = mDurationTimeMax;
                    mLoadedAssetList[resname] = info;
                }
            }
        }

        void InstObject(UnityEngine.Object goTemplate, ResourcesManager.eResType restype, Action<string, UnityEngine.Object> callback, string resname)
        {
            switch (restype)
            {
                case ResourcesManager.eResType.ERT_GAMEOBJECT:
                    {
                        GameObject go = InstGameObject(goTemplate);
                        callback.Invoke(resname, go);
                    }
                    break;
                case ResourcesManager.eResType.ERT_TEXTURE:
                    callback.Invoke(resname, goTemplate);
                    break;
                case ResourcesManager.eResType.ERT_SPRITE:
                    callback.Invoke(resname, goTemplate);
                    break;
                case ResourcesManager.eResType.ERT_MATERIAL:
                    callback.Invoke(resname, goTemplate);
                    break;
                case ResourcesManager.eResType.ERT_TEXT:
                    callback.Invoke(resname, goTemplate);
                    break;
                case ResourcesManager.eResType.ERT_SPRITEATLAS:
                    callback.Invoke(resname, goTemplate);
                    break;
                case ResourcesManager.eResType.ERT_AUDIOCLIP:
                    callback.Invoke(resname, goTemplate);
                    break;
            }
        }

        GameObject InstGameObject(UnityEngine.Object goTemplate)
        {
            GameObject go = (goTemplate != null ? Instantiate(goTemplate) as GameObject : null);
            return go;
        }

        UnityEngine.Object GetGoTemplate(string resname)
        {
            if (mLoadedAssetList.TryGetValue(resname, out LoadedResInfo info))
            {
                info.durationtime = mDurationTimeMax;
                mLoadedAssetList[resname] = info;
                return info.gotemplate;
            }

            if (mUnloadWaitingAssetList.TryGetValue(resname, out info))
            {
                mUnloadWaitingAssetList.Remove(resname);
                info.durationtime = mDurationTimeMax;
                mLoadedAssetList[resname] = info;
                return info.gotemplate;
            }

            return null;
        }
       
        public void LoadResources(string resname, Action<string, UnityEngine.Object> callback, ResourcesManager.eResType restype, bool bAsync = false)
        {
            if (resname != "" && resname != "" && null != callback)
            {
                UnityEngine.Object goTemplate = GetGoTemplate(resname);
                if (null != goTemplate)
                {
                    InstObject(goTemplate, restype, callback, resname);
                    return;
                }

                if (!bAsync)
                {
                    goTemplate = Resources.Load<UnityEngine.Object>(resname);
                    AddLoadedRes(resname, goTemplate, restype);
                    InstObject(goTemplate, restype, callback, resname);
                    return;
                }

                ResourceRequest resReq = Resources.LoadAsync<UnityEngine.Object>(resname);
                if (null != resReq)
                {
                    LoadResInfo info = new LoadResInfo();
                    info.resReq = resReq;
                    info.resname = resname;
                    info.restype = restype;
                    info.callback = callback;
                    mLoadingList.Add(info);
                }
            }
        }

        public void CreateGameObject(string resname, Action<string, UnityEngine.Object> callback, bool bAsync = false)
        {
            LoadResources(resname, callback,ResourcesManager.eResType.ERT_GAMEOBJECT, bAsync);
        }

        public GameObject CreateGameObject(string resname, GameObject parent = null)
        {
            GameObject go = null;
            CreateGameObject(resname, (n, g) => { go = g as GameObject; });
            if (go)
            {
                go.transform.SetParent(parent ? parent.transform : null);
                go.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
                go.transform.localPosition = new Vector3(0, 0, 0);
                go.transform.localEulerAngles = new Vector3(0.0f, 0.0f, 0.0f);
            }
            return go;
        }

        public Texture2D CreateTexture(string resname)
        {
            Texture2D tex = null;
            LoadResources(resname, (n, g) => { tex = g as Texture2D; }, ResourcesManager.eResType.ERT_TEXTURE, false);
            return tex;
        }

        public Sprite CreateSprite(string resname)
        {
            Texture2D tex = CreateTexture(resname);
            return tex ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0)) : null;
        }

        public Sprite CreateSpriteAtlas(string resname, string spritename)
        {
            SpriteAtlas spriteAtlas = null;
            LoadResources(resname, (n, g) => { spriteAtlas = g as SpriteAtlas; }, ResourcesManager.eResType.ERT_SPRITEATLAS, false);
            if (spriteAtlas)
            {
                Sprite sprite = spriteAtlas.GetSprite(spritename);
                return sprite;
            }
            return null;
        }

        public Material CreateMaterial(string resname)
        {
            Material mat = null;
            LoadResources(resname, (n, g) => { mat = g as Material; },ResourcesManager.eResType.ERT_MATERIAL, false);
            return mat;
        }

        public Material CreateMaterialInstance(string resname)
        {
            Material mat = new Material(CreateMaterial(resname));
            return mat;
        }

        public AudioClip CreateAudio(string resname)
        {
            AudioClip audioClip = null;
            LoadResources(resname, (n, g) => { audioClip = g as AudioClip; }, ResourcesManager.eResType.ERT_AUDIOCLIP, false);
            return audioClip;
        }

        public TextAsset CreateText(string resname)
        {
            throw new NotImplementedException();
        }

        // public TextAsset CreateText(string resname)
        // {
        //     TextAsset textAsset = null;
        //     LoadResources(resname, (n, g) => { textAsset = g as TextAsset; }, ResourcesManager.eResType.ERT_TEXT, false);
        //     return textAsset;
        // }

        public string GetText(string resname)
        {
            TextAsset textAsset = CreateText(resname);
            if (textAsset)
            {
                return textAsset.text;
            }
            return null;
        }

        public void UnloadUnusedResources()
        {
            List<string> loadedKeys = mLoadedAssetList.Keys.ToList();
            foreach (var key in loadedKeys)
            {
                var v = mLoadedAssetList[key];
                v.durationtime = mDurationTimeMax;
                mUnloadWaitingAssetList[key] = v;
            }
            mLoadedAssetList.Clear();
        }

        public void UnloadResources(UnityEngine.Object goTemplate)
        {
            Resources.UnloadAsset(goTemplate);
        }

        void Breathe(float deltaTime)
        {
            for (int i = mLoadingList.Count - 1; i >= 0; --i)
            {
                LoadResInfo info = mLoadingList[i];
                if (info.resReq.isDone)
                {
                    string resname = info.resReq.asset.name;
                    UnityEngine.Object goTemplate = info.resReq.asset;
                    AddLoadedRes(resname, goTemplate, info.restype);
                    InstObject(goTemplate, info.restype, info.callback, resname);
                    mLoadingList.RemoveAt(i);
                }
            }

            //List<string> loadedKeys = mLoadedAssetList.Keys.ToList();
            //foreach (var key in loadedKeys)
            //{
            //    var v = mLoadedAssetList[key];
            //    v.durationtime -= deltaTime;
            //    if (v.durationtime <= 0.0f)
            //    {
            //        mLoadedAssetList.Remove(key);
            //        v.durationtime = mDurationTimeMax;
            //        mUnloadWaitingAssetList[key] = v;
            //    }
            //    else 
            //    {
            //        mLoadedAssetList[key] = v;
            //    }
            //}

            bool bUnloadAsset = false;
            List<string> unloadedKeys = mUnloadWaitingAssetList.Keys.ToList();
            foreach (var key in unloadedKeys)
            {
                var v = mUnloadWaitingAssetList[key];
                v.durationtime -= deltaTime;
                if (v.durationtime <= 0.0f)
                {
                    mUnloadWaitingAssetList.Remove(key);
                    if (v.restype == ResourcesManager.eResType.ERT_SPRITEATLAS)
                        UnloadResources(v.gotemplate);
                    v.gotemplate = null;
                    bUnloadAsset = true;
                }
                else
                {
                    mUnloadWaitingAssetList[key] = v;
                }
            }

            if (bUnloadAsset)
            {
                Resources.UnloadUnusedAssets();
                //StopAllCoroutines();
                //StartCoroutine(DoUnloadUnusedAssets());
            }
        }

        IEnumerator DoUnloadUnusedAssets()
        {
            yield return new WaitForSeconds(2.0f);
            AsyncOperation ao = Resources.UnloadUnusedAssets();
            while (!ao.isDone)
            {
                yield return null;
            }
        }

        void Update()
        {
            Breathe(UnityEngine.Time.unscaledDeltaTime);
        }

        //public Dictionary<string, string> SpriteToAtlasMappings { get; private set; }
        //private void LoadSpriteAtlasMapping()
        //{
        //    string path = "Sprites/atlas_mapping";
        //    TextAsset mappingJson = Resources.Load<TextAsset>(path);

        //    SpriteToAtlasMappings = new Dictionary<string, string>();
        //    if (mappingJson != null)
        //    {
        //        SpriteAtlasMap map = JsonUtility.FromJson<SpriteAtlasMap>(mappingJson.text);
        //        foreach (var item in map.mappings)
        //        {
        //            if (SpriteToAtlasMappings.ContainsKey(item.spriteName))
        //            {
        //                Debug.LogError($"Duplicate sprite name {item.spriteName} in atlas mapping");
        //                continue;
        //            }
        //            SpriteToAtlasMappings[item.spriteName] = item.atlasName;
        //        }
        //    }
        //    else
        //    {
        //        Debug.LogError($"Error loading atlas mapping from {path}");
        //    }
        //}
    }
}

