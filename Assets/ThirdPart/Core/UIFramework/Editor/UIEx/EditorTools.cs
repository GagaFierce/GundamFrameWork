/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 07 28
 * description  : editor tools
 * 
 *****************************************************************************/

using Core.UIFramework;
using UnityEditor;
using UnityEngine;

public class EditorTools
{
    //[MenuItem("MyTools/Open CSV Directory")]
    private static void OnOpenCSVDirectory()
    {
        string dir = Application.persistentDataPath + "/csv/";
        EditorUtility.RevealInFinder(dir);
    }

    [MenuItem("GameObject/UI/UIButtonEx", priority = 0)]
    static void CreateButtonEx(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("UIButtonEx");
        go.AddComponent<UIButtonEx>();
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create_" + go.GetHashCode());
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/UI/UIImageEx", priority = 0)]
    static void CreateImageEx(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("UIImageEx");
        go.AddComponent<UIImageEx>();
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create_" + go.GetHashCode());
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/UI/UITextEx", priority = 0)]
    static void CreateTextEx(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("UITextEx");
        go.AddComponent<UITextEx>();
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create_" + go.GetHashCode());
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/UI/UIScrollviewEx", priority = 0)]
    static void CreateScrollviewEx(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("UIScrollviewEx");
        go.AddComponent<UIScrollviewEx>();

        GameObject goView = new GameObject("Viewport");
        goView.AddComponent<UnityEngine.UI.Image>();
        goView.AddComponent< UnityEngine.UI.Mask> ();
        goView.transform.SetParent(go.transform);

        ((RectTransform)goView.transform).pivot = new Vector2(0, 1);
        ((RectTransform)goView.transform).anchorMin = Vector2.zero;
        ((RectTransform)goView.transform).anchorMax = Vector2.one;
        ((RectTransform)goView.transform).sizeDelta = Vector2.zero;

        GameObject goContent = new GameObject("Content");
        RectTransform rrt = goContent.AddComponent<RectTransform>();
        goContent.transform.SetParent(goView.transform);
        rrt.pivot = new Vector2(0, 1);
        rrt.anchorMin = new Vector2(0, 1);
        rrt.anchorMax = Vector2.one;
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;
        rrt.sizeDelta = new Vector2(0, 200);

        UnityEngine.UI.ScrollRect sr = go.GetComponent<UnityEngine.UI.ScrollRect>();
        sr.viewport = (RectTransform)goView.transform;
        sr.content = (RectTransform)goContent.transform;

        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create_" + go.GetHashCode());
        Selection.activeObject = go;
    }
}