using System.Collections.Generic;
namespace Core.Action
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
