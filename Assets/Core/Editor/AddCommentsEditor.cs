/**************************************************
 *
 * Copyright (c) 2024 WangJian 
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
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
        
            RemoveFirstLines(folderPath);
            foreach (var file in files)
            {
              
                AddCommentsToFile(file);
            }

            Debug.Log("注释已添加到所有脚本文件！");
        }

        static void RemoveFirstLines(string path, int linesToRemove=8)
        {
            string[] files = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                string[] lines = File.ReadAllLines(file);
                // 检查文件是否有足够的行数
                if (lines.Length > linesToRemove)
                {
                    // 创建一个新的数组，跳过前7行
                    string[] newLines = new string[lines.Length - linesToRemove];
                    Array.Copy(lines, linesToRemove, newLines, 0, newLines.Length);

                    // 写回文件
                    File.WriteAllLines(file, newLines);

                    Console.WriteLine($"成功处理文件: {file}");
                }
                else
                {
                    Console.WriteLine($"文件行数不足以删除: {file}");
                }
            }

            Console.WriteLine("所有脚本文件的前7行已删除！");
        }
        private static void AddCommentsToFile(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);

            //检查文件是否已经有作者注释
            // if (lines.Length > 0 && lines[0].StartsWith("/*"))
            // {
            //     Debug.Log($"File {filePath} already contains author comments, skipping.");
            //     return;
            // }

            // 添加注释行
            string a = "/**************************************************"; // 这里可以替换为你的名字
            string b = " *";
            string w = " * Copyright (c) 2024 WangJian ";
            string j = " * Licensed under the MIT License. See LICENSE file in the project root for full license information.";
            string c = " * author       : WangJian";
            string d = " * create date  : 2024 11 05";
            string e = " * description  : Core";
            string f = " *";
            string g = "***************************************************/ "; // 这里可以替换为你的名字
            string[] newLines = new string[lines.Length + 9];
            newLines[0] = a;
            newLines[1] = b;
            newLines[2] = w;
            newLines[3] = j;
            newLines[4] = c;
            newLines[5] = d;
            newLines[6] = e;
            newLines[7] = f;
            newLines[8] = g;
            Array.Copy(lines, 0, newLines, 9, lines.Length);

            // 写入文件
            File.WriteAllLines(filePath, newLines);
            Debug.Log($"Comments added to {filePath}");
        }
    }
}

