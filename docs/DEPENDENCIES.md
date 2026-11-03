# Dependency provenance

This branch does not consume mutable `main`, `latest`, or unverified binary URLs during its build. The bundled native and managed bridge files are checked against `source/Assets/Plugins/UnityOrbisBridge.sha256` by CI.

## UnityOrbisBridge

Reviewed source:

- repository: <https://github.com/Unrealisedd/UnityOrbisBridge>
- branch: `fw14-download-hardening`
- commit: [`e05a5757cf7ead34088d02651b1ebfbcac232aab`](https://github.com/Unrealisedd/UnityOrbisBridge/commit/e05a5757cf7ead34088d02651b1ebfbcac232aab)
- successful artifact run: [`41626907254`](https://github.com/Unrealisedd/UnityOrbisBridge/actions/runs/41626907254)

That run built both targets from the exact commit, generated `SHA256SUMS`, and verified the combined artifact with GNU `sha256sum --check` before upload. The same check passes against the files vendored here:

```text
0234ba7420e84d7f42c3f3b5096b42abd858bc0a902e805f83560e331678b8a1  source/Assets/Plugins/PS4/UnityOrbisBridge.prx
9b54aa633a2eeee4eb8120121b3fa7fec9326ad87f891259b00c96831462dab0  source/Assets/Plugins/UnityOrbisBridge.dll
```

The bridge workflow builds artifacts; it never commits generated binaries. Its pinned inputs are:

| Input | Pin |
|---|---|
| OpenOrbis PS4 Toolchain | `v0.5.3`, `toolchain-llvm-12.zip`, SHA-256 `75d1267d1ec29300e9b578d620d7a55fe678a8c0faeaeeb416a67e6d692352d2` |
| `bucanero/oosdk_libraries` | `5b86e2d25e674f64b8dce9aee319a9f8aa6a09ea` |
| `bucanero/ps4-libjbc` | `835fe016ff0ae5dd89b9249f39cc0fe093fd07dd` |
| SDL-PS4 OpenOrbis CMake file | `c3b571b78a7d97d70c621d149b464ad80310788a` |
| LLVM | `12.0.0` through the pinned installer action |

GitHub Actions are pinned to full commits:

| Action | Commit |
|---|---|
| `actions/checkout` | `11d5960a326750d5838078e36cf38b85af677262` |
| `actions/setup-dotnet` | `67a3573c9a986a3f9c594539f4ab511d57bb3ce9` (SDK `8.0.300`) |
| `actions/cache` | `0057852bfaa89a56745cba8c7296529d2fc39830` |
| `KyleMayes/install-llvm-action` | `ebc0426251bc40c7cd31162802432c68818ab8f0` |
| `microsoft/setup-msbuild` | `30375c66a4eea26614e0d39710365f22f8b0af57` |
| `actions/upload-artifact` | `ea165f8d65b6e75b540449e92b4886f43607fa02` |
| `actions/download-artifact` | `d3f86a106a0bac45b974a628896c90dbdf5c8093` |

## HTTPS trust store

`source/Assets/StreamingAssets/cacert.pem` was fetched from <https://curl.se/ca/cacert.pem>. Its SHA-256 is:

```text
796f1389bfea13c0ed78f663b765baaff54a508e5483786bd6ece73c5e3e3cbe  source/Assets/StreamingAssets/cacert.pem
```

Update deliberately; never replace verification with `CURLOPT_SSL_VERIFYPEER=0` or `CURLOPT_SSL_VERIFYHOST=0`:

```powershell
curl.exe --fail --location --proto '=https' --tlsv1.2 https://curl.se/ca/cacert.pem --output source/Assets/StreamingAssets/cacert.pem
Get-FileHash -Algorithm SHA256 source/Assets/StreamingAssets/cacert.pem
```

The CA data notice is in `source/Assets/StreamingAssets/CA-BUNDLE-NOTICE.txt`; the MPL 2.0 text is in `LICENSES/MPL-2.0.txt`.

## Unity application build

The project is pinned to Unity `2017.2.0p2` (`dbc2eb12ac98`). The official Windows editor installer used for host compilation had SHA-256:

```text
e3e3361ec1a2befbd9cf4aebb2d95e6a35d722ad7942463bdaad1c90fa577573  UnitySetup64-2017.2.0p2.exe
```

A PS4 player/FPKG additionally requires the licensed matching Unity PS4 support and PS4 SDK. Those proprietary inputs are not redistributed or represented as reproducible by this repository. Record their exact versions and the final FPKG digest in the firmware qualification log.

## Unity asset identity

Unity resolves scripts and serialized assets by the GUIDs in `.meta` sidecars. Upstream commit `321b7c6` removed those sidecars while leaving their GUIDs in `Scene.unity`, which made a fresh clone load with missing scripts and asset references. This branch restores the canonical sidecars from the parent of that commit and pins later asset GUIDs to their serialized scene references.

Verify both behavior contracts and asset identity before building:

```powershell
dotnet run --project tests/DownloadPolicy.ContractTests/DownloadPolicy.ContractTests.csproj --configuration Release
dotnet run --project tests/UnityAssetReferences.ContractTests/UnityAssetReferences.ContractTests.csproj --configuration Release
```
