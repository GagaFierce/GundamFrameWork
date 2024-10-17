/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework IScrollview UIScrollviewEx
 * 
 *****************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.UIFramework
{
    public interface IScrollview : INode
    {
        public void ClearCells(int count = -1);
        public void SetCellCount(int count);
        public int GetCellCount();
        public void SetCellData(int index, object data, bool bRefreshCell = false);
        public object GetCellData(int index);
        public void RefreshCells();
        //public void AddCell(object data);
    }

    //========================================================================================

    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    [RequireComponent(typeof(ScrollRect))]
    public class UIScrollviewEx : UINode, IScrollview
    {
        [SerializeField] int mDefaultCount = 10;
        [SerializeField] GameObject mCellTemplate;
        [SerializeField] ScrollMoveDir mScrollMoveDir = ScrollMoveDir.VERTICAL;
        [SerializeField] Vector2 mSpacing = Vector2.zero;
        [SerializeField] RectOffset mPadding;

        public enum ScrollMoveDir
        {
            VERTICAL = 0,
            HORIZONTAL,
        }

        public struct CellDataInfo
        {
            public object data;
        }

        List<CellDataInfo> mCellDataInfos = new List<CellDataInfo>();

        ScrollRect mCompScrollRect = null;
        GridLayoutGroup mGridLayoutGroup = null;
        int mHeadIndex = 0;
        int mTailIndex = 0;
        int mCellCountX = 0;
        int mCellCountY = 0;
        List<RectTransform> mCellTFList = new List<RectTransform>();

        Transform mPoolTransform = null;

        protected override void Awake()
        {
            base.Awake();
            mCompScrollRect = gameObject.GetComponent<ScrollRect>();
            mCompScrollRect.onValueChanged.AddListener(OnScrolling);
            mGridLayoutGroup = GetGridLayoutGroup(true);

            GameObject go = new GameObject("CellPool");
            go.transform.SetParent(mCompScrollRect.transform);
            go.SetActive(false);
            mPoolTransform = go.transform;
            for (int i = 0; i < mDefaultCount; ++i)
            {
                GameObject goCell = Instantiate(mCellTemplate, mPoolTransform);
                goCell.SetActive(false);
            }

            RectTransform rt = (RectTransform)mCellTemplate.transform;

            mGridLayoutGroup.spacing = mSpacing;
            mGridLayoutGroup.padding = mPadding;
            mGridLayoutGroup.cellSize = rt.sizeDelta;
            switch (mScrollMoveDir)
            {
                case ScrollMoveDir.VERTICAL:
                    mCompScrollRect.vertical = true;
                    mCompScrollRect.horizontal = false;
                    break;
                case ScrollMoveDir.HORIZONTAL:
                    mCompScrollRect.vertical = false;
                    mCompScrollRect.horizontal = true;
                    break;
            }
        }

        void InitItem(int instNum)
        {
            mCellTFList.Clear();
            for (int i = 0; i < instNum; i++)
            {
                GameObject go = GetCellGameObject();
                mCellTFList.Add(go.GetComponent<RectTransform>());
                UICell cell = go.GetComponent<UICell>();
                if (cell)
                {
                    cell.SetCellIndex(i);
                }
            }
        }

        GameObject GetCellGameObject()
        {
            //if (mPoolTransform.transform.childCount > 0)
            //{
            //    Transform tf = mPoolTransform.transform.GetChild(0);
            //    tf.gameObject.SetActive(true);
            //    tf.SetParent(mCompScrollRect.content);
            //    return tf.gameObject;
            //}
                
            return Instantiate(mCellTemplate, mCompScrollRect.content);
        }

        GridLayoutGroup GetGridLayoutGroup(bool bAdd = false)
        {
            GridLayoutGroup layout = null;
            RectTransform content = mCompScrollRect.content;
            if (content)
            {
                layout = content.gameObject.GetComponent<GridLayoutGroup>();
                if (!layout && bAdd)
                    layout = content.gameObject.AddComponent<GridLayoutGroup>();
            }
            return layout;
        }

        void SetContentSize()
        {
            int totalCount = GetCellCount();
            Vector2 viewSize = mCompScrollRect.viewport.rect.size;
            RectTransform content = mCompScrollRect.content;
            if (content)
            {
                content.anchorMin = Vector2.up;
                content.anchorMax = Vector2.up;

                float width = 0;
                float height = 0;
                GridLayoutGroup layout = GetGridLayoutGroup();
                switch (mScrollMoveDir)
                {
                    case ScrollMoveDir.VERTICAL:
                        {
                            mCellCountX = Math.Max(1, 1 + (int)(viewSize.x - layout.padding.left - layout.padding.right - layout.cellSize.x) / (int)(layout.cellSize.x + layout.spacing.x));
                            if ((int)(viewSize.x - layout.padding.left - layout.padding.right - layout.cellSize.x) % (int)(layout.cellSize.x + layout.spacing.x) > 0)
                            {
                                mCellCountX += 1;
                            }
                            mCellCountY = (int)Math.Ceiling((float)totalCount / mCellCountX);
                            width = viewSize.x;
                            height = layout.padding.top + layout.padding.bottom + mCellCountY * (layout.cellSize.y + layout.spacing.y) - layout.spacing.y;
                        }
                        break;
                    case ScrollMoveDir.HORIZONTAL:
                        {
                            mCellCountY = Math.Max(1, 1 + (int)(viewSize.y - layout.padding.top - layout.padding.bottom - layout.cellSize.y) / (int)(layout.cellSize.y + layout.spacing.y));
                            if ((int)(viewSize.y - layout.padding.top - layout.padding.bottom - layout.cellSize.y) % (int)(layout.cellSize.y + layout.spacing.y) > 0)
                            {
                                mCellCountY += 1;
                            }
                            mCellCountX = (int)Math.Ceiling((float)totalCount / mCellCountY);
                            width = layout.padding.left + layout.padding.right + mCellCountX * (layout.cellSize.x + layout.spacing.x) - layout.spacing.x;
                            height = viewSize.y;
                        }
                        break;
                }

                content.sizeDelta = new Vector2(width, height);
                content.anchoredPosition = Vector2.zero;
            }
        }

        void SetLayout()
        {
            GridLayoutGroup layout = GetGridLayoutGroup();
            if (layout)
            {
                if (mCompScrollRect.horizontal && mCompScrollRect.vertical)
                {
                    UnityEngine.Debug.LogError("UIScrollvieEx SetLayout: only support one direction");
                }

                layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
                layout.childAlignment = TextAnchor.UpperLeft;
                switch (mScrollMoveDir)
                {
                    case ScrollMoveDir.VERTICAL:
                        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
                        layout.constraintCount = mCellCountX;
                        mCompScrollRect.horizontal = false;
                        mCompScrollRect.vertical = true;
                        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                        break;
                    case ScrollMoveDir.HORIZONTAL:
                        layout.startAxis = GridLayoutGroup.Axis.Vertical;
                        layout.constraintCount = mCellCountY;
                        mCompScrollRect.horizontal = true;
                        mCompScrollRect.vertical = false;
                        layout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                        break;
                }
            }
        }

        void OnScrolling(Vector2 pos)
        {
            int totalCount = GetCellCount();
            if (totalCount == 0)
            {
                return;
            }

            RectTransform content = mCompScrollRect.content;
            GridLayoutGroup layout = GetGridLayoutGroup();
            switch (mScrollMoveDir)
            {
                case ScrollMoveDir.VERTICAL:
                    {
                        // up
                        while (content.anchoredPosition.y >= layout.padding.top + (mHeadIndex + 1) * (layout.cellSize.y + layout.spacing.y)
                        && mTailIndex < mCellCountY)
                        {
                            mHeadIndex++;
                            mTailIndex++;
                            int nCount = mCellCountX;
                            if (mTailIndex == mCellCountY && totalCount % mCellCountX > 0)
                            {
                                nCount = totalCount % mCellCountX;
                            }

                            for (int i = 0; i < nCount; ++i)
                            {
                                int index = mCellCountX * mTailIndex + i;
                                RectTransform item = mCellTFList[0];
                                mCellTFList.Remove(item);
                                mCellTFList.Add(item);

                                SetCellPosByIndex(item, index);
                                RefreshCell(item, mCellCountX * (mTailIndex - 1) + i);
                            }
                        }

                        // down
                        while (content.anchoredPosition.y <= layout.padding.top + mHeadIndex * (layout.cellSize.y + layout.spacing.y)
                            && mHeadIndex != 0)
                        {
                            int nCount = mCellCountX;
                            if (mTailIndex == mCellCountY && totalCount % mCellCountX > 0)
                            {
                                nCount = totalCount % mCellCountX;
                            }

                            for (int i = nCount - 1; i >= 0; --i)
                            {
                                int index = mCellCountX * mHeadIndex + i;
                                RectTransform item = mCellTFList.Last();
                                mCellTFList.Remove(item);
                                mCellTFList.Insert(0, item);

                                SetCellPosByIndex(item, index);
                                RefreshCell(item, mCellCountX * (mHeadIndex - 1) + i);
                            }

                            mHeadIndex--;
                            mTailIndex--;
                        }
                    }
                    break;
                case ScrollMoveDir.HORIZONTAL:
                    {
                        // left
                        while (content.anchoredPosition.x <= -layout.padding.left - (mHeadIndex + 1) * (layout.cellSize.x + layout.spacing.x)
                        && mTailIndex < mCellCountX)
                        {
                            int nCount = mCellCountY;
                            if (mTailIndex == mCellCountX && totalCount % mCellCountY > 0)
                            {
                                nCount = totalCount % mCellCountY;
                            }

                            for (int i = 0; i < nCount; ++i)
                            {
                                int index = mCellCountY * mTailIndex + i;
                                if (index < totalCount)
                                {
                                    RectTransform item = mCellTFList[0];
                                    mCellTFList.Remove(item);
                                    mCellTFList.Add(item);

                                    SetCellPosByIndex(item, index);
                                    RefreshCell(item, index);
                                }
                            }
                            mHeadIndex++;
                            mTailIndex++;

                        }
                        // right
                        while (content.anchoredPosition.x >= -layout.padding.left - mHeadIndex * (layout.cellSize.x + layout.spacing.x)
                        && mHeadIndex != 0)
                        {
                            int nCount = mCellCountY;
                            if (mTailIndex == mCellCountX && totalCount % mCellCountY > 0)
                            {
                                nCount = totalCount % mCellCountY;
                            }

                            for (int i = nCount - 1; i >= 0; --i)
                            {
                                int index = mCellCountY * (mHeadIndex - 1) + i;
                                RectTransform item = mCellTFList.Last();
                                mCellTFList.Remove(item);
                                mCellTFList.Insert(0, item);

                                SetCellPosByIndex(item, index);
                                RefreshCell(item, index);
                            }

                            mHeadIndex--;
                            mTailIndex--;
                        }
                    }
                    break;
            }
        }

        //List<RectTransform> GetInvalidGOList()
        //{
        //    List<RectTransform> invalidList = new List<RectTransform>();
        //    int num = mCompScrollRect.content.transform.childCount;
        //    for (int i = num - 1; i >= 0; --i)
        //    {
        //        RectTransform rectTf = (RectTransform)mCompScrollRect.content.transform.GetChild(i);
        //        if (!rectTf.Overlaps((RectTransform)mCompScrollRect.transform))
        //        {
        //            //rectTf.gameObject.SetActive(false);
        //            //rectTf.SetParent(mPoolTransform);
        //            invalidList.Add(rectTf);
        //        }
        //    }

        //    //for (int i = 0; i < mPoolTransform.childCount; ++i)
        //    //{
        //    //    RectTransform rectTf = (RectTransform)mPoolTransform.GetChild(i);
        //    //    invalidList.Add(rectTf);
        //    //}

        //    return invalidList;
        //}

        void RefreshCell(RectTransform goTf, int index)
        {
            if (goTf)
            {
                UICell cell = goTf.GetComponent<UICell>();
                if (cell)
                {
                    cell.SetCellIndex(index);
                    cell.SetCellData(GetCellData(index));
                    cell.OnRefreshCell();
                }
            }
        }

        void SetCellPosByIndex(RectTransform goTf, int index)
        {
            Vector2 pos = Vector2.zero;
            GridLayoutGroup layout = GetGridLayoutGroup();
            int indexX;
            int indexY;
            switch (mScrollMoveDir)
            {
                case ScrollMoveDir.VERTICAL:
                    {
                        indexY = index / mCellCountX;
                        indexX = index % mCellCountX;
                        pos.x = layout.padding.left + indexX * (layout.cellSize.x + layout.spacing.x) + layout.cellSize.x * (((RectTransform)mCellTemplate.transform).pivot.x);
                        pos.y = -(layout.padding.top + indexY * (layout.cellSize.y + layout.spacing.y) - layout.spacing.y - layout.cellSize.y * (((RectTransform)mCellTemplate.transform).pivot.y));
                    }
                    break;
                case ScrollMoveDir.HORIZONTAL:
                    {
                        indexX = index / mCellCountY;
                        indexY = index % mCellCountY;
                        pos.x = layout.padding.left + indexX * (layout.cellSize.x + layout.spacing.x) + layout.cellSize.x * (((RectTransform)mCellTemplate.transform).pivot.x);
                        pos.y = -(layout.padding.top + indexY * (layout.cellSize.y + layout.spacing.y) + layout.cellSize.y * (((RectTransform)mCellTemplate.transform).pivot.y));
                    }
                    break;
            }

            goTf.anchoredPosition = pos;
        }

        public void ClearCells(int count = -1)
        {
            mCellDataInfos.Clear();
            if (mCompScrollRect.content)
            {
                for (int i = mCompScrollRect.content.transform.childCount - 1; i >= 0; --i)
                {
                    Transform tf = mCompScrollRect.content.transform.GetChild(i);
                    tf.SetParent(mPoolTransform);
                    tf.gameObject.SetActive(false);
                }
            }
        }

        public void SetCellCount(int count)
        {
            ClearCells(count);
            for (int i = 0; i < count; ++i)
            {
                CellDataInfo cellDataInfo = new CellDataInfo();
                //cellDataInfo.data = data;
                mCellDataInfos.Add(cellDataInfo);
                //AddCell(null);
            }

            int xcells;
            int ycells;
            bool bSurplus = false;
            Vector2 viewSize = mCompScrollRect.viewport.rect.size;
            GridLayoutGroup layout = GetGridLayoutGroup();
            xcells = Math.Max(1, 1 + (int)((viewSize.x - layout.padding.left - layout.padding.right - layout.cellSize.x) / (layout.cellSize.x + layout.spacing.x)));
            if (((viewSize.x - layout.padding.left - layout.padding.right - layout.cellSize.x) % (layout.cellSize.x + layout.spacing.x)) > 0)
            {
                xcells += 1;
                bSurplus = true;
            }
            ycells = Math.Max(1, 1 + (int)((viewSize.y - layout.padding.top - layout.padding.bottom - layout.cellSize.y) / (layout.cellSize.y + layout.spacing.y)));
            if (((viewSize.y - layout.padding.top - layout.padding.bottom - layout.cellSize.y) % (layout.cellSize.y + layout.spacing.y)) > 0)
            {
                ycells += 1;
                bSurplus = true;
            }
   
            SetContentSize();
            SetLayout();

            mHeadIndex = 0;
            switch (mScrollMoveDir)
            {
                case ScrollMoveDir.VERTICAL:
                    if(!bSurplus) ycells++;
                    mTailIndex = ycells;
                    break;
                case ScrollMoveDir.HORIZONTAL:
                    if (!bSurplus) xcells++;
                    mTailIndex = xcells;
                    break;
            }

            InitItem(Math.Min(count, xcells * ycells));
        }

        public int GetCellCount()
        {
            return mCellDataInfos.Count;
        }

        public void SetCellData(int index, object data, bool bRefreshCell = false)
        {
            if (index >= 0 && index < GetCellCount())
            {
                CellDataInfo cellDataInfo = mCellDataInfos[index];
                cellDataInfo.data = data;
                mCellDataInfos[index] = cellDataInfo;

                if (bRefreshCell)
                {
                    UICell cell = GetUICell(index);
                    if (cell)
                    {
                        cell.SetCellData(GetCellData(index));
                        cell.OnRefreshCell();
                    }
                }
            }
        }

        UICell GetUICell(int index)
        {
            int num = mCompScrollRect.content.transform.childCount;
            for (int i = 0; i < num; ++i)
            {
                RectTransform tf = (RectTransform)mCompScrollRect.content.transform.GetChild(i);
                UICell cell = tf.GetComponent<UICell>();
                if (cell && cell.GetCellIndex() == index)
                {
                    return cell;
                }
            }
            return null;
        }

        public object GetCellData(int index)
        {
            if (index >= 0 && index < GetCellCount())
            {
                return mCellDataInfos[index];
            }
            return null;
        }

        public void RefreshCells()
        {
            if (mCompScrollRect.content)
            {
                int num = mCompScrollRect.content.transform.childCount;
                for (int i = 0; i < num; ++i)
                {
                    Transform tf = mCompScrollRect.content.transform.GetChild(i);
                    SetNodeDragEvent(tf.gameObject);
                    UICell cell = tf.GetComponent<UICell>();
                    if (cell)
                    {
                        cell.SetCellData(GetCellData(cell.GetCellIndex()));
                        cell.OnRefreshCell();
                    }
                }
            }
        }

        void SetNodeDragEvent(GameObject go)
        {
            UINode[] uinodes = go.GetComponentsInChildren<UINode>(true);
            for (int i = 0; i < uinodes.Length; ++i)
            {
                UINode uinode = uinodes[i];
                uinode.SetPointerEventCallback(DragEvent);
            }
        }

        void DragEvent(string eventName, PointerEventData eventData)
        { 
            switch(eventName)
            {
                case "OnBeginDrag":
                    mCompScrollRect.OnBeginDrag(eventData);
                    break;
                case "OnDrag":
                    mCompScrollRect.OnDrag(eventData);
                    break;
                case "OnEndDrag":
                    mCompScrollRect.OnEndDrag(eventData);
                    break;
            }
        }


        //public void AddCell(object data)
        //{
        //    CellDataInfo cellDataInfo = new CellDataInfo();
        //    cellDataInfo.data = data;
        //    mCellDataInfos.Add(cellDataInfo);
        //}
    }
}


