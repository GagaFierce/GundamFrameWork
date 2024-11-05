namespace Core.Actions
{
    public class ActionBase
    {
        System.Action<ActionBase> mCallback;
        float mDelayTime = 0.0f;
        float mElapseTime = 0.0f;
        bool mIsFinished = false;

        public void Init(float delayTime, System.Action<ActionBase> callback)
        {
            mDelayTime = System.Math.Max(0, delayTime);
            mCallback = callback;

            mIsFinished = false;
            mElapseTime = 0.0f;
        }

        public bool IsFinished()
        {
            return mIsFinished;
        }

        public void Update(float deltaTime)
        {
            if (mIsFinished)
                return;

            mElapseTime += deltaTime;
            if (mElapseTime >= mDelayTime)
            {
                mIsFinished = true;
                mCallback?.Invoke(this);
            }
        }
    }
}
