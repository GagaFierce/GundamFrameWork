/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : auto bind & gen lua file
 * 
 *****************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Core.UIFramework;

[CustomEditor(typeof(UIPageImplForLua))]
public class UIPageImplForLuaEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawNodes();
        base.OnInspectorGUI();
    }

    public void DrawNodes()
    {
        DrawHorizontalGUILine();
        //GUILayout.BeginVertical();
        //GUILayout.Space(5);
        //DrawHorizontalGUILine();
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("ReCollect UI Nodes", GUILayout.Width(125)))
        {
            OnClickedReCollectUINodes();
        }

        if (GUILayout.Button("Rebuild Lua File", GUILayout.Width(125)))
        {
            OnClickedRebuildLuaFile();
        }

        if (GUILayout.Button("Open Lua File", GUILayout.Width(125)))
        {
            OnClickedOpenLuaFile();
        }

        if (GUILayout.Button("Select Lua Dir", GUILayout.Width(125)))
        {
            OnClickedSelectLuaDir();
        }

        GUILayout.EndHorizontal();
        //GUILayout.Space(2);
        //ReDrawUINodes();
        //GUILayout.EndVertical();

        DrawHorizontalGUILine();

        UIPageImplForLua uipage = target as UIPageImplForLua;
        if (null == uipage.GetLuaFile() || uipage.GetLuaFile() == "")
        {
            string name = uipage.gameObject.name.Trim().Replace(" ", "").Replace("(Clone)", "");
            uipage.SetLuaFile(string.Format("Assets/Resources/Lua/{0}.lua", name));
        }
    }

    private void ReCollectUINodes()
    {
        List<UINode> uiNodeList = new List<UINode>();
        Dictionary<string, int> nameMap = new Dictionary<string, int>();
        UIPageImplForLua uipage = target as UIPageImplForLua;
        CollectObjs(uipage.gameObject, ref uiNodeList);
        for (int i = 0; i < uiNodeList.Count; ++i)
        {
            int idx = -1;
            GameObject go = uiNodeList[i].GetGameObject();
            string name = go.name.Trim().Replace(" ", "");
            if (nameMap.ContainsKey(name))
            {
                idx = nameMap[name];
            }

            nameMap[name] = ++idx;
            name = string.Format(idx > 0 ? "{0}_{1}" : "{0}", name, idx);
            go.name = name;
            //EditorGUILayout.ObjectField(name, go, typeof(GameObject), true);
        }

        uipage.SetBindList(uiNodeList.ToArray());
        EditorUtility.SetDirty(target);
    }

    private void CollectObjs(GameObject go, ref List<UINode> ls)
    {
        Transform tf = go.transform;
        bool isSelf = (go == (target as UIPageImplForLua).gameObject);
        if (!isSelf && go.GetComponent<UIPageBase>())
        {
            if (go.name.StartsWith("m"))
                ls.Add(tf.GetComponent<UINode>());
            return;
        }
            

        UINode uinode = tf.GetComponent<UINode>();
        if (null != uinode && !isSelf)
        {
            if(uinode.GetGameObject().name.StartsWith("m"))
                ls.Add(uinode);
        }

        for (int i = 0; i < tf.childCount; ++i)
        {
            CollectObjs(tf.GetChild(i).gameObject, ref ls);
        }
    }

    private static void DrawHorizontalGUILine(int height = 1)
    {
        GUILayout.Space(4);
        Rect rect = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
        rect.height = height;
        rect.xMin = 0;
        rect.xMax = EditorGUIUtility.currentViewWidth;
        Color lineColor = new Color(0.10196f, 0.10196f, 0.10196f, 1);
        EditorGUI.DrawRect(rect, lineColor);
        GUILayout.Space(4);
    }

    private static string GetHierarchyRelation(Transform trans)
    {
        StringBuilder hierarchyStrBuilder = new StringBuilder();
        hierarchyStrBuilder.Append(trans.name);
        string separator = "/";
        Transform currentTrans = trans;
        while (currentTrans.parent != null)
        {
            hierarchyStrBuilder.Insert(0, separator);
            hierarchyStrBuilder.Insert(0, currentTrans.parent.name);
            currentTrans = currentTrans.parent;
        }
        string hierarchyStr = hierarchyStrBuilder.ToString();
        const string flagLayer = "/Layer/";
        int idx = hierarchyStr.IndexOf(flagLayer);
        if (idx >= 0)
        {
            int idxStart = idx + flagLayer.Length;
            hierarchyStr = hierarchyStr.Substring(idxStart, hierarchyStr.Length - idxStart);
        }

        return hierarchyStr;
    }

    void OnClickedReCollectUINodes()
    {
        ReCollectUINodes();
    }

    protected virtual string GetLuaFileTemplate()
    {
        const string luaTemplate = "Assets/ThirdPart/Core/UIFramework/Editor/LuaAutoGen/TemplateFiles/LuaTemplate.txt";
        return luaTemplate;
    }

    void OnClickedRebuildLuaFile()
    {
        string luaTemplate = GetLuaFileTemplate();
        const string luaBindFunc = "Assets/ThirdPart/Core/UIFramework/Editor/LuaAutoGen/TemplateFiles/LuaBindFunc.txt";

        const string flagStart = "------------------------------------------------- auto bind start -----------------------------------------------------";
        const string flagEnd = "-------------------------------------------------- auto bind end ------------------------------------------------------";

        const string buttonBindFuncInfo = "\tself.{0}:SetClickedCallback(common:createCallbackFunc(self, self.{1}));{2}";
        const string dragBindFuncInfo = "\tself.{0}:SetDragCallback(common:createCallbackFunc(self, self.{1}));{2}";
        //const string scrollviewBindFuncInfo = "\tself.{0}:SetOnUpdate(common:createCallbackFunc(self, self.{1}));{2}";

        UIPageImplForLua uipage = target as UIPageImplForLua;
        UINode[] uiNodeList = uipage.GetBindList();
        string luaPath = GetLuaFilePath();
        string luaTableName = uipage.gameObject.name.Trim().Replace(" ", "").Replace("(Clone)", "");

        Dictionary<string, int> nameMap = new Dictionary<string, int>();
        StringBuilder nodesBuilder = new StringBuilder();
        StringBuilder bindFuncBuilder = new StringBuilder();
        StringBuilder cbFuncBuilder = new StringBuilder();
        for (int i = 0; i < uiNodeList.Length; ++i)
        {
            int idx = -1;
            INode inode = uiNodeList[i];
            GameObject go = inode.GetGameObject();
            string name = go.name.Trim().Replace(" ", "");
            if (nameMap.ContainsKey(name))
            {
                idx = nameMap[name];
            }

            nameMap[name] = ++idx;
            name = string.Format(idx > 0 ? "{0}_{1}" : "{0}", name, idx);
            go.name = name;
            //string nodePath = GetHierarchyRelation(go.transform);

            string typeName = inode.GetType().Name;
            UIPageImplForLua forLua = inode as UIPageImplForLua;
            if (forLua)
            {
                string luaFilename = forLua.GetLuaFile();
                if (!string.IsNullOrEmpty(luaFilename))
                {
                    typeName = typeName + " | " + Path.GetFileNameWithoutExtension(luaFilename);
                }
            }

            nodesBuilder.Append(string.Format("-- {0} {1}{2}", name, typeName, i == uiNodeList.Length - 1 ? "" : "\n"));

            string realName = name;
            if (realName[0] == 'm')
                realName = realName.Remove(0, 1);

            //Transform tf = go.transform;
            if ((null != (inode as IButton)) || ((null != (inode as IImage)) && ((inode as IImage).IsRegisterClicked())))
            {
                string funcName = "onClicked_" + realName;
                bindFuncBuilder.Append(string.Format(buttonBindFuncInfo, name, funcName, "\n"));
                cbFuncBuilder.Append(string.Format("function #REPLACE_CLS_NAME#:{0}()\nend{1}", funcName, i == uiNodeList.Length - 1 ? "" : "\n\n"));
            }

            if (((null != (inode as IImage)) && ((inode as IImage).IsRegisterDrag())))
            {
                string funcName = "onDrag_" + realName;
                bindFuncBuilder.Append(string.Format(dragBindFuncInfo, name, funcName, "\n"));
                cbFuncBuilder.Append(string.Format("function #REPLACE_CLS_NAME#:{0}(dragtype, info)\t--dragtype:\"OnBeginDrag\"|\"OnDrag\"|\"OnEndDrag\"\nend{1}", funcName, i == uiNodeList.Length - 1 ? "" : "\n\n"));
            }

            //else if (tf.GetComponent<ConScrollView>())
            //{
            //    string funcName = "onScrollviewUpdate_" + name;
            //    nodesBuilder.Append(string.Format(scrollviewBindNodeInfo, "m" + name, nodePath, i == goList.Count - 1 ? "" : "\n"));
            //    bindFuncBuilder.Append(string.Format(scrollviewBindFuncInfo, "m" + name, funcName, i == goList.Count - 1 ? "" : "\n"));
            //    cbFuncBuilder.Append(string.Format("function #REPLACE_CLS_NAME#:{0}(index, obj)\nend{1}", funcName, i == goList.Count - 1 ? "" : "\n\n"));
            //}
            //else if (tf.GetComponent<Text>())
            //{
            //    nodesBuilder.Append(string.Format(textBindNodeInfo, "m" + name, nodePath, i == goList.Count - 1 ? "" : "\n"));
            //}
            //else if (tf.GetComponent<UIComponentBase>())
            //{
            //    nodesBuilder.Append(string.Format(goBindNodeInfo, "m" + name, nodePath, "UIComponentBase", i == goList.Count - 1 ? "" : "\n"));
            //}
        }

        if (bindFuncBuilder.Length > 0 && bindFuncBuilder[bindFuncBuilder.Length - 1] == '\n')
        {
            bindFuncBuilder.Remove(bindFuncBuilder.Length - 1, 1);
        }

        string dateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string luaBindFuncBuffer = File.ReadAllText(luaBindFunc);
        string genBindFuncBuffer = string.Format(luaBindFuncBuffer, dateStr, nodesBuilder, bindFuncBuilder);

        string luaFileBuffer = "";
        bool isExist = File.Exists(luaPath);
        if (isExist)
        {
            luaFileBuffer = File.ReadAllText(luaPath);
            int idxStart = luaFileBuffer.IndexOf(flagStart);
            int idxEnd = luaFileBuffer.IndexOf(flagEnd);
            if (idxStart < 0 || idxEnd < 0)
            {
                EditorUtility.DisplayDialog("tips", "error!!!\ncheck your lua file, not found auto flag", "Ok");
                return;
            }
            luaFileBuffer = luaFileBuffer.Remove(idxStart, idxEnd + flagEnd.Length - idxStart);
            luaFileBuffer = luaFileBuffer.Insert(idxStart, genBindFuncBuffer);
        }
        else
        {
            string luaTemplateBuffer = File.ReadAllText(luaTemplate);
            luaFileBuffer = string.Format(luaTemplateBuffer, Environment.MachineName, dateStr, uipage.gameObject.name + ".prefab", genBindFuncBuffer, cbFuncBuilder);
        }

        string luaFileNewBuffer = luaFileBuffer.Replace("#REPLACE_CLS_NAME#", luaTableName);
        File.WriteAllText(luaPath, luaFileNewBuffer);
        EditorUtility.DisplayDialog("tips", "successed!", "Ok");
        //updateLuaFiles(luaPath);
    }

    private string GetLuaFilePath()
    {
        UIPageImplForLua uipage = target as UIPageImplForLua;
        //UnityEditor.SceneManagement.PrefabStage prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(uipage.gameObject);
        //string path = prefabStage ? prefabStage.assetPath : AssetDatabase.GetAssetPath(Selection.activeObject);
        //string name = uipage.gameObject.name.Trim().Replace(" ", "").Replace("(Clone)", "");

        //if (string.IsNullOrEmpty(path))
        //{
        //    //string[] strs = AssetDatabase.GetAllAssetPaths();

        //    // maybe the path is Incorrect, don't trust it
        //    path = string.Format("Assets/_Assets/UI/Layers/{0}/{1}", name, name);
        //}
        //path = path.Substring(0, path.LastIndexOf("/"));
        //string luaPath = string.Format("{0}/{1}.lua", path, name);
        //return luaPath;

        string path = uipage.GetLuaFile();
        if (string.IsNullOrEmpty(path))
        {
            string name = uipage.gameObject.name.Trim().Replace(" ", "").Replace("(Clone)", "");
            path = string.Format("Assets/Resources/Lua/{0}", name);
        }

        string luaPath = path;
        if (!path.EndsWith(".lua"))
            luaPath = string.Format("{0}.lua", path);
        return luaPath;
    }

    private bool isLuaTableFile(string newFile)
    {
        bool bResult = false;
        XLua.LuaEnv luaEnv = new XLua.LuaEnv();
        string buffer = File.ReadAllText(newFile);
        var objs = luaEnv.DoString(buffer);
        if (null != objs)
        {
            XLua.LuaTable t = objs[0] as XLua.LuaTable;
            string tName = t.ToString();
            if (null != tName)
            {
                bResult = true;
            }
            t.Dispose();
        }
        luaEnv.Dispose();

        return bResult;
    }

    private void updateLuaFiles(string newFile)
    {
        if (!isLuaTableFile(newFile))
        {
            return;
        }

        const string luaFile = "Assets/Resources/Lua/LuaFilesDef.lua";
        const string templateFile = "Assets/ThirdPart/Core/UIFramework/Editor/LuaAutoGen/TemplateFiles/LuaFilesDefTemplate.txt";
        const string fileInfo = "\t\t[\"{0}\"]=\"{1}\",{2}";

        Dictionary<string, string> dict = null;
        
        XLua.LuaEnv luaEnv = new XLua.LuaEnv();
        string buffer = File.ReadAllText(luaFile);
        var objs = luaEnv.DoString(buffer);
        if (null != objs)
        {
            XLua.LuaTable t = objs[0] as XLua.LuaTable;
            var func = t.Get<XLua.LuaFunction>("getFiles");
            if (null != func)
            {
                dict = func.Func<XLua.LuaTable, Dictionary<string, string>>(t);
                func.Dispose();
            }
            t.Dispose();
        }
        luaEnv.Dispose();


        string name = newFile.Substring(newFile.LastIndexOf("/")+1);
        name = name.Substring(0, name.LastIndexOf("."));
        if (null != dict && !dict.ContainsKey(name))
        {
            dict.Add(name, newFile);
            Dictionary<string, string> dictTemp = dict.OrderBy(o => o.Key).ToDictionary(o => o.Key, p => p.Value);

            StringBuilder infosBuilder = new StringBuilder();
            for (int i = 0; i < dictTemp.Count; ++i)
            {
                var item = dictTemp.ElementAt(i);
                infosBuilder.Append(string.Format(fileInfo, item.Key, item.Value, i == dictTemp.Count - 1 ? "" : "\n"));
            }
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string luaFileBuffer = File.ReadAllText(templateFile);
            string newBuffer = string.Format(luaFileBuffer, dateStr, infosBuilder);
            File.WriteAllText(luaFile, newBuffer);
        }
    }

    private void OnClickedOpenLuaFile()
    {
        string luaPath = GetLuaFilePath();
        if (File.Exists(luaPath))
        {
//#if UNITY_EDITOR_WIN
//             if(null == System.Diagnostics.Process.Start("open", "-a Visual Studio Code --args " + Path.GetFullPath(luaPath)))
//             {
//                 if(null == System.Diagnostics.Process.Start("open", "-a Rider --args " + Path.GetFullPath(luaPath)))
//                 {
//                     EditorUtility.DisplayDialog("tips", string.Format("error!!!\nopen file failed, Only VSCode && Rider are supported\npath:{0}", luaPath), "Ok");
//                 }
//             }
//#endif

#if UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start(Path.GetFullPath(luaPath));
#elif UNITY_STANDALONE_OSX
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(luaPath, 0);
            // if(null == System.Diagnostics.Process.Start("open", "-a Visual Studio Code --args " + Path.GetFullPath(luaPath)))
            // {
            //     if(null == System.Diagnostics.Process.Start("open", "-a Rider --args " + Path.GetFullPath(luaPath)))
            //     {
            //         EditorUtility.DisplayDialog("tips", string.Format("error!!!\nopen file failed, Only VSCode && Rider are supported\npath:{0}", luaPath), "Ok");
            //     }
            // }
#endif
        }
        else
            EditorUtility.DisplayDialog("tips", string.Format("error!!!\nnot found file\npath:{0}", luaPath), "Ok");
    }

    void OnClickedSelectLuaDir()
    {
        UIPageImplForLua uipage = target as UIPageImplForLua;
        string name = uipage.gameObject.name.Trim().Replace(" ", "").Replace("(Clone)", "");
        string folderName = EditorUtility.OpenFolderPanel("Select Lua Dir", "Assets/Resources/Lua", "");
        if (folderName != null && folderName != "")
        {
            string luaPath = string.Format("{0}/{1}.lua", folderName, name);
            int index = luaPath.IndexOf("/Assets/");
            if (index >= 0)
            {
                luaPath = luaPath.Substring(index + 1, luaPath.Length - index - 1);
                uipage.SetLuaFile(luaPath);
                EditorUtility.SetDirty(target);
            }
        }
    }
}
