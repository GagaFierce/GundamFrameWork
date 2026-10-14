#if !UNITY_6000_0_OR_NEWER
using System;
using System.Reflection;

namespace Unity.Pipeline.Editor.Console
{
    /// <summary>
    /// 2022.3 back-port of Unity 6's <c>ConsoleWindowUtility.GetConsoleLogCounts</c>.
    ///
    /// Unity 6 reports the Console window's own error/warning/log counts directly. 2022 has no such
    /// API — the same numbers live in the internal <c>UnityEditor.LogEntries</c> store, so this
    /// reaches it by reflection. Every step degrades instead of throwing: when the store cannot be
    /// resolved, the counts stay at zero, which only silences the ground-truth cross-check and the
    /// capture-gap heuristic. The capture buffer itself is unaffected.
    ///
    /// The store is walked only when its total changed since the previous sample, so an idle editor
    /// pays one reflected call per sample rather than one per entry.
    /// </summary>
    internal static class LegacyConsoleCounts
    {
        // UnityEditor.LogEntry.mode is a plain int on 2022 (the Mode enum is internal), so the flags
        // are spelled out. Values are Unity's own LogEntry.Mode bits; ScriptingWarning (512) and
        // ScriptingLog (1024) were confirmed against live entries in this project.
        const int ErrorBit = 1 << 0;
        const int AssertBit = 1 << 1;
        const int LogBit = 1 << 2;
        const int FatalBit = 1 << 4;
        const int AssetImportErrorBit = 1 << 6;
        const int AssetImportWarningBit = 1 << 7;
        const int ScriptingErrorBit = 1 << 8;
        const int ScriptingWarningBit = 1 << 9;
        const int ScriptingLogBit = 1 << 10;
        const int ScriptCompileErrorBit = 1 << 11;
        const int ScriptCompileWarningBit = 1 << 12;
        const int StickyErrorBit = 1 << 13;
        const int ScriptingExceptionBit = 1 << 17;
        const int GraphCompileErrorBit = 1 << 20;
        const int VisualScriptingErrorBit = 1 << 22;

        const int ErrorMask = ErrorBit | AssertBit | FatalBit | AssetImportErrorBit | ScriptingErrorBit
            | ScriptCompileErrorBit | StickyErrorBit | ScriptingExceptionBit | GraphCompileErrorBit
            | VisualScriptingErrorBit;
        const int WarningMask = AssetImportWarningBit | ScriptingWarningBit | ScriptCompileWarningBit;
        const int LogMask = LogBit | ScriptingLogBit;

        static readonly Type s_LogEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor")
            ?? Type.GetType("UnityEditor.LogEntries, UnityEditor.CoreModule");
        static readonly Type s_LogEntry = Type.GetType("UnityEditor.LogEntry, UnityEditor")
            ?? Type.GetType("UnityEditor.LogEntry, UnityEditor.CoreModule");

        static readonly MethodInfo s_Start = Find("StartGettingEntries");
        static readonly MethodInfo s_End = Find("EndGettingEntries");
        static readonly MethodInfo s_GetCount = Find("GetCount");
        static readonly MethodInfo s_GetEntry = Find("GetEntryInternal");
        static readonly FieldInfo s_Mode = s_LogEntry?.GetField("mode",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        static readonly object[] s_Args = new object[2];
        static int s_LastTotal = -1;
        static int s_Errors;
        static int s_Warnings;
        static int s_Logs;

        internal static void Sample(out int errors, out int warnings, out int logs)
        {
            try
            {
                var total = Total();
                if (total != s_LastTotal)
                {
                    s_LastTotal = total;
                    Classify(total);
                }
            }
            catch (Exception)
            {
                // Never let a diagnostic probe break the editor update loop.
            }

            errors = s_Errors;
            warnings = s_Warnings;
            logs = s_Logs;
        }

        static MethodInfo Find(string name) => s_LogEntries?.GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        static int Total()
        {
            if (s_GetCount == null)
                return 0;

            return Convert.ToInt32(s_GetCount.Invoke(null, null));
        }

        static void Classify(int total)
        {
            s_Errors = 0;
            s_Warnings = 0;
            s_Logs = 0;

            if (total <= 0 || s_Start == null || s_End == null || s_GetEntry == null || s_Mode == null || s_LogEntry == null)
                return;

            var entry = Activator.CreateInstance(s_LogEntry);
            s_Start.Invoke(null, null);
            try
            {
                for (var i = 0; i < total; i++)
                {
                    s_Args[0] = i;
                    s_Args[1] = entry;
                    s_GetEntry.Invoke(null, s_Args);
                    entry = s_Args[1];

                    var mode = Convert.ToInt32(s_Mode.GetValue(entry));
                    if ((mode & ErrorMask) != 0)
                        s_Errors++;
                    else if ((mode & WarningMask) != 0)
                        s_Warnings++;
                    else if ((mode & LogMask) != 0)
                        s_Logs++;
                }
            }
            finally
            {
                s_End.Invoke(null, null);
            }
        }
    }
}
#endif
