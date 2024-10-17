/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework ICell UICell
 * 
 *****************************************************************************/
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.UIFramework
{
    public interface ICell : INode
    {
        public int GetCellIndex();
        public void SetCellIndex(int index);
        public void SetCellData(object data);
        public void OnRefreshCell();
    }

    public class UICell : UIPageImplForLua, ICell/*, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler*/
    {
        protected int mCellIndex = -1;
        protected object mCellData = null;
        protected override void InitLuaFuncs()
        {
            base.InitLuaFuncs();
            mFuncList["OnRefreshCell"] = null;
            Action<object, int, object> funcRefreshCell = null;
            mLuaTable?.Get("OnRefreshCell", out funcRefreshCell);
            if (null != funcRefreshCell)
            {
                mFuncList["OnRefreshCell"] = funcRefreshCell;
            }
        }

        public int GetCellIndex()
        {
            return mCellIndex;
        }

        public void SetCellIndex(int index)
        {
            mCellIndex = index;
        }

        public void SetCellData(object data)
        {
            mCellData = data;
        }

        public virtual void OnRefreshCell()
        {
            Debug.Log($"OnRefreshCell index={mCellIndex}");
            if (null != mFuncList && mFuncList.TryGetValue("OnRefreshCell", out object callback))
            {
                var func = (Action<object, int, object>)callback;
                func?.Invoke(mLuaTable, mCellIndex, mCellData);
            }
        }

        //public override void _OnBeginDrag(PointerEventData eventData)
        //{
        //    PassEvent(eventData, ExecuteEvents.beginDragHandler);
        //}

        //public override void _OnDrag(PointerEventData eventData)
        //{
        //    PassEvent(eventData, ExecuteEvents.dragHandler);
        //}

        //public override void _OnEndDrag(PointerEventData eventData)
        //{
        //    PassEvent(eventData, ExecuteEvents.endDragHandler);
        //}

        //public void PassEvent<T>(PointerEventData data, ExecuteEvents.EventFunction<T> function, Transform current) where T : IEventSystemHandler
        //{
        //    GameObject nextGo = ExecuteEvents.GetEventHandler<T>(current.parent.gameObject);
        //    ExecuteEvents.Execute(nextGo, data, function);
        //}

        //public void OnPointerDown(PointerEventData eventData)
        //{
        //    PassEvent(eventData, ExecuteEvents.pointerDownHandler);
        //}


        //public void OnPointerUp(PointerEventData eventData)
        //{
        //    PassEvent(eventData, ExecuteEvents.pointerUpHandler);
        //}


        //public void OnPointerClick(PointerEventData eventData)
        //{
        //    PassEvent(eventData, ExecuteEvents.submitHandler);
        //    PassEvent(eventData, ExecuteEvents.pointerClickHandler);
        //}

        //public void PassEvent<T>(PointerEventData data, ExecuteEvents.EventFunction<T> function) where T : IEventSystemHandler
        //{
        //    List<RaycastResult> results = new List<RaycastResult>();
        //    EventSystem.current.RaycastAll(data, results);
        //    GameObject current = data.pointerCurrentRaycast.gameObject;
        //    for (int i = 0; i < results.Count; i++)
        //    {
        //        if (current != results[i].gameObject)
        //        {
        //            ExecuteEvents.Execute(results[i].gameObject, data, function);
        //            if (results[i].gameObject.GetComponent<UIScrollviewEx>())
        //            {
        //                break;
        //            }
        //            //RaycastAll后ugui会自己排序，如果你只想响应透下去的最近的一个响应，这里ExecuteEvents.Execute后直接break就行。
        //        }
        //    }
        //}
    }
}


