/*****************************************************************************
 * 
 * author       : DuanGuangxiang
 * create date  : 2023 06 20
 * description  : Core.Event IEventLogic EventLogic
 * 
 *****************************************************************************/

using System;

namespace Core.Event
{
    public interface IEventLogic
    {
        public void SetReceiveEventCallback(Action<int, object> callback);
    }

    public class EventLogic : IEvent, IEventLogic
    {
        Action<int, object> mEventReceiveCallback = null;

        ~EventLogic()
        {
            RemoveAllEvent();
            mEventReceiveCallback = null;
        }

        public static EventLogic CreateInstance()
        {
            return new EventLogic();
        }

        public void ReceiveEvent(int msgId, object msgInfo)
        {
            mEventReceiveCallback?.Invoke(msgId, msgInfo);
        }

        public void SetReceiveEventCallback(Action<int, object> callback)
        {
            mEventReceiveCallback = callback;
        }

        public void AddEvent(int msgId)
        {
            EventManagerImpl.Instance.GetLogicEventMgr().AddEvent(msgId, this);
        }
        public void RemoveEvent(int msgId)
        {
            EventManagerImpl.Instance.GetLogicEventMgr().RemoveEvent(msgId, this);
        }

        public void RemoveAllEvent()
        {
            EventManagerImpl.Instance.GetLogicEventMgr().RemoveAllEvent(this);
        }
    }
}