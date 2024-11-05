/**************************************************
 *
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
namespace Core.Actions
{
    public class ActionManager : Singleton.Singleton<ActionManager>
    {
        DelayAction mDelayAction;
        public override void Init()
        {
            mDelayAction = new DelayAction();
        }

        public override void Dispose()
        {
            StopAllDelayDocall();
        }

        public ActionBase Delaydocall(float delayTime, System.Action<ActionBase> callback)
        {
            return mDelayAction.Delaydocall(delayTime, callback);
        }


        public bool StopDelayDocall(ActionBase action)
        {
            return mDelayAction.StopDelayDocall(action);
        }

        public void StopAllDelayDocall()
        {
            mDelayAction.StopAllDelayDocall();
        }

        public void Update(float delayTime)
        {
            mDelayAction.Update(delayTime);
        }
    }
}
