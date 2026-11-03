using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using UnityOrbisBridge;
using static Background;
using static ContentHandler;
using static JsonData;
using static UOBWrapper;
using static Utilities;
using static Variables;

public class ControlMenu : MonoBehaviour
{
    #region Fields
    public GameObject
        menuCanvas,
        detailsCanvas,
        downloadCanvas,
        cancelCanvas,
        updateCanvas,
        queueCanvas;

    [SerializeField]
    private float
        inputCooldown = 0.20f,
        cooldownTimer = 0.00f;

    public Scrollbar content_scrollbar,
        queue_scrollbar;

    public GameObject queued_pkgs;

    private AudioSource audioSource;
    #endregion

    #region Variables
    private bool menuLoaded = false, isDownloading = false, queueLoaded = false;
    private bool cancelDirectDownloadRequested = false, cancelDirectDownloadSignalSent = false;

    public static bool reloadTriggered = false;

    private float lastCirclePressTime = -1;
    private bool isDoublePress = false;

    private Coroutine scrollCoroutine;
    private Coroutine downloadCoroutine;


    public List<DownloadQueueItem> queueList = new List<DownloadQueueItem>();
    private int queueItemsPerPage = 24;
    public int queueCurrentPage = 0;
    public int queueHighlightIndex = 0;

    private sealed class DirectDownloadResult
    {
        public bool Succeeded;
        public bool Cancelled;
        public string Error;
    }
    #endregion

    #region Coroutine Handling
    private IEnumerator InitializeCoroutine()
    {
        float startTime = Time.time;

        while (!initializedApp)
            yield return null;

        audioSource = GetComponent<AudioSource>();
        menuTexts = new Text[MenuTextObjects.Length];

        for (int i = 0; i < menuTexts.Length; i++)
            menuTexts[i] =
                UI.FindInactiveObjectsByPath(MenuTextObjects[i])?.GetComponent<Text>();

        while (loadedOffline == null || !fullyInitialized)
            yield return null;

        if (!String.IsNullOrEmpty(startupError))
        {
            queueLoaded = true;
            Text statusText =
                UI.FindInactiveObjectsByPath("Canvas/Main/ContentSort")?.GetComponent<Text>();
            UI.ChangeText(statusText, startupError);
            statusText?.gameObject.SetActive(true);
            yield break;
        }

        UpdateSettingsOptions();
        LoadDownloadQueue();
        if (!String.IsNullOrEmpty(startupError))
        {
            Text statusText =
                UI.FindInactiveObjectsByPath("Canvas/Main/ContentSort")?.GetComponent<Text>();
            UI.ChangeText(statusText, startupError);
            statusText?.gameObject.SetActive(true);
            yield break;
        }


        Background background = FindObjectOfType<Background>();

        StartCoroutine(background?.UpdateDisplayInfo());

        background?.InitializePkgContent();
        UIManagement.HighlightCurrentPkg();

        if (backgroundMusic)
            audioSource.Play();

        if (MaintainedUpdateChannelAvailable && enableUpdates)
            yield return CheckForAppUpdates();

        float elapsed = Time.time - startTime;
        string formatted = elapsed >= 1f ? $"{elapsed:F0}s" : $"{elapsed:F2}s";
        Print(LogType.Log, $"Initialization completed in {formatted}!");

        yield return null;
    }

    private void ClearDownloadProgress(bool hideCanvas)
    {
        isDownloading = false;
        if (scrollCoroutine != null)
        {
            StopCoroutine(scrollCoroutine);
            scrollCoroutine = null;
        }

        if (downloadCanvas != null)
        {
            UI.ChangeText(downloadCanvas.transform.Find("Text/Title")?.GetComponent<Text>(), string.Empty);
            UI.ChangeText(downloadCanvas.transform.Find("Text/Elapsed")?.GetComponent<Text>(), string.Empty);
            UI.ChangeText(downloadCanvas.transform.Find("Text/WriteSpeed")?.GetComponent<Text>(), string.Empty);
            UI.ChangeText(downloadCanvas.transform.Find("Text/Percentage")?.GetComponent<Text>(), string.Empty);
            UI.ChangeText(downloadCanvas.transform.Find("Text/DownloadSpeed")?.GetComponent<Text>(), string.Empty);
            UI.ChangeText(downloadCanvas.transform.Find("Text/RemainingTime")?.GetComponent<Text>(), string.Empty);
            UI.FindInactiveObjectsByPath("Canvas/Download/ProgressBar")?.SetActive(false);
        }

        if (hideCanvas)
            UI.ShowUIState(null);
    }

    private void ShowDownloadProgress(DownloadQueueItem item)
    {
        Text name = downloadCanvas == null
            ? null : downloadCanvas.transform.Find("Text/Title")?.GetComponent<Text>();
        string title = item != null && item.content != null ? item.content.name : string.Empty;

        UI.ChangeText(name, title);
        if (name != null)
        {
            UI.SetFontByText(ref name);
            name.fontSize = name.font == FindObjectOfType<ContentHandler>()?.Arabic ? 28 : 36;

            if (scrollCoroutine != null)
                StopCoroutine(scrollCoroutine);
            scrollCoroutine = StartCoroutine(ScrollText(name, title));
        }

        UI.ShowUIState(downloadCanvas);
    }

    private void RenderDownloadProgress(DownloadProgress progress, float elapsedSeconds, ulong writeSpeed)
    {
        if (downloadCanvas == null || progress == null) return;

        ulong effectiveSpeed = progress.SpeedBytesPerSecond;
        if (writeSpeed > 0 && (effectiveSpeed == 0 || writeSpeed < effectiveSpeed))
            effectiveSpeed = writeSpeed;

        string total = progress.HasKnownTotal ? IO.FormatByteString(progress.TotalBytes) : "unknown";
        string remaining = progress.HasKnownTotal && effectiveSpeed > 0
            ? FormatTime((float)((double)progress.RemainingBytes / effectiveSpeed))
            : "Calculating...";

        UI.ChangeText(downloadCanvas.transform.Find("Text/Elapsed")?.GetComponent<Text>(),
            $"Elapsed: {FormatTime(elapsedSeconds)}");
        UI.ChangeText(downloadCanvas.transform.Find("Text/WriteSpeed")?.GetComponent<Text>(),
            $"Write Speed: {FormatSpeed(writeSpeed)}");
        UI.ChangeText(downloadCanvas.transform.Find("Text/Percentage")?.GetComponent<Text>(),
            $"{progress.Percent.ToString("0.##", CultureInfo.InvariantCulture)}%");
        UI.ChangeText(downloadCanvas.transform.Find("Text/DownloadSpeed")?.GetComponent<Text>(),
            $"{FormatSpeed(progress.SpeedBytesPerSecond)} - {IO.FormatByteString(progress.CurrentBytes)} of {total}");
        UI.ChangeText(downloadCanvas.transform.Find("Text/RemainingTime")?.GetComponent<Text>(),
            $"Remaining: {remaining}");

        RectTransform progressBar =
            UI.FindInactiveObjectsByPath("Canvas/Download/ProgressBar/Progress")?.GetComponent<RectTransform>();
        if (progressBar != null)
        {
            float amount = (float)(progress.Percent / 100.0);
            progressBar.pivot = new Vector2(0f, 0.5f);
            progressBar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Lerp(10.55f, 735.64f, amount) - 10.56f);
            progressBar.anchoredPosition = new Vector2(10.56f, progressBar.anchoredPosition.y);
            UI.FindInactiveObjectsByPath("Canvas/Download/ProgressBar")?.SetActive(true);
        }
    }

    private IEnumerator ProcessDirectItem(DownloadQueueItem item, DirectDownloadResult result)
    {
        if (TryBlockUnavailableMaintainedUpdate(item))
        {
            result.Error = item.last_error;
            yield break;
        }

        Uri downloadUri;
        PackagePaths paths;
        ulong expectedSize;
        string expectedSha256;
        string validationError;
        if (!TryPrepareQueueItem(item, out downloadUri, out paths, out expectedSize,
            out expectedSha256, out validationError))
        {
            FailQueueItem(item, validationError);
            result.Error = validationError;
            yield break;
        }

        PackageVerification existing = DownloadPolicy.VerifyPackage(
            paths.FinalPath, expectedSize, expectedSha256);
        if (File.Exists(paths.FinalPath))
        {
            if (!existing.IsValid)
            {
                string error =
                    $"Existing package '{paths.FinalPath}' failed verification: {existing.Error} " +
                    "Remove or relocate it before retrying; the file was retained for diagnosis.";
                FailQueueItem(item, error);
                result.Error = error;
                yield break;
            }

            if (!TryInstallVerifiedPackage(item, paths.FinalPath, out validationError))
            {
                if (item.state != "blocked")
                    FailQueueItem(item, validationError);
                result.Error = validationError;
                yield break;
            }

            result.Succeeded = true;
            yield break;
        }

        if (!isConsole)
        {
            validationError = "Direct downloads require the console native bridge.";
            FailQueueItem(item, validationError);
            result.Error = validationError;
            yield break;
        }

        cancelDirectDownloadRequested = false;
        cancelDirectDownloadSignalSent = false;
        for (int retryIndex = 0; retryIndex <= maxDownloadRetries; retryIndex++)
        {
            ulong resumedBytes = 0;
            try
            {
                if (File.Exists(paths.ResumePath))
                    resumedBytes = Math.Min((ulong)new FileInfo(paths.ResumePath).Length, expectedSize);
            }
            catch (Exception ex)
            {
                validationError = $"Could not inspect resume file: {ex.Message}";
                FailQueueItem(item, validationError);
                result.Error = validationError;
                yield break;
            }

            ulong remainingBytes = expectedSize - resumedBytes;
            ulong requiredBytes = remainingBytes > ulong.MaxValue - DownloadSpaceReserveBytes
                ? ulong.MaxValue : remainingBytes + DownloadSpaceReserveBytes;
            ulong freeBytes = NativeBridge.GetDiskFreeBytes(paths.Root);
            if (freeBytes == UInt64.MaxValue)
            {
                Print(LogType.Warning,
                    $"Free-space information is unavailable for '{paths.Root}'; continuing download.");
            }
            else if (freeBytes < requiredBytes)
            {
                validationError =
                    $"Insufficient free space on '{paths.Root}': {IO.FormatByteString(freeBytes)} available, " +
                    $"{IO.FormatByteString(requiredBytes)} required including reserve.";
                FailQueueItem(item, validationError);
                result.Error = validationError;
                yield break;
            }

            item.attempts++;
            if (!SetQueueState(item, "downloading", null))
            {
                validationError =
                    "Download was not started because its active state could not be persisted.";
                SetQueueState(item, "blocked", validationError);
                Print(LogType.Error, validationError);
                result.Error = validationError;
                yield break;
            }

            int startResult = NativeBridge.StartDirectDownload(downloadUri.AbsoluteUri, paths.FinalPath);
            if (startResult != 0)
            {
                NativeBridge.DownloadState failedStartState = NativeBridge.CurrentDownloadState;
                if (NativeBridge.IsTerminal(failedStartState))
                {
                    int failedStartResetResult = NativeBridge.ResetDirectDownload();
                    if (failedStartResetResult != 0)
                        Print(LogType.Warning,
                            $"Native download reset after a failed start returned " +
                            $"0x{((uint)failedStartResetResult).ToString("X8")}.");
                }

                validationError =
                    $"Native download start failed with code 0x{((uint)startResult).ToString("X8")}.";
                FailQueueItem(item, validationError);
                result.Error = validationError;
                yield break;
            }

            isDownloading = true;
            ShowDownloadProgress(item);
            float startedAt = Time.realtimeSinceStartup;
            float lastProgressAt = startedAt;
            ulong previousBytes = resumedBytes;
            NativeBridge.DownloadState state = NativeBridge.CurrentDownloadState;

            while (!NativeBridge.IsTerminal(state))
            {
                if (cancelDirectDownloadRequested && !cancelDirectDownloadSignalSent)
                {
                    NativeBridge.CancelDirectDownload();
                    cancelDirectDownloadSignalSent = true;
                }

                float now = Time.realtimeSinceStartup;
                ulong currentBytes = NativeBridge.GetDirectDownloadCurrentBytes();
                ulong totalBytes = NativeBridge.GetDirectDownloadTotalBytes();
                ulong speed = NativeBridge.GetDirectDownloadSpeedBytesPerSecond();
                DownloadProgress progress =
                    DownloadPolicy.ReduceProgress(previousBytes, currentBytes, totalBytes, speed);
                float interval = Mathf.Max(0.001f, now - lastProgressAt);
                double calculatedWriteSpeed = progress.DeltaBytes / (double)interval;
                ulong writeSpeed = calculatedWriteSpeed >= ulong.MaxValue
                    ? ulong.MaxValue : (ulong)calculatedWriteSpeed;
                RenderDownloadProgress(progress, now - startedAt, writeSpeed);
                previousBytes = currentBytes;
                lastProgressAt = now;

                yield return null;
                state = NativeBridge.CurrentDownloadState;
            }

            int nativeError = NativeBridge.GetDirectDownloadErrorCode();
            int httpStatus = NativeBridge.GetDirectDownloadHttpStatus();
            int resetResult = NativeBridge.ResetDirectDownload();
            if (resetResult != 0)
                Print(LogType.Warning,
                    $"Native download reset returned 0x{((uint)resetResult).ToString("X8")}.");

            isDownloading = false;
            ClearDownloadProgress(false);

            if (state == NativeBridge.DownloadState.Cancelled || cancelDirectDownloadRequested)
            {
                if (deleteOnCancel && File.Exists(paths.ResumePath))
                {
                    try
                    {
                        File.Delete(paths.ResumePath);
                    }
                    catch (Exception ex)
                    {
                        Print(LogType.Warning, $"Could not delete resume file '{paths.ResumePath}': {ex.Message}");
                    }
                }

                validationError = "Download cancelled by user.";
                FailQueueItem(item, validationError);
                result.Cancelled = true;
                result.Error = validationError;
                cancelDirectDownloadRequested = false;
                cancelDirectDownloadSignalSent = false;
                yield break;
            }

            if (state == NativeBridge.DownloadState.Succeeded)
            {
                PackageVerification verification = DownloadPolicy.VerifyPackage(
                    paths.FinalPath, expectedSize, expectedSha256);
                if (!verification.IsValid)
                {
                    validationError =
                        $"Downloaded package verification failed; file retained for diagnosis: {verification.Error}";
                    FailQueueItem(item, validationError);
                    result.Error = validationError;
                    yield break;
                }

                if (!TryInstallVerifiedPackage(item, paths.FinalPath, out validationError))
                {
                    if (item.state != "blocked")
                        FailQueueItem(item, validationError);
                    result.Error = validationError;
                    yield break;
                }

                result.Succeeded = true;
                yield break;
            }

            DownloadFailureKind failureKind = httpStatus > 0
                ? DownloadFailureKind.Http : DownloadFailureKind.Transport;
            validationError = httpStatus > 0
                ? $"Download failed with HTTP status {httpStatus}."
                : $"Download transport failed with code 0x{((uint)nativeError).ToString("X8")}.";

            if (retryIndex < maxDownloadRetries &&
                DownloadPolicy.ShouldRetry(failureKind, httpStatus))
            {
                SetQueueState(item, "retrying", validationError);
                int delay = DownloadPolicy.GetRetryDelaySeconds(retryIndex, retryBaseDelaySeconds);
                Print(LogType.Warning,
                    $"{validationError} Retrying '{item.content.name}' in {delay} seconds.");
                yield return new WaitForSecondsRealtime(delay);
                continue;
            }

            FailQueueItem(item, validationError);
            result.Error = validationError;
            yield break;
        }
    }

    private bool TryInstallVerifiedPackage(DownloadQueueItem item, string finalPath, out string error)
    {
        error = null;
        if (!installAfter)
            return TryPersistQueueCompletion(item, out error);
        if (!isConsole)
        {
            error = "Package installation requires the console native bridge.";
            return false;
        }

        if (!SetQueueState(item, "installing", null))
        {
            error = "Package installation was not started because the installing state " +
                "could not be persisted.";
            SetQueueState(item, "blocked", error);
            Print(LogType.Error, error);
            return false;
        }

        string cover = URL.ProperFormatUrl(item.content.cover_url);
        if (!URL.IsValidURI(cover)) cover = iconPath;

        int installResult = NativeBridge.InstallLocalPackageResult(
            finalPath, item.content.name, cover, deleteAfter);
        if (installResult != 0)
        {
            error =
                $"Local package installation failed with code 0x{((uint)installResult).ToString("X8")}; " +
                "the verified package was retained.";
            return false;
        }

        return TryPersistQueueCompletion(item, out error);
    }

    private bool TryPersistQueueCompletion(DownloadQueueItem item, out string error)
    {
        error = null;
        if (SetQueueState(item, "completed", null))
            return true;

        error = "Package processing completed, but queue completion could not be persisted. " +
            "The item was blocked to prevent duplicate installation; verify the installed package " +
            "before removing and re-adding it.";
        SetQueueState(item, "blocked", error);
        Print(LogType.Error, error);
        return false;
    }

    private void ProcessBackgroundItem(DownloadQueueItem item)
    {
        if (TryBlockUnavailableMaintainedUpdate(item))
            return;

        Uri downloadUri;
        PackagePaths paths;
        ulong expectedSize;
        string expectedSha256;
        string error;
        if (!TryPrepareQueueItem(item, out downloadUri, out paths, out expectedSize,
            out expectedSha256, out error))
        {
            FailQueueItem(item, error);
            return;
        }

        if (!isConsole)
        {
            FailQueueItem(item, "Background submission requires the console native bridge.");
            return;
        }

        item.attempts++;
        if (!SetQueueState(item, "submitting", null))
        {
            error = "Background submission was not started because its intent " +
                "could not be persisted.";
            SetQueueState(item, "blocked", error);
            Print(LogType.Error, error);
            return;
        }

        string cover = URL.ProperFormatUrl(item.content.cover_url);
        if (!URL.IsValidURI(cover)) cover = iconPath;
        string safeName = DownloadPolicy.SanitizeName(
            item.content.name, DownloadPolicy.MaximumFileNameUtf8Bytes);
        string normalizedTitleId = DownloadPolicy.NormalizeTitleId(item.content.title_id);
        int submitResult = NativeBridge.InstallWebPackageResult(
            downloadUri.AbsoluteUri, safeName, normalizedTitleId, cover);
        if (submitResult == 0)
        {
            if (!SetQueueState(item, "submitted", null))
            {
                error = "The background service accepted the request, but the submitted state " +
                    "could not be persisted. The item was blocked to prevent duplicate submission.";
                SetQueueState(item, "blocked", error);
                Print(LogType.Error, error);
                return;
            }

            Print(LogType.Log,
                $"Submitted '{item.content.name}' to the background package service.");
        }
        else
        {
            FailQueueItem(item,
                $"Background package submission failed with code 0x{((uint)submitResult).ToString("X8")}.");
        }
    }

    private IEnumerator ProcessQueueItems(string onlyItemId = null)
    {
        if (queueList.Count == 0)
        {
            downloadCoroutine = null;
            yield break;
        }

        UI.ShowUIState(null);
        List<string> itemIds = onlyItemId == null
            ? queueList.Where(item => item != null && IsDispatchableQueueState(item.state))
                .Select(item => item.id).ToList()
            : new List<string> { onlyItemId };

        for (int i = 0; i < itemIds.Count; i++)
        {
            string itemId = itemIds[i];
            DownloadQueueItem item = queueList.FirstOrDefault(
                queued => queued != null && String.Equals(queued.id, itemId, StringComparison.Ordinal));
            if (item == null) continue;
            if (!IsDispatchableQueueState(item.state))
            {
                Print(LogType.Warning,
                    $"Queue item '{item.content?.name ?? item.id}' is in '{item.state}' state and was not dispatched.");
                continue;
            }
            Print(LogType.Log,
                $"Processing {i + 1}/{itemIds.Count}: " +
                $"{(item.content == null ? "invalid queue item" : item.content.name)}");

            if (directDownload)
            {
                DirectDownloadResult result = new DirectDownloadResult();
                yield return StartCoroutine(ProcessDirectItem(item, result));
                if (result.Succeeded)
                    RemoveQueueItem(item);
                if (result.Cancelled)
                    break;
            }
            else
            {
                ProcessBackgroundItem(item);
            }

            if (queueCanvas != null && queueCanvas.activeSelf)
                UpdateQueueDisplay();
        }

        cancelDirectDownloadRequested = false;
        cancelDirectDownloadSignalSent = false;
        ClearDownloadProgress(true);
        UIManagement.UpdateAllDownloadStatuses();
        downloadCoroutine = null;
        Print(LogType.Log, "Finished processing queued items.");
    }

    public string FormatTime(float totalSeconds)
    {
        if (float.IsNaN(totalSeconds) || totalSeconds == float.MinValue || totalSeconds == float.MaxValue) return "Calculating...";
        return TimeSpan.FromSeconds(totalSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    public string FormatSpeed(float bytesPerSecond)
    {
        int unitIndex = 0;
        float speed = bytesPerSecond;
        string[] units;

        speed *= 8;
        units = new[] { "b/s", "Kb/s", "Mb/s", "Gb/s" };

        while (speed >= 1024 && unitIndex < units.Length - 1)
        {
            speed /= 1024;
            unitIndex++;
        }

        int decimalPlaces = unitIndex == 0 ? 0 : unitIndex == 1 ? 1 : 2;

        return $"{speed.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    private IEnumerator ScrollText(Text textComponent, string content)
    {
        float scrollSpeed = 50f;
        float scrollPosition = 0f;

        if (textComponent == null) yield break;

        textComponent.text = content;

        float contentWidth = textComponent.preferredWidth;
        float viewportWidth = textComponent.rectTransform.rect.width;

        if (contentWidth <= viewportWidth)
            yield break;

        string paddedContent = content + "          " + content;

        while (true)
        {
            scrollPosition += scrollSpeed * Time.deltaTime;

            if (scrollPosition >= contentWidth)
                scrollPosition -= contentWidth;

            int charOffset = Mathf.FloorToInt(scrollPosition / (contentWidth / paddedContent.Length));
            int substringLength = Mathf.Min(content.Length, paddedContent.Length - charOffset);

            textComponent.text = paddedContent.Substring(charOffset, substringLength);

            yield return null;
        }
    }
    #endregion

    #region User Input Handling
    private async void HandleUserInput()
    {
        if (cooldownTimer <= 0)
        {
            float horizontalInput = Input.GetAxis("Dpad-X") + Input.GetAxis("KB-X");
            float verticalInput =
              Input.GetAxis("LStick-Y") + Input.GetAxis("Dpad-Y") + Input.GetAxis("Mouse-Y");

            if (!downloadCanvas.activeSelf && !cancelCanvas.activeSelf && !updateCanvas.activeSelf)
            {
                if (Input.GetButtonDown("Triangle"))
                    ToggleMenu(false);

                if (Input.GetButtonDown("Square"))
                {
                    if (downloadCanvas.activeSelf) return;

                    cooldownTimer = inputCooldown;

                    if (menuLoaded) return;

                    if (string.IsNullOrEmpty(currentPkg.TitleID.text)) return;

                    if (detailsCanvas.activeSelf)
                    {
                        UI.ShowUIState(null);
                        return;
                    }
                    else
                    {
                        string url = URL.ProperFormatUrl(currentContentItem.Value.cover_url);

                        if (string.IsNullOrEmpty(url) || url == null)
                            coverImage.gameObject.SetActive(false);
                        else
                        {
                            if (URL.IsValidImage(url))
                                SetImageFromURL(url, ref coverImage);
                        }

                        bool isPS4Content = IsPS4TitleId(currentContentItem.Value.title_id);

                        var ps4Cover = UI.FindInactiveObjectsByPath("Canvas/Details/CoverArt/PS4Case")?.GetComponent<RawImage>();
                        var ps5Cover = UI.FindInactiveObjectsByPath("Canvas/Details/CoverArt/PS5Case")?.GetComponent<RawImage>();

                        if (ps4Cover != null) ps4Cover.gameObject.SetActive(isPS4Content);
                        if (ps5Cover != null) ps5Cover.gameObject.SetActive(!isPS4Content);

                        ps5Cover?.gameObject.SetActive(!isPS4Content);

                        UI.ShowUIState(detailsCanvas);

                        if (detailsCanvas == null) return;

                        Transform textTransform = detailsCanvas.transform.Find("Text");

                        if (textTransform == null) return;

                        Text name = textTransform.Find("Name")?.GetComponent<Text>();
                        Text title_id = textTransform.Find("TitleID")?.GetComponent<Text>();
                        Text version = textTransform.Find("AppVersion")?.GetComponent<Text>();
                        Text min_fw = textTransform.Find("MinFWReq")?.GetComponent<Text>();
                        Text release = textTransform.Find("Release")?.GetComponent<Text>();

                        UI.ChangeText(name, currentContentItem.Value.name);
                        UI.SetFontByText(ref name);

                        if (name.font == FindObjectOfType<ContentHandler>()?.Arabic)
                            name.fontSize = 32;
                        else name.fontSize = 42;

                        UI.ChangeText(title_id, $"Title ID: {currentContentItem.Value.title_id} " +
                                $"[{currentContentItem.Value.region}]");

                        UI.ChangeText(version, $"Package Version: {currentContentItem.Value.version}");
                        UI.ChangeText(min_fw, $"Required Firmware: {currentContentItem.Value.min_fw}");
                        UI.ChangeText(release, $"Release Date: {currentContentItem.Value.release}");

                        if (scrollCoroutine != null)
                        {
                            StopCoroutine(scrollCoroutine);
                            scrollCoroutine = null;
                        }

                        scrollCoroutine = StartCoroutine(ScrollText(name, currentContentItem.Value.name));
                    }
                }

                if (menuCanvas.activeSelf)
                {
                    if (verticalInput != 0) NavigateMenu(verticalInput);
                    if (horizontalInput != 0 && selectedIndex == 4
                        || selectedIndex == 11) ScrollOption(horizontalInput);
                }
                else
                {
                    if (!detailsCanvas.activeSelf && !queueCanvas.activeSelf)
                    {
                        if (Input.GetButtonDown("L1")) ScrollOption(-1);
                        if (Input.GetButtonDown("R1")) ScrollOption(1);

                        if (Input.GetButtonDown("L1") || Input.GetButtonDown("R1"))
                        {
                            cooldownTimer = inputCooldown;
                            SaveConfiguration();
                        }

                        if (Input.GetButtonDown("Touchpad")) // make UOB HELPER FUNC FOR THIS (KB INPUT HANDLING)
                        {
                            if (!isConsole) return;

                            IntPtr kbInput = UOB.GetKeyboardInput("Search Content (By name or title ID)", "");
                            string kbOutput = Marshal.PtrToStringAnsi(kbInput);

                            if (string.IsNullOrEmpty(kbOutput) || kbOutput == "NULL")
                                searchFilter = string.Empty;
                            else
                                searchFilter = kbOutput;
                        }
                    }

                    if (!detailsCanvas.activeSelf && !downloadCanvas.activeSelf)
                    {
                        int scrollAmount = itemsPerPage;

                        if (Input.GetAxis("L2") != 0)
                        {
                            cooldownTimer = inputCooldown;
                            contentScroll -= scrollAmount;

                            if (contentScroll < 0) contentScroll = filteredCount > 0 ?
                                Mathf.Max(0, filteredCount - (filteredCount % itemsPerPage == 0
                                ? itemsPerPage : filteredCount % itemsPerPage)) : 0;
                        }

                        if (Input.GetAxis("R2") != 0)
                        {
                            cooldownTimer = inputCooldown;

                            contentScroll += scrollAmount;
                            if (contentScroll >= filteredCount)
                                contentScroll = 0;
                        }

                        if (verticalInput != 0 && !queueCanvas.activeSelf)
                        {
                            cooldownTimer = inputCooldown;

                            float clampedValue = Mathf.Clamp(verticalInput, 0f, 1f);
                            bool scrollDown = clampedValue == 0,
                              scrollUp = clampedValue > 0f;

                            if (Content.PKGs != null && Content.PKGs.Count > 0)
                            {
                                foreach (var pkg in Content.PKGs)
                                {
                                    if (pkg != null && pkg.TitleID.enabled)
                                    {
                                        pkg.TitleID.color = Color.white;
                                        pkg.Region.color = Color.white;
                                        pkg.Title.color = Color.white;
                                        pkg.Size.color = Color.white;
                                    }
                                }

                                var previousPkg = Content.PKGs[contentScroll % itemsPerPage];
                                if (previousPkg != null && previousPkg.TitleID.enabled)
                                    previousPkg.TitleID.color = Color.white;
                            }

                            if (scrollDown)
                            {
                                contentScroll++;

                                if (contentScroll >= filteredCount) contentScroll = 0;
                            }
                            else if (scrollUp)
                            {
                                contentScroll--;

                                if (contentScroll < 0) contentScroll
                                        = filteredCount > 0 ? filteredCount - 1 : 0;
                            }
                        }

                        if (horizontalInput < 0 && !menuCanvas.activeSelf && !detailsCanvas.activeSelf && !queueCanvas.activeSelf)
                        {
                            cooldownTimer = inputCooldown;

                            string contentName = currentContentItem.Value.name;
                            DownloadQueueItem candidate = CreateQueueSnapshot(currentContentItem);
                            DownloadQueueItem existing = candidate == null ? null :
                                queueList.FirstOrDefault(item => item != null &&
                                    String.Equals(item.id, candidate.id, StringComparison.Ordinal));

                            if (existing != null)
                            {
                                RemoveQueueItem(existing);
                                Print(LogType.Log, $"Removed '{contentName}' from queue");
                            }
                            else
                            {
                                DownloadQueueItem added = AddQueueItem(candidate);
                                if (added != null)
                                    Print(LogType.Log, $"Added '{contentName}' to queue");
                            }
                        }

                        if (Input.GetButtonDown("R3"))
                        {
                            if (queueCanvas.activeSelf)
                                UI.ShowUIState(null);
                            else
                            {
                                UI.ShowUIState(queueCanvas);
                                // Reset highlight index when opening queue
                                queueHighlightIndex = 0;
                                queueCurrentPage = 0;
                                // Update queue display when opening
                                UpdateQueueDisplay();
                                // Update queue scrollbar when opening
                                if (queue_scrollbar != null)
                                    UI.UpdateScrollbar();
                            }
                        }

                        // Handle vertical input when queue canvas is open
                        if (queueCanvas.activeSelf && verticalInput != 0)
                        {
                            cooldownTimer = inputCooldown;

                            float clampedValue = Mathf.Clamp(verticalInput, 0f, 1f);
                            bool scrollDown = clampedValue == 0;
                            bool scrollUp = clampedValue > 0f;

                            int totalItems = queueList.Count;
                            if (totalItems == 0)
                            {
                                queueCurrentPage = 0;
                                queueHighlightIndex = 0;
                            }
                            else
                            {
                                int totalPages = Mathf.CeilToInt((float)totalItems / queueItemsPerPage);
                                int itemsOnCurrentPage = Mathf.Min(queueItemsPerPage,
                                    totalItems - (queueCurrentPage * queueItemsPerPage));

                                if (scrollDown)
                                {
                                    queueHighlightIndex++;
                                    if (queueHighlightIndex >= itemsOnCurrentPage)
                                    {
                                        queueCurrentPage++;
                                        if (queueCurrentPage >= totalPages)
                                            queueCurrentPage = 0;
                                        queueHighlightIndex = 0;
                                    }
                                }
                                else if (scrollUp)
                                {
                                    queueHighlightIndex--;
                                    if (queueHighlightIndex < 0)
                                    {
                                        queueCurrentPage--;
                                        if (queueCurrentPage < 0)
                                            queueCurrentPage = totalPages - 1;
                                        int pageItems = Mathf.Min(queueItemsPerPage,
                                            totalItems - (queueCurrentPage * queueItemsPerPage));
                                        queueHighlightIndex = Mathf.Max(0, pageItems - 1);
                                    }
                                }
                            }

                            // Update queue display with highlighting
                            UpdateQueueDisplay();

                            // Update scrollbar
                            if (queue_scrollbar != null)
                                UI.UpdateScrollbar();
                        }

                        // Handle horizontal input when queue canvas is open
                        if (queueCanvas.activeSelf && horizontalInput < 0)
                        {
                            cooldownTimer = inputCooldown;

                            // Calculate the actual index in the queue list
                            int actualIndex = (queueCurrentPage * queueItemsPerPage) + queueHighlightIndex;

                            if (actualIndex >= 0 && actualIndex < queueList.Count)
                            {
                                DownloadQueueItem itemToRemove = queueList[actualIndex];
                                string contentName = itemToRemove != null && itemToRemove.content != null
                                    ? itemToRemove.content.name : "Invalid queue item";

                                RemoveQueueItem(itemToRemove);
                                ClampQueueSelection();
                                Print(LogType.Log, $"Removed '{contentName}' from queue");
                                UpdateQueueDisplay();
                            }
                        }
                    }
                }
            }

            if (Input.GetButtonDown("X"))
            {
                // Handle X button press on queue canvas
                if (queueCanvas.activeSelf)
                {
                    if (downloadCoroutine != null)
                    {
                        Print(LogType.Warning, "A queue operation is already running.");
                        return;
                    }
                    if (queueList.Count > 0)
                    {
                        Print(LogType.Log, $"Starting download and install of {queueList.Count} queued items");
                        downloadCoroutine = StartCoroutine(ProcessQueueItems());
                        return;
                    }
                    else
                    {
                        Print(LogType.Log, "Queue is empty, nothing to download");
                        return;
                    }
                }

                if (menuLoaded)
                    ExecuteMenuItemAction();
                else
                {
                    if (cancelCanvas.activeSelf)
                    {
                        if (isDownloading && isConsole && !cancelDirectDownloadRequested)
                        {
                            cancelDirectDownloadRequested = true;
                            NativeBridge.CancelDirectDownload();
                            cancelDirectDownloadSignalSent = true;
                            Print(LogType.Log,
                                "Cancellation requested; waiting for the native download to stop.");
                        }
                        return;
                    }

                    if (updateCanvas.activeSelf)
                    {
                        if (MaintainedUpdateChannelAvailable && isConsole)
                            UOB.UpdateViaHomebrewStore("PKGI13337");
                        else
                            Print(LogType.Warning,
                                "Application updates are unavailable for this maintained build.");

                        UI.ShowUIState(null);
                        return;
                    }

                    if (downloadCoroutine != null)
                    {
                        Print(LogType.Warning, "A queue operation is already running.");
                        return;
                    }
                    if (!downloadCanvas.activeSelf)
                    {
                        if (currentPkg.TitleID.text == "PKGI13337")
                        {
                            if (MaintainedUpdateChannelAvailable)
                                await CheckForAppUpdates();
                            else
                                Print(LogType.Warning,
                                    "Application updates are unavailable for this maintained build.");
                            return;
                        }

                        DownloadQueueItem selectedItem = AddToQueueUI(currentContentItem);
                        if (selectedItem == null)
                            return;

                        Print(LogType.Log, $"Processing '{currentContentItem.Value.name}'.");
                        downloadCoroutine = StartCoroutine(ProcessQueueItems(selectedItem.id));
                    }
                }
            }

            if (Input.GetButtonDown("Circle"))
            {
                cooldownTimer = inputCooldown;
                float currentTime = Time.unscaledTime;

                // Only allow double-press exit when no special canvas is shown (main canvas only)
                bool isOnMainCanvas = !menuCanvas.activeSelf && !detailsCanvas.activeSelf &&
                                     !downloadCanvas.activeSelf && !cancelCanvas.activeSelf &&
                                     !updateCanvas.activeSelf && !queueCanvas.activeSelf;

                if (isOnMainCanvas && currentTime - lastCirclePressTime <= 0.4f)
                {
                    isDoublePress = true;
                    SaveConfiguration();

                    if (isConsole)
                        UOB.ExitApplication();

#if UNITY_EDITOR_WIN
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                }
                else
                {
                    isDoublePress = false;
                }

                lastCirclePressTime = currentTime;

                if (!isDoublePress)
                {
                    if (cancelCanvas.activeSelf)
                    {
                        UI.ShowUIState(downloadCanvas);
                        return;
                    }

                    if (updateCanvas.activeSelf)
                    {
                        UI.ShowUIState(null);
                        return;
                    }

                    if (menuCanvas.activeSelf)
                        ToggleMenu(true);
                    else if (detailsCanvas.activeSelf)
                    {
                        UI.ShowUIState(null);

                        if (scrollCoroutine != null)
                        {
                            StopCoroutine(scrollCoroutine);
                            scrollCoroutine = null;
                        }
                    }
                    else if (cancelCanvas.activeSelf)
                    {
                        if (isDownloading)
                            UI.ShowUIState(downloadCanvas);
                        else
                            UI.ShowUIState(null);
                    }
                    else if (queueCanvas.activeSelf)
                    {
                        UI.ShowUIState(null);
                        return;
                    }
                    else if (isDownloading)
                        UI.ShowUIState(cancelCanvas);
                }
            }

        }

    }

    private void NavigateMenu(float verticalInput)
    {
        cooldownTimer = inputCooldown;

        Menu.ResetMenuItemsToDefault();

        selectedIndex =
          (selectedIndex + (verticalInput > 0 ? -1 : 1) +
            menuTexts.Length) % menuTexts.Length;

        Menu.HighlightMenuItem(selectedIndex);
    }

    private void ScrollOption(float horizontalInput, int textArrayInt = -1)
    {
        if (textArrayInt == -1) textArrayInt = selectedIndex;
        string currentText = menuTexts[textArrayInt].text;

        if (menuLoaded && textArrayInt == 9)
        {
            string rightTriangle = "> ";
            string directDownloadText = $"{rightTriangle}Direct Download";
            string backDownloadText = $"{rightTriangle}Back. Download";

            if (horizontalInput != 0)
            {
                cooldownTimer = inputCooldown;

                string newText = currentText == directDownloadText ?
                  backDownloadText : directDownloadText;

                UI.ChangeText(menuTexts, textArrayInt, newText);

                if (newText == directDownloadText)
                    directDownload = true;
                else directDownload = false;
            }
        }
        else
        {
            if (!menuLoaded || textArrayInt == 4)
            {
                cooldownTimer = inputCooldown;

                if (horizontalInput < 0)
                    ScrollOptionLeft();
                else if (horizontalInput > 0)
                    ScrollOptionRight();
            }
        }
    }

    private void ExecuteMenuItemAction()
    {
        cooldownTimer = inputCooldown;

        var region = string.Empty;
        switch (selectedIndex)
        {
            case 5:
                region = "USA";
                break;
            case 6:
                region = "Europe";
                break;
            case 7:
                region = "Japan";
                break;
            case 8:
                region = "Asia";
                break;
        }

        if (menuLoaded)
            switch (selectedIndex)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    SetSortOption(sortByOptions[(int)(SortBy)selectedIndex], selectedIndex);
                    reloadTriggered = true;
                    break;

                case 4:
                    ScrollOption(1);
                    break;

                case 5:
                case 6:
                case 7:
                case 8:
                    ToggleRegionOption(selectedIndex, region);
                    reloadTriggered = true;
                    break;

                case 9:
                    ScrollOption(1);
                    break;

                case 10: // make UOB HELPER FUNC FOR THIS (KB INPUT HANDLING)
                    ToggleOption(10, "Install Once Done", ref installAfter);
                    break;

                case 11:
                    //  if (GoldHEN == true || !isConsole)
                    ToggleOption(11, "Delete After Install", ref deleteAfter);
                    break;

                case 12:
                    ToggleOption(12, "Delete On Cancel", ref deleteOnCancel);
                    break;

                case 13:
                    ToggleOption(13, "Populate Via Web", ref populateViaWeb);
                    toggleBackToLocal = true;
                    reloadTriggered = true;
                    break;

                case 14:
                    if (MaintainedUpdateChannelAvailable)
                        ToggleOption(14, "Enable App Updates", ref enableUpdates);
                    else
                        Print(LogType.Warning,
                            "Application updates are unavailable for this maintained build.");
                    break;

                case 15:
                    ToggleOption(15, "Background Music", ref backgroundMusic);

                    if (!backgroundMusic)
                        audioSource.Pause();
                    else
                        audioSource.Play();
                    break;

                case 16:
                    if (!isConsole)
                        return;

                    IntPtr _kbInput = UOB.GetKeyboardInput("Enter an image path: (local path or URL)",
                      string.IsNullOrEmpty(background_uri) || background_uri == null ? Path.Combine(directoryPath,
                        $"Backgrounds" + (isConsole ? "/" : "\\")) : background_uri
                    );

                    string _kbOutput = Marshal.PtrToStringAnsi(_kbInput);

                    if (string.IsNullOrEmpty(_kbOutput) || _kbOutput == "NULL")
                    {
                        background_uri = null;
                        background.gameObject.SetActive(false);
                        return;
                    }

                    if (!File.Exists(_kbOutput))
                    {
                        if (URL.IsValidImage(_kbOutput))
                            SetImageFromURL(_kbOutput, ref background);
                    }
                    else IO.LoadImage(_kbOutput, ref background);

                    break;


                case 17: // make UOB HELPER FUNC FOR THIS (KB INPUT HANDLING)
                    if (!isConsole) return;
                    IntPtr kbInput = UOB.GetKeyboardInput("Set Download Location...", downloadPath);
                    string kbOutput = Marshal.PtrToStringAnsi(kbInput);

                    if (string.IsNullOrEmpty(kbOutput) || kbOutput == "NULL") return;
                    else downloadPath = kbOutput;
                    reloadTriggered = true;
                    break;

                case 18:
                    toggleBackToLocal = true;
                    HandleConfiguration();
                    UpdateSettingsOptions();
                    ToggleMenu();
                    break;

            }
    }

    private void SetSortOption(string optionName, int index)
    {
        if (menuTexts[index] != null && menuTexts[index].text.Contains("^"))
        {
            UI.ChangeText(menuTexts, index, $"v {optionName}");

            ascending = false;
        }
        else if (menuTexts[index] != null && menuTexts[index].text.Contains("v"))
        {
            UI.ChangeText(menuTexts, index, $"^ {optionName}");

            ascending = true;
        }
        else
        {
            for (int i = 0; i <= 3; i++)
            {
                if (menuTexts[i] != null)
                    UI.ChangeText(menuTexts, i, sortByOptions[i]);
            }

            if (ascending)
            {
                if (menuTexts[index] != null)
                    UI.ChangeText(menuTexts, index, $"^ {optionName}");
            }
            else
            {
                if (menuTexts[index] != null)
                    UI.ChangeText(menuTexts, index, $"v {optionName}");
            }

            sortCriteria = index;
        }
    }

    #endregion

    #region UI Interaction
    public void UpdateSettingsOptions()
    {
        UI.ChangeText(menuTexts, 4, $"> {contentOptions[contentFilter]}");

        for (int i = 0; i <= 3; i++)
        {
            if (menuTexts[i] != null)
            {
                if (i == sortCriteria)
                {
                    string arrow = ascending ? "^" : "v";
                    UI.ChangeText(menuTexts, i, $"{arrow} {sortByOptions[i]}");
                }
                else
                    UI.ChangeText(menuTexts, i, sortByOptions[i]);
            }
        }

        string[] regionNames = { "USA", "Europe", "Japan", "Asia" };
        for (int i = 5; i < 9; i++)
        {
            string regionName = regionNames[i - 5];
            bool isRegionSelected = Array.Exists(filteredRegions, element => element == regionName);
            UI.ChangeText(menuTexts, i, isRegionSelected ? $"x {regionName}" : $"o {regionName}");
        }

        Text directDownloadText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/UserPreferences/DirectDownload")?.GetComponent<Text>();
        UI.ChangeText(directDownloadText, directDownload ? "> Direct Download" : "> Back. Download");

        Text installAfterText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/UserPreferences/InstallOnceDone")?.GetComponent<Text>();
        UI.ChangeText(installAfterText, installAfter ? "x Install Once Done" : "o Install Once Done");

        Text deleteAfterText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/UserPreferences/DeleteAfterInstall")?.GetComponent<Text>();
        // UI.ChangeText(deleteAfterText, etaHEN == true ? "x Delete After Install" : (deleteAfter ? "x Delete After Install" : "o Delete After Install"));
        UI.ChangeText(deleteAfterText, deleteAfter ? "x Delete After Install" : "o Delete After Install");

        Text deleteOnCancelText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/UserPreferences/DeleteOnCancel")?.GetComponent<Text>();
        UI.ChangeText(deleteOnCancelText, deleteOnCancel ? "x Delete On Cancel" : "o Delete On Cancel");

        Text populateViaWebText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/UserPreferences/PopulateViaWeb")?.GetComponent<Text>();
        UI.ChangeText(populateViaWebText, populateViaWeb ? "x Populate Via Web" : "o Populate Via Web");

        Text enableAppUpdatesText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/UserPreferences/EnableAppUpdates")?.GetComponent<Text>();
        UI.ChangeText(enableAppUpdatesText, MaintainedUpdateChannelAvailable
            ? (enableUpdates ? "x Enable App Updates" : "o Enable App Updates")
            : "- App Updates Unavailable");

        Text backgroundMusicText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/UserPreferences/BackgroundMusic")?.GetComponent<Text>();
        UI.ChangeText(backgroundMusicText, backgroundMusic ? "x Background Music" : "o Background Music");
    }

    public void ToggleMenu(bool resetSettings = false)
    {
        if (downloadCanvas.activeSelf) return;

        if (!menuLoaded)
        {
            pS.ascending = ascending;
            pS.sortCriteria = sortCriteria;
            pS.contentFilter = contentFilter;
            pS.filteredRegions = filteredRegions;

            if (pS.previousBg != null)
                pS.background_uri = background_uri;

            if (pS.previousBg != null)
                pS.previousBg = background.texture;

            pS.directDownload = directDownload;
            pS.installAfter = installAfter;
            pS.deleteAfter = deleteAfter;
            pS.deleteOnCancel = deleteOnCancel;
            pS.populateViaWeb = populateViaWeb;
            pS.enableUpdates = enableUpdates;
            pS.backgroundMusic = backgroundMusic;
        }

        cooldownTimer = inputCooldown;
        menuLoaded = !menuLoaded;

        if (menuLoaded)
        {
            UI.ShowUIState(menuCanvas);
            Menu.HighlightMenuItem(selectedIndex);
        }
        else
        {
            UI.ShowUIState(null);

            if (resetSettings)
            {
                ascending = pS.ascending;
                sortCriteria = pS.sortCriteria;
                contentFilter = pS.contentFilter;
                filteredRegions = pS.filteredRegions;

                if (pS.previousBg != null)
                    background_uri = pS.background_uri;

                if (pS.previousBg != null)
                    background.texture = pS.previousBg;

                directDownload = pS.directDownload;
                installAfter = pS.installAfter;
                deleteAfter = pS.deleteAfter;
                deleteOnCancel = pS.deleteOnCancel;
                populateViaWeb = pS.populateViaWeb;
                enableUpdates = pS.enableUpdates;
                backgroundMusic = pS.backgroundMusic;

                UpdateSettingsOptions();

                background.gameObject.SetActive(URL.IsValidURI(URL.ProperFormatUrl(background_uri)) || URL.IsValidImage(background_uri));

                if (!backgroundMusic)
                    audioSource.Stop();
                else
                    audioSource.Play();
            }
            else
                SaveConfiguration();
        }
    }

    private void ScrollOptionLeft() => ScrollOptionToOption(false);

    private void ScrollOptionRight() => ScrollOptionToOption(true);

    private void ScrollOptionToOption(bool scrollRight)
    {
        Text selectionText = UI.FindInactiveObjectsByPath("Canvas/Menu/Text/FilteringOptions/Content/Selection")?.GetComponent<Text>();

        const string rightTriangle = "> ";
        string currentText = selectionText.text.TrimStart(rightTriangle[0], ' ');

        int currentIndex = Array.IndexOf(contentOptions, currentText);

        if (currentIndex == -1) currentIndex = contentFilter;

        int newIndex;
        do
        {
            newIndex = scrollRight ? (currentIndex + 1) % contentOptions.Length :
                (currentIndex - 1 + contentOptions.Length) % contentOptions.Length;
            currentIndex = newIndex;
        } while (GoldHEN == true && contentOptions[newIndex] == "PS5");

        contentFilter = newIndex;

        UI.ChangeText(selectionText, $"{rightTriangle}{contentOptions[newIndex]}");
    }

    public static void AddRegion(string region)
    {
        if (!Array.Exists(filteredRegions, element => element == region))
        {
            Array.Resize(ref filteredRegions, filteredRegions.Length + 1);
            filteredRegions[filteredRegions.Length - 1] = region;
        }
    }

    public static void RemoveRegion(string region)
    {
        int index = Array.IndexOf(filteredRegions, region);

        if (index != -1)
        {
            for (int i = index; i < filteredRegions.Length - 1; i++)
                filteredRegions[i] = filteredRegions[i + 1];

            Array.Resize(ref filteredRegions, filteredRegions.Length - 1);
        }
    }

    private void ToggleRegionOption(int index, string regionName)
    {
        bool isRegionSelected = menuTexts[index].text.Contains($"x {regionName}");

        ToggleOption(index, regionName, ref isRegionSelected);

        if (isRegionSelected) AddRegion(regionName);
        else RemoveRegion(regionName);
    }

    private void ToggleOption(int index, string text, ref bool toggle)
    {
        string currentState = menuTexts[index].text;
        string checkedState = $"x {text}";
        string uncheckedState = $"o {text}";

        string newState = currentState == checkedState ? uncheckedState : checkedState;

        UI.ChangeText(menuTexts, index, newState);

        toggle = newState == checkedState;
    }
    #endregion

    #region Queue Management
    private string GetQueueFilePath()
    {
        return Path.Combine(directoryPath, "download-queue.json");
    }

    private bool PersistQueue()
    {
        DownloadQueue queue = new DownloadQueue
        {
            schema_version = 1,
            items = queueList
        };

        try
        {
            IO.WriteAllTextAtomic(GetQueueFilePath(),
                JsonConvert.SerializeObject(queue, Formatting.Indented));
            return true;
        }
        catch (Exception ex)
        {
            Print(LogType.Error, $"Could not persist download queue: {ex.Message}");
            return false;
        }
    }

    private void LoadDownloadQueue()
    {
        queueLoaded = false;
        string queuePath = GetQueueFilePath();
        queueList = new List<DownloadQueueItem>();
        IO.RecoverAtomicFile(queuePath);
        if (!File.Exists(queuePath) &&
            AtomicFileRecovery.HasRecoveryArtifacts(queuePath))
        {
            startupError =
                "Startup failed: interrupted download queue write could not be recovered. " +
                "Recovery files were retained.";
            Print(LogType.Error, startupError);
            queueLoaded = true;
            return;
        }
        if (!File.Exists(queuePath))
        {
            if (!PersistQueue())
                startupError = "Startup failed: the download queue could not be created.";
            queueLoaded = true;
            return;
        }

        try
        {
            DownloadQueue queue =
                JsonConvert.DeserializeObject<DownloadQueue>(File.ReadAllText(queuePath));
            if (queue == null || queue.schema_version != 1 || queue.items == null)
                throw new InvalidDataException("The queue schema is missing or unsupported.");

            bool changed = false;
            List<DownloadQueueItem> invalidItems = new List<DownloadQueueItem>();
            foreach (DownloadQueueItem item in queue.items)
            {
                if (item == null)
                {
                    changed = true;
                    continue;
                }
                if (String.IsNullOrEmpty(item.id))
                {
                    item.id = CreateInvalidQueueId(item.url, item.content);
                    changed = true;
                }

                Uri validatedUri;
                PackagePaths validatedPaths;
                ulong validatedSize;
                string validatedSha256;
                string validationError;
                if (!TryValidateQueueItem(item, false, out validatedUri, out validatedPaths,
                    out validatedSize, out validatedSha256, out validationError))
                {
                    MarkQueueItemBlocked(item, validationError);
                    Print(LogType.Error, validationError);
                    invalidItems.Add(item);
                    changed = true;
                    continue;
                }

                string normalizedTitleId =
                    DownloadPolicy.NormalizeTitleId(item.content.title_id);
                string normalizedSize = validatedSize.ToString(CultureInfo.InvariantCulture);
                if (!String.Equals(item.url, validatedUri.AbsoluteUri, StringComparison.Ordinal) ||
                    !String.Equals(item.content.title_id, normalizedTitleId,
                        StringComparison.Ordinal) ||
                    !String.Equals(item.content.size, normalizedSize, StringComparison.Ordinal) ||
                    !String.Equals(item.content.sha256, validatedSha256,
                        StringComparison.Ordinal) ||
                    !String.Equals(item.final_path, validatedPaths.FinalPath,
                        isConsole ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                    changed = true;
                item.url = validatedUri.AbsoluteUri;
                item.content.title_id = normalizedTitleId;
                item.content.size = normalizedSize;
                item.content.sha256 = validatedSha256;
                item.final_path = validatedPaths.FinalPath;

                if (IsUnavailableMaintainedUpdate(item))
                {
                    MarkQueueItemBlocked(item, UnavailableMaintainedUpdateError);
                    changed = true;
                }

                DownloadQueueItem duplicate = queueList.FirstOrDefault(existing =>
                    existing != null && !String.IsNullOrEmpty(item.id) &&
                    String.Equals(existing.id, item.id, StringComparison.Ordinal));
                if (duplicate != null)
                {
                    if (!QueueSnapshotsMatch(duplicate, item))
                    {
                        MarkQueueItemBlocked(duplicate, QueueSnapshotConflictError);
                        Print(LogType.Error, QueueSnapshotConflictError);
                    }
                    changed = true;
                    continue;
                }

                DownloadQueueItem pathCollision = FindPathCollision(item);
                if (pathCollision != null)
                {
                    changed = true;
                    string existingHash;
                    string candidateHash;
                    ulong existingSize;
                    ulong candidateSize;
                    bool sameContent =
                        TryGetNormalizedHash(pathCollision, out existingHash) &&
                        TryGetNormalizedHash(item, out candidateHash) &&
                        String.Equals(existingHash, candidateHash, StringComparison.Ordinal) &&
                        TryGetExpectedSize(pathCollision, out existingSize) &&
                        TryGetExpectedSize(item, out candidateSize) &&
                        existingSize == candidateSize;
                    if (!sameContent)
                        LogPathCollision(pathCollision, item);
                    continue;
                }

                queueList.Add(item);
            }

            foreach (DownloadQueueItem invalidItem in invalidItems)
            {
                if (!queueList.Any(existing => existing != null &&
                    String.Equals(existing.id, invalidItem.id, StringComparison.Ordinal)))
                    queueList.Add(invalidItem);
            }

            foreach (DownloadQueueItem item in queueList.ToList())
            {
                QueueStartupDisposition disposition =
                    QueueStatePolicy.GetStartupDisposition(item.state);
                if (disposition == QueueStartupDisposition.RemoveCompleted)
                {
                    queueList.Remove(item);
                    changed = true;
                    continue;
                }

                if (disposition == QueueStartupDisposition.BlockUnknownInstall)
                {
                    MarkQueueItemBlocked(item,
                        "The previous session ended while installation status was unknown. " +
                        "Verify the package on the console, then remove and re-add this item if needed.");
                    Print(LogType.Warning, item.last_error);
                    changed = true;
                    continue;
                }
                if (disposition == QueueStartupDisposition.BlockUnknownSubmission)
                {
                    MarkQueueItemBlocked(item,
                        "The previous session ended while background submission status was unknown. " +
                        "Check PS4 notifications/downloads, then remove and re-add this item if needed.");
                    Print(LogType.Warning, item.last_error);
                    changed = true;
                    continue;
                }

                if (item.attempts < 0)
                {
                    item.attempts = 0;
                    string repairedState =
                        QueueStatePolicy.RepairInvalidAttemptState(item.state);
                    if (!String.Equals(item.state, repairedState, StringComparison.Ordinal))
                    {
                        item.state = repairedState;
                        item.last_error = "Queue attempt count was invalid.";
                    }
                    Print(LogType.Warning,
                        $"Clamped invalid queue attempt count for '{item.id}'.");
                    changed = true;
                }

                if (disposition == QueueStartupDisposition.ResetToQueued)
                {
                    item.state = "queued";
                    item.last_error =
                        "The previous session ended before this download completed.";
                    changed = true;
                }
                else if (disposition == QueueStartupDisposition.FailInvalid)
                {
                    item.state = "failed";
                    item.last_error = "Queue state was invalid.";
                    changed = true;
                }

                if (IsUnavailableMaintainedUpdate(item))
                {
                    MarkQueueItemBlocked(item, UnavailableMaintainedUpdateError);
                    Print(LogType.Warning, item.last_error);
                    changed = true;
                    continue;
                }

            }

            if (changed && !PersistQueue())
            {
                startupError =
                    "Startup failed: recovered download queue state could not be persisted.";
                Print(LogType.Error, startupError);
            }
            queueLoaded = true;
        }
        catch (Exception ex)
        {
            string invalidPath = queuePath + ".invalid";
            if (File.Exists(invalidPath))
                invalidPath += "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture);

            bool preserved = false;
            try
            {
                File.Move(queuePath, invalidPath);
                preserved = true;
                Print(LogType.Error,
                    $"Malformed download queue preserved as '{invalidPath}': {ex.Message}");
            }
            catch (Exception moveError)
            {
                Print(LogType.Error,
                    $"Malformed download queue could not be preserved: {moveError.Message}");
            }

            queueList.Clear();
            if (!preserved)
            {
                startupError =
                    "Startup failed: malformed download queue could not be preserved.";
                Print(LogType.Error, startupError);
                queueLoaded = true;
                return;
            }

            if (!PersistQueue())
            {
                startupError =
                    "Startup failed: an empty replacement download queue could not be persisted.";
                Print(LogType.Error, startupError);
            }
            queueLoaded = true;
        }
    }

    private static bool IsQueueState(string state)
    {
        return QueueStatePolicy.IsKnown(state);
    }

    private static bool IsDispatchableQueueState(string state)
    {
        return QueueStatePolicy.IsDispatchable(state);
    }

    private const string UnavailableMaintainedUpdateError =
        "This package targets the maintained application's title ID, but no immutable update " +
        "channel is configured. The item was blocked to prevent replacement by an unverified build.";
    private const string QueueSnapshotConflictError =
        "A queue item with the same stable identity has different immutable metadata. " +
        "It was blocked; remove it and re-add the corrected entry.";

    private static bool IsUnavailableMaintainedUpdate(DownloadQueueItem item)
    {
        if (MaintainedUpdateChannelAvailable || item == null || item.content == null)
            return false;

        try
        {
            return String.Equals(DownloadPolicy.NormalizeTitleId(item.content.title_id),
                "PKGI13337", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private bool TryBlockUnavailableMaintainedUpdate(DownloadQueueItem item)
    {
        if (!IsUnavailableMaintainedUpdate(item))
            return false;

        MarkQueueItemBlocked(item, UnavailableMaintainedUpdateError);
        Print(LogType.Warning, UnavailableMaintainedUpdateError);
        PersistQueue();
        if (queueCanvas != null && queueCanvas.activeSelf)
            UpdateQueueDisplay();
        return true;
    }

    private static void MarkQueueItemBlocked(DownloadQueueItem item, string error)
    {
        if (item == null) return;
        item.state = "blocked";
        item.last_error = error;
    }


    private DownloadQueueItem CreateQueueSnapshot(KeyValuePair<string, GameContent> contentItem)
    {
        if (contentItem.Value == null) return null;

        GameContent content = new GameContent
        {
            title_id = contentItem.Value.title_id,
            region = contentItem.Value.region,
            name = contentItem.Value.name,
            version = contentItem.Value.version,
            release = contentItem.Value.release,
            size = contentItem.Value.size,
            min_fw = contentItem.Value.min_fw,
            cover_url = contentItem.Value.cover_url,
            sha256 = contentItem.Value.sha256
        };

        string source = contentItem.Key;
        DownloadQueueItem item = new DownloadQueueItem
        {
            url = source,
            content = content,
            state = "queued",
            attempts = 0
        };

        try
        {
            source = URL.ProperFormatUrl(URL.DecryptBase64(contentItem.Key));
            item.url = source;
            Uri uri = DownloadPolicy.NormalizeDownloadUrl(source);
            DownloadPolicy.ParseExpectedSize(content.size);
            content.sha256 = DownloadPolicy.NormalizeSha256(content.sha256);
            PackagePaths paths = DownloadPolicy.BuildPackagePaths(
                downloadPath, isConsole, content.title_id, content.version, content.name);
            item.url = uri.AbsoluteUri;
            item.id = DownloadPolicy.CreateStableQueueId(
                item.url, content.title_id, content.version);
            item.final_path = paths.FinalPath;
            if (IsUnavailableMaintainedUpdate(item))
                MarkQueueItemBlocked(item, UnavailableMaintainedUpdateError);
        }
        catch (Exception ex)
        {
            item.id = CreateInvalidQueueId(source, content);
            item.state = "failed";
            item.last_error = $"Invalid queue metadata: {ex.Message}";
            Print(LogType.Error, item.last_error);
        }

        return item;
    }

    private static string CreateInvalidQueueId(string source, GameContent content)
    {
        string material = (source ?? string.Empty) + "\n" +
            (content == null ? string.Empty : content.title_id ?? string.Empty) + "\n" +
            (content == null ? string.Empty : content.version ?? string.Empty) + "\n" +
            (content == null ? string.Empty : content.name ?? string.Empty) + "\n" +
            (content == null ? string.Empty : content.size ?? string.Empty) + "\n" +
            (content == null ? string.Empty : content.sha256 ?? string.Empty);
        string failureUrl = "https://invalid.invalid/?metadata=" + Uri.EscapeDataString(material);
        return DownloadPolicy.CreateStableQueueId(failureUrl, "INVALID", "INVALID");
    }

    private DownloadQueueItem AddToQueueUI(KeyValuePair<string, GameContent> contentItem)
    {
        return AddQueueItem(CreateQueueSnapshot(contentItem));
    }

    private DownloadQueueItem AddQueueItem(DownloadQueueItem candidate)
    {
        if (candidate == null) return null;

        DownloadQueueItem existing = queueList.FirstOrDefault(item => item != null &&
            String.Equals(item.id, candidate.id, StringComparison.Ordinal));
        if (existing != null)
        {
            if (QueueSnapshotsMatch(existing, candidate))
                return existing;

            MarkQueueItemBlocked(existing, QueueSnapshotConflictError);
            Print(LogType.Error, QueueSnapshotConflictError);
            PersistQueue();
            if (queueCanvas != null && queueCanvas.activeSelf)
                UpdateQueueDisplay();
            return null;
        }

        DownloadQueueItem pathCollision = FindPathCollision(candidate);
        if (pathCollision != null)
        {
            string existingHash;
            string candidateHash;
            ulong existingSize;
            ulong candidateSize;
            bool sameContent =
                TryGetNormalizedHash(pathCollision, out existingHash) &&
                TryGetNormalizedHash(candidate, out candidateHash) &&
                String.Equals(existingHash, candidateHash, StringComparison.Ordinal) &&
                TryGetExpectedSize(pathCollision, out existingSize) &&
                TryGetExpectedSize(candidate, out candidateSize) &&
                existingSize == candidateSize;
            if (sameContent)
                return pathCollision;

            LogPathCollision(pathCollision, candidate);
            return null;
        }

        queueList.Add(candidate);
        if (!PersistQueue())
        {
            queueList.Remove(candidate);
            Print(LogType.Error,
                "Queue item was not added because its metadata could not be persisted.");
            if (queueCanvas != null && queueCanvas.activeSelf)
                UpdateQueueDisplay();
            return null;
        }
        if (queueCanvas != null && queueCanvas.activeSelf)
            UpdateQueueDisplay();
        return candidate;
    }

    private bool QueueSnapshotsMatch(DownloadQueueItem existing, DownloadQueueItem candidate)
    {
        if (existing == null || candidate == null ||
            existing.content == null || candidate.content == null ||
            !String.Equals(existing.id, candidate.id, StringComparison.Ordinal) ||
            !String.Equals(existing.content.name, candidate.content.name,
                StringComparison.Ordinal))
            return false;

        try
        {
            Uri existingUri = DownloadPolicy.NormalizeDownloadUrl(existing.url);
            Uri candidateUri = DownloadPolicy.NormalizeDownloadUrl(candidate.url);
            if (!String.Equals(existingUri.AbsoluteUri, candidateUri.AbsoluteUri,
                    StringComparison.Ordinal) ||
                !String.Equals(
                    DownloadPolicy.NormalizeTitleId(existing.content.title_id),
                    DownloadPolicy.NormalizeTitleId(candidate.content.title_id),
                    StringComparison.Ordinal))
                return false;

            string existingId = DownloadPolicy.CreateStableQueueId(existingUri.AbsoluteUri,
                existing.content.title_id, existing.content.version);
            string candidateId = DownloadPolicy.CreateStableQueueId(candidateUri.AbsoluteUri,
                candidate.content.title_id, candidate.content.version);
            if (!String.Equals(existing.id, existingId, StringComparison.Ordinal) ||
                !String.Equals(candidate.id, candidateId, StringComparison.Ordinal))
                return false;
        }
        catch
        {
            return false;
        }

        ulong existingSize;
        ulong candidateSize;
        if (!TryGetExpectedSize(existing, out existingSize) ||
            !TryGetExpectedSize(candidate, out candidateSize) ||
            existingSize != candidateSize)
            return false;

        string existingHash;
        string candidateHash;
        if (!TryGetOptionalNormalizedHash(existing, out existingHash) ||
            !TryGetOptionalNormalizedHash(candidate, out candidateHash) ||
            !String.Equals(existingHash, candidateHash, StringComparison.Ordinal))
            return false;

        StringComparison pathComparison = isConsole
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return !String.IsNullOrEmpty(existing.final_path) &&
               !String.IsNullOrEmpty(candidate.final_path) &&
               String.Equals(existing.final_path, candidate.final_path, pathComparison);
    }

    private DownloadQueueItem FindPathCollision(DownloadQueueItem candidate)
    {
        if (candidate == null || String.IsNullOrEmpty(candidate.final_path)) return null;
        StringComparison comparison = isConsole
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return queueList.FirstOrDefault(item => item != null &&
            !String.IsNullOrEmpty(item.final_path) &&
            String.Equals(item.final_path, candidate.final_path, comparison));
    }

    private static bool TryGetNormalizedHash(DownloadQueueItem item, out string hash)
    {
        hash = null;
        if (item == null || item.content == null ||
            String.IsNullOrWhiteSpace(item.content.sha256))
            return false;
        try
        {
            hash = DownloadPolicy.NormalizeSha256(item.content.sha256);
            return hash != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetOptionalNormalizedHash(
        DownloadQueueItem item, out string hash)
    {
        hash = null;
        if (item == null || item.content == null)
            return false;

        try
        {
            hash = DownloadPolicy.NormalizeSha256(item.content.sha256);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetExpectedSize(DownloadQueueItem item, out ulong size)
    {
        size = 0;
        if (item == null || item.content == null) return false;
        try
        {
            size = DownloadPolicy.ParseExpectedSize(item.content.size);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LogPathCollision(
        DownloadQueueItem existing, DownloadQueueItem candidate)
    {
        string existingName = existing != null && existing.content != null
            ? existing.content.name : "existing queue item";
        string path = candidate == null ? null : candidate.final_path;
        Print(LogType.Error,
            $"Destination collision at '{path}' with '{existingName}'. " +
            "Remove the existing queue item or correct the package metadata before retrying.");
    }

    private bool TryPrepareQueueItem(DownloadQueueItem item, out Uri uri,
        out PackagePaths paths, out ulong expectedSize, out string expectedSha256,
        out string error)
    {
        return TryValidateQueueItem(item, true, out uri, out paths, out expectedSize,
            out expectedSha256, out error);
    }

    private bool TryValidateQueueItem(DownloadQueueItem item,
        bool rejectUnavailableMaintainedUpdate, out Uri uri, out PackagePaths paths,
        out ulong expectedSize, out string expectedSha256, out string error)
    {
        uri = null;
        paths = null;
        expectedSize = 0;
        expectedSha256 = null;
        error = null;

        try
        {
            if (item == null || item.content == null)
                throw new ArgumentException("Queue content snapshot is missing.");
            if (rejectUnavailableMaintainedUpdate && IsUnavailableMaintainedUpdate(item))
                throw new InvalidOperationException(UnavailableMaintainedUpdateError);

            uri = DownloadPolicy.NormalizeDownloadUrl(item.url);
            expectedSize = DownloadPolicy.ParseExpectedSize(item.content.size);
            expectedSha256 = DownloadPolicy.NormalizeSha256(item.content.sha256);
            paths = DownloadPolicy.BuildPackagePaths(downloadPath, isConsole,
                item.content.title_id, item.content.version, item.content.name);
            string expectedId = DownloadPolicy.CreateStableQueueId(
                uri.AbsoluteUri, item.content.title_id, item.content.version);
            if (!String.Equals(item.id, expectedId, StringComparison.Ordinal))
                throw new ArgumentException("Queue identity does not match its metadata snapshot.");

            StringComparison comparison = isConsole
                ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (!String.Equals(item.final_path, paths.FinalPath, comparison))
                throw new ArgumentException(
                    "Stored package path is outside the selected download destination.");

            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid queue item: {ex.Message}";
            return false;
        }
    }

    private bool SetQueueState(DownloadQueueItem item, string state, string error)
    {
        if (item == null) return false;
        item.state = IsQueueState(state) ? state : "failed";
        item.last_error = error;
        bool persisted = PersistQueue();
        if (queueCanvas != null && queueCanvas.activeSelf)
            UpdateQueueDisplay();
        return persisted;
    }

    private void FailQueueItem(DownloadQueueItem item, string error)
    {
        Print(LogType.Error, error);
        SetQueueState(item, "failed", error);
    }

    private void RemoveQueueItem(DownloadQueueItem item)
    {
        if (item == null) return;
        queueList.Remove(item);
        ClampQueueSelection();
        PersistQueue();
    }

    private void ClampQueueSelection()
    {
        if (queueList.Count == 0)
        {
            queueCurrentPage = 0;
            queueHighlightIndex = 0;
            return;
        }

        int totalPages = Mathf.CeilToInt((float)queueList.Count / queueItemsPerPage);
        queueCurrentPage = Mathf.Clamp(queueCurrentPage, 0, totalPages - 1);
        int pageItems = Mathf.Min(queueItemsPerPage,
            queueList.Count - queueCurrentPage * queueItemsPerPage);
        queueHighlightIndex = Mathf.Clamp(queueHighlightIndex, 0, pageItems - 1);
    }

    public void ClearQueueUI()
    {
        queueList.Clear();
        queueCurrentPage = 0;
        queueHighlightIndex = 0;
        PersistQueue();
        UpdateQueueDisplay();
    }

    private void UpdateQueueDisplay()
    {
        if (queued_pkgs == null) return;

        for (int i = queued_pkgs.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = queued_pkgs.transform.GetChild(i);
            if (child.name.StartsWith("PKG"))
                DestroyImmediate(child.gameObject);
        }

        ClampQueueSelection();
        int startIndex = queueCurrentPage * queueItemsPerPage;
        int endIndex = Mathf.Min(startIndex + queueItemsPerPage, queueList.Count);
        for (int i = startIndex; i < endIndex; i++)
            CreateQueueItem(queueList[i], i - startIndex);
    }

    private void CreateQueueItem(DownloadQueueItem item, int displayIndex)
    {
        if (queued_pkgs == null || item == null) return;

        Background background = FindObjectOfType<Background>();
        if (background == null || background.prefab == null) return;

        GameObject queueItem = Instantiate(background.prefab, queued_pkgs.transform);
        queueItem.name = $"PKG{displayIndex + 1}";
        RectTransform rectTransform = queueItem.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            const float spacing = 32f;
            const float offset = -12f;
            Vector2 startPosition = new Vector2(0, queueItemsPerPage * spacing / 2);
            rectTransform.anchoredPosition =
                startPosition - new Vector2(0, displayIndex * spacing - offset);
        }

        GameContent content = item.content;
        Transform titleIdTransform = queueItem.transform.Find("TitleID");
        Transform regionTransform = queueItem.transform.Find("Region");
        Transform titleTransform = queueItem.transform.Find("Title");
        Transform sizeTransform = queueItem.transform.Find("Size");
        Transform downloadedTransform = queueItem.transform.Find("Downloaded");

        Text titleIdText = titleIdTransform == null ? null : titleIdTransform.GetComponent<Text>();
        Text regionText = regionTransform == null ? null : regionTransform.GetComponent<Text>();
        Text titleText = titleTransform == null ? null : titleTransform.GetComponent<Text>();
        Text sizeText = sizeTransform == null ? null : sizeTransform.GetComponent<Text>();
        Text stateText = downloadedTransform == null ? null : downloadedTransform.GetComponent<Text>();

        UI.ChangeText(titleIdText, content == null || String.IsNullOrEmpty(content.title_id)
            ? "INVALID" : content.title_id);
        UI.ChangeText(regionText, content == null ? "???" : content.region ?? "???");
        UI.ChangeText(titleText, content == null || String.IsNullOrEmpty(content.name)
            ? "Invalid queue item" : content.name);
        if (titleText != null)
            UI.SetFontByText(ref titleText);
        string formattedSize = "invalid";
        if (content != null)
        {
            try
            {
                formattedSize = IO.FormatByteString(
                    DownloadPolicy.ParseExpectedSize(content.size));
            }
            catch (ArgumentException) { }
        }
        UI.ChangeText(sizeText, formattedSize);
        if (sizeText != null)
            sizeText.gameObject.SetActive(true);

        string state = IsQueueState(item.state) ? item.state : "failed";
        UI.ChangeText(stateText, state);
        if (stateText != null)
        {
            stateText.gameObject.SetActive(true);
            stateText.color = state == "failed" || state == "blocked" ? redish :
                state == "submitted" || state == "completed" ? blueish : yellowish;
        }

        Color itemColor = displayIndex == queueHighlightIndex ? blueish : Color.white;
        if (titleIdText != null) titleIdText.color = itemColor;
        if (regionText != null) regionText.color = itemColor;
        if (titleText != null) titleText.color = itemColor;
        if (sizeText != null) sizeText.color = itemColor;
    }
    #endregion

    private IEnumerator Start()
    {
        string previousText = string.Empty;

        StartCoroutine(InitializeCoroutine());

        while (!fullyInitialized || !queueLoaded)
            yield return null;
        if (!String.IsNullOrEmpty(startupError))
            yield break;

        while (true)
        {
            string currentText = $"{contentOptions[contentFilter]}";

            HandleUserInput();

            if (cooldownTimer > 0)
                cooldownTimer = Mathf.Clamp(cooldownTimer - Time.deltaTime, 0, inputCooldown);

            var textComponent =
                UI.FindInactiveObjectsByPath("Canvas/Main/ContentSort")?.GetComponent<Text>();

            if (currentText != previousText)
            {
                textComponent.text = $"{currentText} Content";
                previousText = currentText;
                reloadTriggered = true;
            }

            UIManagement.HighlightCurrentPkg();

            // Update queue scrollbar if queue canvas is active
            if (queueCanvas.activeSelf && queue_scrollbar != null)
            {
                UI.UpdateScrollbar();
            }

            yield return null;
        }
    }

}