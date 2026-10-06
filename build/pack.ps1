<#
.SYNOPSIS
Builds the NuGet packages and proves they work before anyone publishes them.

.DESCRIPTION
The Windows counterpart of build/pack.sh, step for step: pack into
artifacts/, check the template package's layout from the package itself,
then build a project that references nothing but the packages and look for
the compiled views and components inside its assembly.

pack.sh needs Git Bash and `strings`; this runs in Windows PowerShell 5.1 or
PowerShell 7 with nothing else installed. Both scripts must say the same
thing, so a change to one is a change to the other.

Note that verification installs Basalt.Templates into the user's dotnet new
cache, exactly as pack.sh does. Use -SkipTest to pack only.

.EXAMPLE
build\pack.ps1
build\pack.ps1 -SkipTest
build\pack.ps1 -WinFormsOnly   # Basalt.Drawing and Basalt.WinForms only; installs nothing
#>
param(
    [switch] $SkipTest,
    [switch] $WinFormsOnly
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts"
$projects = @(
    "src/Basalt.Razor.Vb.Generator",
    "src/Basalt.Razor.Vb.AspNetCore",
    "src/Basalt.Razor.Vb.Hosting",
    "templates",
    "src/Basalt.Drawing",
    "src/Basalt.WinForms"
)
if ($WinFormsOnly) {
    $projects = @("src/Basalt.Drawing", "src/Basalt.WinForms")
}

# A native command that fails does not stop a PowerShell script on its own:
# every call goes through this so a failed pack is not reported as success.
function Invoke-Checked {
    param([string] $What, [scriptblock] $Command)

    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE." }
}

Write-Host "==> Packing into $out"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

foreach ($project in $projects) {
    Invoke-Checked "dotnet pack $project" { dotnet pack (Join-Path $root $project) -c Release -o $out --nologo -v q }
}

Get-ChildItem $out -Filter *.nupkg | ForEach-Object { Write-Host "    $($_.Name)" }

if ($SkipTest) {
    Write-Host "==> Verification skipped"
    exit 0
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("basalt-pack-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

try {
    # A project that has only Basalt.WinForms, as a user's would: a net10.0
    # VB WinExe restored from the local feed. Built and not run, because
    # starting a window needs a display. See pack.sh for what this catches.
    Write-Host "==> Verifying the WinForms package"
    $winformsPackage = Get-ChildItem $out -Filter "Basalt.WinForms.*.nupkg" | Select-Object -First 1

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($winformsPackage.FullName)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -eq "Basalt.WinForms.nuspec" }
        $reader = New-Object IO.StreamReader($nuspecEntry.Open())
        try { $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally {
        $archive.Dispose()
    }

    foreach ($assembly in "System.Windows.Forms", "System.Windows.Forms.Primitives", "Microsoft.VisualBasic.Forms", "System.Windows.Extensions", "Accessibility") {
        if ($entries -notcontains "lib/net10.0/$assembly.dll") {
            throw "The WinForms package does not hold lib/net10.0/$assembly.dll."
        }
    }
    if (-not $nuspec.Contains('<dependency id="Basalt.Drawing"')) {
        throw "The WinForms package does not depend on Basalt.Drawing."
    }

    $app = Join-Path $work "WinApp"
    New-Item -ItemType Directory -Force $app | Out-Null

    Set-Content -Path (Join-Path $app "WinApp.vbproj") -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>WinApp</RootNamespace>
    <OptionStrict>On</OptionStrict>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Basalt.WinForms" Version="*-*" />
  </ItemGroup>
</Project>
'@

    Set-Content -Path (Join-Path $app "MainForm.vb") -Encoding UTF8 -Value @'
Imports System.Windows.Forms

Public Class MainForm
    Inherits Form

    Private WithEvents Greet As New Button()

    Public Sub New()
        Text = "Probe"
        Greet.Text = "Click me"
        Controls.Add(Greet)
    End Sub

    Private Sub Greet_Click(sender As Object, e As EventArgs) Handles Greet.Click
        MessageBox.Show("Hello")
    End Sub

    <STAThread>
    Public Shared Sub Main()
        Application.Run(New MainForm())
    End Sub
End Class
'@

    Set-Content -Path (Join-Path $app "nuget.config") -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$out" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

    # Cached copies of an earlier pack at the same version would be restored
    # instead of the ones just built.
    $nugetCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME ".nuget\packages" }
    foreach ($id in "basalt.winforms", "basalt.drawing") {
        $cachedPackage = Join-Path $nugetCache $id
        if (Test-Path $cachedPackage) { Remove-Item -Recurse -Force $cachedPackage }
    }

    Invoke-Checked "dotnet build (WinForms)" { dotnet build $app -c Release --nologo -v q }
    Write-Host "==> A WinForms project builds against Basalt.WinForms."

    # Building proves only what the consumer's own code touches; the dependency
    # file of the build output says what will actually be loaded at run time.
    $deps = Get-ChildItem -Path (Join-Path $app "bin/Release") -Recurse -Filter "WinApp.deps.json" | Select-Object -First 1
    $depsText = Get-Content -Raw $deps.FullName
    foreach ($library in @("Basalt.Drawing/", "Avalonia.Desktop/", "Avalonia.Skia/", "System.Resources.Extensions/", "System.IO.Ports/")) {
        if (-not $depsText.Contains('"' + $library)) {
            throw "The WinForms application would run without $library"
        }
    }
    Write-Host "==> Its run-time dependencies are resolved."

    if ($WinFormsOnly) { exit 0 }

    # The layout read from the package, not from what dotnet new happens to
    # offer on this machine: see pack.sh for how that check once lied.
    Write-Host "==> Verifying the template package"
    $templatePackage = Get-ChildItem $out -Filter "Basalt.Templates.*.nupkg" | Select-Object -First 1

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($templatePackage.FullName)
    try {
        $templatesFound = @($zip.Entries | Where-Object {
            $_.FullName -match '^content/[^/]+/\.template\.config/template\.json$'
        }).Count
    }
    finally {
        $zip.Dispose()
    }

    if ($templatesFound -lt 5) {
        throw "The template package holds $templatesFound templates under content/, expected 5. dotnet new only looks there, so it would install and offer nothing."
    }

    # Then really installed, because a correct layout can still fail to register.
    dotnet new uninstall Basalt.Templates *> $null
    Invoke-Checked "dotnet new install" { dotnet new install $templatePackage.FullName | Out-Null }

    Write-Host "==> Verifying against a project that only has the packages"
    $site = Join-Path $work "Site"
    Invoke-Checked "dotnet new mvc" { dotnet new mvc -lang VB -o $site | Out-Null }

    # Written before the first build: MSBuild expands the props globs when
    # the project is evaluated.
    Set-Content -Path (Join-Path $site "Probe.vbrazor") -Encoding UTF8 -Value @'
@Page "/probe"
<h1>Probe</h1>
'@

    Set-Content -Path (Join-Path $site "nuget.config") -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$out" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

    # Cached copies of a previous pack at the same version would be restored
    # instead of the ones just built.
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME ".nuget\packages" }
    foreach ($id in "basalt.razor.vb", "basalt.razor.vb.aspnetcore", "basalt.razor.vb.hosting") {
        $cached = Join-Path $packages $id
        if (Test-Path $cached) { Remove-Item -Recurse -Force $cached }
    }

    Invoke-Checked "dotnet build" { dotnet build $site -c Release --nologo -v q }

    # The metadata strings of the assembly, which is what `strings` reads in
    # pack.sh. Type names and the route attribute's argument are stored as
    # UTF-8.
    $assembly = Join-Path $site "bin\Release\net10.0\Site.dll"
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($assembly))

    if (-not $text.Contains("Site.Views.Home.Index")) {
        throw "The project built but its .vbhtml views were not compiled. The generator or its MSBuild props did not reach the project."
    }

    Write-Host "==> Verifying a Blazor component"

    if (-not $text.Contains("/probe")) {
        throw "A .vbrazor component was not compiled into the assembly. The component generator or its MSBuild props did not reach it."
    }

    Write-Host "==> Views and components compiled. Packages are good."
    Write-Host ""
    Write-Host "To publish:"
    Write-Host "    dotnet nuget push `"$out\*.nupkg`" -s https://api.nuget.org/v3/index.json -k YOUR_KEY"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
