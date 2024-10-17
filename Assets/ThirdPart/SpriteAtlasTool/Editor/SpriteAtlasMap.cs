using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using UnityEngine.U2D;

public class SpriteAtlasMapping
{
    // [MenuItem("PokemonTool/图集管理/生成图集资源映射表")]
    public static void GenerateSpriteAtlasMapping()
    {
        string spriteFolderPath = "Assets/AddressableRes/Texture/UI";
        string outputJsonPath = "Assets/AddressableRes/Texture/UI/atlas_mapping.json";

        Dictionary<string, string> atlasMapping = new Dictionary<string, string>();

        AtlasList.Clear();
        // 获取 sprite 文件夹下的所有子文件夹
        CheckAssetAtlas(spriteFolderPath);
        if (AtlasList.Count == 0)
        {
            Debug.Log("No SpriteAtlas found in the folder: " + spriteFolderPath);
            return;
        }

        foreach (var atlas in AtlasList)
        {
            // string atlasPath = AssetDatabase.GetAssetPath(atlas);
            // string resourcesPath = "Assets/ArtLibrary/Resources";

            string atlasPath = GetSpriteAtlasPath(atlas);

            Sprite[] sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);
            foreach (var sprite in sprites)
            {
                if (sprite == null) continue;
                string spriteName = sprite.name.Replace("(Clone)", "").Trim();
                if (atlasMapping.ContainsKey(spriteName))
                {
                    Debug.LogError($"Sprite name conflict: {spriteName}");
                }
                else
                {
                    atlasMapping[spriteName] = atlasPath;
                }
            }
        }

        SaveAtlasMapping(atlasMapping, outputJsonPath);
    }

    /// <summary>
    /// 将映射表保存为 json 文件
    /// </summary>
    /// <param name="atlasMapping"></param>
    /// <param name="outputJsonPath"></param>
    private static void SaveAtlasMapping(Dictionary<string, string> atlasMapping, string outputJsonPath)
    {
        var spriteAtlasMapping = new SpriteAtlasMappingContainer
        {
            mappings = new List<SpriteToAtlasMapping>()
        };

        foreach (var pair in atlasMapping)
        {
            spriteAtlasMapping.mappings.Add(new SpriteToAtlasMapping
            {
                spriteName = pair.Key,
                atlasName = pair.Value
            });
        }

        string jsonContent = JsonUtility.ToJson(spriteAtlasMapping, prettyPrint: true);
        File.WriteAllText(outputJsonPath, jsonContent);

        Debug.Log($"图集映射生成: {outputJsonPath}");
    }

    static List<SpriteAtlas> AtlasList = new List<SpriteAtlas>();

    static void CheckAssetAtlas(string relativePath)
    {
        List<SpriteAtlas> atlasList = GetFileAtlas(relativePath);
        AtlasList.AddRange(atlasList);

        DirectoryInfo direction = new DirectoryInfo(relativePath);
        if (direction == null) return;

        DirectoryInfo[] dirChild = direction.GetDirectories();
        foreach (var item in dirChild)
        {
            CheckAssetAtlas(relativePath + "/" + item.Name);
        }
    }

    /// <summary>
    /// 查找文件夹下的所有sprite
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
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

    static string GetSpriteAtlasPath(SpriteAtlas atlas)
    {
        string assetPath = AssetDatabase.GetAssetPath(atlas);
        string resourcesPath = "Assets/AddressableRes/Texture/UI/";
        int startIndex = assetPath.IndexOf(resourcesPath);
        string atlasRelativePath = "";

        if (startIndex != -1)
        {
            int subStringStartIndex = startIndex + resourcesPath.Length;
            int subStringLength = assetPath.LastIndexOf('.') - subStringStartIndex;
            atlasRelativePath = assetPath.Substring(subStringStartIndex, subStringLength);
        }
        else
        {
            Debug.LogError("The atlas is not located in a Resources folder.");
        }

        return atlasRelativePath;
    }
}

// 映射表容器类
[System.Serializable]
public class SpriteAtlasMappingContainer
{
    public List<SpriteToAtlasMapping> mappings;
}

// 单个 Sprite 到图集的映射关系类
[System.Serializable]
public class SpriteToAtlasMapping
{
    public string spriteName;
    public string atlasName;
}