/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework IButton UIButtonEx
 * 
 *****************************************************************************/
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.UIFramework
{
    public interface IButton : INode, IPointerDownHandler, IPointerUpHandler
    {
        public void SetSprite(Sprite sprite);
    }

    //========================================================================================
    [RequireComponent(typeof(Image))]
    [RequireComponent(typeof(Button))]
    public class UIButtonEx : UINode, IButton, IText
    {
        [SerializeField] string mAudioName;
        [SerializeField] float mLongPressDurationTimeThreshold = 0.0f;
        [SerializeField] float mLongPressIntervalTimeThreshold = 0.05f;
        Button mCompButton = null;
        Image mCompImage = null;
        Text mCompText = null;
        bool mIsLongPress = false;
        float mLongPressDurationTime = 0.0f;
        float mLongPressIntervalTime = 0.0f;


        protected override void Awake()
        {
            base.Awake();
            mCompButton = gameObject.GetComponent<Button>();
            mCompImage = gameObject.GetComponent<Image>();
            mCompText = gameObject.GetComponentInChildren<Text>();
            if (mCompButton)
            {
                mCompButton.onClick.AddListener(OnClickedCallback);
            }
        }

        void Update()
        {
            if (mIsLongPress && mLongPressDurationTimeThreshold > 0.0f)
            {
                mLongPressDurationTime -= Time.deltaTime;
                if (mLongPressDurationTime <= 0.0f)
                {
                    mLongPressIntervalTime += Time.deltaTime;
                    if (mLongPressIntervalTime >= mLongPressIntervalTimeThreshold)
                    {
                        mLongPressIntervalTime = 0.0f;
                        mCompButton.onClick.Invoke();
                    }
                }
            }
        }

        public void SetSprite(Sprite sprite)
        {
            if(mCompImage)
                mCompImage.sprite = sprite;
        }

        public void SetText(string text)
        {
            if (mCompText)
                mCompText.text = text;
        }

        public void SetColor(Color color)
        {
            if (mCompText)
                mCompText.color = color;
        }

        public void SetFontSize(float size)
        {
            if (mCompText)
                mCompText.fontSize = (int)size;
        }

        public override void OnClickedCallback()
        {
            if(null != GetLuaTable())
                ResLoad.LuaManagerImpl.Instance.ClickedButton_Global(this, mAudioName);
            base.OnClickedCallback();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            mIsLongPress = true;
            mLongPressDurationTime = mLongPressDurationTimeThreshold;
            mLongPressIntervalTime = 0.0f;
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            mIsLongPress = false;
            mLongPressDurationTime = mLongPressDurationTimeThreshold;
            mLongPressIntervalTime = 0.0f;
        }
    }
}
