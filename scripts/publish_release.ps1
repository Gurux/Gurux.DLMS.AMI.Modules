$ErrorActionPreference = 'Stop'
$release = Get-Content release/release.json -Raw | ConvertFrom-Json
$repo = $env:GITHUB_REPOSITORY
$tag = $release.tag
$package = Join-Path release $release.package
$compatibility = Join-Path release $release.compatibilityAsset
if (-not $env:CATALOG_TOKEN) { throw 'Missing CATALOG_DISPATCH_TOKEN' }

# Published releases are immutable here; retrying a notification does not replace a ZIP.
$existing = gh release view $tag --repo $repo --json isDraft,assets 2>$null
if ($LASTEXITCODE -eq 0) {
    $existing = $existing | ConvertFrom-Json
    $draft = $existing.isDraft
    if (-not $draft -and $release.package -notin $existing.assets.name) { throw 'Published release is missing its module ZIP' }
    if (-not $draft -and $release.compatibilityAsset -notin $existing.assets.name) { throw 'Published release is missing its compatibility metadata' }
} else {
    $arguments = @('release', 'create', $tag, '--repo', $repo, '--verify-tag', '--draft',
                   '--title', "$($release.id) $($release.version)", '--notes-file', 'release/notes.md')
    if ($release.prerelease) { $arguments += '--prerelease' }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create draft release' }
    $draft = $true
}
if ($draft) {
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Missing package: $package" }
    if (-not (Test-Path -LiteralPath $compatibility -PathType Leaf)) { throw "Missing compatibility metadata: $compatibility" }
    $requirements = Get-Content -LiteralPath $compatibility -Raw | ConvertFrom-Json
    if ($requirements.schemaVersion -ne 1 -or $requirements.id -cne $release.id -or
        $requirements.version -cne $release.version -or $requirements.targetFramework -ne 'net10.0' -or
        @($requirements.references).Count -eq 0) { throw 'Invalid module compatibility metadata' }
    gh release upload $tag $package $compatibility --repo $repo --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Failed to upload module ZIP' }
    gh release edit $tag --repo $repo --notes-file release/notes.md --draft=false --latest=false
    if ($LASTEXITCODE -ne 0) { throw 'Failed to publish module release' }
}

# Cross-repository dispatch needs its own token, scoped to Catalog Contents: write.
$env:GH_TOKEN = $env:CATALOG_TOKEN
$payload = @{ event_type = 'module-release'; client_payload = @{
    module = $release.id; tag = $tag; repository = $repo
}} | ConvertTo-Json -Depth 3
$payload | gh api --method POST repos/Gurux/Gurux.AMI.Catalog/dispatches --input -
if ($LASTEXITCODE -ne 0) { throw 'Release published, but Catalog notification failed. Retry this workflow with the same tag.' }
