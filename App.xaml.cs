using System;
using System.Windows;
using System.Windows.Media;

namespace TaskManagementWidget
{
    public partial class App : Application
    {
        public static void UpdateAccentTheme()
        {
            if (Current == null) return;
            var glass = SystemParameters.WindowGlassColor;
            byte r = (byte)(glass.R * 0.28);
            byte g = (byte)(glass.G * 0.28);
            byte b = (byte)(glass.B * 0.28);
            // Replace the entry with a new brush — avoids frozen-brush mutation
            Current.Resources["WidgetFrameBrush"] = new SolidColorBrush(Color.FromArgb(0x99, r, g, b));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
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
    }
}

