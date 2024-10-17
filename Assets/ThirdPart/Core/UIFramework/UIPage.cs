/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework IPage UIPageBase
 * 
 *****************************************************************************/


using UnityEngine;

namespace Core.UIFramework
{
    class UIAnimEvent : Anim.AnimEventBase
    {
        bool mbInit = false;
        public override void OnInit()
        {
            if (!mbInit)
            {
                mbInit = true;
                base.OnInit();
            }
        }

        public override void OnDispose()
        {
            mbInit = false;
            base.OnDispose();
        }

        protected override string GetAnimEventsCallback()
        {
            return "OnUIAnimEventsCallback";
        }
    }

    //=======================================================================================================================

    public interface IPage : INode
    {
        public void SetUIName(string name);
        public string GetUIName();
        public void SetParams(object param);
        public object GetParams();
        //public void OnOpen();
        public void OnRefresh();
        //public void OnClose();
        //public void SetLuaTable(XLua.LuaTable luaTable);

        public void SetEnableAnimEvents(bool bEnable);
        public void PlayAnim(string animname);
        public bool AddAnimEvent(string animName, string eventName, float keytime);

    }

    //=======================================================================================================================

    public abstract class UIPageBase : UINode, IPage, Core.Event.IEvent
    {
        [SerializeField] protected string mLuaFile;
        [SerializeField] protected UINode[] mBindList;

        protected string mUIName;
        protected object mParams;
        protected Animator mAnimator;
        bool mEnableAnimEvent = false;

        protected override void Awake()
        {
            base.Awake();
            mAnimator = GetComponent<Animator>();
        }

        public void SetUIName(string name)
        {
            mUIName = name;
        }

        public string GetUIName()
        {
            return mUIName;
        }

        public void SetParams(object param)
        {
            mParams = param;
        }

        public object GetParams()
        {
            return mParams;
        }


        protected override void OnInit()
        {
            _OnInit();
            OnOpen();
        }

        public override void LoadLuaFile()
        {
            XLua.LuaTable luaTable = null;
            if (null == mLuaTable && !string.IsNullOrEmpty(mLuaFile))
            {
                luaTable = Core.ResLoad.LuaManagerImpl.Instance.GeLuaTable(mLuaFile);
            }

            foreach (var v in mBindList)
            {
                v.LoadLuaFile();
            }

            if (null != luaTable)
            {
                SetLuaTable(luaTable);
            }
        }

        public virtual void InitBinds()
        {
        }

        protected override void OnDispose()
        {
            OnClose();
        }

        public virtual void OnOpen()
        {
            _OnOpen();
            OnShow();
        }

        public override void OnShow()
        {
            _OnShow();
        }

        public override void OnHide()
        {
            _OnHide();
        }

        public virtual void OnClose()
        {
            _OnClose();
        }

        public virtual void OnRefresh()
        {
            _OnRefresh();
        }

        public virtual void ReceiveEvent(int msgId, object msgInfo)
        {
            _ReceiveEvent(msgId, msgInfo);
        }

        public virtual void OnAnimEventsCallback(string animname, string eventname)
        {
            _OnAnimEventsCallback(animname, eventname);
        }

        public void AddEvent(int msgId)
        {
            Event.EventManagerImpl.Instance.GetUIEventMgr().AddEvent(msgId, this);
        }
        public void RemoveEvent(int msgId)
        {
            Event.EventManagerImpl.Instance.GetUIEventMgr().RemoveEvent(msgId, this);
        }

        public void RemoveAllEvent()
        {
            Event.EventManagerImpl.Instance.GetUIEventMgr().RemoveAllEvent(this);
        }

        public void SetEnableAnimEvents(bool bEnable)
        {
            if (mEnableAnimEvent != bEnable)
            {
                mEnableAnimEvent = bEnable;
                if (mAnimator)
                {
                    if (bEnable)
                    {
                        InitAnimEvent();
                    }
                    else
                    {
                        UIAnimEvent animEvent = mAnimator.gameObject.GetComponent<UIAnimEvent>();
                        if (animEvent)
                            animEvent.OnDispose();
                    }
                }
            }
        }

        protected void InitAnimEvent()
        {
            if (mAnimator && mEnableAnimEvent)
            {
                UIAnimEvent animEvent = mAnimator.gameObject.GetComponent<UIAnimEvent>();
                if (!animEvent)
                {
                    animEvent = mAnimator.gameObject.AddComponent<UIAnimEvent>();
                }
                animEvent.SetAnimEventsCallback(OnAnimEventsCallback);
                animEvent.OnInit();
            }
        }

        public void PlayAnim(string animname)
        {
            if (gameObject.activeInHierarchy && mAnimator)
                mAnimator.Play(animname);
        }

        public bool AddAnimEvent(string animName, string eventName, float keytime)
        {
            if (mAnimator && mEnableAnimEvent)
            {
                UIAnimEvent animEvent = mAnimator.gameObject.GetComponent<UIAnimEvent>();
                if (animEvent)
                {
                    return animEvent.AddAnimEvent(animName, eventName, keytime);
                }
            }
            return false;
        }

        protected abstract void _OnInit();
        protected abstract void _OnOpen();
        protected abstract void _OnShow();
        protected abstract void _OnRefresh();
        protected abstract void _OnHide();
        protected abstract void _OnClose();
        protected abstract void _ReceiveEvent(int msgId, object msgInfo);
        protected abstract void _OnAnimEventsCallback(string animname, string eventname);
    }

}



