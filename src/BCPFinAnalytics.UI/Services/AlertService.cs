using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.UI.Services;

/// <summary>
/// Centralised alert service — components push alerts here,
/// the MainLayout renders them as persistent dismissible banners.
/// Errors and warnings must be manually dismissed.
/// Info/success auto-dismiss after 5 seconds.
/// </summary>
public class AlertService
{
    public event Action? OnChange;

    private readonly List<AppAlert> _alerts = new();
    public IReadOnlyList<AppAlert> Alerts => _alerts.AsReadOnly();

    public void AddError(string message)   => Add(message, AlertLevel.Error);
    public void AddWarning(string message) => Add(message, AlertLevel.Warning);
    public void AddInfo(string message)    => Add(message, AlertLevel.Info);
    public void AddSuccess(string message) => Add(message, AlertLevel.Success);

    public void Dismiss(Guid id)
    {
        _alerts.RemoveAll(a => a.Id == id);
        OnChange?.Invoke();
    }

    public void DismissAll()
    {
        _alerts.Clear();
        OnChange?.Invoke();
    }

    private void Add(string message, AlertLevel level)
    {
        _alerts.Add(new AppAlert(message, level));
        OnChange?.Invoke();
    }
}

public class AppAlert
{
    public Guid       Id      { get; } = Guid.NewGuid();
    public string     Message { get; }
    public AlertLevel Level   { get; }
    public DateTime   Created { get; } = DateTime.Now;

    public AppAlert(string message, AlertLevel level)
    {
        Message = message;
        Level   = level;
    }
}

public enum AlertLevel { Success, Info, Warning, Error }
