using PaperlessDesktop.Services;
using PaperlessDesktop.Shared;
using PaperlessDesktop.ViewModels;

namespace PaperlessDesktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWin { get; private set; }

    public App()
    {
        InitializeComponent();
        var sc = new ServiceCollection();
        sc.AddSingleton(AppSettings.Load());
        sc.AddSingleton<LicenseService>();
        sc.AddSingleton<PdfCoreService>();
        sc.AddSingleton<PdfConvertService>();
        sc.AddSingleton<BatchService>();
        sc.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<LicenseService>(),
            sp.GetRequiredService<PdfCoreService>(),
            sp.GetRequiredService<PdfConvertService>(),
            sp.GetRequiredService<BatchService>(),
            sp.GetRequiredService<AppSettings>(),
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
        ));
        Services = sc.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWin = new MainWindow();
        MainWin.Activate();
    }
}
