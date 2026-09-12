Lumen 1.0.0 RC1 - Visual Studio Installer Project
=================================================

This setup project uses Microsoft's Visual Studio Installer Projects extension.
No Inno Setup or other third-party installer builder is required.

Build
-----
1. Keep your normal Lumen Assets folder in the project root, including:
   Assets\Lumen.ico
   Assets\LumenLogo.png
   Assets\AutoSync\ggml-tiny.bin
2. Open Lumen.sln in Visual Studio.
3. Select Release / x64.
4. Rebuild Lumen.Setup.
5. The MSI is written to Lumen.Setup\Release\Lumen-Setup-1.0.0-RC1.msi.

Installer flow
--------------
The normal install flow now includes:
  Welcome
  Release Candidate Terms (must be accepted)
  Additional Tasks
    - Create a Start Menu shortcut (checked by default)
    - Create a desktop shortcut (checked by default)
  Installation Folder
  Confirm Installation
  Progress
  Finished

Branding
--------
The setup project generates a 493x58 Lumen banner from the existing transparent
Assets\LumenLogo.png after Visual Studio builds the MSI, then injects it into the
standard Windows Installer dialogs. This keeps the installer Microsoft-native
while giving the wizard Lumen branding.

Visual Studio Installer Projects uses the standard Windows Installer dialog
engine, so it cannot be fully reskinned into Lumen's dark WinUI theme. The
banner, Lumen icon, product text and installer flow are the supported branding
layer used here.

Post-build MSI fixup
--------------------
PostBuild.cmd runs two Microsoft/Windows-native steps:
  1. GenerateInstallerBranding.ps1 creates the branded banner.
  2. FixMsiDirectories.js uses the Windows Installer automation API to:
     - repair nested Publish Items directory rows;
     - apply the Lumen banner to wizard pages;
     - make Start Menu/Desktop shortcuts obey the Additional Tasks checkboxes.

For a verbose installation log, run Install-With-Log.cmd after building.


RC1 installer revision notes
----------------------------
The application remains Lumen 1.0.0 RC1. The MSI ProductVersion is 1.0.7 only
to distinguish this test installer from earlier RC1 MSI revisions and allow an
in-place upgrade. The UpgradeCode remains stable while ProductCode/PackageCode
are refreshed for this package.

Assets\Lumen.ico is embedded in the MSI for the Windows Installed Apps entry.
Start Menu and Desktop shortcuts target the installed Lumen.exe directly, so
Windows uses the icon already embedded in the executable. Both shortcut options
default to enabled and can be deselected on the Additional Tasks page.

The installer banner intentionally contains no text. Windows Installer draws its
own page title and subtitle over the banner, so the generated artwork keeps the
text area clear and places the Lumen logo at the right.
