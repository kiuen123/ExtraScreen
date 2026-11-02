using System.Windows;

namespace ExtraScreen.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            System.Windows.MessageBox.Show(
                $"Lỗi khởi động hệ thống:\n{args.ExceptionObject}",
                "ExtraScreen Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            System.Windows.MessageBox.Show(
                $"Lỗi giao diện:\n{args.Exception.Message}",
                "ExtraScreen Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
