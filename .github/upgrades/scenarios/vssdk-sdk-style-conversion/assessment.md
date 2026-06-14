# Assessment: VSSDK SDK-Style Conversion

## Target Project

| Property | Value |
|----------|-------|
| Project | PasteAsClassExtension |
| Path | `PasteAsClassExtension/PasteAsClassExtension.csproj` |
| Current TFM | `net472` (.NET Framework 4.7.2) |
| Target TFM | `net48` (.NET Framework 4.8.1) |
| Solution format | `.slnx` (modern format) |
| packages.config | No (already using PackageReference) |
| Project Format | **Already SDK-style** ✓ |

## VSIX Components Found

- ✅ VSIX manifest: `source.extension.vsixmanifest` (28 lines, well-formed)
- ✅ VSCT command table: `SolutionExplorerCommand.vsct` (66 lines, single VSCT file)
- ✅ Project capability: `<ProjectCapability Include="CreateVsixContainer" />`
- ✅ VSSDK build tools: `Microsoft.VSSDK.BuildTools` v18.5.40034
- ✅ Visual Studio SDK: `Microsoft.VisualStudio.SDK` v17.14.40265
- ✅ Framework references: `System.Design` (for UI designers)
- ✅ Debug configuration: `StartAction`, `StartProgram`, `StartArguments` (for F5 debugging)

## Current Package References

```xml
<PackageReference Include="Microsoft.VisualStudio.SDK" Version="17.14.40265" ExcludeAssets="runtime" />
<PackageReference Include="Microsoft.VSSDK.BuildTools" Version="18.5.40034" />
```

**VSSDK Build Tools Status**: ✅ Version `18.5.40034` **meets minimum requirement** (≥18.5.38461)

## Project Structure Assessment

**Current State**:
- Project is already SDK-style with proper VSSDK configuration
- All VSIX components present and correctly configured
- VSCT items properly declared with `<VSCTCompile>`
- VSIX manifest retained with designer metadata
- No legacy `packages.config` or ASP.NET-style web components

**What Needs to Change**:
1. **Target Framework**: Upgrade `<TargetFramework>` from `net472` to `net48` (aligns the project file with .NET Framework 4.8.1)
2. **Debug Configuration**: Remove legacy `StartAction`, `StartProgram`, `StartArguments` properties
3. **Deploy Markers**: Add deploy marker to `.slnx` solution file for F5 debugging support
4. **Property Check**: Verify VSIX-specific properties are properly set (`VSSDKBuildToolsAutoSetup`, `VsixDeployOnDebug`, `GeneratePkgDefFile`)

## Baseline Build Status

✅ **Project builds successfully** in Debug configuration
- Output: `bin/Debug/net472/PasteAsClassExtension.vsix` (generated correctly)
- All references resolve without errors
- No warnings or missing dependencies

## Key Findings

1. **No Complex Migration Needed**: Project is already SDK-style with VSSDK setup. This is a **straightforward upgrade** rather than a conversion.
2. **VSSDK Build Tools Compatible**: Current version `18.5.40034` is above the minimum floor of `18.5.38461` — no version conflict.
3. **No Auto-Generated File Issues**: No `AssemblyInfo.cs` to delete or legacy compile includes to clean up.
4. **Solution File**: Uses modern `.slnx` format, which requires `<Deploy />` element instead of legacy `.Deploy.0` entries.
5. **VSIX Manifest**: Already has proper generator metadata; no changes needed.

## Scope

This scenario will:
- ✅ Update target framework to `net48` (minor version bump within .NET Framework)
- ✅ Ensure VSSDK properties are correctly set for F5 debugging
- ✅ Add deploy marker to solution file for experimental instance launch
- ✅ Remove legacy debug properties (`StartAction`, etc.)
- ✅ Verify F5 debugging works with the experimental instance
- ✅ Validate VSIX output after upgrade

## Risks & Considerations

| Risk | Likelihood | Mitigation |
|------|-----------|-----------|
| **Stale VSSDK build artifacts** | Medium | Full clean of `bin`/`obj` folders before final build; never clear NuGet caches |
| **F5 debugging fails** | Low | Deploy marker in solution file must be correct; VSSDK BuildTools version must remain ≥18.5.38461 |
| **IDE caches stale project state** | Medium | If running in IDE, unload project before modifications; reload after all changes applied |
| **.NET Framework 4.8.1 unavailable** | Very Low | Framework is standard on Windows; no runtime installation needed for compilation |

## Success Criteria

- ✅ Project file updated to target `net48`
- ✅ VSSDK properties properly configured (`VSSDKBuildToolsAutoSetup=true`, `VsixDeployOnDebug=true`, etc.)
- ✅ Deploy marker added to `.slnx` solution file
- ✅ Legacy debug properties removed
- ✅ Project builds successfully and produces `.vsix` output
- ✅ F5 debugging launches experimental instance correctly
- ✅ No build warnings or errors
- ✅ Solution can be reloaded in Visual Studio without stale cache issues
