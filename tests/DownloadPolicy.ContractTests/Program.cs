using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "fpkgi-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            TestPaths(temp);
            TestUrlsAndIds();
            TestMetadata();
            TestVerification(temp);
            TestProgress();
            TestRetry();
            TestQueueStates();
            TestAtomicRecovery(temp);
            Console.WriteLine("DownloadPolicy contract tests passed: " + assertions);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    private static void TestPaths(string temp)
    {
        Equal("/user/data/", DownloadPolicy.NormalizeDownloadRoot("/user/data/", true), "user root");
        Equal("/user/data/FPKGi/Downloads/", DownloadPolicy.NormalizeDownloadRoot("/user/data/FPKGi/Downloads", true), "user subdirectory");
        Equal("/mnt/usb0/", DownloadPolicy.NormalizeDownloadRoot("/mnt/usb0", true), "usb root");
        Equal("/mnt/ext12/", DownloadPolicy.NormalizeDownloadRoot("/mnt/ext12/", true), "ext root");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot("/mnt/usb", true); }, "unnumbered mount");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot("/mnt/hdd0", true); }, "disallowed mount");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot("/mnt/usb0/../ext0", true); }, "root traversal");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot("//mnt/usb0", true); }, "root confusion");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot("/mnt\\usb0", true); }, "root separator");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot("/mnt/usb0\n", true); }, "root control");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot("", true); }, "empty root");
        Throws(delegate { DownloadPolicy.NormalizeDownloadRoot(Path.GetPathRoot(temp), false); }, "host filesystem root");

        Equal("CUSA00001", DownloadPolicy.NormalizeTitleId("cusa00001"), "title normalization");
        Throws(delegate { DownloadPolicy.NormalizeTitleId(""); }, "empty title");
        Throws(delegate { DownloadPolicy.NormalizeTitleId("../CUSA"); }, "title traversal");
        Throws(delegate { DownloadPolicy.NormalizeTitleId("a/b"); }, "title separator");
        Throws(delegate { DownloadPolicy.NormalizeTitleId("a\u0001b"); }, "title control");
        Throws(delegate { DownloadPolicy.NormalizeTitleId(new string('a', 65)); }, "overlong title");

        Equal("Pokémon-Deluxe", DownloadPolicy.SanitizeName(" Pokémon/Deluxe ", 100), "unicode name");
        Throws(delegate { DownloadPolicy.SanitizeName("///", 20); }, "separator-only name");
        Throws(delegate { DownloadPolicy.SanitizeName("", 20); }, "empty name");
        string longName = DownloadPolicy.SanitizeName(new string('é', 200), 31);
        True(Encoding.UTF8.GetByteCount(longName) <= 31, "UTF8 cap");
        PackagePaths paths = DownloadPolicy.BuildPackagePaths(temp, false, "CUSA00001", "01.00", new string('界', 200));
        True(paths.FinalPath.StartsWith(paths.Root, StringComparison.OrdinalIgnoreCase), "path inside root");
        True(paths.FinalPath.EndsWith(".pkg", StringComparison.Ordinal), "pkg extension");
        Equal(paths.FinalPath + ".resume", paths.ResumePath, "resume extension");
        True(Encoding.UTF8.GetByteCount(Path.GetFileName(paths.ResumePath)) <= DownloadPolicy.MaximumFileNameUtf8Bytes, "full filename cap");
        PackagePaths noVersion = DownloadPolicy.BuildPackagePaths(temp, false, "CUSA00002", null, "Probe");
        True(noVersion.FinalPath.Contains("_unknown_"), "missing version fallback");
    }

    private static void TestUrlsAndIds()
    {
        Uri url = DownloadPolicy.NormalizeDownloadUrl("HTTPS://Example.COM:443/a%20b?q=1");
        Equal("https://example.com/a%20b?q=1", url.AbsoluteUri, "normalized URL");
        Throws(delegate { DownloadPolicy.NormalizeDownloadUrl("ftp://example.com/a"); }, "scheme");
        Throws(delegate { DownloadPolicy.NormalizeDownloadUrl("/relative"); }, "relative URL");
        Throws(delegate { DownloadPolicy.NormalizeDownloadUrl("https://u:p@example.com/a"); }, "userinfo");
        Throws(delegate { DownloadPolicy.NormalizeDownloadUrl("https://example.com/a#x"); }, "fragment");
        string first = DownloadPolicy.CreateStableQueueId("https://EXAMPLE.com:443/a", "CUSA00001", "01.00");
        string second = DownloadPolicy.CreateStableQueueId("https://example.com/a", "CUSA00001", "01.00");
        Equal(first, second, "stable queue ID");
        Equal(64, first.Length, "queue ID length");
    }

    private static void TestMetadata()
    {
        Equal((ulong)1, DownloadPolicy.ParseExpectedSize("1"), "minimum size");
        Equal(UInt64.MaxValue, DownloadPolicy.ParseExpectedSize(UInt64.MaxValue.ToString()), "maximum size");
        Throws(delegate { DownloadPolicy.ParseExpectedSize("0"); }, "zero size");
        Throws(delegate { DownloadPolicy.ParseExpectedSize("-1"); }, "negative size");
        Throws(delegate { DownloadPolicy.ParseExpectedSize(" 1"); }, "size whitespace");
        Equal(null, DownloadPolicy.NormalizeSha256(null), "absent hash");
        Equal(new string('a', 64), DownloadPolicy.NormalizeSha256(new string('A', 64)), "hash lowercase");
        Throws(delegate { DownloadPolicy.NormalizeSha256(new string('a', 63)); }, "hash length");
        Throws(delegate { DownloadPolicy.NormalizeSha256(new string('z', 64)); }, "hash syntax");
    }

    private static void TestVerification(string temp)
    {
        string path = Path.Combine(temp, "verify.pkg");
        byte[] bytes = { 0x7F, 0x43, 0x4E, 0x54, 1, 2, 3, 4 };
        File.WriteAllBytes(path, bytes);
        string hash;
        using (SHA256 sha = SHA256.Create()) hash = Hex(sha.ComputeHash(bytes));
        True(DownloadPolicy.VerifyPackage(path, (ulong)bytes.Length, null).IsValid, "valid no hash");
        True(DownloadPolicy.VerifyPackage(path, (ulong)bytes.Length, hash).IsValid, "valid hash");
        True(!DownloadPolicy.VerifyPackage(path + ".missing", 8, null).IsValid, "missing file");
        True(!DownloadPolicy.VerifyPackage(path, 9, null).IsValid, "bad size");
        bytes[0] = 0;
        File.WriteAllBytes(path, bytes);
        True(!DownloadPolicy.VerifyPackage(path, 8, null).IsValid, "bad magic");
        bytes[0] = 0x7F; bytes[7] ^= 1;
        File.WriteAllBytes(path, bytes);
        True(!DownloadPolicy.VerifyPackage(path, 8, hash).IsValid, "tampered hash");
    }

    private static void TestProgress()
    {
        DownloadProgress unknown = DownloadPolicy.ReduceProgress(2, 5, 0, 0);
        True(!unknown.HasKnownTotal && unknown.Percent == 0 && unknown.RemainingBytes == 0, "unknown total");
        Equal((ulong)0, unknown.SpeedBytesPerSecond, "zero speed");
        DownloadProgress overrun = DownloadPolicy.ReduceProgress(90, 120, 100, 5);
        Equal(100.0, overrun.Percent, "overrun clamp");
        Equal((ulong)0, overrun.RemainingBytes, "remaining saturation");
        DownloadProgress reset = DownloadPolicy.ReduceProgress(1000, 7, 100, 1);
        Equal((ulong)7, reset.DeltaBytes, "counter reset delta");
        Equal((ulong)93, reset.RemainingBytes, "normal remaining");
    }

    private static void TestRetry()
    {
        True(DownloadPolicy.ShouldRetry(DownloadFailureKind.Transport, 0), "transport retry");
        int[] retry = { 408, 425, 429, 500, 502, 503, 504 };
        for (int i = 0; i < retry.Length; i++) True(DownloadPolicy.ShouldRetry(DownloadFailureKind.Http, retry[i]), "HTTP retry " + retry[i]);
        True(!DownloadPolicy.ShouldRetry(DownloadFailureKind.Cancelled, 503), "cancel no retry");
        True(!DownloadPolicy.ShouldRetry(DownloadFailureKind.Http, 404), "ordinary 4xx");
        Equal(3, DownloadPolicy.GetRetryDelaySeconds(0, 3), "initial delay");
        Equal(24, DownloadPolicy.GetRetryDelaySeconds(3, 3), "exponential delay");
        Equal(DownloadPolicy.MaximumRetryDelaySeconds, DownloadPolicy.GetRetryDelaySeconds(30, 3), "delay cap");
    }

    private static void TestQueueStates()
    {
        string[] known =
        {
            "queued", "downloading", "retrying", "failed", "submitted",
            "submitting", "installing", "completed", "blocked"
        };
        for (int i = 0; i < known.Length; i++)
            True(QueueStatePolicy.IsKnown(known[i]), "known queue state " + known[i]);
        True(!QueueStatePolicy.IsKnown("unknown"), "unknown queue state");

        True(QueueStatePolicy.IsDispatchable("queued"), "queued dispatchable");
        True(QueueStatePolicy.IsDispatchable("failed"), "failed dispatchable");
        for (int i = 0; i < known.Length; i++)
        {
            if (known[i] != "queued" && known[i] != "failed")
                True(!QueueStatePolicy.IsDispatchable(known[i]),
                    "non-dispatchable queue state " + known[i]);
        }

        Equal(QueueStartupDisposition.Keep,
            QueueStatePolicy.GetStartupDisposition("queued"), "keep queued");
        Equal(QueueStartupDisposition.ResetToQueued,
            QueueStatePolicy.GetStartupDisposition("downloading"), "recover downloading");
        Equal(QueueStartupDisposition.ResetToQueued,
            QueueStatePolicy.GetStartupDisposition("retrying"), "recover retrying");
        Equal(QueueStartupDisposition.BlockUnknownInstall,
            QueueStatePolicy.GetStartupDisposition("installing"), "block unknown install");
        Equal(QueueStartupDisposition.BlockUnknownSubmission,
            QueueStatePolicy.GetStartupDisposition("submitting"), "block unknown submission");
        Equal(QueueStartupDisposition.RemoveCompleted,
            QueueStatePolicy.GetStartupDisposition("completed"), "remove completed");
        Equal(QueueStartupDisposition.FailInvalid,
            QueueStatePolicy.GetStartupDisposition("unknown"), "fail invalid state");
        Equal("failed", QueueStatePolicy.RepairInvalidAttemptState("queued"),
            "invalid attempts make queued retry explicit");
        Equal("submitted", QueueStatePolicy.RepairInvalidAttemptState("submitted"),
            "invalid attempts preserve submitted");
        Equal("blocked", QueueStatePolicy.RepairInvalidAttemptState("blocked"),
            "invalid attempts preserve blocked");
        Equal("downloading", QueueStatePolicy.RepairInvalidAttemptState("downloading"),
            "startup recovery still owns downloading");
    }

    private static void TestAtomicRecovery(string temp)
    {
        string path = Path.Combine(temp, "atomic.json");
        True(!AtomicFileRecovery.HasRecoveryArtifacts(path),
            "no atomic recovery artifacts");
        Equal(AtomicRecoverySource.None, AtomicFileRecovery.Recover(path),
            "no atomic recovery source");

        File.WriteAllText(path + ".bak", "backup");
        True(AtomicFileRecovery.HasRecoveryArtifacts(path),
            "backup recovery artifact detected");
        Equal(AtomicRecoverySource.Backup, AtomicFileRecovery.Recover(path),
            "backup recovery source");
        Equal("backup", File.ReadAllText(path), "backup recovery content");

        File.Delete(path);
        File.WriteAllText(path + ".tmp", "temporary");
        Equal(AtomicRecoverySource.Temporary, AtomicFileRecovery.Recover(path),
            "temporary recovery source");
        Equal("temporary", File.ReadAllText(path), "temporary recovery content");

        File.Delete(path);
        File.WriteAllText(path + ".bak", "preferred backup");
        File.WriteAllText(path + ".tmp", "stale temporary");
        Equal(AtomicRecoverySource.Backup, AtomicFileRecovery.Recover(path),
            "backup recovery precedence");
        Equal("preferred backup", File.ReadAllText(path), "backup precedence content");
        Equal("stale temporary", File.ReadAllText(path + ".tmp"),
            "unused temporary retained");

        File.Delete(path);
        File.Delete(path + ".tmp");
        File.WriteAllText(path, "primary");
        File.WriteAllText(path + ".bak", "stale backup");
        Equal(AtomicRecoverySource.Primary, AtomicFileRecovery.Recover(path),
            "existing primary precedence");
        Equal("primary", File.ReadAllText(path), "existing primary content");
        Throws(delegate { AtomicFileRecovery.Recover(""); }, "empty atomic path");
    }

    private static string Hex(byte[] bytes)
    {
        StringBuilder value = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++) value.Append(bytes[i].ToString("x2"));
        return value.ToString();
    }

    private static void True(bool value, string name)
    {
        assertions++;
        if (!value) throw new Exception("Assertion failed: " + name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        assertions++;
        if (!Object.Equals(expected, actual)) throw new Exception("Assertion failed: " + name + "; expected " + expected + ", got " + actual);
    }

    private static void Throws(Action action, string name)
    {
        assertions++;
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Assertion failed: " + name + " did not throw");
    }
}
