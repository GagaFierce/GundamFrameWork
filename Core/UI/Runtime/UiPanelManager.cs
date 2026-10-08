using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Diagnostics;

namespace WFrameWork.UI
{
    public sealed class UiPanelManager : IFrameUpdate, IDisposable
    {
        private sealed class OpenWaiter
        {
            internal readonly TaskCompletionSource<UiPanelHandle> Completion = new TaskCompletionSource<UiPanelHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal CancellationTokenRegistration Registration;
            internal bool Active = true;
        }

        private sealed class Entry
        {
            internal readonly UiPanelDefinition Definition;
            internal UiPanelHandle Handle;
            internal readonly TaskCompletionSource<UiPanelHandle> OpenCompletion = new TaskCompletionSource<UiPanelHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly List<OpenWaiter> Waiters = new List<OpenWaiter>(2);
            internal readonly CancellationTokenSource LoadCancellation = new CancellationTokenSource();
            internal Task OpenOperation;
            internal UiPanelState State = UiPanelState.Loading;
            internal UiResourceHandle Resource;
            internal IUiPanelInstance Instance;
            internal object Argument;
            internal object FocusBeforeOpen;
            internal bool FocusCaptured;
            internal IDisposable ModalToken;
            internal int CancelRequested;
            internal int ActiveWaiters;
            internal bool SharedWaiterActive;
            internal bool PartsDisposed;
            internal bool Dirty = true;
            internal int Index;
            internal Entry(UiPanelDefinition definition, object argument) { Definition = definition; Argument = argument; }
        }

        private readonly IUiResourceProvider _resources;
        private readonly IUiPanelFactory _factory;
        private readonly IUiFocusService _focus;
        private readonly IUiModalInputBlocker _modalBlocker;
        private readonly DiagnosticLogger _diagnostics;
        private readonly Dictionary<UiPanelId, UiPanelDefinition> _definitions = new Dictionary<UiPanelId, UiPanelDefinition>();
        private readonly Dictionary<UiPanelId, List<Entry>> _entries = new Dictionary<UiPanelId, List<Entry>>();
        private readonly List<Entry> _allEntries = new List<Entry>();
        private readonly List<Entry> _modalStack = new List<Entry>();
        private IDisposable _frameBinding;
        private FrameUpdateManager _frameManager;
        private UpdateGroup _ownedGroup;
        private bool _ownsGroup;
        private bool _disposed;
        private Task _closeTask;

        public UiPanelManager(IUiResourceProvider resources, IUiPanelFactory factory, IUiFocusService focus = null,
            IUiModalInputBlocker modalBlocker = null, IDiagnosticSink diagnostics = null)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _focus = focus ?? new NullUiFocusService();
            _modalBlocker = modalBlocker ?? new NullUiModalInputBlocker();
            _diagnostics = new DiagnosticLogger("UI", diagnostics);
        }

        public bool IsDisposed => _disposed;
        public int OpenPanelCount => _allEntries.Count;
        public int ModalDepth => _modalStack.Count;

        public void Register(UiPanelDefinition definition)
        {
            EnsureUsable();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_entries.TryGetValue(definition.Id, out var list))
            {
                if (!_definitions[definition.Id].Equals(definition)) throw new InvalidOperationException("The panel id is already registered with another definition.");
                return;
            }
            _definitions.Add(definition.Id, definition);
            _entries.Add(definition.Id, new List<Entry>());
        }

        public Task<UiPanelHandle> OpenAsync(UiPanelId panelId, object argument = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<UiPanelHandle>(cancellationToken);
            if (!_entries.TryGetValue(panelId, out var list)) throw new KeyNotFoundException(panelId.ToString());
            var definition = _definitions[panelId];
            if (definition.InstanceMode == UiPanelInstanceMode.Single && list.Count > 0)
            {
                var existing = list[0];
                if (existing.State == UiPanelState.Loading) return AttachWaiter(existing, cancellationToken);
                if (existing.State == UiPanelState.Hidden) Show(existing);
                return Task.FromResult(existing.Handle);
            }
            var entry = new Entry(definition, argument);
            entry.Index = list.Count;
            entry.Handle = new UiPanelHandle(this, entry);
            list.Add(entry); _allEntries.Add(entry);
            Task<UiPanelHandle> result = AttachWaiter(entry, cancellationToken);
            entry.OpenOperation = StartOpenAsync(entry);
            return result;
        }

        private Task<UiPanelHandle> AttachWaiter(Entry entry, CancellationToken token)
        {
            if (!token.CanBeCanceled)
            {
                if (!entry.SharedWaiterActive) { entry.SharedWaiterActive = true; entry.ActiveWaiters++; }
                return entry.OpenCompletion.Task;
            }
            var waiter = new OpenWaiter();
            entry.Waiters.Add(waiter); entry.ActiveWaiters++;
            waiter.Registration = token.Register(() => CancelWaiter(entry, waiter, token));
            return waiter.Completion.Task;
        }

        private void CancelWaiter(Entry entry, OpenWaiter waiter, CancellationToken token)
        {
            if (!waiter.Active) return;
            waiter.Active = false; waiter.Registration.Dispose(); waiter.Completion.TrySetCanceled(token);
            if (entry.ActiveWaiters > 0) entry.ActiveWaiters--;
            if (entry.ActiveWaiters == 0 && !entry.SharedWaiterActive && entry.State == UiPanelState.Loading) CancelEntry(entry);
        }

        public Task HideAsync(UiPanelId panelId)
        {
            EnsureUsable();
            if (!_entries.TryGetValue(panelId, out var list) || list.Count == 0) return Task.CompletedTask;
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].State == UiPanelState.Visible) { Hide(list[i]); break; }
            return Task.CompletedTask;
        }

        public Task CloseAsync(UiPanelId panelId)
        {
            EnsureUsable();
            if (!_entries.TryGetValue(panelId, out var list) || list.Count == 0) return Task.CompletedTask;
            return CloseEntryAsync(list[list.Count - 1]);
        }

        public Task CloseTopModalAsync()
        {
            EnsureUsable();
            return _modalStack.Count == 0 ? Task.CompletedTask : CloseEntryAsync(_modalStack[_modalStack.Count - 1]);
        }

        public bool IsOpen(UiPanelId panelId) => _entries.TryGetValue(panelId, out var list) && list.Count > 0;

        public void MarkDirty(UiPanelId panelId)
        {
            if (!_entries.TryGetValue(panelId, out var list)) return;
            for (int i = 0; i < list.Count; i++) if (list[i].State == UiPanelState.Visible) list[i].Dirty = true;
        }

        public void CopyOpenPanels(List<UiPanelInfo> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            for (int i = 0; i < _allEntries.Count; i++)
            {
                var entry = _allEntries[i];
                results.Add(new UiPanelInfo(entry.Definition.Id, entry.State, entry.Definition.Layer, entry.Definition.IsModal, entry.Index));
            }
        }

        public void AttachToFrameUpdate(FrameUpdateManager manager, UpdateLoop presentationLoop,
            UpdateGroup group = default(UpdateGroup), UpdateScope scope = default(UpdateScope))
        {
            EnsureUsable();
            if (_frameBinding != null) throw new InvalidOperationException("UI is already attached to FrameUpdate.");
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (!presentationLoop.IsValid) throw new ArgumentException("A valid Presentation loop is required.", nameof(presentationLoop));
            if (!group.IsValid)
            {
                _ownedGroup = manager.CreateGroup("UI.Unscaled", new UpdateGroupOptions { TimeSource = UpdateTimeSource.Unscaled, TimeScale = 1 });
                group = _ownedGroup; _ownsGroup = true;
            }
            var handle = manager.Register(this, new FrameUpdateOptions
            {
                Loop = presentationLoop, Phase = UpdatePhase.NormalUpdate, Group = group,
                Scope = scope, Schedule = UpdateSchedule.EveryStep(), WorkClass = UpdateWorkClass.Required
            });
            _frameManager = manager; _frameBinding = new FrameBinding(manager, handle, this);
        }

        public void OnFrameUpdate(in FrameUpdateContext context)
        {
            if (_disposed) return;
            for (int i = 0; i < _allEntries.Count;)
            {
                var entry = _allEntries[i];
                if (entry.State != UiPanelState.Visible || entry.Instance == null) { i++; continue; }
                if (!entry.Instance.IsAlive) { _ = CloseEntryAsync(entry); continue; }
                if (!entry.Dirty && !entry.Instance.RequiresContinuousUpdate) { i++; continue; }
                bool dirty = entry.Dirty; entry.Dirty = false;
                try { entry.Instance.OnUpdate(new UiPanelUpdateContext(context.DeltaTime, context.RawDeltaTime, dirty)); }
                catch (Exception error) { _diagnostics.Error("Panel update failed: " + entry.Definition.Id, error); }
                if (i < _allEntries.Count && ReferenceEquals(_allEntries[i], entry)) i++;
            }
        }

        internal bool IsEntryOpen(object boxed) => boxed is Entry entry && !_disposed && (entry.State == UiPanelState.Visible || entry.State == UiPanelState.Hidden);
        internal UiPanelId GetEntryId(object boxed) => ((Entry)boxed).Definition.Id;
        internal UiPanelState GetEntryState(object boxed) => ((Entry)boxed).State;
        internal Task CloseEntryAsync(object boxed) => boxed is Entry entry ? CloseEntryAsync(entry) : Task.CompletedTask;

        private async Task StartOpenAsync(Entry entry)
        {
            try
            {
                entry.Resource = await _resources.LoadAsync(entry.Definition.ResourceKey, entry.LoadCancellation.Token);
                if (!IsEntryActive(entry)) { DisposeEntryParts(entry); return; }
                entry.Instance = _factory.Create(entry.Definition, entry.Resource);
                if (entry.Instance == null) throw new InvalidOperationException("The panel factory returned null.");
                if (!IsEntryActive(entry) || !entry.Instance.IsAlive) { DisposeEntryParts(entry); return; }
                entry.State = UiPanelState.Visible;
                entry.Instance.SetVisible(true);
                if (entry.Definition.IsModal) { entry.FocusBeforeOpen = _focus.CaptureFocusedElement(); entry.FocusCaptured = true; }
                Safe(() => entry.Instance.OnOpened(entry.Argument), "OnOpened");
                if (!IsEntryActive(entry)) return;
                PushModal(entry);
                if (!IsEntryActive(entry)) return;
                Safe(entry.Instance.OnShown, "OnShown");
                if (!IsEntryActive(entry)) return;
                CompleteOpen(entry);
            }
            catch (OperationCanceledException) { CancelEntry(entry); }
            catch (Exception error) { FailOpen(entry, error); }
            finally { entry.LoadCancellation.Dispose(); }
        }

        private async Task CloseEntryAsync(Entry entry)
        {
            if (entry == null || entry.State == UiPanelState.Closed || entry.State == UiPanelState.Failed) return;
            if (entry.State == UiPanelState.Loading)
            {
                CancelEntry(entry);
                if (entry.OpenOperation != null) { try { await entry.OpenOperation; } catch { } }
                await DisposeEntryPartsAsync(entry);
                return;
            }
            entry.State = UiPanelState.Closing;
            try
            {
                if (entry.Instance != null)
                {
                    Safe(entry.Instance.OnHidden, "OnHidden");
                    Safe(() => entry.Instance.SetVisible(false), "SetVisible(false)");
                }
                bool wasModal = _modalStack.Contains(entry); RemoveModal(entry);
                if (entry.Definition.IsModal && !wasModal && entry.FocusCaptured)
                    Safe(() => _focus.RestoreFocusedElement(entry.FocusBeforeOpen), "RestoreFocusedElement");
                Safe(() => entry.Instance?.OnClosed(), "OnClosed");
            }
            finally
            {
                await DisposeEntryPartsAsync(entry); entry.State = UiPanelState.Closed; CancelOpenCompletion(entry); RemoveEntry(entry);
            }
        }

        private void Show(Entry entry)
        {
            if (entry.State != UiPanelState.Hidden) return;
            entry.State = UiPanelState.Visible; Safe(() => entry.Instance?.SetVisible(true), "SetVisible(true)");
            if (!IsEntryActive(entry)) return;
            PushModal(entry); if (!IsEntryActive(entry)) return;
            Safe(() => entry.Instance?.OnShown(), "OnShown"); if (IsEntryActive(entry)) entry.Dirty = true;
        }

        private void Hide(Entry entry)
        {
            if (entry.State != UiPanelState.Visible) return;
            Safe(() => entry.Instance?.OnHidden(), "OnHidden"); if (!IsEntryActive(entry)) return;
            Safe(() => entry.Instance?.SetVisible(false), "SetVisible(false)"); if (!IsEntryActive(entry)) return;
            entry.State = UiPanelState.Hidden; RemoveModal(entry);
        }

        private void PushModal(Entry entry)
        {
            if (!entry.Definition.IsModal || entry.ModalToken != null || !IsEntryActive(entry)) return;
            entry.ModalToken = _modalBlocker.PushModal(entry.Definition.Id); _modalStack.Add(entry);
        }

        private void CancelEntry(Entry entry)
        {
            if (Interlocked.Exchange(ref entry.CancelRequested, 1) != 0) return;
            try { entry.LoadCancellation.Cancel(); } catch (ObjectDisposedException) { }
            CancelOpenCompletion(entry); entry.State = UiPanelState.Closed; RemoveModal(entry); RemoveEntry(entry);
        }

        private void CancelOpenCompletion(Entry entry)
        {
            entry.OpenCompletion.TrySetCanceled();
            for (int i = 0; i < entry.Waiters.Count; i++)
            {
                var waiter = entry.Waiters[i]; if (!waiter.Active) continue;
                waiter.Active = false; waiter.Registration.Dispose(); waiter.Completion.TrySetCanceled();
            }
            entry.Waiters.Clear(); entry.ActiveWaiters = 0;
        }

        private void CompleteOpen(Entry entry)
        {
            entry.OpenCompletion.TrySetResult(entry.Handle);
            for (int i = 0; i < entry.Waiters.Count; i++)
            {
                var waiter = entry.Waiters[i]; waiter.Registration.Dispose();
                if (waiter.Active) waiter.Completion.TrySetResult(entry.Handle);
            }
            entry.Waiters.Clear(); entry.ActiveWaiters = 0;
        }

        private void FailOpen(Entry entry, Exception error)
        {
            _diagnostics.Error("Panel open failed: " + entry.Definition.Id, error);
            entry.State = UiPanelState.Failed; RemoveModal(entry); RemoveEntry(entry); DisposeEntryParts(entry);
            entry.OpenCompletion.TrySetException(error);
            for (int i = 0; i < entry.Waiters.Count; i++)
            {
                var waiter = entry.Waiters[i]; waiter.Registration.Dispose(); if (waiter.Active) waiter.Completion.TrySetException(error);
            }
            entry.Waiters.Clear(); entry.ActiveWaiters = 0;
        }

        private bool IsEntryActive(Entry entry) => !_disposed && !IsCanceled(entry) && _allEntries.Contains(entry) &&
            entry.State != UiPanelState.Closed && entry.State != UiPanelState.Failed && entry.Instance?.IsAlive != false;
        private bool IsCanceled(Entry entry) => Volatile.Read(ref entry.CancelRequested) != 0;

        private void RemoveModal(Entry entry)
        {
            int index = _modalStack.IndexOf(entry); if (index < 0) return;
            bool wasTop = index == _modalStack.Count - 1; _modalStack.RemoveAt(index);
            var token = entry.ModalToken; entry.ModalToken = null; token?.Dispose();
            if (wasTop) Safe(() => _focus.RestoreFocusedElement(entry.FocusBeforeOpen), "RestoreFocusedElement");
        }

        private void RemoveEntry(Entry entry)
        {
            _allEntries.Remove(entry); if (_entries.TryGetValue(entry.Definition.Id, out var list)) list.Remove(entry);
        }

        private void DisposeEntryParts(Entry entry)
        {
            if (entry.PartsDisposed) return; entry.PartsDisposed = true;
            try { entry.Instance?.Dispose(); } catch (Exception error) { _diagnostics.Error("Panel instance dispose failed", error); }
            entry.Instance = null;
            try { entry.Resource?.Dispose(); } catch (Exception error) { _diagnostics.Error("Panel resource dispose failed", error); }
            entry.Resource = null;
        }

        private async Task DisposeEntryPartsAsync(Entry entry)
        {
            if (entry.PartsDisposed) return;
            entry.PartsDisposed = true;
            try
            {
                if (entry.Instance is IUiAsyncPanelInstance asyncInstance) await asyncInstance.DisposeAsync();
                else entry.Instance?.Dispose();
            }
            catch (Exception error) { _diagnostics.Error("Panel instance dispose failed", error); }
            entry.Instance = null;
            try { entry.Resource?.Dispose(); }
            catch (Exception error) { _diagnostics.Error("Panel resource dispose failed", error); }
            entry.Resource = null;
        }

        private void Safe(Action action, string operation)
        {
            if (action == null) return;
            try { action(); } catch (Exception error) { _diagnostics.Error("Panel " + operation + " callback failed", error); }
        }

        private void EnsureUsable() { if (_disposed) throw new ObjectDisposedException(nameof(UiPanelManager)); }

        public Task CloseAllAsync()
        {
            if (_closeTask != null) return _closeTask;
            _disposed = true;
            _closeTask = CloseAllCoreAsync();
            return _closeTask;
        }

        private async Task CloseAllCoreAsync()
        {
            _frameBinding?.Dispose(); _frameBinding = null;
            if (_ownsGroup && _frameManager != null && !_frameManager.IsDisposed) _frameManager.RemoveGroup(_ownedGroup);
            _frameManager = null;
            for (int i = _allEntries.Count - 1; i >= 0; i--)
            {
                var entry = _allEntries[i]; try { entry.LoadCancellation.Cancel(); } catch (ObjectDisposedException) { }
                RemoveModal(entry);
                if (entry.OpenOperation != null) { try { await entry.OpenOperation; } catch { } }
                await DisposeEntryPartsAsync(entry); entry.State = UiPanelState.Closed; CancelOpenCompletion(entry);
            }
            _allEntries.Clear(); _modalStack.Clear(); _entries.Clear(); _definitions.Clear();
            if (_ownsGroup) _ownedGroup = default(UpdateGroup);
        }

        public void Dispose() { _ = CloseAllAsync(); }

        private sealed class FrameBinding : IDisposable
        {
            private readonly FrameUpdateManager _manager; private readonly UpdateHandle _handle; private UiPanelManager _owner;
            internal FrameBinding(FrameUpdateManager manager, UpdateHandle handle, UiPanelManager owner) { _manager = manager; _handle = handle; _owner = owner; }
            public void Dispose() { if (_owner == null) return; _owner = null; if (!_manager.IsDisposed) _manager.Unregister(_handle); }
        }
    }
}
