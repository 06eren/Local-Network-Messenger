using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Local_Network_Messenger
{
    public partial class App : System.Windows.Application
    {
        private const string MutexName = "Local\\LocalNetworkMessenger_SingleInstance";
        private const string ActivateEventName = "Local\\LocalNetworkMessenger_Activate";
        private Mutex? _instanceMutex;
        private EventWaitHandle? _activateEvent;
        private CancellationTokenSource? _activateCts;

        protected override void OnStartup(StartupEventArgs e)
        {
            var createdNew = false;
            _instanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                SignalExistingInstance();
                Shutdown();
                return;
            }

            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            StartActivateListener();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _activateCts?.Cancel();
            if (_activateEvent != null)
            {
                try
                {
                    _activateEvent.Set();
                }
                catch (ObjectDisposedException)
                {
                }
                _activateEvent.Dispose();
                _activateEvent = null;
            }
            _activateCts?.Dispose();
            _activateCts = null;

            if (_instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
                _instanceMutex.Dispose();
                _instanceMutex = null;
            }

            base.OnExit(e);
        }

        private void StartActivateListener()
        {
            if (_activateEvent == null)
            {
                return;
            }

            _activateCts = new CancellationTokenSource();
            var token = _activateCts.Token;
            _ = Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        _activateEvent.WaitOne();
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        if (Current?.MainWindow is MainWindow window)
                        {
                            window.BringToFrontFromExternal();
                        }
                    });
                }
            }, token);
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ActivateEventName);
                handle.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
