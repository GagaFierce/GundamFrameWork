/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework IImage UIImageEx
 * 
 *****************************************************************************/
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.UIFramework
{
    public interface IImage : INode, IPointerClickHandler
    {
        public void SetSprite(Sprite sprite);
        public void SetFillAmount(float amout);
        public void SetRegisterClicked(bool bRegister);
        public bool IsRegisterClicked();
        public void SetRegisterDrag(bool bRegister);
        public bool IsRegisterDrag();
        public void SetColor(Color color);
        public Image GetImage();
        public void SetRaycasetTarget(bool bRaycastTarget);
    }

    //========================================================================================
    [RequireComponent(typeof(Image))]
    public class UIImageEx : UINode, IImage
    {
        [SerializeField] bool mRegisterDrag;
        [SerializeField] bool mRegisterClicked;
        Image mCompImage = null;
        bool mIsDragged = false;

        protected override void Awake()
        {
            base.Awake();
            mCompImage = gameObject.GetComponent<Image>();
        }

        public void SetRegisterClicked(bool bRegister)
        {
            mRegisterClicked = bRegister;
        }

        public bool IsRegisterClicked()
        {
            return mRegisterClicked;
        }

        public void SetRegisterDrag(bool bRegister)
        {
            mRegisterDrag = bRegister;
        }

        public bool IsRegisterDrag()
        {
            return mRegisterDrag;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsRegisterClicked() || (mIsDragged && !IsRegisterDrag()))
                return;

            OnClickedCallback();
        }

        public void SetSprite(Sprite sprite)
        {
            if(mCompImage)
                mCompImage.sprite = sprite;
        }

        public void SetFillAmount(float amout)
        {
            if (mCompImage)
            {
                if (mCompImage.type == Image.Type.Filled)
                {
                    mCompImage.fillAmount = amout;
                }
            }
        }

        public void SetColor(Color color)
        {
            if (mCompImage)
            {
                mCompImage.color = color;
            }
        }

        public Color GetColor()
        {
            if (mCompImage)
            {
                return mCompImage.color;
            }
            return Color.white;
        }

        public Image GetImage()
        {
            return mCompImage;
        }

        public void SetRaycasetTarget(bool bRaycastTarget)
        {
            if(mCompImage)
                mCompImage.raycastTarget = bRaycastTarget;
        }

        public override void _OnBeginDrag(PointerEventData eventData) 
        {
            mIsDragged = true;
        }

        public override void _OnDrag(PointerEventData eventData) 
        { 
        }

        public override void _OnEndDrag(PointerEventData eventData) 
        {
            mIsDragged = false;
        }
    }
}
