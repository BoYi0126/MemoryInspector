namespace MemoryInspector.Wpf.Services;

public interface IUserConfirmationService
{
    bool Confirm(string title, string message);
}
