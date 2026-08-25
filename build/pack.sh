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
packages=(
  Basalt.Razor.Vb.Generator
  Basalt.Razor.Vb.AspNetCore
  Basalt.Razor.Vb.Hosting
)

echo "==> Packing into $out"
rm -rf "$out"
for project in "${packages[@]}"; do
  dotnet pack "$root/src/$project" -c Release -o "$out" --nologo -v q
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

echo "==> Verifying against a project that only has the packages"
cp -R "$root/templates/content/BasaltVbMvc" "$work/Site"
rm -rf "$work/Site/.template.config"

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
assembly="$work/Site/bin/Release/net10.0/BasaltVbMvc.dll"
if ! strings -a "$assembly" | grep -q 'BasaltVbMvc.Views.Home.Index'; then
  echo "FAILED: the project built but its .vbhtml views were not compiled." >&2
  echo "        The generator or its MSBuild props did not reach the project." >&2
  exit 1
fi

echo "==> Views compiled. Packages are good."
echo
echo "To publish:"
echo "    dotnet nuget push \"$out/*.nupkg\" -s https://api.nuget.org/v3/index.json -k YOUR_KEY"
