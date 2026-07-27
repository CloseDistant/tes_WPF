using System.Windows;
using System.Windows.Threading;
using RuinaoHardwareEngineer.Features.Stimulation.Services;
using RuinaoHardwareEngineer.Features.Stimulation.ViewModels;
using RuinaoTesHardware;

namespace RuinaoHardwareEngineer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        try
        {
            var client = new BackplaneClient(
                new WindowsUsbBackplaneDiscovery(),
                new UsbTestCompatibleBackplaneTransport());
            var stimulationService = new EngineerStimulationService(client);
            var stimulation = new EngineerStimulationViewModel(stimulationService);
            MainWindow = new MainWindow(client, stimulation);
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"工程师工具启动失败：\n\n{exception}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"工程师工具发生未处理异常：\n\n{e.Exception}",
            "运行异常",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
