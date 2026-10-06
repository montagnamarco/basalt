#!/usr/bin/env bash
#
# Builds the NuGet packages and proves they work before anyone publishes them.
#
# The proof is the point. A package can pack cleanly, install cleanly, and
# still produce nothing: the generator flows through a dependency but the
# MSBuild props that hand it the views do not, so the project builds green with
# no views in it and says nothing about why. That is exactly what happened
# here, and only a real build against the real packages showed it.
#
#   build/pack.sh              # pack and verify
#   build/pack.sh --skip-test  # pack only
#   build/pack.sh --winforms-only  # pack and verify Basalt.Drawing and
#                                  # Basalt.WinForms only; installs nothing
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/artifacts"
projects=(
  src/Basalt.Razor.Vb.Generator
  src/Basalt.Razor.Vb.AspNetCore
  src/Basalt.Razor.Vb.Hosting
  templates
  src/Basalt.Drawing
  src/Basalt.WinForms
)
mode="${1:-}"
if [[ "$mode" == "--winforms-only" ]]; then
  projects=(src/Basalt.Drawing src/Basalt.WinForms)
fi

echo "==> Packing into $out"
rm -rf "$out"
for project in "${projects[@]}"; do
  dotnet pack "$root/$project" -c Release -o "$out" --nologo -v q
done

ls "$out"/*.nupkg | sed 's|.*/|    |'

if [[ "$mode" == "--skip-test" ]]; then
  echo "==> Verification skipped"
  exit 0
fi

# Scratch projects consuming the packages the way a user would: from a feed,
# by version, with no reference to this repository's sources.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# A project that has only Basalt.WinForms, as a user's would: a net10.0 VB
# WinExe restored from the local feed. Built and not run, because starting a
# window needs a display. This catches a package that packs but does not work:
# an assembly missing from lib/, or a dependency left out of the nuspec, which
# fails the restore or the compile of the form below.
echo "==> Verifying the WinForms package"
winforms_package="$(ls "$out"/Basalt.WinForms.*.nupkg | head -1)"
# Read into variables first: grep -q closes the pipe early, and with pipefail
# the resulting SIGPIPE in unzip would fail the check for a package that is fine.
winforms_files="$(unzip -l "$winforms_package")"
winforms_nuspec="$(unzip -p "$winforms_package" Basalt.WinForms.nuspec)"
for assembly in System.Windows.Forms System.Windows.Forms.Primitives \
                Microsoft.VisualBasic.Forms System.Windows.Extensions Accessibility; do
  if ! grep -q "lib/net10.0/$assembly.dll" <<<"$winforms_files"; then
    echo "FAILED: the WinForms package does not hold lib/net10.0/$assembly.dll." >&2
    exit 1
  fi
done
if ! grep -q '<dependency id="Basalt.Drawing"' <<<"$winforms_nuspec"; then
  echo "FAILED: the WinForms package does not depend on Basalt.Drawing." >&2
  exit 1
fi

mkdir -p "$work/WinApp"
cat > "$work/WinApp/WinApp.vbproj" <<'PROJECT'
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
PROJECT
cat > "$work/WinApp/MainForm.vb" <<'FORM'
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
FORM
cat > "$work/WinApp/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$out" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

# Cached copies of an earlier pack at the same version would be restored
# instead of the ones just built.
for id in basalt.winforms basalt.drawing; do
  rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/$id"
done

dotnet build "$work/WinApp" -c Release --nologo -v q
echo "==> A WinForms project builds against Basalt.WinForms."

# Building proves only what the consumer's own code touches. The windowing,
# drawing and resource packages are used inside the WinForms assemblies, so a
# dependency dropped from the package would still build and fail at run time:
# the dependency file of the build output says what will actually be loaded.
deps="$(find "$work/WinApp/bin/Release" -name WinApp.deps.json | head -1)"
for library in Basalt.Drawing/ Avalonia.Desktop/ Avalonia.Skia/ System.Resources.Extensions/ System.IO.Ports/; do
  if ! grep -q "\"$library" "$deps"; then
    echo "error: the WinForms application would run without $library" >&2
    exit 1
  fi
done
echo "==> Its run-time dependencies are resolved."

if [[ "$mode" == "--winforms-only" ]]; then
  exit 0
fi

# The package's own layout, read from the package. Installing and asking
# dotnet new what it offers measures the machine instead: templates left over
# from an earlier run answer the question, and a package that puts its
# templates somewhere dotnet new never looks passes anyway. That is exactly
# what happened — the check reported success with the layout deliberately
# broken.
echo "==> Verifying the template package"
template_package="$(ls "$out"/Basalt.Templates.*.nupkg | head -1)"
templates_found="$(unzip -l "$template_package" | grep -c 'content/[^/]*/\.template\.config/template\.json' || true)"

if [[ "$templates_found" -lt 5 ]]; then
  echo "FAILED: the template package holds $templates_found templates under content/, expected 5." >&2
  echo "        dotnet new only looks there, so it would install and offer nothing." >&2
  exit 1
fi

# Then really installed, because a correct layout can still fail to register.
dotnet new uninstall Basalt.Templates >/dev/null 2>&1 || true
dotnet new install "$template_package" >/dev/null

echo "==> Verifying against a project that only has the packages"
dotnet new mvc -lang VB -o "$work/Site" >/dev/null

# Written before anything is built: MSBuild expands the props globs when the
# project is evaluated, so a file created after the first build is invisible
# to the second.
cat > "$work/Site/Probe.vbrazor" <<'COMPONENT'
@Page "/probe"
<h1>Probe</h1>
COMPONENT

cat > "$work/Site/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$out" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

# Cached copies of a previous pack at the same version would be restored
# instead of the ones just built, and the run would prove nothing.
for id in basalt.razor.vb basalt.razor.vb.aspnetcore basalt.razor.vb.hosting; do
  rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/$id"
done

dotnet build "$work/Site" -c Release --nologo -v q

# The views must be classes in the assembly. Checking only that the build
# succeeded is what let the missing props go unnoticed: without the generator
# the project still compiles, it just has no views.
assembly="$work/Site/bin/Release/net10.0/Site.dll"
if ! strings -a "$assembly" | grep -q 'Site.Views.Home.Index'; then
  echo "FAILED: the project built but its .vbhtml views were not compiled." >&2
  echo "        The generator or its MSBuild props did not reach the project." >&2
  exit 1
fi

# And a Blazor component, which goes through a different generator and a
# different writer: a .vbrazor builds a render tree rather than writing
# markup, so the view passing says nothing about it.
echo "==> Verifying a Blazor component"

# By its route, which the component carries as an attribute: the class name
# alone appears in an assembly for other reasons, and the namespace is stored
# separately from it rather than as one string.
if ! strings -a "$assembly" | grep -q '^/probe$'; then
  echo "FAILED: a .vbrazor component was not compiled into the assembly." >&2
  echo "        The component generator or its MSBuild props did not reach it." >&2
  exit 1
fi

echo "==> Views and components compiled. Packages are good."
echo
echo "To publish:"
echo "    dotnet nuget push \"$out/*.nupkg\" -s https://api.nuget.org/v3/index.json -k YOUR_KEY"
