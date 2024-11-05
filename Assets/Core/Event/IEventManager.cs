/**************************************************
 *
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
using System.Collections.Generic;
namespace Core.Event
{
    public interface IEventManager
    {
        void AddEvent(int msgId, IEvent callback);
        void RemoveEvent(int msgId, IEvent callback);
        void RemoveAllEvent(IEvent callback);
        void SendEvent(int msgId, object msgInfo, bool bImmediately = false);
        void UpdateEvent(float deltatime);
        void Dispose();
    }

    
    public class EventManager : IEventManager
    {
        struct MsgInfo
        {
            public int id;
            public object info;
        }

        struct EventInfo
        {
            public int id;
            public IEvent callback;
        }


        Dictionary<int, List<EventInfo>> mEventList = new Dictionary<int, List<EventInfo>>();
        List<MsgInfo> mMsgInfos = new List<MsgInfo>();
        List<EventInfo> mAddEventList = new List<EventInfo>();
        List<EventInfo> mRemoveEventList = new List<EventInfo>();


        public void Dispose()
        {
            mEventList.Clear();
            mMsgInfos.Clear();
            mAddEventList.Clear();
            mRemoveEventList.Clear();
        }

        public void AddEvent(int msgId, IEvent callback)
        {
            if (mEventList.TryGetValue(msgId, out List<EventInfo> evts))
            {
                for (int i = 0; i < evts.Count; ++i)
                {
                    if (evts[i].callback == callback && evts[i].id == msgId)
                    {
                        return;
                    }
                }
            }

            for (int i = 0; i < mAddEventList.Count; ++i)
            {
                if (mAddEventList[i].callback == callback && mAddEventList[i].id == msgId)
                {
                    return;
                }
            }

            EventInfo evtInfo = new EventInfo();
            evtInfo.id = msgId;
            evtInfo.callback = callback;
            mAddEventList.Add(evtInfo);
        }

        void _AddEvent(EventInfo evtInfo)
        {
            if (!mEventList.TryGetValue(evtInfo.id, out List<EventInfo> evts))
            {
                evts = new List<EventInfo>();
            }
            evts.Add(evtInfo);
            mEventList[evtInfo.id] = evts;
        }

        void _RemoveEvent(EventInfo evtInfo)
        {
            if (mEventList.TryGetValue(evtInfo.id, out List<EventInfo> evts))
            {
                evts.Remove(evtInfo);
                mEventList[evtInfo.id] = evts;
            }
        }

        public void RemoveEvent(int msgId, IEvent callback)
        {
            if (mEventList.TryGetValue(msgId, out List<EventInfo> evts))
            {
                for (int i = 0; i < evts.Count; ++i)
                {
                    EventInfo evtInfo = evts[i];
                    if (evtInfo.callback == callback && evtInfo.id == msgId)
                    {
                        mRemoveEventList.Add(evtInfo);
                        break;
                    }
                }
            }

            for (int i = mAddEventList.Count - 1; i >= 0; --i)
            {
                if (msgId == mAddEventList[i].id && mAddEventList[i].callback == callback)
                {
                    mAddEventList.RemoveAt(i);
                    return;
                }
            }
        }

        public void RemoveAllEvent(IEvent callback)
        {
            foreach (var v in mEventList)
            {
                List<EventInfo> evts = v.Value;
                for (int i = 0; i < evts.Count; ++i)
                {
                    EventInfo evtInfo = evts[i];
                    if (evtInfo.callback == callback)
                    {
                        mRemoveEventList.Add(evtInfo);
                        break;
                    }
                }
            }
        }

        public void SendEvent(int msgId, object msgInfo, bool bImmediately = false)
        {
            if (bImmediately)
                _SendEvent(msgId, msgInfo);
            else
            {
                var item = new MsgInfo();
                item.id = msgId;
                item.info = msgInfo;
                mMsgInfos.Add(item);
            }
        }

        void _SendEvent(int msgId, object msgInfo)
        {
            if (mEventList.TryGetValue(msgId, out List<EventInfo> evts))
            {
                for (int i = 0; i < evts.Count; ++i)
                {
                    EventInfo evtInfo = evts[i];
                    if (mRemoveEventList.Contains(evtInfo))
                        continue;
                    evtInfo.callback.ReceiveEvent(msgId, msgInfo);
                }
            }
        }

        public void UpdateEvent(float deltatime)
        {
            for (int i = 0; i < mAddEventList.Count; ++i)
            {
                var item = mAddEventList[i];
                _AddEvent(item);
            }
            mAddEventList.Clear();


            for (int i = 0; i < mMsgInfos.Count; ++i)
            {
                var item = mMsgInfos[i];
                _SendEvent(item.id, item.info);
            }
            mMsgInfos.Clear();


            for (int i = 0; i < mRemoveEventList.Count; ++i)
            {
                var item = mRemoveEventList[i];
                _RemoveEvent(item);
            }
            mRemoveEventList.Clear();

            //foreach (var item in mEventList)
            //{
            //    List<EventInfo> evts = item.Value;
            //    for (int i = evts.Count - 1; i >= 0; --i)
            //    {
            //        EventInfo evt = evts[i];
            //        if (!evt.isValid)
            //            evts.RemoveAt(i);
            //    }
            //}
        }
    }

    public class EventManagerImpl : Singleton.Singleton<EventManagerImpl>
    {

        EventManager mNetEventMgr;
        EventManager mLogicEventMgr;
        EventManager mUIEventMgr;

        public override void Init()
        {
            mNetEventMgr = new EventManager();
            mLogicEventMgr = new EventManager();
            mUIEventMgr = new EventManager();
        }

        public override void Dispose()
        {
            mNetEventMgr?.Dispose();
            mLogicEventMgr?.Dispose();
            mUIEventMgr?.Dispose();
        }

        public void SendEvent(int msgId, object msgInfo = null, bool bImmediately = true)
        {
            mNetEventMgr.SendEvent(msgId, msgInfo, bImmediately);
            mLogicEventMgr.SendEvent(msgId, msgInfo, bImmediately);
            mUIEventMgr.SendEvent(msgId, msgInfo, bImmediately);
        }

        public EventManager GetNetEventMgr()
        {
            return mNetEventMgr;
        }

        public EventManager GetLogicEventMgr()
        {
            return mLogicEventMgr;
        }

        public EventManager GetUIEventMgr()
        {
            return mUIEventMgr;
        }

        public void UpdateEvents(float deltaTime)
        {
            mNetEventMgr.UpdateEvent(deltaTime);
            mLogicEventMgr.UpdateEvent(deltaTime);
            mUIEventMgr.UpdateEvent(deltaTime);
        }
    }
}
