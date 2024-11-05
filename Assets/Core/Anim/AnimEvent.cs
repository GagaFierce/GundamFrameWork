/**************************************************
 *
 * Copyright (c) 2024 WangJian 
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Core.Anim
{
    public interface IAnimEvent
    {
        public void OnInit();
        public void OnDispose();
    }

    public class AnimEventBase : MonoBehaviour, IAnimEvent
    {
        const float mThreshold = 0.016f;
        Animator mAnimator;
        Action<string, string> mAnimEventsCallback;
        void Awake()
        {
            OnInit();
        }

        void OnDestroy()
        {
            OnDispose();
        }

        public virtual void OnInit()
        {
            InitDefaultEvents();
        }

        public virtual void OnDispose()
        {
            RemoveAllEvents();
        }

        protected virtual string GetAnimEventsCallback()
        {
            return "OnAnimEventsCallback";
        }

        void InitDefaultEvents()
        {
            int hashCode = GetHashCode();
            mAnimator = GetComponentInChildren<Animator>();
            AnimationClip[] animationClips = mAnimator.runtimeAnimatorController.animationClips;
            string funcName = GetAnimEventsCallback();
            string startEventName = "Anim_Run_Start";
            string endEventName = "Anim_Run_End";
            for (int i = 0; i < animationClips.Length; ++i)
            {
                AnimationClip animationClip = animationClips[i];
                {
                    {
                        AnimationEvent endStart = new AnimationEvent();
                        endStart.functionName = funcName;
                        endStart.stringParameter = endEventName;
                        endStart.time = animationClip.length;
                        endStart.intParameter = hashCode;
                        animationClip.AddEvent(endStart);
                    }

                    {
                        AnimationEvent evtStart = new AnimationEvent();
                        evtStart.functionName = funcName;
                        evtStart.stringParameter = startEventName;
                        evtStart.time = 0;
                        evtStart.intParameter = hashCode;
                        animationClip.AddEvent(evtStart);
                    }
                }
            }
        }

        void OnAnimEventsCallback(AnimationEvent animationEvent)
        {
            if (animationEvent.intParameter != GetHashCode())
                return;

            AnimatorStateInfo stateInfo = animationEvent.animatorStateInfo;
            AnimationClip clip = animationEvent.animatorClipInfo.clip;
            mAnimEventsCallback?.Invoke(clip.name, animationEvent.stringParameter);
        }

        void OnActerAnimEventsCallback(AnimationEvent animationEvent)
        {
            OnAnimEventsCallback(animationEvent);
        }

        void OnUIAnimEventsCallback(AnimationEvent animationEvent)
        {
            OnAnimEventsCallback(animationEvent);
        }

        public void SetAnimEventsCallback(Action<string, string> callback)
        {
            mAnimEventsCallback = callback;
        }

        public bool AddAnimEvent(string animName, string eventName, float keytime)
        {
            int hashCode = GetHashCode();
            string strEventName = eventName;
            string funcName = GetAnimEventsCallback();
            AnimationClip[] animationClips = mAnimator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < animationClips.Length; ++i)
            {
                AnimationClip animationClip = animationClips[i];
                if (animationClip.name.Equals(animName))
                {
                    {
                        AnimationEvent animEvent = new AnimationEvent();
                        animEvent.functionName = funcName;
                        animEvent.stringParameter = strEventName;
                        animEvent.intParameter = hashCode;
                        animEvent.time = keytime;
                        animationClip.AddEvent(animEvent);
                        return true;
                    }
                }
            }

            return false;
        }

        bool HasEvent(AnimationClip clip, string eventName)
        {
            int hashCode = GetHashCode();
            for (int i = 0; i < clip.events.Length; ++i)
            {
                if (clip.events[i].functionName == eventName && clip.events[i].intParameter == hashCode)
                    return true;
            }

            return false;
        }

        void RemoveAllEvents()
        {
            int hashCode = GetHashCode();
            if (mAnimator == null) return;
            AnimationClip[] animationClips = mAnimator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < animationClips.Length; ++i)
            {
                List<AnimationEvent> events = animationClips[i].events.ToList();
                for (int j = events.Count - 1; j >= 0; --j)
                {
                    if (events[j].intParameter == hashCode)
                        events.RemoveAt(j);
                }
                animationClips[i].events = events.ToArray();
            }
        }
    }
}
