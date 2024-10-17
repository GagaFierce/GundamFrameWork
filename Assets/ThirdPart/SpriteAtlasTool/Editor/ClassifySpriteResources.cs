using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class ClassifySpriteResources : EditorWindow
{
    //#if UNITY_EDITOR
    //    [MenuItem("PokemonTool/删除数据")]
    //    private static void DeleteData()
    //    {
    //        PlayerPrefs.DeleteAll();
    //        Directory.Delete(Application.persistentDataPath, true);
    //        EditorApplication.isPlaying = false;
    //    }
    //#endif
    struct SpriteRefInfo
    {
        public string prefabName;
        public string dirName;
    };


    [MenuItem("图集工具/图集管理/分类Sprite资源")]
    private static void ClassifySprites()
    {
        // UI Prefabs folder
        string uiPrefabsFolderPath = "Assets/AddressableRes/Prefabs/UI";
        // string[] allPrefabPaths = Directory.GetFiles(uiPrefabsFolderPath, "*.prefab", SearchOption.AllDirectories);
        // string[] allPrefabFilePaths = Directory.GetFiles(uiPrefabsFolderPath, "*", SearchOption.AllDirectories);

        // UI Prefab - Sprites map
        Dictionary<string, HashSet<Sprite>> uiPrefabsToSpritesMap = new Dictionary<string, HashSet<Sprite>>();

        // All unique sprites


        Dictionary<string, List<SpriteRefInfo>> allSprites = new Dictionary<string, List<SpriteRefInfo>>();

        // Allowed folders to move the sprite from
        List<string> disallowedSourceFolders = new List<string>
        {
            "Assets/AddressableRes/Texture/UI/SkillIcon",
            //"Assets/ArtLibrary/Art/Effect/Textures",
            //"Assets/ArtLibrary/Art/Effect/ui/ui_textures",
            //"Assets/ArtLibrary/Font",
            // Add more folder paths here as needed
        };

        DirectoryInfo directoryInfo = new DirectoryInfo(uiPrefabsFolderPath);
        if (directoryInfo.Exists)
        {
            DirectoryInfo[] subDirectories = directoryInfo.GetDirectories();
            foreach (DirectoryInfo subDirectory in subDirectories)
            {

                string[] allPrefabFiles = Directory.GetFiles($"{uiPrefabsFolderPath}/{subDirectory.Name}", "*.prefab", SearchOption.AllDirectories);
                for (int i = 0; i < allPrefabFiles.Length; ++i)
                {
                    string prefabPath = allPrefabFiles[i];
                    if (File.Exists(prefabPath))
                    {
                        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        Image[] images = prefab.GetComponentsInChildren<Image>(true);

                        HashSet<Sprite> spritesInPrefab = new HashSet<Sprite>();
                        foreach (Image image in images)
                        {
                            Sprite sprite = image.sprite;

                            if (sprite != null)
                            {
                                spritesInPrefab.Add(sprite);
                                string sourcePath = AssetDatabase.GetAssetPath(sprite);

                                if (allSprites.TryGetValue(sourcePath, out List<SpriteRefInfo> infos))
                                {
                                    SpriteRefInfo info = new SpriteRefInfo();
                                    info.dirName = subDirectory.Name;
                                    info.prefabName = prefabPath;
                                    infos.Add(info);
                                    allSprites[sourcePath] = infos;
                                }
                                else 
                                {
                                    infos = new List<SpriteRefInfo>();
                                    SpriteRefInfo info = new SpriteRefInfo();
                                    info.dirName = subDirectory.Name;
                                    info.prefabName = prefabPath;
                                    infos.Add(info);
                                    allSprites[sourcePath] = infos;
                                }
                            }
                        }

                        uiPrefabsToSpritesMap[subDirectory.Name] = spritesInPrefab;
                    }
                }
            }
        }

        // // Iterate over all prefabs and get their sprites
        // foreach (string filePath in allPrefabFilePaths)
        // {
        //     GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        //     Image[] images = prefab.GetComponentsInChildren<Image>(true);
        //
        //     HashSet<Sprite> spritesInPrefab = new HashSet<Sprite>();
        //     foreach (Image image in images)
        //     {
        //         Sprite sprite = image.sprite;
        //
        //         if (sprite != null)
        //         {
        //             spritesInPrefab.Add(sprite);
        //             allSprites.Add(sprite);
        //         }
        //     }
        //
        //     uiPrefabsToSpritesMap[prefab] = spritesInPrefab;
        // }

        // Common folder for shared sprites
        string commonFolder = "Assets/AddressableRes/Texture/UI/Common";
        if (!Directory.Exists(commonFolder))
        {
            Directory.CreateDirectory(commonFolder);
        }

        // Move sprites to corresponding folders
        foreach (var v in allSprites)
        {
            //string sourcePath = AssetDatabase.GetAssetPath(sprite);
            string sourcePath = v.Key;

            // Check if the sprite is from one of the disallowed folders, if yes, skip moving
            if (IsSpriteFromDisallowedFolder(sourcePath, disallowedSourceFolders))
            {
                continue;
            }

            //List<string> prefabsUsingSprite = new List<string>();

            //foreach (var uiPrefab in uiPrefabsToSpritesMap)
            //{
            //    if (uiPrefab.Value.Contains(sprite))
            //    {
            //        prefabsUsingSprite.Add(uiPrefab.Key);
            //    }
            //}

            string targetFolder;
            if (v.Value.Count > 1)
            {
                targetFolder = commonFolder;
            }
            else
            {
                targetFolder = $"Assets/AddressableRes/Texture/UI/{v.Value[0].dirName}";
            }

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            string fileName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(targetFolder, fileName);

            AssetDatabase.MoveAsset(sourcePath, destPath);
        }

        AssetDatabase.Refresh();

        Debug.Log("ClassifySpriteResources finished~");
    }

    private static bool IsSpriteFromDisallowedFolder(string sourcePath, List<string> disallowedSourceFolders)
    {
        foreach (string folder in disallowedSourceFolders)
        {
            if (sourcePath.StartsWith(folder))
            {
                return true;
            }
        }

        return false;
    }

    // [MenuItem("PokemonTool/图集管理/分类UI资源")]
    // private static void ClassifyUIResources()
    // {
    //     string uiPrefabsFolderPath = "Assets/ArtLibrary/Resources/Prefab/UI";
    //     //遍历当前目录下所有文件，并对每个文件创建一个同名的文件夹，把文件移动到对应的文件夹下
    //     string[] allPrefabPaths = Directory.GetFiles(uiPrefabsFolderPath, "*.prefab", SearchOption.AllDirectories);
    //     foreach (string prefabPath in allPrefabPaths)
    //     {
    //         string folderPath = prefabPath.Replace(".prefab", "");
    //         if (!Directory.Exists(folderPath))
    //         {
    //             Directory.CreateDirectory(folderPath);
    //         }
    //         string fileName = Path.GetFileName(prefabPath);
    //         string destPath = Path.Combine(folderPath, fileName);
    //         AssetDatabase.MoveAsset(prefabPath, destPath);
    //     }
    // }
}