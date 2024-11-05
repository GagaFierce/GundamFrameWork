/**************************************************
 *
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Core.Editor
{
    public class AddCommentsEditor : EditorWindow
    {
        [MenuItem("Tools/Add Comments to Scripts in Folder")]
        public static void AddCommentsToScriptsInFolder()
        {
            // 选择要操作的文件夹路径
            string folderPath = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");

            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.LogWarning("No folder selected.");
                return;
            }

            // 递归获取所有 .cs 文件
            string[] files = Directory.GetFiles(folderPath, "*.cs", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                AddCommentsToFile(file);
            }

            Debug.Log("注释已添加到所有脚本文件！");
        }

        private static void AddCommentsToFile(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);

            //检查文件是否已经有作者注释
            if (lines.Length > 0 && lines[0].StartsWith("/*"))
            {
                Debug.Log($"File {filePath} already contains author comments, skipping.");
                return;
            }

            // 添加注释行
            string a = "/**************************************************";  // 这里可以替换为你的名字
            string b = " *";  
            string c = " * author       : WangJian";  
            string d = " * create date  : 2024 11 05";  
            string e = " * description  : Core"; 
            string f = " *";
            string g = "***************************************************/ ";  // 这里可以替换为你的名字
            string[] newLines = new string[lines.Length + 7];
            newLines[0] = a;
            newLines[1] = b;
            newLines[2] = c;
            newLines[3] = d;
            newLines[4] = e;
            newLines[5] = f;
            newLines[6] = g;
            Array.Copy(lines, 0, newLines, 7, lines.Length);

            // 写入文件
            File.WriteAllLines(filePath, newLines);
            Debug.Log($"Comments added to {filePath}");
        }
    }
}

