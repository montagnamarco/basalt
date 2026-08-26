#!/usr/bin/env bash
#
# Builds the three NuGet packages and proves they work before anyone publishes
# them.
#
# The proof is the point. A package can pack cleanly, install cleanly, and
# still produce nothing: the generator flows through a dependency but the
# MSBuild props that hand it the views do not, so the project builds green with
# no views in it and says nothing about why. That is exactly what happened
# here, and only a real build against the real packages showed it.
#
#   build/pack.sh              # pack and verify
#   build/pack.sh --skip-test  # pack only
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/artifacts"
projects=(
  src/Basalt.Razor.Vb.Generator
  src/Basalt.Razor.Vb.AspNetCore
  src/Basalt.Razor.Vb.Hosting
  templates
)

echo "==> Packing into $out"
rm -rf "$out"
for project in "${projects[@]}"; do
  dotnet pack "$root/$project" -c Release -o "$out" --nologo -v q
done

ls "$out"/*.nupkg | sed 's|.*/|    |'

if [[ "${1:-}" == "--skip-test" ]]; then
  echo "==> Verification skipped"
  exit 0
fi

# A scratch project consuming the packages the way a user would: from a feed,
# by version, with no reference to this repository's sources.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

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
