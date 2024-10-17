/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.UIFramework IText UIText
 * 
 *****************************************************************************/
using TMPro;
using UnityEngine;

namespace Core.UIFramework
{
    public interface IText : INode
    {
        public void SetText(string text);
        public void SetColor(Color color);
        public void SetFontSize(float size);
    }

    //========================================================================================
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class UITextEx : UINode, IText
    {
        TextMeshProUGUI mCompText = null;
        protected override void Awake()
        {
            base.Awake();
            mCompText = gameObject.GetComponent<TextMeshProUGUI>();
        }
        public void SetText(string text)
        {
            if(mCompText)
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
                mCompText.fontSize = size;
        }
    }
}
