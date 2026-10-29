# CurseForge integration

Community browsing supports Modrinth, CurseForge, and All. `CurseForgeCommunityService` uses the application credential injected during the build. The standalone typed client still defaults to disabled; the community entry point explicitly enables it when this application credential is present. The application operator remains responsible for obtaining approval for the intended usage.

All alternates projects from both sources, then continues with the longer list. It does not claim a shared popularity ranking. Totals and page offsets account for both lists; a failed provider produces a visible warning and leaves the other provider usable. Category and advanced filters remain source-specific. CurseForge supports game versions, supported mod loaders, categories, downloads, update date, and release date; unsupported ordering and Modrinth-only advanced fields are disabled in the interface.

## Approval and credentials

Before activation, the operator must apply for access for this specific application and review the applicable [third-party API terms](https://support.curseforge.com/support/solutions/articles/9000207405-curseforge-3rd-party-api-terms-and-conditions), including the restrictions on competing services. Code cannot establish permission to operate the application. See the official [application process](https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key).

The typed client defaults to disabled and still requires an explicit approval flag and key-provider callback. Its constructor does not automatically consume the build credential. The approval flag is a caller assertion, not a license grant or proof of approval.

## CI credential injection

Create a repository Actions secret named `CURSEFORGE_API_KEY` containing the key issued for this application. Push and manually dispatched builds require it; pull-request builds skip injection and build without a key. Missing keys fail trusted builds before publishing.

`scripts/configure-curseforge.ps1` reads the secret from its environment and writes `curseforge-api.json` under `RUNNER_TEMP` without printing the value. CI passes only that file path to MSBuild through `CurseForgeConfigurationFile`; the key never appears in the publish command. The desktop and native projects embed that JSON as `CMCLauncher.CurseForgeApi`; `CurseForgeCredentials` reads it for the modpack service. The temporary file stays outside the checkout and is removed after publishing, including failed publishes. The optional local-development file `winui/curseforge.local.json` is ignored by Git and excluded from ZIP packaging. Before upload, `scripts/verify-curseforge-secrets.ps1` rejects the configured key in tracked files or non-executable ZIP entries. Runtime logs redact both the configured value and API-key fields. A build without a key reports that CurseForge is not configured when a request is attempted; there is no borrowed-key fallback.

For a local build, set `CURSEFORGE_API_KEY` in the process environment and run the same script before publishing. Remove the generated local file when it is no longer needed.

An embedded desktop credential can be extracted from the binary. CI Secrets protect the source and build inputs, not the confidentiality of a key shipped to users. Use credentials issued for this application's intended distribution and usage; a confidential key requires a server-side arrangement.

## Available extension points

`ICurseForgeClient` in `native/CurseForgeClient.cs` exposes Minecraft project search, project details, paginated file lists, individual files, and a conservative file-access assessment. Models retain project links, authors, game versions and hashes for later attribution and integrity checks. Refer to the [official API reference](https://docs.curseforge.com/rest-api/).

- API requests use the fixed official HTTPS origin and a per-request `x-api-key` header. There is no API-key query parameter or shared default authentication header.
- Production transport disables proxies, redirects and cookies, validates TLS, and does not inherit the launcher's proxy or SSL-error override settings. This is separate from Git's command-specific proxy setting.
- Responses are transient return values, not stored or cached. Requests ask intermediaries not to cache. Do not route these results through the Modrinth metadata, image or content caches.
- Project distribution must be explicitly `true` under this client's conservative policy. `false`, `null` and missing permission fail closed. Unavailable and early-access files are blocked, as are mismatched project/file identities.
- The access check only accepts a supplied HTTPS `edge.forgecdn.net` URL without credentials, query parameters, fragments or a custom port. Other hosts require review; missing URLs never trigger URL construction, scraping, mirror lookup or fallback downloads.
- Authentication errors, forbidden responses, redirects and rate limits stop the request. `CurseForgeApiException` exposes status and optional `RetryAfter`, without echoing server content. The caller must respect quotas; the client does not automatically retry or scrape around restrictions.

## Downloads

Community installations and imported CurseForge packs recheck project permission and file identity before downloading. The dedicated download transport sends `x-api-key` only to the supplied official `edge.forgecdn.net` HTTPS URL, rejects redirects, verifies the declared size and SHA-1, and removes partial files on failure or cancellation. Required mod dependencies are resolved against the selected Minecraft version and loader. Project details preserve the author attribution and link to CurseForge. See the official [CDN authentication announcement](https://blog.curseforge.com/introducing-api-key-authentication-for-curseforge-file-downloads/).

## Verification

`native.tests/CurseForgeClientTests.cs` uses a fake handler and a fake key. It checks disabled access, request scoping, pagination, cancellation, author metadata, distribution restrictions, unavailable files, untrusted URLs, identity mismatches, rate limits, redirects and sanitized errors. It makes no live CurseForge requests and does not accept terms or obtain credentials on the operator's behalf.
