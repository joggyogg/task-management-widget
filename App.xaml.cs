using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace TaskManagementWidget
{
    public partial class App : Application
    {
        private Mutex? _instanceMutex;
        public static void UpdateAccentTheme()
        {
            if (Current == null) return;
            var glass = SystemParameters.WindowGlassColor;
            byte r = (byte)(glass.R * 0.28);
            byte g = (byte)(glass.G * 0.28);
            byte b = (byte)(glass.B * 0.28);
            Current.Resources["WidgetFrameBrush"] = new SolidColorBrush(Color.FromArgb(0x99, r, g, b));

            // Accent brush — full-strength version of the Windows accent color
            Current.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(glass.R, glass.G, glass.B));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _instanceMutex = new Mutex(true, "TaskManagementWidget_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                _instanceMutex.Dispose();
                Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                MessageBox.Show(args.ExceptionObject?.ToString(), "Startup crash",
                    MessageBoxButton.OK, MessageBoxImage.Error);

            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(args.Exception?.ToString(), "Dispatcher crash",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // Apply accent color before window is created
            UpdateAccentTheme();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Best-effort final sync (3s budget) so latest changes hit Drive before close.
            try
            {
                if (TaskManagementWidget.Services.SyncCoordinator.Instance is { } sc)
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                    sc.SyncNowAsync(cts.Token).Wait(TimeSpan.FromSeconds(3));
                }
            }
            catch { /* don't block shutdown on sync errors */ }

            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}

