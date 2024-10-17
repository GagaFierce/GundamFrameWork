using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

public class DeleteAllAtlas : EditorWindow
{
    [MenuItem("图集工具/图集管理/一键删除所有图集")]
    private static void Delete()
    {
        // CheckAssetAtlas("Assets/ArtLibrary/Resources/Sprites");
        DeleteAtlasAssets("Assets/AddressableRes/Texture/UI");
        ClearNonReferenceAtlasCache();
    }

    /// <summary>
    /// 删除文件夹下的所有图集资源
    /// </summary>
    /// <param name="relativePath">相对于Assets文件夹的路径</param>
    static void DeleteAtlasAssets(string relativePath)
    {
        CheckAssetAtlas(relativePath);

        foreach (var item in atlasDatas)
        {
            string assetPath = AssetDatabase.GetAssetPath(item.Value.atlas);
            ClearAtlasCache(item.Value.atlas);
            AssetDatabase.DeleteAsset(assetPath);
            Debug.Log("Deleted atlas asset: " + assetPath);
        }

        // 保存资源更改
        AssetDatabase.SaveAssets();
        // 刷新资源
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 递归查找文件夹下的所有atlas，创建图集数据
    /// </summary>
    /// <param name="relativePath"></param>
    static void CheckAssetAtlas(string relativePath)
    {
        List<SpriteAtlas> atlasList = GetFileAtlas(relativePath);
        if (atlasList != null && atlasList.Count > 0)
        {
            foreach (var atlas in atlasList)
            {
                Sprite[] sprites = new Sprite[atlas.spriteCount];
                atlas.GetSprites(sprites);
                string atlasPath = AssetDatabase.GetAssetPath(atlas);
                AtlasData data = new AtlasData()
                {
                    atlasName = atlas.name,
                    assetPath = atlasPath,
                    atlas = atlas,
                    sprites = sprites.ToList(),
                };
                atlasDatas.Add(atlasPath, data);
            }
        }

        DirectoryInfo direction = new DirectoryInfo(relativePath);
        if (direction == null) return;

        DirectoryInfo[] dirChild = direction.GetDirectories();
        foreach (var item in dirChild)
        {
            CheckAssetAtlas(relativePath + "/" + item.Name);
        }
    }

    private static Dictionary<string, AtlasData> atlasDatas = new Dictionary<string, AtlasData>();

    static List<SpriteAtlas> GetFileAtlas(string relativePath)
    {
        if (Directory.Exists(relativePath))
        {
            DirectoryInfo direction = new DirectoryInfo(relativePath);
            FileInfo[] files = direction.GetFiles("*"); //只查找本文件夹下
            if (files == null) return null;

            List<SpriteAtlas> atlases = new List<SpriteAtlas>();
            foreach (var file in files)
            {
                if (file.Name.EndsWith(".meta")) continue;
                var item = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(relativePath + "/" + file.Name);
                if (item != null)
                {
                    atlases.Add((SpriteAtlas) item);
                }
            }

            return atlases;
        }

        return null;
    }

    /// <summary>
    /// 删除图集对应的缓存哈希文件
    /// </summary>
    /// <param name="spriteAtlas"></param>
    static void ClearAtlasCache(SpriteAtlas spriteAtlas)
    {
        System.Reflection.Assembly editorAssembly = typeof(UnityEditor.Editor).Assembly;
        var spriteAtlasExtensionsType = editorAssembly.GetType("UnityEditor.U2D.SpriteAtlasExtensions");
        // var storeInfo= spriteAtlasExtensionsType.GetMethod("GetAtlasStore", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var storeInfo = spriteAtlasExtensionsType.GetMethod("GetStoredHash",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        var storeHash = storeInfo.Invoke(null, new object[] {spriteAtlas});

        Debug.Log($"图集缓存文件 storeHash:{storeHash}");

        var cacheDir = Application.dataPath + "/../Library/AtlasCache";
        DirectoryInfo dirInfo = new DirectoryInfo(cacheDir);
        if (dirInfo.Exists)
        {
            FileInfo[] files = dirInfo.GetFiles("*" + storeHash, SearchOption.AllDirectories);

            foreach (FileInfo file in files)
            {
                Debug.Log($"找到文件：{file.FullName}");
                file.Delete();
            }
        }
        else
        {
            Debug.LogError($"目录不存在：{cacheDir}");
        }
    }

    static void ClearNonReferenceAtlasCache()
    {
        var cacheDir= Application.dataPath + "/../Library/AtlasCache";
        DirectoryInfo dirInfo = new DirectoryInfo(cacheDir);
        if (dirInfo.Exists)
        {
            FileInfo[] files = dirInfo.GetFiles("*", SearchOption.AllDirectories);

            foreach (FileInfo file in files)
            {
                Debug.Log($"找到文件：{file.FullName}");
                file.Delete();
            }

            DeleteAllSubDirectories(dirInfo);
        }
        else
        {
            Debug.LogError($"目录不存在：{cacheDir}");
        }
    }
    
    static void DeleteAllSubDirectories(DirectoryInfo parentDir)
    {
        DirectoryInfo[] subDirs = parentDir.GetDirectories();

        foreach (DirectoryInfo subDir in subDirs)
        {
            Debug.Log($"删除子文件夹：{subDir.FullName}");
            try
            {
                subDir.Delete(true); // 将参数设为 true，以递归删除子文件夹中的内容
            }
            catch (IOException e)
            {
                Debug.LogError($"删除子文件夹时出错：{subDir.FullName}\n{e}");
            }
        }
    }
}