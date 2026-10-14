/**************************************************
 *
 * Copyright (c) 2024 WangJian
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
 ***************************************************/
#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


namespace WFrameWork.Core.Editor
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

            string[] files = Directory.GetFiles(folderPath, "*.cs", SearchOption.AllDirectories);
            var changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                try
                {
                    byte[] original = File.ReadAllBytes(file);
                    byte[] transformed = CommentTextTransformer.TransformSource(original);
                    if (!StructuralComparisons.StructuralEqualityComparer.Equals(original, transformed)) changes.Add(file, transformed);
                }
                catch (Exception error)
                {
                    Debug.LogError($"Cannot safely transform {file}: {error.Message}");
                    return;
                }
            }
            if (changes.Count == 0) { Debug.Log("No source files require comment changes."); return; }
            if (!EditorUtility.DisplayDialog("Add source comments", "Files to update: " + changes.Count + "\nA .bak backup will be created for each file.", "Apply", "Cancel")) return;
            foreach (var change in changes) AddCommentsToFile(change.Key, change.Value);

            Debug.Log("注释已添加到所有脚本文件！");
        }

        private static void AddCommentsToFile(string filePath, byte[] transformed)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (StructuralComparisons.StructuralEqualityComparer.Equals(bytes, transformed)) return;
            File.Copy(filePath, filePath + ".bak", true);
            File.WriteAllBytes(filePath, transformed);
            Debug.Log($"Comments added to {filePath}");
        }
    }
}
#endif
