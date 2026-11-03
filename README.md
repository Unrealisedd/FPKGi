# FPKGi - A Server-Based PS4 & PS5 Content Installer

FPKGi is an open-source browser/downloader for lawful homebrew and owner-supplied FPKGs described by local, LAN, or web JSON catalogs. It does not provide package catalogs, credentials, entitlement bypass, or package content.

> [!IMPORTANT]
> PS4 firmware 14.00 is **experimental until the real-console matrix passes** with a recorded HEN, package, and bridge digest. Source-level FW14 HEN support exists, but that does not prove Unity, AppInstUtil, BGFT, dialogs, downloads, or rest mode. See [the evidence and qualification matrix](docs/FW14-COMPATIBILITY.md).

Use FPKGi only for content you are legally entitled to use. Prefer a controlled LAN server and provide exact byte size plus SHA-256 for every package.
## Setup

This maintained branch does not publish a prebuilt FPKG until the firmware-14.00 matrix passes. Its in-app updater is disabled so it cannot replace this build with an unqualified upstream package. Build the exact reviewed commit and record the FPKG and bridge digests before testing. The [upstream v1.10 releases](https://github.com/ItsJokerZz/FPKGi/releases) are useful baselines, but they do not contain this branch's startup, TLS, queue, retry, or integrity fixes.

Install an owner-built package with your normal homebrew package installer, then record the console, firmware, HEN, FPKGi, and bridge versions used.
### Populate Content
Launch the app to automatically create the necessary directories and `.json` files in the `/user/data/FPKGi/` folder.

#### Populate Content Locally
Edit the `.json` files generated at `/user/data/FPKGi/ContentJSONs/` to add your content. <br>
**You can also generate / populate, and save the necessary `.json` file [here](https://www.itsjokerzz.site/projects/FPKGi/gen/) on my site.**

> [!NOTE]
> Use bytes for `"size"` and for specifying content's region: `"USA"`, `"JAP"`, `"EUR"`, `"ASIA"`, or `null`.

### Example `.json` structure:
```json
{
    "DATA": {
        "https://www.example.com/directLinkToContent.pkg": {
            "title_id": "CUSA00001",
            "region": "USA",
            "name": "Content Title",
            "version": "1.00",
            "release": "11-15-2014",
            "size": "1000000000",
            "min_fw": null,
            "cover_url": "https://www.example.com/cover.png",
            "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        }
    }
}
```

> [!IMPORTANT]
> `title_id`, `name`, and an exact nonzero byte `size` are required. `sha256` is optional but strongly recommended and, when present, is enforced before installation. `version`, `region`, `release`, `min_fw`, and `cover_url` may be `null`. Download URLs must be absolute HTTP(S) URLs without embedded credentials or fragments.

#### Populate Content Via Web
To enable web population, edit the `config.json` file located at `/user/data/FPKGi/` and enable it in the menu.

### Locate and edit the following section:
```json
    "CONTENT_URLS": {
      "PS1": null,
      "PS2": null,
      "PSP": null,
      "games": null,
      "apps": null,
      "updates": null,
      "DLC": null,
      "demos": null,
      "homebrew": null,
      "emulators": null,
      "themes": null
    }
```

#### Replace `null` with URLs pointing to `.json` files containing your content:
```json
    "CONTENT_URLS": {
      "games": "https://www.example.com/GAMES.json"
    }
```

Unspecified fields will default to loading content from local `.json` files.

## Controls & Settings
### Navigation
- **Move Through Items**: Use **<kbd>(LS)tick</kbd>/<kbd>(RS)tick</kbd>** or use the dpad to navigate.
- **Select/Download**: Press **![X](https://www.github.com/bucanero/pkgi-ps3/raw/master/data/CROSS.png)** to select or download content.
- **Page & Category Navigation**:
  - **<kbd>L1</kbd>/<kbd>R1</kbd>**: Changes pages.
  - **<kbd>L2</kbd>/<kbd>R2</kbd>**: Changes category.
- **View Details**: Press **![square](https://www.github.com/bucanero/pkgi-ps3/raw/master/data/SQUARE.png)** to view detailed information about the selected content.

### Settings Menu
- Press **![triangle](https://www.github.com/bucanero/pkgi-ps3/raw/master/data/TRIANGLE.png)** to open settings.
- **Save/Cancel**:
  - Press **![triangle](https://www.github.com/bucanero/pkgi-ps3/raw/master/data/TRIANGLE.png)** to save current settings changes.
  - Press **![circle](https://www.github.com/bucanero/pkgi-ps3/raw/master/data/CIRCLE.png)** to toggle menu & not save setting.
 
<br>

- **Press the touchpad to search or filter through content by title ID or name.**
  
## Features
### Core Features
- **Search**: Quickly find content using keywords.
- **Sorting**: Organize content by size, name, region, or title ID.
- **Filtering**: Filter content by type for faster navigation.
- **View Options**: Toggle between ascending and descending order.

### Customization
- **Background Music**: Toggle the original PKGi background music by [nobodo](https://www.github.com/nobodo).
- **Custom Backgrounds**:
Add images via URL or locally (supports `.png`, `.bmp`, `.jpg`, and `.jpeg`).
  - URL Example:
    ```json
    "background_uri": "https://www.example.com/image.png"
    ```
  - Local Example:
    ```json
    "background_uri": "/user/data/FPKGi/Backgrounds/custom.png"
    ```
  - Reset to default:
    ```json
    "background_uri": null
    ```

### Download Management

- **Direct mode**: persistent queue, canonical paths, free-space preflight, safe cancellation, resumable partial files, bounded transient retries, and exact size/optional SHA-256 verification before install.
- **Background mode**: submits a URL to PS4 BGFT. FPKGi records `submitted`; it does not claim the package is installed or cryptographically verified.
- **Queue**: R3 opens it; left adds/removes the selected entry; X starts queued/failed entries. Queue state survives relaunch. Failed entries remain until retried or removed. Interrupted native install or BGFT-submission intent becomes `blocked` instead of being submitted twice.
- **Destinations**: `/user/data/...`, `/mnt/usbN/...`, and `/mnt/extN/...` are accepted after canonical-root validation.
- **TLS**: HTTPS verifies hostname and peer with the bundled Mozilla CA store. Plain HTTP should be limited to a controlled LAN and paired with `sha256`.
- **ZIP**: unsupported and retained as a failed response; it is never silently deleted.

See [download behavior, queue states, schema, and migration](docs/DOWNLOADS.md).

Example destinations:

```json
\"downloadPath\": \"/user/data/FPKGi/Downloads/\"
```

```json
\"downloadPath\": \"/mnt/usb0/FPKGi/\"
```
    
## How to Build
<details>
  <summary><strong>Prerequisites</strong></summary>
  <ul>
    <li><strong>Unity 2017.2.0p2</strong> (the version pinned by this project)</li>
    <li><strong>PS4 SDK 4.50</strong> with matching Unity integration</li>
    <li><a href="https://github.com/CyB1K/PS4-Fake-PKG-Tools-3.87" target="_blank">PS4 Fake PKG Tools 3.87</a></li>
    <li><a href="https://www.dotnet.microsoft.com/en-us/download/dotnet-framework/net46" target="_blank">.NET Framework 4.6 Developer Pack</a></li>
  </ul>

  <details>
    <summary><strong>Included Precompiled Dependencies</strong></summary>
    <ul>
      <li><a href="https://www.github.com/SaladLab/Json.Net.Unity3D" target="_blank">Json.Net.Unity3D</a></li>
      <li><a href="https://www.github.com/ItsJokerZz/store-api" target="_blank">HB Store's API</a></li>
      <li><a href="https://github.com/Unrealisedd/UnityOrbisBridge/tree/fw14-download-hardening" target="_blank">Reviewed UnityOrbisBridge source</a></li>
      <li><a href="https://github.com/Unrealisedd/UnityOrbisBridge/blob/fw14-download-hardening/source/wrapper/UOBWrapper.cs" target="_blank">UOBWrapper source</a></li>
    </ul>
  </details>
</details>

### Build Prerequisites
1. Install Unity 2017.2.0p2, the matching PS4 Unity integration, PS4 SDK 4.50, Fake PKG Tools 3.87, and the .NET Framework 4.6 Developer Pack.
2. Build the [pinned UnityOrbisBridge source and inputs](docs/DEPENDENCIES.md), then copy its reviewed PRX/DLL artifacts into `source/Assets/Plugins`.
3. Confirm `source/Assets/StreamingAssets/cacert.pem` is packaged unchanged.
4. Build the PS4 player/package, record its SHA-256, and execute the firmware matrix before making a compatibility claim.

**For more detailed guidance with images, check out [RetroGamer74's guide](https://github.com/RetroGamer74/HowToBuildWithUnityPS4FakePKG/blob/master/README.md).**

## Troubleshooting

- **Startup error instead of main UI**: record the exact stage/code and `/data/UnityOrbisBridge.log`. Confirm the HEN build explicitly supports the installed firmware and grants sandbox/module access. Do not treat an infinite wait as expected behavior.
- **`0x80990086` / content already downloading**: remove the stale task from PS4 notifications/downloads, then retry. FPKGi retains the failed queue record.
- **Background task does not appear**: use direct mode to separate BGFT registration from HTTP/package integrity. Record the native result; a returned UI is not proof of task acceptance.
- **Integrity or size failure**: verify the catalog's exact byte count and SHA-256 against the server file. The package is intentionally not installed.
- **Certificate failure**: update the CA bundle from the documented curl/Mozilla source; never disable peer or hostname verification as a workaround.

Changing the console NP environment is not a general FPKGi fix and can affect unrelated services. Do not apply third-party NP-environment workarounds without understanding and recording the exact HEN/BGFT failure they address.

## Special Thanks
- **Original App Creator**: [Bucanero](https://www.github.com/bucanero)
- **Original PKGi Music**: [nobodo](https://www.github.com/nobodo)

  ### Unity Sources:
    - [RetroGamer74](https://www.github.com/RetroGamer74) & [Lapy055](https://www.github.com/Lapy055)

A huge thank you to the following members of the [OOSDK Discord](https://www.discord.com/invite/GQr8ydn) for their support:
- **[TheMagicalBlob](https://github.com/TheMagicalBlob)**, [LightningMods](https://github.com/LightningMods), [Al-Azif](https://github.com/Al-Azif), Da Puppeh, Kernel Panic, lainofthewired, and others.

For more credits, please check out [UnityOrbisBridge](https://www.github.com/ItsJokerZz/UnityOrbisBridge). If I've missed anyone, feel free to reach out. <br><br>
For assistance, please leave an issue and/or join my community [Discord server](https://discord.com/invite/RjG4Whf) and I'll help you out.

## License

FPKGi and this derivative are licensed under GNU GPL v2.0; see [LICENSE](LICENSE). The bundled Mozilla CA certificate data is covered by [MPL 2.0](LICENSES/MPL-2.0.txt); see [its source notice](source/Assets/StreamingAssets/CA-BUNDLE-NOTICE.txt). Preserve upstream credits and the [button-artwork CC BY 4.0 attribution](source/Assets/Images/Buttons/LICENSE.md) when redistributing builds.
