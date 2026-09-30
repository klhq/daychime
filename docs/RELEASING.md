# Releasing Daychime

## Release model

Use `main` and version tags, not a full Gitflow branch model:

**Push a version tag → GitHub tests and packages → download the artifact → upload to Partner Center → submit for certification.**

GitHub handles CI and release packaging. Store submission and publication are manual. No Partner Center credentials, Microsoft Entra tenant, or package-signing secrets are needed in GitHub.

The public listing is [Daychime](https://apps.microsoft.com/detail/9NJKQT7SNSJL), product ID `9NJKQT7SNSJL`. Always update this existing product; do not register another app.

## Prepare and tag a version

1. Verify the release changes, including local packaged behavior. Passing CI does not verify actual Widgets Board rendering or notification button clicks.
2. Set `Identity Version` in `src/DaychimeWidget/Package.appxmanifest` higher than all previously submitted package versions, with the final component `0`. Keep the Store-assigned identity and publisher unchanged. Never reuse a package version for different contents.
3. Commit and push the release source to `main`, then create and push an annotated tag. For example, manifest version `1.0.37.0` corresponds to `v1.0.37`:

   ```powershell
   git push origin main
   git tag -a v1.0.37 -m "Daychime 1.0.37 submission"
   git push origin v1.0.37
   ```

   Replace the example with the actual release version. Do not move or overwrite existing release tags. Tag creation does not change the manifest; CI rejects a mismatched tag.
4. Wait for the matching **Build Microsoft Store package** workflow run to succeed. A tag records release source, not Store certification or publication. If fixes are needed after submission, increment the package version and create a new tag.

## Download the submission file from GitHub

1. Sign in to GitHub and open [Daychime → Actions → Build Microsoft Store package](https://github.com/klhq/daychime/actions/workflows/store-package.yml).
2. Select the successful run for the intended version tag. Check the tag and source commit, not just the most recent run.
3. On the run summary page, scroll to **Artifacts** and click **Daychime-store-upload** to download a ZIP.
4. Extract the ZIP. The submission file inside is **Daychime.msixupload**. Do not upload the ZIP wrapper or a developer-signed local bundle.

The workflow summary also displays the package version and SHA256. Artifacts are configured to remain available for 90 days, subject to repository policy and manual deletion. Keep a copy of the exact submitted file for your release records. [GitHub artifact download documentation](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).

If no artifact appears, confirm that the packaging job succeeded. Normal pull requests and `main` pushes run tests/build checks but do not produce a Store upload artifact.

For a trial build without a new tag, select **Run workflow** and choose the branch or tag to build. This produces the same artifact but does not submit anything. An older tag uses the workflow committed at that tag; to use new workflow changes, build a ref that contains them.

## Submit the update in Partner Center

1. Open the existing Daychime product and choose **Update / Create a new submission**.
2. Under **Packages**, upload the downloaded `Daychime.msixupload`. Wait for validation and verify the detected version. Resolve all errors before proceeding.
3. Review the existing listing, update release notes as needed, and complete any incomplete sections. Existing languages and listing content do not need to be recreated for every update.
4. Review availability and submission options, including the publishing schedule. To publish immediately after approval, choose that option rather than a manual or future release.
5. Click **Submit for certification**.
6. After publication, verify installation/update from the Store on a device. End users get updates through Microsoft Store, not GitHub artifacts.

Microsoft signs the Store package; do not sign the upload locally or replace its Store identity with a self-signed one. Local developer installation is a separate workflow and requires developer signing. Official guidance: [publish an update](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/publish-update-to-your-app-on-store) and [upload packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages).

## Local checks and packaging

```powershell
./scripts/Validate-Release.ps1 -Tag v1.0.37
./tests/ReleaseValidation.Tests.ps1
dotnet run --project tests/ShiftLifecycle.Tests.csproj --configuration Release
./scripts/Build.ps1 -Configuration Release -Architecture x64 -StoreUpload -OutputDirectory artifacts
```

Use the actual version tag. Local packaging emits `artifacts\Daychime.msixupload`; for tagged releases, prefer the GitHub artifact so the file comes from the recorded source commit.
