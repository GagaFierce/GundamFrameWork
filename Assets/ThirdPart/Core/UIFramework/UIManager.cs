/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework UIManager
 * 
 *****************************************************************************/
using Core.ResLoad;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Core.UIFramework
{

    public class UIManager : Singleton.MonoSingleton<UIManager>
    {
        Func<string, GameObject> mLoadResFunc;
        Action<UnityEngine.UI.Image> mLoadSpriteResFunc;
        GameObject mGoParent;
        Camera mUICamera;
        Dictionary<string, IPage> mUIList = new Dictionary<string, IPage>();

        public void Init(GameObject goParent, Camera camera)
        {
            mGoParent = goParent;
            mUICamera = camera;
        }

        public Camera GetUICamera()
        {
            return mUICamera;
        }

        public GameObject GetCanvas()
        {
            return mGoParent;
        }

        public void SetLoadResFunc(Func<string, GameObject> loadResFunc, Action<UnityEngine.UI.Image> loadSpriteResFunc)
        {
            mLoadResFunc = loadResFunc;
            mLoadSpriteResFunc = loadSpriteResFunc;
        }

        public Vector2 WorldPositionConvertToUIPosition(Transform parentTFInUI, Vector3 worldPosition)
        {
            Camera uicamera = GetUICamera();
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(Camera.allCameras[0], worldPosition);
            //screenPos.x = Mathf.Clamp(screenPos.x, 0, Screen.width);
            //screenPos.y = Mathf.Clamp(screenPos.y, 0, Screen.height);
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)parentTFInUI, screenPos, uicamera ? uicamera : Camera.allCameras[1], out localPoint);
            return localPoint;
        }

        public Vector2 UIPositionConvertToUIPosition(Transform parentTFInUI, Vector3 worldPosition)
        {
            Camera uicamera = GetUICamera();
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uicamera ? uicamera : Camera.allCameras[1], worldPosition);
            //screenPos.x = Mathf.Clamp(screenPos.x, 0, Screen.width);
            //screenPos.y = Mathf.Clamp(screenPos.y, 0, Screen.height);
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)parentTFInUI, screenPos, uicamera ? uicamera : Camera.allCameras[1], out localPoint);
            return localPoint;
        }

        public Vector2 WorldPositionConvertToUIPosition(Transform parentTFInUI, Vector3 worldPosition, Vector2 screenPosOffset)
        {
            Camera uicamera = GetUICamera();
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(Camera.allCameras[0], worldPosition);
            //screenPos.x = Mathf.Clamp(screenPos.x, 0, Screen.width);
            //screenPos.y = Mathf.Clamp(screenPos.y, 0, Screen.height);
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)parentTFInUI, screenPos + screenPosOffset, uicamera ? uicamera : Camera.allCameras[1], out localPoint);
            return localPoint;
        }

        public Vector2 WorldPositionConvertToUIPosition(GameObject parentNodeInUI, Transform tfInScene)
        {
            return WorldPositionConvertToUIPosition(parentNodeInUI.transform, tfInScene.position);
        }

        public 
        
        //private FontManager _fontManager;
        // ReSharper disable Unity.PerformanceAnalysis

        void ReplaceSprite(GameObject go)
        {
            UnityEngine.UI.Image[] images = go.GetComponentsInChildren<UnityEngine.UI.Image>();
            for (int i = 0; i < images.Length; ++i)
            {
                mLoadSpriteResFunc?.Invoke(images[i]);
            }
        }

        public IPage OpenUI(string uiname, object param = null)
        {
            //bool bNeedInit = false;
            IPage page;
            if (!mUIList.TryGetValue(uiname, out page))
            {
                //bNeedInit = true;
                string filebasename = Path.GetFileNameWithoutExtension(uiname);
                GameObject uiGo = (mLoadResFunc != null ? mLoadResFunc.Invoke(filebasename) : ResourcesManagerImpl.Instance.CreateGameObject(uiname));
                //ReplaceSprite(uiGo);
                var objRect = uiGo.GetComponent<RectTransform>();
                objRect.SetParent(mGoParent.transform);
                objRect.localPosition = new Vector3(0, 0, 0);
                objRect.localScale = new Vector3(1, 1, 1);
                var pivotVec2 = new Vector2(0.5f, 0.5f);
                objRect.anchorMin = Vector2.zero;
                objRect.anchorMax = Vector2.one;
                objRect.pivot = pivotVec2;
                objRect.localPosition = Vector3.zero;
                objRect.localScale = Vector3.one;
                objRect.sizeDelta = Vector2.zero;

                page = uiGo.GetComponent<IPage>();
                if (null == page)
                {
                    page = uiGo.AddComponent<UIPageImplForLua>();
                }

                page.SetUIName(uiname);
                page.SetParams(param);
                mUIList[uiname] = page;

                //if (_fontManager == null)
                //{
                //    _fontManager = FindObjectOfType<FontManager>();
                //}
                //if (_fontManager != null)
                //{
                //    _fontManager.SetUIFont(uiGo);
                //}
            }


            //if (page.IsVisible() && !bNeedInit)
            //{
            //    page.OnRefresh();
            //}
            //else
            //{
            //    uiGo.SetActive(true);
            //    if (bNeedInit)
            //    {
            //        List<UIComponent> uicompList = new List<UIComponent>();
            //        GetUIComponentList(uicompList, uiGo);
            //        for (int i = 0; i < uicompList.Count; ++i)
            //        {
            //            uicompList[i].OnInit();
            //        }
            //    }
            //    uicomp.OnOpen();
            //}

            return page;
        }

        //void GetUIComponentList(List<UIComponent> ls, GameObject go)
        //{
        //    UIComponent uicomp = go.GetComponent<UIComponent>();
        //    if (uicomp)
        //    {
        //        ls.Add(uicomp);
        //    }

        //    for (int i = 0; i < go.transform.childCount; ++i)
        //    {
        //        GetUIComponentList(ls, go.transform.GetChild(i).gameObject);
        //    }
        //}

        public void CloseUIByName(string uiname)
        {
            if (null != uiname && mUIList.TryGetValue(uiname, out IPage page))
            {
                //List<UIComponent> uicompList = new List<UIComponent>();
                //GetUIComponentList(uicompList, go);
                //for (int i = 0; i < uicompList.Count; ++i)
                //{
                //    uicompList[i].OnClose();
                //}
                mUIList.Remove(uiname);
                Destroy(page.GetGameObject());
                ResourcesManagerImpl.Instance.UnloadUnusedResources();
            }
        }

        public void CloseUI(IPage page)
        {
            CloseUIByName(page.GetUIName());
            ResourcesManagerImpl.Instance.UnloadUnusedResources();
        }

        public void RefreshUI(string uiname, object param = null)
        {
            if (mUIList.TryGetValue(uiname, out IPage page))
            {
                if (page.IsVisible())
                {
                    //UIComponent uicomp = go.GetComponent<UIComponent>();
                    if (null != param)
                        page.SetParams(param);
                    page.OnRefresh();
                }
            }
        }

        public IPage GetUI(string uiname)
        {
            if (mUIList.TryGetValue(uiname, out IPage page))
            {
                return page;
            }
            return null;
        }

        public void CloseAllUI()
        {
            foreach (var v in mUIList)
            {
                if (v.Value.IsVisible())
                {
                    //List<UIComponent> uicompList = new List<UIComponent>();
                    //GetUIComponentList(uicompList, v.Value);
                    //for (int i = 0; i < uicompList.Count; ++i)
                    //{
                    //    uicompList[i].OnClose();
                    //}
                    Destroy(v.Value.GetGameObject());
                }
            }

            mUIList.Clear();
            ResourcesManagerImpl.Instance.UnloadUnusedResources();
        }
    }
}

