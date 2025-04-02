/**************************************************
 *
 * Copyright (c) 2024 WangJian 
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/

using System.Collections.Generic;

namespace WFrameWork.Core.Actions
{
    public class DelayAction
    {
        List<ActionBase> mActionList = new List<ActionBase>();

        public ActionBase Delaydocall(float delayTime, System.Action<ActionBase> callback)
        {
            ActionBase action = new ActionBase();
            action.Init(delayTime, callback);
            mActionList.Add(action);
            return action;
        }

        public void Update(float delayTime)
        {
            for (int i = mActionList.Count - 1; i >= 0; --i)
            {
                ActionBase action = mActionList[i];
                action.Update(delayTime);
                if (action.IsFinished())
                {
                    mActionList.RemoveAt(i);
                }
            }
        }

        public bool StopDelayDocall(ActionBase action)
        {
            return mActionList.Remove(action);
        }

        public void StopAllDelayDocall()
        {
            mActionList.Clear();
        }
    }
}
