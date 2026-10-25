# Reserved CurseForge integration

This is a developer-facing extension point, not an enabled content provider. The launcher does not create a client, request credentials, import CurseForge manifests, or download CurseForge files. No production API calls have been made to validate this integration.

## Approval and credentials

Before activation, the operator must apply for access for this specific application and review the applicable [third-party API terms](https://support.curseforge.com/support/solutions/articles/9000207405-curseforge-3rd-party-api-terms-and-conditions), including the restrictions on competing services. Code cannot establish permission to operate the application. See the official [application process](https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key).

The client defaults to disabled. An operator-controlled host may explicitly enable it only after approval, supplying an authorized key through a runtime callback. No key is read from launcher settings, embedded in the assembly, or included in source or release archives. The approval flag is a caller assertion, not a license grant or proof of approval. Do not embed an operator key in a publicly distributed desktop build or borrow another application's key; a permitted credential-delivery/deployment arrangement must be established before adding a public UI.

## Available extension points

`ICurseForgeClient` in `native/CurseForgeClient.cs` exposes Minecraft project search, project details, paginated file lists, individual files, and a conservative file-access assessment. Models retain project links, authors, game versions and hashes for later attribution and integrity checks. Refer to the [official API reference](https://docs.curseforge.com/rest-api/).

- API requests use the fixed official HTTPS origin and a per-request `x-api-key` header. There is no API-key query parameter or shared default authentication header.
- Production transport disables proxies, redirects and cookies, validates TLS, and does not inherit the launcher's proxy or SSL-error override settings. This is separate from Git's command-specific proxy setting.
- Responses are transient return values, not stored or cached. Requests ask intermediaries not to cache. Do not route these results through the Modrinth metadata, image or content caches.
- Project distribution must be explicitly `true` under this client's conservative policy. `false`, `null` and missing permission fail closed. Unavailable and early-access files are blocked, as are mismatched project/file identities.
- The access check only accepts a supplied HTTPS `edge.forgecdn.net` URL without credentials, query parameters, fragments or a custom port. Other hosts require review; missing URLs never trigger URL construction, scraping, mirror lookup or fallback downloads.
- Authentication errors, forbidden responses, redirects and rate limits stop the request. `CurseForgeApiException` exposes status and optional `RetryAfter`, without echoing server content. The caller must respect quotas; the client does not automatically retry or scrape around restrictions.

## Before implementing downloads

The access result is metadata, not a downloader, installation approval, or a promise that the file will still be available later. Recheck permission when implementing the actual operation, preserve project/author attribution, and handle revoked access. Do not pass the URL to the existing generic downloader: it lacks CurseForge-specific CDN authentication. The official [CDN authentication announcement](https://blog.curseforge.com/introducing-api-key-authentication-for-curseforge-file-downloads/) requires download authentication and recommends the `x-api-key` header. Add a dedicated, reviewed transport, integrity checks, cancellation/cleanup tests and dependency compatibility checks before enabling installation.

## Verification

`native.tests/CurseForgeClientTests.cs` uses a fake handler and a fake key. It checks disabled access, request scoping, pagination, cancellation, author metadata, distribution restrictions, unavailable files, untrusted URLs, identity mismatches, rate limits, redirects and sanitized errors. It makes no live CurseForge requests and does not accept terms or obtain credentials on the operator's behalf.
