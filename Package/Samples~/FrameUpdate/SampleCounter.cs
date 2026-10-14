namespace WFrameWork.Core.FrameUpdate.Samples
{
    /// <summary>A plain C# object that can be registered in any loop.</summary>
    public sealed class SampleCounter : IFrameUpdate
    {
        public int CallCount { get; private set; }
        public double WorkedTime { get; private set; }

        public void OnFrameUpdate(in FrameUpdateContext context)
        {
            CallCount++;
            WorkedTime += context.DeltaTime;
        }
    }
}
