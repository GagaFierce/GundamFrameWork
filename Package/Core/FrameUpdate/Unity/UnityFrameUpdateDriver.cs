using System;
using UnityEngine;

namespace WFrameWork.Core.FrameUpdate.Unity
{
    /// <summary>Thin Unity callback adapter. Create it through UnityFrameUpdateHost.Install.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class UnityFrameUpdateDriver : MonoBehaviour
    {
        private UnityFrameUpdateHost _host;

        internal void Attach(UnityFrameUpdateHost host) { _host = host; }
        internal void Detach() { _host = null; }

        private void Update()
        {
            if (_host == null) return;
            try { _host.RenderUpdate(Time.frameCount, Time.deltaTime, Time.unscaledDeltaTime); }
            catch (Exception error) { Fail(error); }
        }

        private void LateUpdate()
        {
            if (_host == null) return;
            try { _host.RenderLateUpdate(); }
            catch (Exception error) { Fail(error); }
        }

        private void FixedUpdate()
        {
            if (_host == null) return;
            try { _host.PhysicsUpdate(Time.frameCount, Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime); }
            catch (Exception error) { Fail(error); }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (_host == null) return;
            try { _host.SetApplicationFocus(focused); }
            catch (Exception error) { Fail(error); }
        }

        private void OnApplicationPause(bool paused)
        {
            if (_host == null) return;
            try { _host.SetApplicationPaused(paused); }
            catch (Exception error) { Fail(error); }
        }

        private void OnDisable() { _host?.Dispose(); }
        private void OnDestroy() { _host?.Dispose(); }
        private void OnApplicationQuit() { _host?.Dispose(); }

        private void Fail(Exception error)
        {
            // Propagate is a development policy. Stop this host so a partially submitted
            // cycle can never be mistaken for a valid future cycle.
            Debug.LogException(error, this);
            _host?.Dispose();
        }
    }
}
