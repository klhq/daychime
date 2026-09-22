# Releasing Workday Widget

## Choose the right distribution path

| Audience | Recommended path | Signing |
| --- | --- | --- |
| General public | Microsoft Store | Microsoft signs the package; no certificate management or install workaround for users. |
| Direct public download | HTTPS-hosted App Installer | Use Azure Artifact Signing or another CA-trusted code-signing service. |
| Company-managed devices | Intune or Configuration Manager | An organization certificate is acceptable when its trust is deployed with device management. |
| Local development and testers | App Installer | Self-signed certificate; every test device must trust it first. |

Do not use a self-signed certificate for public distribution. Windows blocks packages whose signer is not trusted.

## Current CI workflow

The GitHub Actions workflow creates an MSIX and `.appinstaller` release when a `vMAJOR.MINOR.PATCH` tag is pushed. The tag must match the first three components of the manifest package version: `v1.0.30` requires `1.0.30.0` in `Package.appxmanifest`.

The current workflow accepts a PFX only as a development/testing bridge. It needs these repository **Secrets**:

- `PACKAGE_CERTIFICATE_BASE64`: Base64-encoded PFX containing the signing private key.
- `PACKAGE_CERTIFICATE_PASSWORD`: password protecting that PFX.

Never put either value in a variable, committed file, release asset, or issue. The workflow adds a trusted timestamp to every signature so a signed package remains verifiable after the certificate expires.

## Before a public release

1. Move signing to Microsoft Store or Azure Artifact Signing / a CA-trusted code-signing provider.
2. Keep the certificate subject equal to the manifest publisher: `CN=Workday Widget`.
3. Verify the package installs on a clean Windows 11 device without importing a certificate.
4. Create a GitHub Release from a tag whose version matches the manifest.

Microsoft's guidance: [code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options), [choosing a distribution path](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path), and [App Installer troubleshooting](https://learn.microsoft.com/en-us/windows/msix/app-installer/troubleshoot-appinstaller-issues).
