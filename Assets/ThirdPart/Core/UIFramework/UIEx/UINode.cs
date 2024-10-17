/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework INode UINode
 * 
 *****************************************************************************/
using System.Collections;
using UnityEngine;
using System;
using UnityEngine.EventSystems;

namespace Core.UIFramework
{
    public interface INode
    {
        public GameObject GetGameObject();
        public RectTransform GetRectTransform();
        public void SetLocalPosition(Vector3 pos);
        public void SetPosition(Vector3 pos);
        public Vector3 GetPosition();
        public Vector2 GetUISize();
        public Vector2 GetUIPos();
        public void SetVisible(bool bVisible);
        public bool IsVisible();
        //public void SetLuaTable(XLua.LuaTable luaTable);
        //public void SetClickedCallback(UnityAction callback);
        public void SetClickedCallback(Action<object> callback);
        public void SetPointerEventCallback(Action<string, PointerEventData> callback);
        public Coroutine DelayDocall(float delayTime, System.Action callback);
        public void StopDelayDocall(Coroutine coroutine);
        public void StopAllDelayDocall();
        public void SetLuaTable(XLua.LuaTable luaTable);
        public XLua.LuaTable GetLuaTable();
        public void LoadLuaFile();
        public void MoveToLocalPosition(Vector3 pos, float time);
        public void MoveToWorldPosition(Vector3 pos, float time);
        public void MoveToAlpha(float alpha, float time);

        //public GameObject AddChild(GameObject go);
        //public void RemoveChildren();
        //public int GetChildCount();
        //public GameObject GetChild(int index);
        
    }


    //========================================================================
    [RequireComponent(typeof(GameObject))]
    public class UINode: UIBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, INode
    {
        protected XLua.LuaTable mLuaTable = null;
        protected Action<object> mClickedCallback = null;
        protected Action<string, PointerEventData> mPointerEventCallback = null;
        protected bool mbInit = false;

        protected override void Awake()
        {
            base.Awake();
            LoadLuaFile();
        }

        public virtual void LoadLuaFile()
        { 
        }

        protected override void Start()
        {
            base.Start();
            mbInit = true;
            OnInit();
        }

        protected override void OnDestroy()
        {
            mClickedCallback = null;
            mPointerEventCallback = null;
            OnDispose();
            StopAllDelayDocall();
            base.OnDestroy();
        }

        protected override void OnDisable()
        {
            OnHide();
            base.OnDisable();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (mbInit)
                OnShow();
            else
                LoadLuaFile();
        }

        protected virtual void OnInit()
        {
            
        }

        protected virtual void OnDispose()
        {
            
        }

        public virtual void OnShow()
        { 
        }

        public virtual void OnHide()
        {
        }

        public virtual void SetLuaTable(XLua.LuaTable luaTable)
        {
            mLuaTable = luaTable;
        }

        public virtual XLua.LuaTable GetLuaTable()
        {
            return mLuaTable;
        }

        public GameObject GetGameObject()
        {
            return gameObject;
        }

        public RectTransform GetRectTransform()
        {
            return gameObject.GetComponent<RectTransform>();
        }

        public void SetLocalPosition(Vector3 pos)
        {
            gameObject.transform.localPosition = pos;
        }

        public void SetPosition(Vector3 pos)
        {
            gameObject.transform.position = pos;
        }

        public Vector3 GetPosition()
        {
            return transform.position;
        }

        public Vector2 GetUISize()
        {
            RectTransform tf = (RectTransform)transform;
            return tf.sizeDelta;
        }

        public Vector2 GetUIPos()
        {
            RectTransform tf = (RectTransform)transform;
            return tf.anchoredPosition;
        }

        public virtual void SetVisible(bool bVisible)
        {
            if (IsVisible() == bVisible)
                return;
            gameObject.SetActive(bVisible);
            //if (bVisible)
            //    OnShow();
            //else
            //    OnHide();
        }

        public virtual bool IsVisible()
        {
            return gameObject.activeSelf;
        }

        public virtual void OnClickedCallback()
        {
            mClickedCallback?.Invoke(this);
        }

        public void SetClickedCallback(Action<object> callback)
        {
            mClickedCallback = callback;
        }

        public void SetPointerEventCallback(Action<string, PointerEventData> callback)
        {
            mPointerEventCallback = callback;
        }

        IEnumerator DelayDo(float delay, System.Action callback)
        {
            yield return new WaitForSeconds(delay);
            callback?.Invoke();
        }

        public Coroutine DelayDocall(float delayTime, System.Action callback)
        {
            if (gameObject.activeInHierarchy)
                return StartCoroutine(DelayDo(delayTime, callback));
            return null;
        }

        public void StopDelayDocall(Coroutine coroutine)
        {
            if (gameObject.activeInHierarchy)
            {
                StopCoroutine(coroutine);
            }
        }

        public void StopAllDelayDocall()
        {
            if (gameObject.activeInHierarchy)
            {
                StopAllCoroutines();
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            mPointerEventCallback?.Invoke("OnBeginDrag", eventData);
            _OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            mPointerEventCallback?.Invoke("OnDrag", eventData);
            _OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            mPointerEventCallback?.Invoke("OnEndDrag", eventData);
            _OnEndDrag(eventData);
        }

        public virtual void OnPointerDown(PointerEventData eventData)
        {
            mPointerEventCallback?.Invoke("OnPointerDown", eventData);
        }

        public virtual void OnPointerUp(PointerEventData eventData)
        {
            mPointerEventCallback?.Invoke("OnPointerUp", eventData);
        }

        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            mPointerEventCallback?.Invoke("OnPointerEnter", eventData);
        }

        public virtual void OnPointerExit(PointerEventData eventData)
        {
            mPointerEventCallback?.Invoke("OnPointerExit", eventData);
        }

        public virtual void _OnBeginDrag(PointerEventData eventData) { }

        public virtual void _OnDrag(PointerEventData eventData) { }

        public virtual void _OnEndDrag(PointerEventData eventData) { }

        public void MoveToLocalPosition(Vector3 pos, float time)
        {
          //  DG.Tweening.DOTween.To(() => transform.localPosition, x => transform.localPosition = x, pos, time);
        }
        public void MoveToWorldPosition(Vector3 pos, float time)
        {
          //  DG.Tweening.DOTween.To(() => transform.position, x => transform.position = x, pos, time);
        }

        public void MoveToAlpha(float alpha, float time)
        {
            CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup)
            {
             //   DG.Tweening.DOTween.To(() => canvasGroup.alpha, x => canvasGroup.alpha = x, alpha, time);
                //DG.Tweening.DOTween.ToAlpha(() => canvasGroup.alpha, x => canvasGroup.alpha = x, pos, time);
            }
        }

        //public GameObject AddChild(GameObject go)
        //{
        //    go.transform.SetParent(transform);
        //    return go;
        //}
        //public void RemoveChildren()
        //{ 
        //}
        //public int GetChildCount()
        //{
        //    return transform.childCount;
        //}
        //public GameObject GetChild(int index)
        //{
        //    return transform.GetChild(index).gameObject;
        //}
    }
}