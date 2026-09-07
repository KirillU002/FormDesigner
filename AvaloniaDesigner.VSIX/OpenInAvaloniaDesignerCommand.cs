using AvaloniaDesigner.Host.Protocol;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AvaloniaDesigner.VSIX;

internal sealed class OpenInAvaloniaDesignerCommand
{
    private readonly AsyncPackage _package;
    private readonly VsHostBridgeClient _bridge;
    private readonly VsOutputWindowLogger _output;
    private VsDocumentBuffer? _buffer;
    private VsDocumentSnapshot? _snapshot;

    private OpenInAvaloniaDesignerCommand(AsyncPackage package, VsHostBridgeClient bridge, VsOutputWindowLogger output)
    {
        _package = package;
        _bridge = bridge;
        _output = output;
        _bridge.PatchReceived += ApplyPatchAsync;
        _bridge.ReloadRequested += ReloadFromVisualStudioAsync;
        _bridge.Disconnected += () => Log("VSIX_HOST_DISCONNECTED");
        _bridge.Log += Log;
    }

    public static async Task InitializeAsync(AsyncPackage package, VsHostBridgeClient bridge, Action<string>? packageLoadLog = null)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var command = new OpenInAvaloniaDesignerCommand(package, bridge, new VsOutputWindowLogger(package));
        var menu = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Visual Studio menu command service is unavailable.");
        packageLoadLog?.Invoke("AVALONIA_DESIGNER_VSIX_COMMAND_SERVICE_RESOLVED");
#pragma warning disable VSSDK007 // OleMenuCommand callbacks cannot return a task; ExecuteAsync reports operational errors itself.
        var menuCommand = new OleMenuCommand((_, _) => ThreadHelper.JoinableTaskFactory.RunAsync(command.ExecuteAsync).FileAndForget("AvaloniaDesigner/Open"), new CommandID(Guids.CommandSet, CommandIds.OpenInDesigner));
#pragma warning restore VSSDK007
        menuCommand.BeforeQueryStatus += command.BeforeQueryStatus;
        menu.AddCommand(menuCommand);
        packageLoadLog?.Invoke("AVALONIA_DESIGNER_VSIX_COMMAND_REGISTERED");
    }

    private void BeforeQueryStatus(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (sender is not OleMenuCommand command)
            return;

        // This top-level Tools command is the discoverable entry point for the PoC. The handler
        // validates the active document and reports a targeted message when it is not AXAML.
        command.Visible = true;
        command.Enabled = true;
    }

    private async Task ExecuteAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var bufferResult = await GetDocumentBufferAsync();
        var captureError = string.Empty;
        if (bufferResult.Buffer is null
            || !bufferResult.Buffer.TryCaptureActiveAxaml(out var snapshot, out captureError))
        {
            var message = bufferResult.Buffer is null ? bufferResult.Error : captureError;
            VsShellUtilities.ShowMessageBox(_package, message, "Avalonia UI Visual Designer", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        _snapshot = snapshot;
        try
        {
            Log($"VSIX_DESIGNER_COMMAND document={snapshot.FilePath}; version={snapshot.Version}");
            var opened = await _bridge.OpenDocumentAsync(snapshot);
            if (!opened.CanEdit)
                VsShellUtilities.ShowMessageBox(_package, opened.Status, "Avalonia UI Visual Designer", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        catch (Exception ex)
        {
            Log($"VSIX_OPEN_FAILED {ex}");
            VsShellUtilities.ShowMessageBox(_package, ex.Message, "Avalonia UI Visual Designer", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private async Task ApplyPatchAsync(ApplyDesignerPatchPayload patch)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        if (_snapshot is null)
            throw new VsSourceVersionConflictException("В Visual Studio нет исходного snapshot для Designer patch.");

        if (!DesignerHostPatchGuard.Matches(new OpenDocumentPayload
            {
                Version = _snapshot.Version,
                Checksum = _snapshot.Checksum
            }, patch))
            throw new VsSourceVersionConflictException("AXAML был изменён в Visual Studio после открытия Designer.");

        if (_buffer is null)
            throw new VsSourceVersionConflictException("Visual Studio document buffer is unavailable for the Designer patch.");

        if (!_buffer.TryApplyPatch(_snapshot, patch.Edits, out var applied, out var error))
            throw new VsSourceVersionConflictException(error);

        _snapshot = applied;
        await _bridge.SendPatchAppliedAsync(Guid.NewGuid().ToString("N"), applied.DocumentId, applied);
    }

    private async Task ReloadFromVisualStudioAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var bufferResult = await GetDocumentBufferAsync();
        var captureError = string.Empty;
        if (bufferResult.Buffer is null
            || !bufferResult.Buffer.TryCaptureActiveAxaml(out var snapshot, out captureError))
        {
            Log($"VSIX_RELOAD_FAILED {(bufferResult.Buffer is null ? bufferResult.Error : captureError)}");
            return;
        }

        _snapshot = snapshot;
        await _bridge.ReloadDocumentAsync(snapshot);
    }

    private async Task<(VsDocumentBuffer? Buffer, string Error)> GetDocumentBufferAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        if (_buffer is not null)
            return (_buffer, string.Empty);

        var dte = await _package.GetServiceAsync(typeof(DTE)) as DTE;
        if (dte is null)
        {
            Log("VSIX_DTE_SERVICE_UNAVAILABLE");
            return (null, "Visual Studio DTE service is unavailable.");
        }

        _buffer = new VsDocumentBuffer(dte);
        Log("VSIX_DTE_SERVICE_RESOLVED");
        return (_buffer, string.Empty);
    }

    private void Log(string message) => _output.Write(message);
}
