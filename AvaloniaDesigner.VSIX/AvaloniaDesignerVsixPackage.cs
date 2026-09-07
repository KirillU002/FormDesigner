using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Avalonia UI Visual Designer", "External AXAML Designer host bridge", "0.1.6")]
[ProvideMenuResource("Menus.ctmenu", 2)]
[ProvideBindingPath]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(Guids.PackageString)]
public sealed class AvaloniaDesignerVsixPackage : AsyncPackage
{
    private const string ActivityLogSource = "Avalonia UI Visual Designer";

    internal VsHostBridgeClient? BridgeClient { get; private set; }

    public AvaloniaDesignerVsixPackage()
    {
        VsixPackageLoadProbe.Write("AVALONIA_DESIGNER_VSIX_PACKAGE_CONSTRUCTOR");
        ActivityLog.LogInformation(ActivityLogSource, "AVALONIA_DESIGNER_VSIX_PACKAGE_CONSTRUCTOR");
    }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        VsixPackageLoadProbe.Write("AVALONIA_DESIGNER_VSIX_INITIALIZE_START");
        ActivityLog.LogInformation(ActivityLogSource, "AVALONIA_DESIGNER_VSIX_INITIALIZE_START");
        try
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            BridgeClient = new VsHostBridgeClient(this);
            await OpenInAvaloniaDesignerCommand.InitializeAsync(this, BridgeClient, WriteActivityLog);
            VsixPackageLoadProbe.Write("AVALONIA_DESIGNER_VSIX_INITIALIZE_SUCCESS");
            ActivityLog.LogInformation(ActivityLogSource, "AVALONIA_DESIGNER_VSIX_INITIALIZE_SUCCESS");
        }
        catch (Exception ex)
        {
            VsixPackageLoadProbe.Write("AVALONIA_DESIGNER_VSIX_INITIALIZE_FAILED", ex);
            ActivityLog.LogError(ActivityLogSource, $"AVALONIA_DESIGNER_VSIX_INITIALIZE_FAILED{Environment.NewLine}{ex}");
            throw;
        }
    }

    private static void WriteActivityLog(string message) =>
        ActivityLog.LogInformation(ActivityLogSource, message);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            BridgeClient?.Dispose();
        base.Dispose(disposing);
    }
}
