using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PasteAsClassExtension.Commands;

/// <summary>
/// Handles the right-click menu command and gets the file path of the selected node.
/// </summary>
internal sealed class SolutionExplorerCommand {
    private const string FolderNameProperty = "MRBR_FOLDER_NAME";
    private const string ProjectItemProperty = "MRBR_PROJECT_ITEM";
    private const string ProjectProperty = "MRBR_PROJECT";
    public static readonly Guid CommandSet = new("968c03c3-e904-4f98-8cac-7262c34a305a");
    public const int GroupId = 0x0600;
    public const int CommandId = 0x0100;

    private readonly AsyncPackage package;
    private readonly DTE2 dte;

    private SolutionExplorerCommand(AsyncPackage package, OleMenuCommandService commandService, DTE2 dte) {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
        this.dte = dte ?? throw new ArgumentNullException(nameof(dte));

        if (commandService != null) {
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }
    }

    private void OnBeforeQueryStatus(object sender, EventArgs e) {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (sender is OleMenuCommand command) {
            command.Properties.Remove(FolderNameProperty);
            command.Properties.Remove(ProjectItemProperty);
            command.Properties.Remove(ProjectProperty);
            SetSelectedNodeProperties(sender);
            var folderPath = command.Properties[FolderNameProperty]?.ToString();
            bool isFolderSelected = !string.IsNullOrEmpty(folderPath);
            bool hasClipboardText = false;
            if (isFolderSelected) {
                try {
                    hasClipboardText = Clipboard.ContainsText(TextDataFormat.Text) ||
                                      Clipboard.ContainsText(TextDataFormat.UnicodeText);
                    command.Properties[FolderNameProperty] = folderPath;
                }
                catch {
                    hasClipboardText = false;
                }
            }
            command.Enabled = isFolderSelected && hasClipboardText;
            command.Text = hasClipboardText ? "Paste as Class" : "Paste as Class (Clipboard Empty)";
            command.Visible = true;
        }
    }

    /// <summary>
    /// Gets the instance of the package.
    /// </summary>
    private IServiceProvider ServiceProvider => this.package;

    /// <summary>
    /// Initializes the singleton instance of the command.
    /// Call this inside your AsyncPackage's InitializeAsync method.
    /// </summary>
    public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package) {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
        new SolutionExplorerCommand(package, commandService!, dte!);
    }

    /// <summary>
    /// This method runs automatically whenever the user clicks your menu button.
    /// </summary>
    private void Execute(object sender, EventArgs e) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var sentItem = sender as OleMenuCommand;
        this.SetSelectedNodeProperties(sender);

        string clipboardText;
        try {
            if (!Clipboard.ContainsText(TextDataFormat.Text) && !Clipboard.ContainsText(TextDataFormat.UnicodeText)) {
                ShowError("Clipboard does not contain text.");
                return;
            }

            clipboardText = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clipboardText)) {
                ShowError("Clipboard text is empty.");
                return;
            }
        }
        catch (Exception ex) {
            ShowError($"Failed to read clipboard: {ex.Message}");
            return;
        }

        string path = sentItem?.Properties[FolderNameProperty].ToString() ?? "";
        if (string.IsNullOrEmpty(path)) {
            ShowError("Could not determine the folder path.");
            return;
        }
        var className = TryExtractClassName(clipboardText);
        if (string.IsNullOrWhiteSpace(className)) {
            ShowError("Could not find a C# class name in clipboard text.");
            return;
        }

        var filePath = Path.Combine(path, className + ".cs");
        if (File.Exists(filePath)) {
            ShowError("File already exists: " + className + ".cs");
            return;
        }
        try {
            string folderName = sentItem?.Properties[FolderNameProperty].ToString() ?? "";
            if (string.IsNullOrWhiteSpace(folderName)) {
                ShowError("Could not determine the folder path.");
                return;
            }
            var dteEnvProject = (sentItem?.Properties[ProjectProperty] as EnvDTE.Project)!;
            var projectItem = (sentItem?.Properties[ProjectItemProperty] as ProjectItem)!;
            File.WriteAllText(filePath, clipboardText, new UTF8Encoding(false));
            if (dteEnvProject != null && projectItem != null) {

                projectItem.ProjectItems?.AddFromFile(filePath);
            }
            else if (dteEnvProject != null && projectItem is null) {
                dteEnvProject.ProjectItems?.AddFromFile(filePath);
            }

        }
        catch (Exception ex) {
            ShowError("Failed to create file. " + ex.Message);
        }

    }

    private static string? TryExtractClassName(string source) {
        var match = Regex.Match(source, @"\bclass\s+([_@A-Za-z][_A-Za-z0-9]*)\b", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.TrimStart('@') : null;
    }

    /// <summary>
    /// Searches Visual Studio's active selection to pull out the hard drive path.
    /// </summary>
    private bool SetSelectedNodeProperties(object sender) {
        ThreadHelper.ThrowIfNotOnUIThread();

        var sentObject = sender as OleMenuCommand;

        EnvDTE.Project? selectedProject = null!;
        ProjectItem? selectedProjectItem = null!;
        if (sender is EnvDTE.Project senderProject) {
            selectedProject = senderProject;
        }
        else if (dte == null) {
            return false;
        }
        else if (dte.ToolWindows?.SolutionExplorer?.SelectedItems is not Array selectedItems || selectedItems.Length != 1) {
            return false;
        }
        else if (selectedItems.GetValue(0) is EnvDTE.Project dteProject) {
            selectedProject = dteProject;
        }
        else if (selectedItems.GetValue(0) is ProjectItem dteProjectItem) {
            selectedProject = dteProjectItem.ContainingProject;
            selectedProjectItem = dteProjectItem;
        }
        else if (selectedItems.GetValue(0) is UIHierarchyItem uiHierarchyItem && uiHierarchyItem.Object is EnvDTE.Project uiProject) {
            selectedProject = uiProject;
            selectedProjectItem = uiHierarchyItem.Object as ProjectItem;
        }
        else if (selectedItems.GetValue(0) is UIHierarchyItem uiHierarchyItemProjectItem && uiHierarchyItemProjectItem.Object is ProjectItem projectItem) {
            selectedProjectItem = projectItem;
            selectedProject = projectItem.ContainingProject;
        }
        if (selectedProject == null && selectedProjectItem == null) {
            return false;
        }
        else if (selectedProjectItem != null) {
            try {
                string? folderPath = selectedProjectItem.Properties?.Item("FullPath")?.Value as string;
                if (!string.IsNullOrWhiteSpace(folderPath)) {
                    sentObject?.Properties[FolderNameProperty] = folderPath;
                    sentObject?.Properties[ProjectItemProperty] = selectedProjectItem;
                    sentObject?.Properties[ProjectProperty] = selectedProject;
                    return true;
                }
            }
            catch {
                return false;
            }
        }
        else if (selectedProject != null) {
            try {
                string? projectPath = selectedProject.Properties?.Item("FullPath")?.Value as string;
                if (!string.IsNullOrWhiteSpace(projectPath)) {
                    sentObject?.Properties[FolderNameProperty] = projectPath;
                    sentObject?.Properties[ProjectItemProperty] = selectedProjectItem;
                    sentObject?.Properties[ProjectProperty] = selectedProject;
                    return true;
                }
            }
            catch {
                return false;
            }
        }
        return false;
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