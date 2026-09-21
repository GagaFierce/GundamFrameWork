using System;

namespace WFrameWork.Core.FrameUpdate.Samples
{
    /// <summary>Registers one bounded maintenance task instead of every item it owns.</summary>
    public static class BatchSystemSample
    {
        public static string Run()
        {
            using (var manager = new FrameUpdateManager(FrameUpdateConfig.Default))
            using (var driver = manager.BindDriver(
                manager.DefaultLoop, "Sample batch driver", UpdatePhaseMask.Normal))
            {
                var values = new int[1000];
                for (int i = 0; i < values.Length; i++)
                    values[i] = i + 1;

                var batch = new IncrementalChecksum(values, 100);
                manager.Register(batch, new FrameUpdateOptions
                {
                    Schedule = UpdateSchedule.EveryStep(),
                    WorkClass = UpdateWorkClass.Deferrable
                });

                // There is no phase budget by default, so all ten opportunities execute.
                for (long step = 1; step <= 10; step++)
                    manager.Tick(driver, UpdatePhase.NormalUpdate,
                        new FrameTimeSample(step, 1.0 / 60.0, 1.0 / 60.0));

                return $"Batch system: items={values.Length}, items per callback=100, " +
                       $"completed passes={batch.CompletedPasses}, checksum={batch.LastCompletedChecksum}.";
            }
        }

        /// <summary>
        /// A sample maintenance operation with a bounded amount of work per invocation.
        /// Callers must keep the input unchanged during a pass if they need a consistent checksum.
        /// </summary>
        public sealed class IncrementalChecksum : IFrameUpdate
        {
            private readonly int[] _values;
            private readonly int _itemsPerCallback;
            private int _cursor;
            private long _runningChecksum;

            public int CompletedPasses { get; private set; }
            public long LastCompletedChecksum { get; private set; }

            public IncrementalChecksum(int[] values, int itemsPerCallback)
            {
                if (values == null) throw new ArgumentNullException(nameof(values));
                if (values.Length == 0) throw new ArgumentException("At least one item is required.", nameof(values));
                if (itemsPerCallback <= 0) throw new ArgumentOutOfRangeException(nameof(itemsPerCallback));
                _values = values;
                _itemsPerCallback = itemsPerCallback;
            }

            public void OnFrameUpdate(in FrameUpdateContext context)
            {
                int remaining = Math.Min(_itemsPerCallback, _values.Length - _cursor);
                int end = _cursor + remaining;
                for (; _cursor < end; _cursor++)
                    _runningChecksum += _values[_cursor];

                if (_cursor == _values.Length)
                {
                    LastCompletedChecksum = _runningChecksum;
                    CompletedPasses++;
                    _runningChecksum = 0;
                    _cursor = 0;
                }
            }
        }
    }
}
