/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework UIPageImplForLua
 * 
 *****************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Core.UIFramework
{
    public class UIPageImplForLua : UIPageBase
    {

        protected Dictionary<string, object> mFuncList = new Dictionary<string, object>();

#if UNITY_EDITOR
        public UINode[] GetBindList()
        {
            return mBindList;
        }

        public void SetBindList(UINode[] bindList)
        {
            mBindList = bindList;
        }

        public string GetLuaFile()
        {
            return mLuaFile;
        }

        public void SetLuaFile(string luafile)
        {
            mLuaFile = luafile;
        }
#endif

        public override void SetLuaTable(XLua.LuaTable luaTable)
        {
            base.SetLuaTable(luaTable);
            InitBinds();
        }

        protected virtual void InitLuaFuncs()
        {
            mFuncList.Clear();
            mFuncList["OnInit"] = null;
            mFuncList["OnOpen"] = null;
            mFuncList["OnShow"] = null;
            mFuncList["OnHide"] = null;
            mFuncList["OnRefresh"] = null;
            mFuncList["OnClose"] = null;
            
            List<string> keys = mFuncList.Keys.ToList();
            foreach (var key in keys)
            {
                Action<object> func = null;
                mLuaTable?.Get(key, out func);
                if (null != func)
                {
                    mFuncList[key] = func;
                }
            }

            mFuncList["OnReceiveEvent"] = null;
            Action<object, int, object> funcEvt = null;
            mLuaTable?.Get("OnReceiveEvent", out funcEvt);
            if (null != funcEvt)
            {
                mFuncList["OnReceiveEvent"] = funcEvt;
            }

            mFuncList["OnUpdate"] = null;
            Action<object, float> funcUpdate = null;
            mLuaTable?.Get("OnUpdate", out funcUpdate);
            if (null != funcUpdate)
            {
                mFuncList["OnUpdate"] = funcUpdate;
            }

            mFuncList["OnAnimEventsCallback"] = null;
            Action<object, string, string> funcAnimEvt = null;
            mLuaTable?.Get("OnAnimEventsCallback", out funcAnimEvt);
            if (null != funcAnimEvt)
            {
                mFuncList["OnAnimEventsCallback"] = funcAnimEvt;
            }

            mLuaTable?.Set("mSelfPage", this);
        }

        public override void InitBinds()
        {
            base.InitBinds();
            InitLuaFuncs();
            foreach (var v in mBindList)
            {
                XLua.LuaTable luaTable = v.GetLuaTable();
                if (null != luaTable)
                {
                    UIPageBase pb = v as UIPageBase;
                    if (pb)
                        pb.InitBinds();
                    mLuaTable?.Set(v.GetGameObject().name, luaTable);
                }
                else
                {
                    mLuaTable?.Set(v.GetGameObject().name, v);
                }
            }
        }

        protected override void OnDispose()
        {
            base.OnDispose();
            mLuaTable = null;
            mFuncList.Clear();
        }

        void Update()
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnUpdate", out object callback))
            { 
                var func = (Action<object, float>)callback;
                func?.Invoke(mLuaTable, UnityEngine.Time.deltaTime);
            }
        }

        protected override void _OnInit()
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnInit", out object callback))
            {
                var func = (Action<object>)callback;
                func?.Invoke(mLuaTable);
            }
        }

        protected override void _OnOpen()
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnOpen", out object callback))
            {
                var func = (Action<object>)callback;
                func?.Invoke(mLuaTable);
            }
        }

        protected override void _OnShow()
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnShow", out object callback))
            {
                var func = (Action<object>)callback;
                func?.Invoke(mLuaTable);
            }
        }

        protected override void _OnRefresh()
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnRefresh", out object callback))
            {
                var func = (Action<object>)callback;
                func?.Invoke(mLuaTable);
            }
        }

        protected override void _OnHide()
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnHide", out object callback))
            {
                var func = (Action<object>)callback;
                func?.Invoke(mLuaTable);
            }
        }

        protected override void _OnClose()
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnClose", out object callback))
            {
                var func = (Action<object>)callback;
                func?.Invoke(mLuaTable);
            }
        }

        protected override void _ReceiveEvent(int msgId, object msgInfo)
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnReceiveEvent", out object callback))
            {
                var func = (Action<object, int, object>)callback;
                func?.Invoke(mLuaTable, msgId, msgInfo);
            }
        }

        protected override void _OnAnimEventsCallback(string animname, string eventname)
        {
            if (null != mFuncList && mFuncList.TryGetValue("OnAnimEventsCallback", out object callback))
            {
                var func = (Action<object, string, string>)callback;
                func?.Invoke(mLuaTable, animname, eventname);
            }
        }
    }
}
