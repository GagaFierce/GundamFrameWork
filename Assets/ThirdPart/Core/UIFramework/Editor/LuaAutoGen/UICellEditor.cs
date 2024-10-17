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

[CustomEditor(typeof(UICell))]
public class UICellEditor : UIPageImplForLuaEditor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
    }

    protected override string GetLuaFileTemplate()
    {
        const string luaTemplate = "Assets/ThirdPart/Core/UIFramework/Editor/LuaAutoGen/TemplateFiles/LuaTemplate_Cell.txt";
        return luaTemplate;
    }
}
