/**************************************************
 *
 * Copyright (c) 2024 WangJian 
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
namespace Core.Event
{
    public interface IEvent
    {
        void AddEvent(int msgId);
        void RemoveEvent(int msgId);
        void RemoveAllEvent();
        public void ReceiveEvent(int msgId, object msgInfo);
    }

    //public interface IEventNet : IEvent
    //{
    //    protected void ReceiveNetEvent(int msgId, object msgInfo);
    //}

    //public interface IEventLogic : IEvent
    //{
    //    protected void ReceiveLogicEvent(int msgId, object msgInfo);
    //}

    //public interface IEventUI : IEvent
    //{
    //    protected void ReceiveLogicEvent(int msgId, object msgInfo);
    //}
}
