using System.Text;
using System.Windows;

namespace ExtraScreen.App;

public partial class LogWindow : Window
{
    private readonly StringBuilder _logBuffer = new();

    public LogWindow()
    {
        InitializeComponent();
    }

    public void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _logBuffer.AppendLine(message);
            TxtLogs.Text = _logBuffer.ToString();
            TxtLogs.ScrollToEnd();
        });
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        _logBuffer.Clear();
        TxtLogs.Text = string.Empty;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
