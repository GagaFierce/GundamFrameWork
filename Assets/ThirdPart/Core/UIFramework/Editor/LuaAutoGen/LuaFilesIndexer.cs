using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static Core.ResLoad.LuaManagerImpl;

public class LuaFilesIndexer
{
    //// 映射表容器类
    //[System.Serializable]
    //public class FilesMappingContainer
    //{
    //    public List<FilesMapping> mappings;
    //}

    //// 单个 Sprite 到图集的映射关系类
    //[System.Serializable]
    //public class FilesMapping
    //{
    //    public string name;
    //    public string path;
    //}


    [MenuItem("XLua/Generate Index")]
    public static void GenerateLuaFilesIndex()
    {
        // Get all lua files recursively in "Assets/Resources/Lua"
        string[] allLuaFiles = Directory.GetFiles("Assets/Resources/Lua", "*.lua", SearchOption.AllDirectories);

        // Dictionary to store file mappings
        Dictionary<string, string> luaFilesDict = new Dictionary<string, string>();

        // Make the dictionary for the files
        foreach (string filePath in allLuaFiles)
        {
            string relativePath = filePath.Substring(filePath.IndexOf("Assets/Resources/Lua") + "Assets/Resources/Lua/".Length);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(relativePath);
            string directoryPathWithoutExtension = Path.GetDirectoryName(relativePath).Replace('\\', '/');
            string value = string.IsNullOrEmpty(directoryPathWithoutExtension) ? fileNameWithoutExtension : directoryPathWithoutExtension + "/" + fileNameWithoutExtension;

            if (luaFilesDict.ContainsKey(fileNameWithoutExtension))
            {
                Debug.LogError("Duplicate file name found: " + fileNameWithoutExtension + " in " + value + " and " + luaFilesDict[fileNameWithoutExtension]);
                continue;
            }
            luaFilesDict.Add(fileNameWithoutExtension, "Lua/" + value);
        }

        var filesMappingContainer = new FilesMappingContainer
        {
            mappings = new List<FilesMapping>()
        };

        List<string> keys = luaFilesDict.Keys.ToList();
        keys.Sort();
        for (int i = 0; i < keys.Count; ++i)
        {
            var k = keys[i];
            var v = luaFilesDict[k];
            filesMappingContainer.mappings.Add(new FilesMapping
            {
                name = k,
                path = "Assets/Resources/" + v + ".lua",
            });
        }

        // Convert the dictionary to JSON
        //string luaFilesJson = JsonConvert.SerializeObject(luaFilesDict, Formatting.Indented);
        string luaFilesJson = JsonUtility.ToJson(filesMappingContainer, prettyPrint: true);

        // Save the JSON to a file
        File.WriteAllText("Assets/Resources/luafilesIndex.json", luaFilesJson);

        // Refresh Unity's asset database
        AssetDatabase.Refresh();

        Debug.Log("Generated Lua Files Index!");
    }

    [MenuItem("XLua/Gen Pack Lua Files")]
    public static void OnGenPackLuaFiles()
    {
        string luaSrcDir = "Assets/Resources/Lua";
        string[] allLuaFiles = Directory.GetFiles(luaSrcDir, "*.lua", SearchOption.AllDirectories);

        var targetRootDir = "Assets/Resources/LuaPack";
        if (!Directory.Exists(targetRootDir))
            Directory.CreateDirectory(targetRootDir);

        foreach (string filePath in allLuaFiles)
        {
            string luafilename = filePath.Substring(luaSrcDir.Length, filePath.Length - luaSrcDir.Length);
            string path = targetRootDir + luafilename + ".txt";
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(filePath, path, true);
        }
        Debug.Log("Gen Pack Lua Files!");
    }

    public static void ExportLuaFile(Dictionary<string, List<string>> fieldDict)
    {
        string exprotLuaPath = "Assets/Resources/Lua/Config";
        if (!Directory.Exists(exprotLuaPath))
            Directory.CreateDirectory(exprotLuaPath);

        string luaTemplateBuffer = File.ReadAllText("Assets/ThirdPart/Core/UIFramework/Editor/LuaAutoGen/TemplateFiles/ConfLuaTemplate.txt");
        string luaConfBaseBuffer = File.ReadAllText("Assets/ThirdPart/Core/UIFramework/Editor/LuaAutoGen/TemplateFiles/ConfigsBaseTemplate.txt");
        StringBuilder builderCfgList = new StringBuilder();
        List<string> keys = fieldDict.Keys.ToList();
        keys.Sort();
        for (int i = 0; i < keys.Count; ++i)
        {
            var k = keys[i];
            var v = fieldDict[k];
            string name = "Conf" + k;
            StringBuilder builder = new StringBuilder();
            for (int vi = 0; vi < v.Count; ++vi)
            {
                builder.Append("\"" + v[vi] + "\"" + (vi == v.Count - 1 ? "" : ","));
            }

            builderCfgList.Append(string.Format("\t_G[\"{0}\"] = require(\"{1}\").new();{2}", name, name, (i < keys.Count - 1 ? "\n" : "")));

            string luaPath = string.Format("{0}/{1}.lua", exprotLuaPath, name);
            string buffer = string.Format(luaTemplateBuffer, k, builder, v[0]);
            string luaFileNewBuffer = buffer.Replace("#REPLACE_CLS_NAME#", name);
            File.WriteAllText(luaPath, luaFileNewBuffer);
        }
        File.WriteAllText(exprotLuaPath + "/ConfigsBase.lua", string.Format(luaConfBaseBuffer, builderCfgList));

        GenerateLuaFilesIndex();
        Debug.LogFormat("Complete ExportLuaFile .filecount = {0}", keys.Count);
    }
}