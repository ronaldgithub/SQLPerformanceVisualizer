using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class ConnectServerViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private string _serverName = "localhost";
    [ObservableProperty] private bool _useWindowsAuth = true;
    [ObservableProperty] private string _sqlUser = string.Empty;
    [ObservableProperty] private string _sqlPassword = string.Empty;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _errorMessage;

    public event EventHandler? Connected;

    public ConnectServerViewModel(ISqlServerService service)
    {
        _service = service;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ErrorMessage = null;
        IsConnecting = true;
        try
        {
            var cs = UseWindowsAuth
                ? $"Server={ServerName};Database=master;Integrated Security=True;TrustServerCertificate=True;"
                : $"Server={ServerName};Database=master;User Id={SqlUser};Password={SqlPassword};TrustServerCertificate=True;";

            _service.SetConnectionString(cs);
            await _service.GetDatabasesAsync();
            IsConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
