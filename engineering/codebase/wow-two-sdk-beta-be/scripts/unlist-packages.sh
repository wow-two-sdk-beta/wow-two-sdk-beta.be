#!/usr/bin/env bash
# Unlists package versions on nuget.org with the trusted-publishing key; nuget.org has no delete, so unlisting hides a
# version from search and new restores while every existing restore keeps working. Relisting is the web UI's.
#
#   unlist-packages.sh <version>                 every package of the family at that version
#   unlist-packages.sh <id>@<version> [...]      the named versions only
#
# Env: NUGET_API_KEY   the temporary key NuGet/login returns; the policy needs the "Unlist or relist" scope
set -euo pipefail

[ "$#" -gt 0 ] || { echo "usage: unlist-packages.sh <version> | <id>@<version> [...]" >&2; exit 2; }
: "${NUGET_API_KEY:?NUGET_API_KEY is not set}"

version_pattern='^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$'
id_pattern='^[A-Za-z0-9_.-]+$'

targets=()
if [ "$#" -eq 1 ] && [[ "$1" =~ $version_pattern ]]; then
  # The eight published ids, as verify-published-packages.sh lists them.
  for id in WoW2.Sdk.Beta.Be WoW2.Sdk.Beta.Be.Abstractions WoW2.Sdk.Beta.Be.Data.Abstractions \
    WoW2.Sdk.Beta.Be.Data.Migrations.Cli WoW2.Sdk.Beta.Be.Testing WoW2.Sdk.Beta.Be.Testing.Data \
    WoW2.Sdk.Beta.Be.Testing.Integrations WoW2.Sdk.Beta.Be.Testing.Messaging; do
    targets+=("$id@$1")
  done
else
  for target in "$@"; do
    id=${target%@*} version=${target#*@}
    if [ "$id" = "$target" ] || ! [[ "$id" =~ $id_pattern ]] || ! [[ "$version" =~ $version_pattern ]]; then
      echo "::error::'$target' is neither a version nor <id>@<version>" >&2
      exit 2
    fi
    targets+=("$target")
  done
fi

failed=0
for target in "${targets[@]}"; do
  id=${target%@*} version=${target#*@}
  lower=$(tr '[:upper:]' '[:lower:]' <<< "$id")
  # A version nuget.org never published is skipped, so a family-wide unlist tolerates a package added later.
  status=$(curl --silent --head --output /dev/null --write-out '%{http_code}' --max-time 20 \
    "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg" || true)
  if [ "$status" != 200 ]; then
    echo "skipped: $id $version is not published"
    continue
  fi
  if dotnet nuget delete "$id" "$version" --source https://api.nuget.org/v3/index.json \
    --api-key "$NUGET_API_KEY" --non-interactive > /dev/null; then
    echo "unlisted: $id $version"
  else
    echo "::error::could not unlist $id $version" >&2
    failed=1
  fi
done
exit "$failed"
