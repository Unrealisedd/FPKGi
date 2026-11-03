using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityOrbisBridge;
using static JsonData;
using static UOBWrapper;
using static Utilities;
using static Variables;
using Application = UnityEngine.Application;

public class Background : MonoBehaviour
{
    [SerializeField]
    private RawImage background, coverImage;

    [Header("Content Data")]
    public RectTransform textTransform;

    public GameObject prefab;

    [SerializeField]
    private JsonData Content;

    private const float Spacing = 32f;
    private const float Offset = -12f;

    public static bool initializedApp = false;
    public static bool updateChecked = false;
    public static bool fullyInitialized = false;

    [SerializeField]
    public Transform controlContainer;

    [SerializeField]
    public GameObject touch, cross, square,
        triangle, circle, R3, dpad_left;

    public static void SaveConfiguration()
    {
        string _sortCriteria = null;
        switch (sortCriteria)
        {
            case 0:
                _sortCriteria = "size";
                break;
            case 1:
                _sortCriteria = "region";
                break;
            case 2:
                _sortCriteria = "name";
                break;
            case 3:
                _sortCriteria = "titleID";
                break;
        }

        string _contentFilter = null;
        switch (contentFilter)
        {
            case (int)ContentType.PS1:
                _contentFilter = "ps1";
                break;
            case (int)ContentType.PS2:
                _contentFilter = "ps2";
                break;
            case (int)ContentType.PSP:
                _contentFilter = "psp";
                break;
            case (int)ContentType.Games:
                _contentFilter = "games";
                break;
            case (int)ContentType.Apps:
                _contentFilter = "apps";
                break;
            case (int)ContentType.Updates:
                _contentFilter = "updates";
                break;
            case (int)ContentType.DLC:
                _contentFilter = "dlc";
                break;
            case (int)ContentType.Demos:
                _contentFilter = "demos";
                break;
            case (int)ContentType.Homebrew:
                _contentFilter = "homebrew";
                break;
            case (int)ContentType.Emulators:
                _contentFilter = "emulators";
                break;
            case (int)ContentType.Themes:
                _contentFilter = "themes";
                break;
            case (int)ContentType.ALL:
                _contentFilter = "all";
                break;
        }

        var configJSON = new
        {
            FILTERING = new
            {
                CONTENT = _contentFilter,
                SORT = new
                {
                    type = _sortCriteria,
                    ascending
                },
                REGIONS = filteredRegions?.Distinct().ToArray() ?? new string[0],
            },
            PREFERENCES = new
            {
                DOWNLOADS = new
                {
                    directDownload,
                    downloadPath,
                    installAfter,
                    deleteAfter,
                    deleteOnCancel,
                    maxRetries = maxDownloadRetries,
                    retryBaseDelaySeconds
                },
                APPLICATION = new
                {
                    background_uri,
                    backgroundMusic,
                    populateViaWeb,
                    enableUpdates
                },
                CONTENT_URLS = new
                {
                    PS1 = Variables.ContentURLs["ps1"],
                    PS2 = Variables.ContentURLs["ps2"],
                    PSP = Variables.ContentURLs["psp"],
                    PS5 = Variables.ContentURLs["ps5"],
                    games = Variables.ContentURLs["games"],
                    apps = Variables.ContentURLs["apps"],
                    updates = Variables.ContentURLs["updates"],
                    DLC = Variables.ContentURLs["dlc"],
                    demos = Variables.ContentURLs["demos"],
                    homebrew = Variables.ContentURLs["homebrew"],
                    emulators = Variables.ContentURLs["emulators"],
                    themes = Variables.ContentURLs["themes"]
                }
            }
        };

        Variables.ContentURLs["ps1"] = configJSON.PREFERENCES.CONTENT_URLS.PS1 ?? Variables.ContentURLs["ps1"];
        Variables.ContentURLs["ps2"] = configJSON.PREFERENCES.CONTENT_URLS.PS2 ?? Variables.ContentURLs["ps2"];
        Variables.ContentURLs["psp"] = configJSON.PREFERENCES.CONTENT_URLS.PSP ?? Variables.ContentURLs["psp"];
        Variables.ContentURLs["ps5"] = configJSON.PREFERENCES.CONTENT_URLS.PS5 ?? Variables.ContentURLs["ps5"];
        Variables.ContentURLs["games"] = configJSON.PREFERENCES.CONTENT_URLS.games ?? Variables.ContentURLs["games"];
        Variables.ContentURLs["apps"] = configJSON.PREFERENCES.CONTENT_URLS.apps ?? Variables.ContentURLs["apps"];
        Variables.ContentURLs["updates"] = configJSON.PREFERENCES.CONTENT_URLS.updates ?? Variables.ContentURLs["updates"];
        Variables.ContentURLs["dlc"] = configJSON.PREFERENCES.CONTENT_URLS.DLC ?? Variables.ContentURLs["dlc"];
        Variables.ContentURLs["demos"] = configJSON.PREFERENCES.CONTENT_URLS.demos ?? Variables.ContentURLs["demos"];
        Variables.ContentURLs["homebrew"] = configJSON.PREFERENCES.CONTENT_URLS.homebrew ?? Variables.ContentURLs["homebrew"];
        Variables.ContentURLs["emulators"] = configJSON.PREFERENCES.CONTENT_URLS.emulators ?? Variables.ContentURLs["emulators"];
        Variables.ContentURLs["themes"] = configJSON.PREFERENCES.CONTENT_URLS.themes ?? Variables.ContentURLs["themes"];

        foreach (var key in Variables.ContentURLs.Keys.ToList())
        {
            if (string.IsNullOrEmpty(Variables.ContentURLs[key]))
                Variables.ContentURLs[key] = null;
        }

        string jsonString = JsonConvert.SerializeObject(configJSON, Formatting.Indented);
        string configPath = Path.Combine(directoryPath, "config.json");

        IO.WriteAllTextAtomic(configPath, jsonString);
    }

    public static void HandleConfiguration()
    {
        string configPath = Path.Combine(directoryPath, "config.json");

        IO.RecoverAtomicFile(configPath);
        if (!File.Exists(configPath) &&
            AtomicFileRecovery.HasRecoveryArtifacts(configPath))
        {
            startupError =
                "Startup failed: interrupted configuration write could not be recovered.";
            Print(LogType.Error, startupError);
            return;
        }
        if (!File.Exists(configPath))
        {
            Print(LogType.Log, "Creating JSON file: config.json");

            SaveConfiguration();
            return;
        }


        Config config;
        try
        {
            string jsonContent = File.ReadAllText(configPath);
            config = JsonConvert.DeserializeObject<Config>(jsonContent) ?? new Config();
        }
        catch (Exception ex)
        {
            string rejectedPath = configPath + ".invalid";
            File.Copy(configPath, rejectedPath, true);
            Print(LogType.Error, $"Invalid configuration preserved at {rejectedPath}: {ex.Message}");
            config = new Config();
        }
        if (config.filtering == null)
            config.filtering = new ContentFilter();
        if (config.filtering.sort == null)
            config.filtering.sort = new Sort();
        if (config.filtering.regions == null)
            config.filtering.regions = new List<string>();

        string contentFilterStr = "all";
        if (!string.IsNullOrEmpty(config.filtering.content))
            contentFilterStr = config.filtering.content.ToLower();

        if (contentFilterStr == "ps1") contentFilter = (int)ContentType.PS1;
        else if (contentFilterStr == "ps2") contentFilter = (int)ContentType.PS2;
        else if (contentFilterStr == "psp") contentFilter = (int)ContentType.PSP;
        else if (contentFilterStr == "ps5") contentFilter = (int)ContentType.PS5;
        else if (contentFilterStr == "games") contentFilter = (int)ContentType.Games;
        else if (contentFilterStr == "apps") contentFilter = (int)ContentType.Apps;
        else if (contentFilterStr == "updates") contentFilter = (int)ContentType.Updates;
        else if (contentFilterStr == "dlc") contentFilter = (int)ContentType.DLC;
        else if (contentFilterStr == "demos") contentFilter = (int)ContentType.Demos;
        else if (contentFilterStr == "homebrew") contentFilter = (int)ContentType.Homebrew;
        else if (contentFilterStr == "emulators") contentFilter = (int)ContentType.Emulators;
        else if (contentFilterStr == "themes") contentFilter = (int)ContentType.Themes;
        else contentFilter = (int)ContentType.ALL;

        if (config.preferences == null)
            config.preferences = new Preferences();
        if (config.preferences.content_urls == null)
            config.preferences.content_urls = new ContentURLs();

        foreach (var key in Variables.ContentURLs.Keys.ToList())
        {
            var prop = config.preferences.content_urls.GetType()
                .GetProperty(key, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
            string value = prop != null ? prop.GetValue(config.preferences.content_urls) as string : null;

            if (!string.IsNullOrEmpty(value))
            {
                string formatted = URL.ProperFormatUrl(URL.DecryptBase64(value));
                Variables.ContentURLs[key] = URL.IsValidURI(formatted) ? formatted : null;
            }
            else if (Variables.ContentURLs[key] == null)
                Variables.ContentURLs[key] = null;
        }

        string sortType = "size";
        if (config.filtering.sort != null && !string.IsNullOrEmpty(config.filtering.sort.type))
            sortType = config.filtering.sort.type.ToLower();

        if (sortType == "size") sortCriteria = 0;
        else if (sortType == "region") sortCriteria = 1;
        else if (sortType == "name") sortCriteria = 2;
        else if (sortType == "titleid") sortCriteria = 3;
        else sortCriteria = 0;

        ascending = config.filtering.sort == null || config.filtering.sort.ascending;
        filteredRegions = (config.filtering.regions != null) ? config.filtering.regions.Distinct().ToArray() : new string[0];

        directDownload = config.preferences.downloads == null || config.preferences.downloads.directDownload;

        string defaultDownloadPath = isConsole
            ? "/user/data/FPKGi/Downloads/"
            : Path.Combine(directoryPath, "Downloads");
        string requestedDownloadPath =
            config.preferences.downloads != null && !string.IsNullOrWhiteSpace(config.preferences.downloads.downloadPath)
                ? config.preferences.downloads.downloadPath
                : defaultDownloadPath;
        try
        {
            downloadPath = DownloadPolicy.NormalizeDownloadRoot(requestedDownloadPath, isConsole);
        }
        catch (ArgumentException ex)
        {
            Print(LogType.Warning, $"Invalid download path '{requestedDownloadPath}': {ex.Message} Using the default.");
            downloadPath = DownloadPolicy.NormalizeDownloadRoot(defaultDownloadPath, isConsole);
        }

        installAfter = config.preferences.downloads == null || config.preferences.downloads.installAfter;
        deleteAfter = config.preferences.downloads != null && config.preferences.downloads.deleteAfter;
        deleteOnCancel = config.preferences.downloads != null && config.preferences.downloads.deleteOnCancel;
        maxDownloadRetries = config.preferences.downloads == null || config.preferences.downloads.maxRetries <= 0
            ? 3
            : Mathf.Clamp(config.preferences.downloads.maxRetries, 1, 10);
        retryBaseDelaySeconds = config.preferences.downloads == null || config.preferences.downloads.retryBaseDelaySeconds <= 0
            ? 2
            : Mathf.Clamp(config.preferences.downloads.retryBaseDelaySeconds, 1, 60);

        background_uri = config.preferences.application?.background_uri;
        backgroundMusic = config.preferences.application == null || config.preferences.application.backgroundMusic;
        enableUpdates = MaintainedUpdateChannelAvailable &&
            (config.preferences.application == null || config.preferences.application.enableUpdates);
        populateViaWeb = config.preferences.application != null &&
            config.preferences.application.populateViaWeb;

        FindObjectOfType<Background>()?.LoadCustomBackground();

        SaveConfiguration();
    }

    public void LoadCustomBackground()
    {
        if (!File.Exists(background_uri))
        {
            if (URL.IsValidImage(background_uri))
            {
                var bg = URL.ProperFormatUrl(background_uri);
                if (URL.IsValidURI(bg))
                    SetImageFromURL(bg, ref background);
            }
        }
        else IO.LoadImage(background_uri, ref background);
    }

    private void OnApplicationQuit() => SaveConfiguration();

    public void InitializePkgContent()
    {
        Variables.Content = Content;
        Transform pkgsTransform = GameObject.Find("PKGs")?.transform;

        if (pkgsTransform != null)
        {
            Transform textTransform = pkgsTransform.Find("Text");
            if (textTransform != null)
            {
                Content.PKGs.Clear();
                Vector2 startPosition = new Vector2(0, ContentHandler.itemsPerPage * Spacing / 2);

                for (int i = 0; i < ContentHandler.itemsPerPage; i++)
                {
                    Vector2 position = startPosition - new Vector2(0, i * Spacing - Offset);
                    GameObject newPrefab = Instantiate(prefab, textTransform);
                    newPrefab.GetComponent<RectTransform>().anchoredPosition = position;
                    newPrefab.name = $"PKG{i + 1}";

                    Transform pkgTransform = textTransform.Find($"PKG{i + 1}");
                    if (pkgTransform != null)
                    {
                        Content.PKGs.Add(new PKG
                        {
                            TitleID = pkgTransform.Find("TitleID")?.GetComponent<Text>(),
                            Region = pkgTransform.Find("Region")?.GetComponent<Text>(),
                            Downloaded = pkgTransform.Find("Downloaded")?.GetComponent<Text>(),
                            Title = pkgTransform.Find("Title")?.GetComponent<Text>(),
                            Size = pkgTransform.Find("Size")?.GetComponent<Text>()
                        });
                    }
                }
            }
        }
    }

    private void Awake()
    {
        Variables.background = background;
        Variables.coverImage = coverImage;

        Text versionText = UI.FindInactiveObjectsByPath("Canvas/Main/Version")?.GetComponent<Text>();

        UI.ChangeText(versionText, $"v{UI.FormatVersion(version)}");

        UI.ShowUIState(null);

        if (isConsole)
            GoldHEN = !(etaHEN = UOB.IsPlayStation5());
        else
        {
            directoryPath = Path.GetFullPath(Application.dataPath + @"\..\DATA\");
            downloadPath = Path.GetFullPath(Application.dataPath + @"\..\DATA\Downloads\");
        }
        if (directoryPath.Contains("/data/") && !directoryPath.StartsWith("/user/"))
            directoryPath = Path.Combine("/user", directoryPath.TrimStart('/')).Replace("\\", "/");

        if (downloadPath.Contains("/data/") && !downloadPath.StartsWith("/user/"))
            downloadPath = Path.Combine("/user", downloadPath.TrimStart('/')).Replace("\\", "/");

        if (!downloadPath.EndsWith("/")) downloadPath += "/";
        if (!directoryPath.EndsWith("/")) directoryPath += "/";

        initializedApp = true;
    }
    private void FailStartup(string stage, int errorCode = 0)
    {
        startupError = errorCode == 0
            ? $"Startup failed: {stage}"
            : $"Startup failed: {stage} (0x{errorCode:X8})";

        Print(LogType.Exception, startupError);
        loadedOffline = true;
        fullyInitialized = true;

        var statusText = UI.FindInactiveObjectsByPath("Canvas/Main/ContentSort")?.GetComponent<Text>();
        UI.ChangeText(statusText, startupError);
        statusText?.gameObject.SetActive(true);

        if (isConsole)
            UOB.TextNotify(222, startupError);
    }

    public IEnumerator UpdateDisplayInfo()
    {
        Print(LogType.Assert, "Displaying content and system information...");
        UI.FindInactiveObjectsByPath("Canvas/Main/ContentSort")?.SetActive(true);
        UI.FindInactiveObjectsByPath("Canvas/Main/PkgCount")?.SetActive(true);
        UI.FindInactiveObjectsByPath("Canvas/Main/Temperature")?.SetActive(true);
        UI.FindInactiveObjectsByPath("Canvas/Main/FreeSpace")?.SetActive(true);

        InitializePkgContent();

        var freeSpaceText = UI.FindInactiveObjectsByPath("Canvas/Main/FreeSpace")?.GetComponent<Text>();
        var temperatureText = UI.FindInactiveObjectsByPath("Canvas/Main/Temperature")?.GetComponent<Text>();

        if (freeSpaceText != null) freeSpaceText.text = "Not Available";
        if (temperatureText != null) temperatureText.text = "Not Available";

        while (true)
        {
            string mountPoint = "/user";
            if (downloadPath.StartsWith("/mnt/usb") || downloadPath.StartsWith("/mnt/ext"))
            {
                var parts = downloadPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) mountPoint = $"/{parts[0]}/{parts[1]}";
            }

            if (isConsole)
            {
                UpdateTemperature(temperatureText,
                    new Color32(119, 221, 119, 255),
                    new Color32(255, 237, 0, 255),
                    new Color32(156, 82, 82, 255),
                    UOB.Temperature.CPU, 55f, 70f);

                if (freeSpaceText != null)
                {
                    ulong freeBytes = NativeBridge.GetDiskFreeBytes(mountPoint);
                    freeSpaceText.text = freeBytes == UInt64.MaxValue
                        ? "Free: Not Available"
                        : $"Free: {IO.FormatByteString(freeBytes)}";
                }

                /*
                string val0 = Marshal.PtrToStringAnsi(GetDiskInfo("/data", "percent"));
                string val1 = Marshal.PtrToStringAnsi(GetDiskInfo("/data", "total"));
                string val2 = Marshal.PtrToStringAnsi(GetDiskInfo("/data", "used"));
                string val3 = Marshal.PtrToStringAnsi(GetDiskInfo("/data", "free"));

                Print(LogType.Assert, $"PERCENT: {val0}\n" + $"TOTAL: {val1}\n" + $"USED: {val2}\n" + $"FREE: {val3}");
                */
            }

            yield return new WaitForSeconds(1f);
        }
    }

    public static async Task<bool> CheckForAppUpdates()
    {
        string latestHash = string.Empty, currentHash = string.Empty, fileVersion = string.Empty;

        if (loadedOffline == false)
        {
            if (!updateChecked)
            {
                Print(LogType.Assert, "Fetching the latest MD5 hash to compare...");

                latestHash = await DownloadAsBytes(updateHashUrl);

                var parts = UI.FormatVersion(version).Split('.');
                fileVersion = $"V{int.Parse(parts[0]):D2}{int.Parse(parts[1]):D2}";

                string path;
                if (isConsole)
                    path = "/user/app/PKGI13337/app.pkg";
                else path = $"D:\\Projects\\Unity\\PS4\\FPKGi\\BUILD\\ED1633-PKGI13337_00-0000000000000000-A0100-{fileVersion}.pkg";

                currentHash = IO.ComputeFileMD5(path);
                if (updateAvailable == null)
                    updateAvailable = !IO.CompareMD5Hashes(currentHash, latestHash);

                if (latestVersion == null)
                {
                    Print(LogType.Assert, "Checking for the latest version available...");

                    string result = await DownloadAsBytes(updateVersionUrl);

                    if (result.Contains("No valid version found in any release."))
                        latestVersion = version;
                    else
                    {
                        double found;
                        if (double.TryParse(result, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out found))
                            latestVersion = (float)found;

                        Print(LogType.Assert, $"Current MD5: {currentHash}");
                        Print(LogType.Assert, $"Latest MD5: {latestHash}");
                        Print(LogType.Assert, $"Latest Version: {UI.FormatVersion(latestVersion)}");
                        Print(LogType.Assert, $"Current Version: {UI.FormatVersion(version)}");
                        Print(LogType.Assert, $"Build Number: {buildNumber}");
                    }
                }

                updateChecked = true;
            }
            else updateChecked = true;

            if (version < latestVersion && updateAvailable == true)
            {
                Print(LogType.Warning, $"MD5 hash and version mismatch, update required!");

                UI.ShowUIState(UI.FindInactiveObjectsByPath("Canvas/Update"));
                Text currentVersionText = UI.FindInactiveObjectsByPath("Canvas/Update/Text/Versions/Current")?.GetComponent<Text>();
                Text latestVersionText = UI.FindInactiveObjectsByPath("Canvas/Update/Text/Versions/Latest")?.GetComponent<Text>();
                Text latestMD5Text = UI.FindInactiveObjectsByPath("Canvas/Update/Text/Versions/Latest/MD5")?.GetComponent<Text>();
                Text currentMD5Text = UI.FindInactiveObjectsByPath("Canvas/Update/Text/Versions/Current/MD5")?.GetComponent<Text>();
                Text latestSizeText = UI.FindInactiveObjectsByPath("Canvas/Update/Text/Versions/Latest/Size")?.GetComponent<Text>();
                currentVersionText.text = $"Current Version: {UI.FormatVersion(version)}";
                latestVersionText.text = $"Latest Version: {UI.FormatVersion(latestVersion)}";
                currentMD5Text.text = $"MD5: {currentHash}";
                latestMD5Text.text = $"MD5: {latestHash}";

                string latestSize = await DownloadAsBytes(updateSizeUrl);
                latestSizeText.text = $"Size: {IO.FormatByteString(latestSize)}";
            }
        }

        return true;
    }

    /*
       private Dictionary<string, SfoHandler.SfoInfo> pageSfo = new Dictionary<string, SfoHandler.SfoInfo>();

       public async Task FetchAndLogSfo(string pkgUrl)
       {
           var sfo = await PkgHandler.DownloadAndParseParamSfo(pkgUrl);

           foreach (var entry in sfo.AllEntries)
           {
               var key = entry.Key;
               var value = entry.Value as string;

               if (key == null) continue;
           }

           var titleEntry = sfo.AllEntries.FirstOrDefault(e => (e.Key) == "TITLE_ID");
           var titleId = titleEntry != null ? titleEntry.Value as string : null;

           if (!string.IsNullOrEmpty(titleId))
           {
               if (pageSfo.ContainsKey(titleId))
                   pageSfo[titleId] = sfo;
               else
                   pageSfo.Add(titleId, sfo);
           }
       }

       private static async Task<byte[]> FetchIconBytes(string pkgUrl)
       {
           try
           {
               var offset = await PngHandler.FindPngOffset(pkgUrl);
               if (!offset.HasValue)
               {
                   Print("icon0.png not found in stream.", LogType.Warning);
                   return null;
               }

               var bytes = await PngHandler.ReadPngBytes(pkgUrl, offset.Value);
               if (bytes == null || bytes.Length == 0)
               {
                   Print("icon0.png bytes are empty or null.", LogType.Error);
                   return null;
               }

               int previewLen = Math.Min(bytes.Length, 128);
               string previewHex = BitConverter.ToString(bytes, 0, previewLen).Replace("-", " ");

               return bytes;
           }
           catch (Exception ex)
           {
               Print($"Failed to fetch icon0.png bytes: {ex.GetType().Name}: {ex.Message}", LogType.Error);
               return null;
           }
       }
    */

    [DllImport("UnityOrbisBridge")] public static extern long GetRemoteFileSize(string url);

    private IEnumerator Start()
    {
        while (!initializedApp) yield return null;

        if (isConsole)
        {
            float jailbreakStart = Time.realtimeSinceStartup;
            UOB.BreakFromSandbox();

            while ((!UOB.IsFreeOfSandbox() || !IO.DoesPathExist("/system/common/lib")) &&
                   Time.realtimeSinceStartup - jailbreakStart < StartupTimeoutSeconds)
                yield return null;

            if (!UOB.IsFreeOfSandbox() || !IO.DoesPathExist("/system/common/lib"))
            {
                FailStartup("HEN sandbox access was not granted");
                yield break;
            }

            if (IO.DoesPathExist("/data/UnityOrbisBridge.log"))
                File.Delete("/data/UnityOrbisBridge.log");

            string consoleType = etaHEN == true ? "5" : "4";
            IntPtr firmwarePointer = UOB.GetFWVersion();
            firmwareVersion = firmwarePointer == IntPtr.Zero
                ? "unknown"
                : Marshal.PtrToStringAnsi(firmwarePointer).Replace(" ", "");

            Print(LogType.Assert, $"Currently running on PlayStation {consoleType} ({firmwareVersion})");
            Print(LogType.Assert, $"App broke from the sandbox in {Time.realtimeSinceStartup - jailbreakStart:F2}s.");

            int initializationResult = NativeBridge.InitializePackageServices();
            if (initializationResult != 0)
            {
                FailStartup("AppInstUtil/BGFT initialization", initializationResult);
                yield break;
            }

            string caBundlePath = Path.Combine(Application.streamingAssetsPath, "cacert.pem");
            int httpResult = NativeBridge.ConfigureHttp(caBundlePath);
            if (httpResult != 0)
            {
                FailStartup("HTTPS trust store initialization", httpResult);
                yield break;
            }

            Print(LogType.Assert, "Native package services and HTTPS trust store initialized.");
        }

        IO.EnsureDirectoryExists(Path.Combine(directoryPath, "Backgrounds"));
        IO.EnsureDirectoryExists(Path.Combine(directoryPath, "ContentJSONs"));
        IO.EnsureDirectoryExists(Path.Combine(directoryPath, "Downloads"));

        HandleConfiguration();
        if (!String.IsNullOrEmpty(startupError))
        {
            const string prefix = "Startup failed: ";
            string stage = startupError.StartsWith(prefix, StringComparison.Ordinal)
                ? startupError.Substring(prefix.Length)
                : startupError;
            FailStartup(stage);
            yield break;
        }

        IO.EnsureDirectoryExists(downloadPath);

        for (int i = 0; i < (int)ContentType.ALL; i++)
        {
            var task = JSON.ParseJSON((ContentType)i);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
                Print(LogType.Error, $"Failed to load {(ContentType)i}: {task.Exception?.GetBaseException().Message}");
        }

        float downloadStart = Time.time;
        var downloadTask = DownloadAsBytes(updateHashUrl);

        while (!downloadTask.IsCompleted && Time.time - downloadStart < 5)
            yield return null;

        string result = null;
        if (downloadTask.IsCompleted && !downloadTask.IsFaulted)
            result = downloadTask.Result;

        if (string.IsNullOrEmpty(result))
        {
            if (isConsole)
                UOB.TextNotify(222, "Update service unavailable; local and cached catalogs remain available.");

            Print(LogType.Warning, "Update service unavailable. Catalog source preferences were not changed.");
            loadedOffline = true;
        }
        else
            loadedOffline = false;

        fullyInitialized = true;

        /*
           string pkgUrl = "https://pkg-zone.com/download/ps4/PKGI13337/latest";
           string path = Path.Combine(directoryPath, "/TEMP/LOL/icon0.png");

           IO.EnsureDirectoryExists(path);

           var sfoTask = FetchAndLogSfo(pkgUrl);
           yield return new WaitUntil(() => sfoTask.IsCompleted);

           var sfoInfo = pageSfo["PKGI13337"];
           var sb = new System.Text.StringBuilder();
           sb.AppendLine("SFO Info for PKGI13337:");

           foreach (var entry in sfoInfo.AllEntries)
           {
               var key = entry.Key as string;
               if (key == null) continue;
               sb.AppendLine(key + " = " + (entry.Value ?? ""));
           }

           Print(LogType.Log, sb.ToString());

           // var iconTask = FetchIconBytes(pkgUrl);
           // yield return new WaitUntil(() => iconTask.IsCompleted);

           // var iconBytes = iconTask.Result;
           //if (iconBytes != null)
           // {
           //  SetImageFromBytes(iconBytes, ref background);
           //   File.WriteAllBytes(path, iconBytes);
           // }
        */

    }

}