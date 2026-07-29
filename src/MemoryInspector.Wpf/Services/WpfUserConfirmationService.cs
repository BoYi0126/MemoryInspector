using System.Windows;

namespace MemoryInspector.Wpf.Services;

public sealed class WpfUserConfirmationService :
    IUserConfirmationService
{
    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
