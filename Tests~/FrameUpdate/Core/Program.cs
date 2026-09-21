using System;
using System.Collections.Generic;

namespace WFrameWork.FrameUpdate.Tests
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--benchmark") return Benchmark.Run(args);
            string filter = args.Length == 2 && args[0] == "--filter" ? args[1] : null;
            var tests = new List<TestCase>();
            RegistrationTests.Add(tests);
            DriverTests.Add(tests);
            TimeTests.Add(tests);
            DiagnosticsTests.Add(tests);
            int passed = 0, failed = 0;
            foreach (var test in tests)
            {
                if (filter != null && test.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    test.Body();
                    Console.WriteLine("PASS " + test.Name);
                    passed++;
                }
                catch (Exception error)
                {
                    Console.WriteLine("FAIL " + test.Name + "\n" + error);
                    failed++;
                }
            }
            Console.WriteLine($"RESULT passed={passed} failed={failed}");
            return failed == 0 && passed > 0 ? 0 : 1;
        }
    }
}
