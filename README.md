# Gurux DLMS AMI Modules

Modules and extensions for Gurux DLMS AMI, with automated releases and module-specific changelogs.

## Releases

Each module has independent versions and GitHub Releases. ZIP packages and commit-based changelogs are published automatically when a module tag is pushed.

For example, to publish SMTP version 1.2.0, commit the changes and push the tag:

```shell
git tag Gurux.AMI.SMTP/v1.2.0
git push origin Gurux.AMI.SMTP/v1.2.0
```

Use a suffix such as `-rc1` for prereleases. After publishing, the workflow notifies [Gurux AMI Catalog](https://github.com/Gurux/Gurux.AMI.Catalog), which updates its catalog and RSS feed.

## Build and GitHub Actions

Releases run on GitHub's `windows-latest` runner. The workflow installs .NET 10 and Python automatically. SMTP restores all dependencies from NuGet; no local AMI or UI source trees or self-hosted runner are needed.

Commit `NuGet.Config` and the module's `packages.lock.json` along with the project files. Releases use locked NuGet restore so dependency changes must be committed before tagging.

To build SMTP locally with the .NET 10 SDK:

```shell
dotnet restore Gurux.AMI.SMTP/Gurux.AMI.SMTP.csproj --locked-mode
dotnet publish Gurux.AMI.SMTP/Gurux.AMI.SMTP.csproj --configuration Release --no-restore -p:AmiModuleReleaseVersion=1.2.0
```

The ZIP is created in `Gurux.AMI.SMTP/bin/Release/`. Shared host assemblies are excluded from the package. To update package versions, edit the project, run `dotnet restore --force-evaluate` for that project and commit the updated lock file.

Add repository secret `CATALOG_DISPATCH_TOKEN`: a fine-grained PAT scoped to `Gurux/Gurux.AMI.Catalog`, with Contents read/write permission. Catalog must have its matching workflow and GitHub Pages enabled. The former `AMI_SOURCE_ROOT` and `UI_COMPONENTS_SOURCE_ROOT` variables are no longer used.

To retry a release or Catalog notification, run **Actions → Release AMI module → Run workflow** with the existing tag. Published ZIP packages are not overwritten.

## Package compatibility

Publish builds generate `module.compatibility.json` inside the ZIP. The workflow uploads a matching `<id>-<version>.compatibility.json` release asset before publishing the draft. Catalog validates this sidecar and copies its exact host assembly requirements to the release's `references` field, used by AMI's existing pre-install compatibility check.

For additional modules, set `AmiModuleCompatibilityMetadata` to `true` in the project, keep its assembly name equal to its module ID, and set `compatibilityMetadata: true` on its Catalog source. Include the `scripts/Gurux.AMI.Module.Metadata/` tool when uploading the repository. Missing or mismatched metadata prevents publication or Catalog replacement.

Run the checks locally:

```shell
python -m unittest discover -s tests -v
dotnet test tests/Smtp.Tests/Smtp.Tests.csproj --configuration Release -p:RestoreLockedMode=true
dotnet test tests/ModuleMetadata.Tests/ModuleMetadata.Tests.csproj --configuration Release
```
