using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PasteAsClassExtension;

internal sealed class PasteAsClassCommand {
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new Guid("dcd8a4ea-9302-4cd5-8a64-264b90acb831");

    private readonly AsyncPackage package;

    private PasteAsClassCommand(AsyncPackage package, OleMenuCommandService commandService) {
        this.package = package;

        var menuCommandId = new CommandID(CommandSet, CommandId);
        var menuItem = new OleMenuCommand(Execute, menuCommandId);
        menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
        commandService.AddCommand(menuItem);
    }

    public static async Task InitializeAsync(AsyncPackage package) {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService) {
            _ = new PasteAsClassCommand(package, commandService);
        }
    }

    private void OnBeforeQueryStatus(object sender, EventArgs e) {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (sender is OleMenuCommand command) {
            bool isFolderSelected = TryGetSelectedFolder(out _, out _);
            command.Visible = isFolderSelected;

            // Only enable if we have both a folder selected and valid clipboard text
            bool hasClipboardText = false;
            if (isFolderSelected) {
                try {
                    //if (System.Windows.Forms.Clipboard.ContainsText()) {
                    //    Your code here
                    //}
                    //hasClipboardText = Clipboard.ContainsText(TextDataFormat.Text) ||
                    //                  Clipboard.ContainsText(TextDataFormat.UnicodeText);
                    hasClipboardText = true;
                }
                catch {
                    // Ignore clipboard access errors in menu visibility check
                    hasClipboardText = false;
                }
            }

            command.Enabled = isFolderSelected && hasClipboardText;
            command.Text = hasClipboardText ? "Paste as Class" : "Paste as Class (Clipboard Empty)";
            command.Visible = true;
        }
    }

    private void Execute(object sender, EventArgs e) {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!TryGetSelectedFolder(out var folderPath, out var folderProjectItem) || string.IsNullOrWhiteSpace(folderPath)) {
            ShowError("Select a physical folder in Solution Explorer.");
            return;
        }

        string clipboardText = """
                        using Microsoft.Extensions.DependencyInjection;
            using Mrbr.OllamaRunner.Research.Chunking;

            namespace Mrbr.OllamaRunner.Research.DependencyInjection;

            /// <summary>
            /// Dependency injection registration for research services.
            /// </summary>
            public static class ResearchServiceCollectionExtensions
            {
                public static IServiceCollection AddOllamaResearch(
                    this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);

                    services.AddSingleton<ITextChunker, BasicTextChunker>();

                    return services;
                }
            }
            """;
        //try {
        //    if (!Clipboard.ContainsText(TextDataFormat.Text) && !Clipboard.ContainsText(TextDataFormat.UnicodeText)) {
        //        ShowError("Clipboard does not contain text.");
        //        return;
        //    }

        //    clipboardText = Clipboard.GetText();
        //    if (string.IsNullOrWhiteSpace(clipboardText)) {
        //        ShowError("Clipboard text is empty.");
        //        return;
        //    }
        //}
        //catch (Exception ex) {
        //    ShowError($"Failed to read clipboard: {ex.Message}");
        //    return;
        //}

        var className = TryExtractClassName(clipboardText);
        if (string.IsNullOrWhiteSpace(className)) {
            ShowError("Could not find a C# class name in clipboard text.");
            return;
        }

        var filePath = Path.Combine(folderPath, className + ".cs");
        if (File.Exists(filePath)) {
            ShowError("File already exists: " + className + ".cs");
            return;
        }

        try {
            File.WriteAllText(filePath, clipboardText, new UTF8Encoding(false));
            folderProjectItem?.ProjectItems?.AddFromFile(filePath);
        }
        catch (Exception ex) {
            ShowError("Failed to create file. " + ex.Message);
        }
    }

    private static string? TryExtractClassName(string source) {
        var match = Regex.Match(source, @"\bclass\s+([_@A-Za-z][_A-Za-z0-9]*)\b", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.TrimStart('@') : null;
    }

    private bool TryGetSelectedFolder(out string? folderPath, out ProjectItem? folderProjectItem) {
        ThreadHelper.ThrowIfNotOnUIThread();

        folderPath = null;
        folderProjectItem = null;

        if (ThreadHelper.JoinableTaskFactory.Run(async () => await package.GetServiceAsync(typeof(DTE))) is not DTE2 dte) {
            return false;
        }

        if (dte.ToolWindows?.SolutionExplorer?.SelectedItems is not Array selectedItems || selectedItems.Length != 1) {
            return false;
        }

        if (selectedItems.GetValue(0) is not UIHierarchyItem uiHierarchyItem || uiHierarchyItem.Object is not ProjectItem projectItem) {
            return false;
        }

        if (!string.Equals(projectItem.Kind, EnvDTE.Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        try {
            folderPath = projectItem.Properties?.Item("FullPath")?.Value as string;
            folderProjectItem = projectItem;
            return !string.IsNullOrWhiteSpace(folderPath);
        }
        catch {
            return false;
        }
    }

    private void ShowError(string message) {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(
            package,
            message,
            "Paste as Class",
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
