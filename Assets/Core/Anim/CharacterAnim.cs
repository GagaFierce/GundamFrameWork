/**************************************************
 *
 * Copyright (c) 2024 [WangJian] , Licensed under the MIT License.  See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Anim
{
    public interface ICharacterAnim
    {
        public enum eAnimNameType
        {
            EANT_IDLE,           // 闲着
            EANT_WALK,           // 走
            EANT_RUN,            // 跑
            EANT_JUMP,           // 跳
            EANT_ATTACK_PREPARE, // 攻击
            EANT_ATTACK,         // 攻击
            EANT_ATTACKED,       // 被攻击
            EANT_DEATH,          // 死亡
        }

        void PlayAnim(eAnimNameType type, float timeLen = 0.0f);
        void PlayAnim(string animName, float timeLen = 0.0f);
        void StopAnim();
        bool IsAnimPlaying(eAnimNameType type);

        // 动画名字(pm0001_00_00_ba10_waitB01), 事件名字
        void SetAnimEventsCallback(Action<string, string> callback);
        // 动画名字(pm0001_00_00_ba10_waitB01), 事件名字, 时间点
        // 这俩事件不要用 Anim_Run_Start(开始播放) Anim_Run_End(结束播放)
        bool AddAnimEvent(string animName, string eventName, float keytime);
    }

    class CharacterAnimEvent : AnimEventBase
    {
        public override void OnInit()
        {

        }

        protected override string GetAnimEventsCallback()
        {
            return "OnActerAnimEventsCallback";
        }
    }

    public class CharacterAnim : MonoBehaviour, ICharacterAnim
    {
        string mLastPlayAnimName = "";
        float mTimeLen = 0.0f;
        Animator mAnimator;
        CharacterAnimEvent mAnimevent;
        Dictionary<ICharacterAnim.eAnimNameType, string> mAnimList = new Dictionary<ICharacterAnim.eAnimNameType, string>();

        void Awake()
        {
            // 静态注册
            mAnimList[ICharacterAnim.eAnimNameType.EANT_IDLE]           = "Idle";
            mAnimList[ICharacterAnim.eAnimNameType.EANT_WALK]           = "Walk";
            mAnimList[ICharacterAnim.eAnimNameType.EANT_RUN]            = "Run";
            mAnimList[ICharacterAnim.eAnimNameType.EANT_JUMP]           = "Jump";
            mAnimList[ICharacterAnim.eAnimNameType.EANT_ATTACK_PREPARE] = "Attack_Prepare";
            mAnimList[ICharacterAnim.eAnimNameType.EANT_ATTACK]         = "Attack";
            mAnimList[ICharacterAnim.eAnimNameType.EANT_ATTACKED]       = "Attacked";
            mAnimList[ICharacterAnim.eAnimNameType.EANT_DEATH]          = "Death";

            mAnimator = GetComponent<Animator>();
            if (null == mAnimator)
            {
                mAnimator = GetComponentInChildren<Animator>();
            }

            if (mAnimator)
            {
                mAnimevent = mAnimator.gameObject.GetComponent<CharacterAnimEvent>();
                if (!mAnimevent)
                {
                    mAnimevent = mAnimator.gameObject.AddComponent<CharacterAnimEvent>();
                }
            }
        }

        void Start()
        {

        }

        void Update()
        {
            if (mAnimator)
            {
                float animSpeed = 1.0f;
                AnimatorStateInfo info = mAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(mLastPlayAnimName))
                {
                    animSpeed = (mTimeLen > 0 ? info.length / mTimeLen : 1.0f);
                }
                mAnimator.speed = animSpeed;
            }
        }


        public void PlayAnim(ICharacterAnim.eAnimNameType type, float timeLen = 0.0f)
        {
            if (mAnimList.TryGetValue(type, out string animname))
            {
                PlayAnim(animname, timeLen);
            }
        }

        public void PlayAnim(string animName, float timeLen = 0.0f)
        {
            if (mAnimator)
            {
                mTimeLen = timeLen;
                mLastPlayAnimName = animName;
                mAnimator?.Play(animName, 0, 0);
            }
        }
        public void StopAnim()
        {
            PlayAnim(ICharacterAnim.eAnimNameType.EANT_IDLE);
        }

        public bool IsAnimPlaying(ICharacterAnim.eAnimNameType type)
        {
            if (mAnimator && mAnimList.TryGetValue(type, out string animname))
            {
                AnimatorStateInfo info = mAnimator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(animname))
                {
                    return true;
                }
            }

            return false;
        }

        public void SetAnimEventsCallback(Action<string, string> callback)
        {
            mAnimevent?.SetAnimEventsCallback(callback);
        }

        public bool AddAnimEvent(string animName, string eventName, float keytime)
        {
            return (mAnimevent ? mAnimevent.AddAnimEvent(animName, eventName, keytime) : false);
        }
    }

}
