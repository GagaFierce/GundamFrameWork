using UnityEngine;

namespace WFrameWork.Diagnostics.Unity
{
    public sealed class UnityDiagnosticSink : IDiagnosticSink
    {
        public void Report(in DiagnosticEvent diagnostic)
        {
            string text = "[" + diagnostic.Module + "] " + diagnostic.Message;
            switch (diagnostic.Level)
            {
                case DiagnosticLevel.Error: if (diagnostic.Exception == null) Debug.LogError(text); else Debug.LogException(diagnostic.Exception); break;
                case DiagnosticLevel.Warning: Debug.LogWarning(text); break;
                case DiagnosticLevel.Info: Debug.Log(text); break;
                default: Debug.Log(text); break;
            }
        }
    }
}
