# Paste As Class Visual Studio Extension

Visual Studio extension that creates a new `.cs` file from class text on the clipboard.

## What it does

This extension adds two context-menu commands in **Solution Explorer**:

- **Paste as Class**
  - Reads C# text from the clipboard
  - Extracts the class name
  - Creates `<ClassName>.cs` in the selected project or folder
- **Paste as Namespace and Class**
  - Does everything above
  - Also updates (or inserts) the namespace so it matches:
    - Project `DefaultNamespace`
    - Selected folder path under the project

## Where commands appear

Commands are available when right-clicking:

- Project node (`IDM_VS_CTXT_PROJNODE`)
- Folder node (`IDM_VS_CTXT_FOLDERNODE`)

The command group is not placed on the Solution node.

## Command availability

Commands are shown dynamically and enabled only when:

- Exactly one project/folder context is selected
- Clipboard contains text

## Package behavior

- Package is registered for background loading
- Auto-loads when a solution is fully loaded
- Command initialization switches to the main thread before registering menu commands

## Notes

- If the target file already exists, the command stops and shows an error.
- Files are written as UTF-8 without BOM.
- Designed for the classic VS extension model targeting .NET Framework 4.7.2.
