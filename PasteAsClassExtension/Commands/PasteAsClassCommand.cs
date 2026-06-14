using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PasteAsClassExtension.Commands;

/// <summary>
/// Handles the right-click menu command and gets the file path of the selected node.
/// </summary>
internal sealed class PasteAsClassCommand {
    private const int VK_CONTROL = 0x11;
    private const string FolderNameProperty = "MRBR_FOLDER_NAME";
    private const string ProjectItemProperty = "MRBR_PROJECT_ITEM";
    private const string ProjectProperty = "MRBR_PROJECT";
    public static readonly Guid CommandSet = new("968c03c3-e904-4f98-8cac-7262c34a305a");
    public const int GroupId = 0x0600;
    public const int PasteAsClassCommandId = 0x0100;
    public const int PasteAsNamespaceClassCommandId = 0x0101;

    private readonly AsyncPackage package;
    private readonly DTE2 dte;
    /// <summary>
    /// Initializes a new instance of the <see cref="PasteAsClassCommand"/> class.
    /// </summary>
    /// <param name="package">The package instance.</param>
    /// <param name="commandService">The command service.</param>
    /// <param name="dte">The DTE2 instance.</param>
    /// <exception cref="ArgumentNullException"></exception>
    private PasteAsClassCommand(AsyncPackage package, OleMenuCommandService commandService, DTE2 dte) {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
        this.dte = dte ?? throw new ArgumentNullException(nameof(dte));

        if (commandService != null) {
            var menuPasteCommandID = new CommandID(CommandSet, PasteAsClassCommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuPasteCommandID);
            var menuNamespaceClassCommandID = new CommandID(CommandSet, PasteAsNamespaceClassCommandId);
            var menuNamespaceClassItem = new OleMenuCommand(this.Execute, menuNamespaceClassCommandID);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            menuNamespaceClassItem.BeforeQueryStatus += OnBeforeQueryStatus;

            commandService.AddCommand(menuItem);
            commandService.AddCommand(menuNamespaceClassItem);
        }
    }
    /// <summary>
    /// This method is called before the command's status is queried, allowing you to enable or disable the command based on the current context.
    /// </summary>
    /// <param name="sender">The command sender.</param>
    /// <param name="e">The event arguments.</param>
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
            if (command.CommandID.ID == PasteAsClassCommandId) {
                command.Text = "Paste as Class";
            }
            else if (command.CommandID.ID == PasteAsNamespaceClassCommandId) {
                command.Text = "Paste as Namespace and Class";
            }
            command.Visible = true;
        }
    }

    /// <summary>
    /// Gets the instance of the package.
    /// </summary>
    private IServiceProvider ServiceProvider => this.package;

    /// <summary>
    /// Initializes the command and adds it to the command service.
    /// </summary>
    /// <param name="package">The package instance.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async System.Threading.Tasks.Task InitializeAsync(AsyncPackage package) {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;
        new PasteAsClassCommand(package, commandService!, dte!);
    }

    /// <summary>
    /// Executes the command when it is invoked. This method reads the clipboard text, extracts the class name, and creates a new .cs file in the selected folder with the clipboard content. It also handles namespace adjustments if the "Paste as Namespace and Class" command is used.
    /// </summary>
    /// <param name="sender">The command sender.</param>
    /// <param name="e">The event arguments.</param>
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
        var className = TryExtractClassName(clipboardText, out string fileType);
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

            if (sentItem?.CommandID.ID == PasteAsNamespaceClassCommandId) {
                var defaultNamespace = (dteEnvProject.Properties?.Item("DefaultNamespace")?.Value as string) ?? "";
                string newNamespace = defaultNamespace;
                if (projectItem is not null) {
                    // get all the folders from the root of the project to the selected folder and create a namespace from them.
                    var namespaceFromFolder = GetParentFolderNames(projectItem);
                    newNamespace = string.Join(".", [defaultNamespace, .. namespaceFromFolder, projectItem.Name]);
                }
                var namespaceMatch = NamespaceRegex.Match(clipboardText);
                if (namespaceMatch.Success) {
                    if (namespaceMatch.Groups["namespaceFile"].Success) {
                        var namespaceName = namespaceMatch.Groups["namespaceFileName"].Value.Trim();
                        clipboardText = clipboardText.Replace(namespaceMatch.Groups["namespaceFileName"].Value, newNamespace);
                    }
                    else if (namespaceMatch.Groups["namespaceBlock"].Success) {
                        var namespaceName = namespaceMatch.Groups["namespaceBlockName"].Value.Trim();
                        clipboardText = clipboardText.Replace(namespaceMatch.Groups["namespaceBlockName"].Value, newNamespace + " ");
                    }
                }
                else {
                    // If there is no namespace use file based namespace.
                    // Using the usings Regex find all the usings and insert the namespace after the last using.
                    // If there are no usings, insert the namespace at the top of the file.
                    var usingsMatch = UsingsRegex.Matches(clipboardText);
                    if (usingsMatch.Count > 0) {
                        var lastUsing = usingsMatch[usingsMatch.Count - 1];
                        clipboardText = clipboardText.Insert(lastUsing.Index + lastUsing.Length, $"\r\nnamespace {newNamespace};\r\n");
                    }
                    else {
                        clipboardText = $"namespace {newNamespace};\r\n{clipboardText}";
                    }
                }
            }

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

    /// <summary>
    /// Gets the names of all parent folders for a given ProjectItem, starting from the immediate parent up to the root of the project. This can be useful for constructing namespaces based on folder structure.
    /// </summary>
    /// <param name="item">The ProjectItem for which to retrieve parent folder names.</param>
    /// <returns>A list of parent folder names, ordered from the root to the immediate parent.</returns>
    public static List<string> GetParentFolderNames(ProjectItem item) {
        ThreadHelper.ThrowIfNotOnUIThread();
        List<string> folderNames = new List<string>();

        // Start with the immediate parent collection of the current item
        object currentParent = item.Collection?.Parent!;

        // Loop upwards until we hit the root Project object
        while (currentParent is ProjectItem parentItem) {
            // Check if the parent item is actually a folder
            if (parentItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFolder ||
                parentItem.Kind == EnvDTE.Constants.vsProjectItemKindVirtualFolder) {
                folderNames.Add(parentItem.Name);
            }

            // Move one level higher in the tree hierarchy
            currentParent = parentItem.Collection?.Parent!;
        }

        // Optional: Reverse the list so it reads from Root -> Leaf
        folderNames.Reverse();

        return folderNames;
    }



    /// <summary>
    /// Regular expression to match using directives in C# code. This regex captures all using statements, allowing for multi-line matches and various whitespace characters. It is used to identify the location of using directives in the clipboard text when adjusting namespaces.
    /// </summary>
    Regex UsingsRegex = new Regex("(?<usings>using [\\s\\S]+?;)", RegexOptions.Multiline);
    /// <summary>
    /// Regular expression to match namespace declarations in C# code. This regex captures both file-scoped and block-scoped namespaces, allowing for multi-line matches and various whitespace characters. It is used to identify the location of namespace declarations in the clipboard text when adjusting namespaces.
    /// </summary>
    Regex NamespaceRegex = new Regex("((?<namespaceFile>(namespace\\s+(?<namespaceFileName>[\\s\\S]+?));[\\s\\S]*?(class|interface|enum)))|((?<namespaceBlock>(namespace\\s+(?<namespaceBlockName>[\\s\\S]+?){)[\\s\\S]*?(class|interface|enum)))", RegexOptions.Multiline);
    /// <summary>
    /// Attempts to extract the class name from the provided C# source code using a regular expression. The regex looks for the keyword "class" followed by a valid C# identifier, which is captured and returned. If no class name is found, the method returns null.
    /// </summary>
    /// <param name="source">The C# source code from which to extract the class name.</param>
    /// <param name="fileType">The type of the file, e.g., "class", "interface", etc.</param>
    /// <returns>The extracted class name, or null if no class name is found.</returns>
    private static string? TryExtractClassName(string source, out string fileType) {
        var match = Regex.Match(source, @"\b(?<fileType>class|interface|enum)\s+(?<fileName>[_@A-Za-z][_A-Za-z0-9]*)\b", RegexOptions.Multiline);
        string retVal = string.Empty;
        if (match.Success) {
            fileType = match.Groups["fileType"].Value;
            retVal = match.Groups["fileName"].Value;
        }
        else {
            fileType = string.Empty;
        }
        return match.Success ? retVal.TrimStart('@') : null;
    }

    /// <summary>
    /// Sets the properties of the selected node in the Solution Explorer, including the folder path, project item, and project. This method is used to determine the context for the "Paste as Class" command, ensuring that the command operates on the correct project and folder. It handles various types of selected items, including projects and project items, and retrieves their full paths.
    /// </summary>
    /// <param name="sender">The source of the event, typically the command or menu item.</param>
    /// <returns>True if the properties were successfully set; otherwise, false.</returns>
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
    /// <summary>
    /// Displays an error message to the user using Visual Studio's message box. This method is used to inform the user of issues encountered during the execution of the "Paste as Class" command, such as clipboard errors or file creation failures.
    /// </summary>
    /// <param name="message">The error message to display.</param>
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