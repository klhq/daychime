# Releasing Daychime

## Store package workflow

The GitHub Actions workflow runs on a version tag or manually, then uploads `Daychime.msixupload` as a workflow artifact. Download that artifact and submit it in Partner Center.

The Store-assigned package identity is committed in `Package.appxmanifest`. Do not replace it with a self-signed identity and do not sign Store upload packages locally: Microsoft signs the package after it passes certification.

## Publish a release

1. In Partner Center, open Daychime and choose **Start your submission**.
2. Download the `Daychime-store-upload` artifact from the matching GitHub Actions run.
3. Upload `Daychime.msixupload` under **Packages**.
4. Complete Store listing, availability, age rating, and privacy policy information.
5. Submit for certification.

After certification, Microsoft Store handles installation, trusted signing, and updates. Microsoft guidance: [publish an MSIX app](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-certification-process) and [app package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements).
