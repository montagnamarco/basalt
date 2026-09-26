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
#>
param(
    [switch] $SkipTest
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts"
$projects = @(
    "src/Basalt.Razor.Vb.Generator",
    "src/Basalt.Razor.Vb.AspNetCore",
    "src/Basalt.Razor.Vb.Hosting",
    "templates"
)

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
