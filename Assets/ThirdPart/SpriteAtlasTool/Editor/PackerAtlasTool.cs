using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

public class PackerAtlasTool : EditorWindow
{
    [MenuItem("图集工具/图集打包")]
    private static void ShowWindow()
    {
        GetWindow<PackerAtlasTool>("图集打包");
    }

    private void OnEnable()
    {
        AssetDatabase.Refresh();
        CheckAssetAtlas("Assets/AddressableRes/Texture/UI");
    }

    private Vector2 scrollPosition;

    private void OnGUI()
    {
        scrollPosition =
            EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Width(position.width), GUILayout.Height(500));
        DrawSpriteAtlasSetting();
        DrawAtlasInfo();
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("更新/打包图集"))
        {
            CheckAssetFile("Assets/AddressableRes/Texture/UI");
            SaveAtlasData();
        }
    }

    private void DelayedAction()
    {
        EditorApplication.delayCall -= DelayedAction;
        // 在此处访问 SpriteAtlas，此时资源应该已经导入并准备好。

        AssetDatabase.Refresh();
        atlasDatas.Clear();
        CheckAssetAtlas("Assets/AddressableRes/Texture/UI");
    }

    private bool isIncludeInBuild = true;
    private int maxSpriteSize = 1024;
    private float maxSpritePixelNum = 1024;
    private int maxSpriteAtlasSize = 1024;
    private Dictionary<string, AtlasData> atlasDatas = new Dictionary<string, AtlasData>();

    /// <summary>
    /// 递归查找文件夹下的所有atlas，创建图集数据
    /// </summary>
    /// <param name="relativePath"></param>
    void CheckAssetAtlas(string relativePath)
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

    /// <summary>
    /// 查找文件夹下的所有atlas
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    List<SpriteAtlas> GetFileAtlas(string relativePath)
    {
        if (Directory.Exists(relativePath))
        {
            DirectoryInfo direction = new DirectoryInfo(relativePath);
            FileInfo[] files = direction.GetFiles("*.spriteatlas"); //只查找本文件夹下
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
    /// 序列化图集信息
    /// </summary>
    void SaveAtlasData()
    {
        foreach (var item in atlasDatas.Values)
        {
            string path = item.assetPath;
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (atlas != null)
            {
                
                SetUpAtlasInfo(ref atlas);
                List<Sprite> sprites = new List<Sprite>();
                foreach (var sprite in item.sprites)
                {
                    if (atlas.GetSprite(sprite.name) == null)
                    {
                        sprites.Add(sprite);
                    }
                }

                ClearAtlasCache(atlas);
                EditorUtility.SetDirty(atlas); 
                
                atlas.Add(sprites.ToArray());
                item.atlas = atlas;
                continue;
            }

            atlas = new SpriteAtlas();
            SetUpAtlasInfo(ref atlas);
            atlas.Add(item.sprites.ToArray());
            item.atlas = atlas;
            AssetDatabase.CreateAsset(atlas, path);

            SpriteAtlasUtility.PackAtlases(new[] {atlas}, EditorUserBuildSettings.activeBuildTarget);
            AssetDatabase.Refresh();
        }
        
        Debug.Log("图集打包完毕");
        
        SpriteAtlasMapping.GenerateSpriteAtlasMapping();
    }

    /// <summary>
    /// 递归查找文件夹下的所有sprite，创建图集数据
    /// </summary>
    /// <param name="relativePath"></param>
    void CheckAssetFile(string relativePath)
    {
        List<Sprite> sprites = GetFileSprites(relativePath);
        if (sprites != null && sprites.Count > 1)
        {
            string atlasname = GetAtlasNameFromPath(relativePath);
            string atlasPath = relativePath + "/" + atlasname + ".spriteatlas";
            CreateSpriteAtlas(atlasname, atlasPath, sprites);
        }

        DirectoryInfo direction = new DirectoryInfo(relativePath);
        if (direction == null) return;

        DirectoryInfo[] dirChild = direction.GetDirectories();
        foreach (var item in dirChild)
        {
            CheckAssetFile(relativePath + "/" + item.Name);
        }
    }


    /// <summary>
    /// 查找文件夹下的所有sprite
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    List<Sprite> GetFileSprites(string relativePath)
    {
        if (Directory.Exists(relativePath))
        {
            DirectoryInfo direction = new DirectoryInfo(relativePath);
            FileInfo[] files = direction.GetFiles("*"); //只查找本文件夹下
            if (files == null) return null;

            List<Sprite> sprites = new List<Sprite>();
            foreach (var file in files)
            {
                if (file.Name.EndsWith(".meta")) continue;
                var item = AssetDatabase.LoadAssetAtPath<Sprite>(relativePath + "/" + file.Name);
                if (item != null && ChackSpritePackerState(item))
                {
                    sprites.Add((Sprite) item);
                }
            }

            return sprites;
        }

        return null;
    }

    /// <summary>
    /// 检查sprite是否符合打包规范
    /// </summary>
    /// <param name="sprite"></param>
    /// <returns></returns>
    private bool ChackSpritePackerState(Sprite sprite)
    {
        if (sprite.rect.width > maxSpriteSize)
        {
            if (sprite.rect.width % 2 != 0 || sprite.rect.height % 2 != 0)
            {
                Debug.LogError($"{sprite.name}尺寸不符合压缩规范（宽高均为2的倍数），请注意");
            }

            return false;
        }

        if (sprite.rect.height > maxSpriteSize)
        {
            if (sprite.rect.width % 2 != 0 || sprite.rect.height % 2 != 0)
            {
                Debug.LogError($"{sprite.name}宽度不符合压缩规范（宽高均为2的倍数），请注意");
            }

            return false;
        }

        if (sprite.rect.width * sprite.rect.height > maxSpritePixelNum * 1024)
        {
            Debug.LogError($"{sprite.name}尺寸过大，请注意");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 通过文件夹路径获取图集名字
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    string GetAtlasNameFromPath(string path)
    {
        string atlasName = path.Substring(path.LastIndexOf("/") + 1);
        return atlasName;
    }

    void CreateSpriteAtlas(string atlasname, string atlasPath, List<Sprite> sprites)
    {
        if (atlasDatas.ContainsKey(atlasPath))
        {
            //删除图集资源需要清除缓存
            ClearAtlasCache(atlasDatas[atlasPath].atlas);
            AssetDatabase.DeleteAsset(atlasPath);
            atlasDatas.Remove(atlasPath);
            Debug.Log($"删除原图集资源{atlasPath}");
        }

        AtlasData data = new AtlasData()
        {
            atlasName = atlasname.Replace(".spriteatlas", ""),
            assetPath = atlasPath,
            sprites = sprites
        };
        atlasDatas.Add(atlasPath, data);
    }

    void SetUpAtlasInfo(ref SpriteAtlas atlas)
    {
        atlas.SetIncludeInBuild(isIncludeInBuild);

        SpriteAtlasPackingSettings packSetting = new SpriteAtlasPackingSettings()
        {
            blockOffset = 1,
            enableRotation = false,
            enableTightPacking = false,
            padding = 2,
        };
        atlas.SetPackingSettings(packSetting);

        SpriteAtlasTextureSettings textureSetting = new SpriteAtlasTextureSettings()
        {
            readable = false,
            generateMipMaps = false,
            sRGB = true,
            filterMode = FilterMode.Bilinear,
        };
        atlas.SetTextureSettings(textureSetting);

        TextureImporterPlatformSettings platformSetting = new TextureImporterPlatformSettings()
        {
            maxTextureSize = (int) maxSpriteAtlasSize,
            format = TextureImporterFormat.Automatic,
            crunchedCompression = true,
            textureCompression = TextureImporterCompression.Compressed,
            compressionQuality = 50,
        };
        atlas.SetPlatformSettings(platformSetting);
    }

    /// <summary>
    /// 删除图集对应的缓存哈希文件
    /// </summary>
    /// <param name="spriteAtlas"></param>
    void ClearAtlasCache(SpriteAtlas spriteAtlas)
    {
        System.Reflection.Assembly editorAssembly = typeof(UnityEditor.Editor).Assembly;
        var spriteAtlasExtensionsType = editorAssembly.GetType("UnityEditor.U2D.SpriteAtlasExtensions");
        // var storeInfo= spriteAtlasExtensionsType.GetMethod("GetAtlasStore", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var storeInfo = spriteAtlasExtensionsType.GetMethod("GetStoredHash",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        var storeHash = storeInfo.Invoke(null, new object[] {spriteAtlas});
        
        // Debug.Log($"图集缓存文件 storeHash:{storeHash}");
        
        var cacheDir= Application.dataPath + "/../Library/AtlasCache";
        DirectoryInfo dirInfo = new DirectoryInfo(cacheDir);
        if (dirInfo.Exists)
        {
            FileInfo[] files = dirInfo.GetFiles("*" + storeHash, SearchOption.AllDirectories);

            foreach (FileInfo file in files)
            {
                // Debug.Log($"找到文件：{file.FullName}");
                file.Delete();
            }
        }
        else
        {
            Debug.LogError($"目录不存在：{cacheDir}");
        }
    }

    string[] sizeStrs = {"32", "64", "128", "256", "512", "1024"};
    int[] sizes = {32, 64, 128, 256, 512, 1024};

    void DrawSpriteAtlasSetting()
    {
        EditorGUILayout.LabelField("图集相关设定：", new GUIStyle()
        {
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            normal = new GUIStyleState()
            {
                textColor = Color.cyan
            }
        });
        GUILayout.Space(4);

        maxSpriteAtlasSize = EditorGUILayout.IntPopup("图集最大尺寸为", maxSpriteAtlasSize, sizeStrs, sizes);
        isIncludeInBuild = EditorGUILayout.Toggle("是否将图集打入包内", isIncludeInBuild);
        GUILayout.Space(5);

        EditorGUILayout.LabelField("Sprite资源限制策略：", new GUIStyle()
        {
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            normal = new GUIStyleState()
            {
                textColor = Color.cyan
            }
        });
        GUILayout.Space(4);

        maxSpritePixelNum = EditorGUILayout.Slider("Sprite最大像素量（单位K）：", maxSpritePixelNum, 0, 1024);
        maxSpriteSize = EditorGUILayout.IntPopup("Sprite最大尺寸限制", maxSpriteSize, sizeStrs, sizes);
    }

    void DrawAtlasInfo()
    {
        GUILayout.Label("图集资源列表： ", new GUIStyle()
        {
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            normal = new GUIStyleState()
            {
                textColor = Color.cyan
            }
        });
        GUILayout.Space(10);
        foreach (var item in atlasDatas.Values)
        {
            item.isShowDetail = EditorGUILayout.BeginFoldoutHeaderGroup(item.isShowDetail, "展开:   " + item.atlasName);
            if (item.isShowDetail)
            {
                EditorGUILayout.LabelField("      图集名字：", item.atlasName);
                EditorGUILayout.LabelField("      资源路径：", item.assetPath);
                EditorGUILayout.LabelField("      图集中Sprite数量", item.atlas.spriteCount.ToString());
                if (GUILayout.Button("打开并查看该图集资源"))
                {
                    AssetDatabase.MakeEditable(item.assetPath);
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(item.assetPath);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(5);
        }
    }
}

public class AtlasData
{
    public string atlasName;
    public string assetPath;

    /// <summary>
    /// 缓存中的SpriteAtlas，不直接指向本地资源
    /// </summary>
    public SpriteAtlas atlas;

    public List<Sprite> sprites;

    //编辑器界面数据
    public bool isShowDetail;
}