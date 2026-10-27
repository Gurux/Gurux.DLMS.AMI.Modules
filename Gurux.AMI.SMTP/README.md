# Gurux AMI SMTP

SMTP email module for Gurux DLMS AMI. Provides HTML email delivery through your SMTP server and a configuration view within AMI.

## Features

- SMTP server and sender account configuration.
- STARTTLS support.
- Test email delivery from the configuration view.
- Server-only SMTP services with safe error messages and detailed server-side logging.

## Configuration

Configure the module in Gurux DLMS AMI using your email provider's SMTP settings:

| Setting | Description |
|---|---|
| Server | SMTP server hostname or IP address |
| Port | SMTP port; default is `587` |
| Timeout | Maximum delivery time in seconds; default `30`, allowed range `1–300` |
| Email | Sender email address and SMTP username |
| Password | SMTP account password |
| STARTTLS | Encrypt the connection; enabled by default |

Use **Send test email** to verify the settings.

## Releases

The module builds with .NET 10 and NuGet dependencies. GitHub Actions publishes releases on GitHub's own runner; local AMI source projects are not required. Package versions are recorded in `packages.lock.json`.

Module ZIP packages are available in the [repository releases](https://github.com/Gurux/Gurux.DLMS.AMI.Modules/releases) and listed in [Gurux AMI Catalog](https://github.com/Gurux/Gurux.AMI.Catalog).

SMTP releases use tags such as `Gurux.AMI.SMTP/v1.2.0`. See the [repository README](../README.md) for build and publishing instructions.

## Compatibility

Each ZIP includes `module.compatibility.json`, generated from the compiled module and its bundled dependencies. It declares .NET 10 and the exact host DLL versions required by this build. The release also includes `<id>-<version>.compatibility.json`, which Catalog reads before offering the update.

AMI's existing assembly compatibility check can block installation when these requirements do not match the host. Requirements are binary DLL versions, not a guessed minimum AMI product version.

## Tests

```shell
dotnet test tests/Smtp.Tests/Smtp.Tests.csproj --configuration Release -p:RestoreLockedMode=true
```

Run from the repository root. Tests use a fake transport to cover settings, passwords, validation, errors, cancellation and resource disposal without contacting an SMTP server.

## License

GNU General Public License v2.0 only (`GPL-2.0-only`).
