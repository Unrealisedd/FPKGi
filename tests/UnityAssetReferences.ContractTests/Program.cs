using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static class Program
{
    private static readonly Regex MetaGuid = new Regex(
        @"^guid:\s*([0-9a-f]{32})\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex SerializedGuid = new Regex(
        @"guid:\s*([0-9a-f]{32})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ExternalUnityGuids = new HashSet<string>(StringComparer.Ordinal)
    {
        "0000000000000000e000000000000000",
        "0000000000000000f000000000000000",
        "f70555f144d8491a825f0804e09c671c",
        "f5f67c52d1564df4a8936ccd202a3bd8"
    };

    private static readonly HashSet<string> SerializedExtensions = new HashSet<string>(
        new[] { ".anim", ".asset", ".controller", ".mat", ".overrideController", ".prefab", ".renderTexture", ".spriteatlas", ".unity" },
        StringComparer.OrdinalIgnoreCase);

    private static int Main()
    {
        try
        {
            string assets = FindAssetsDirectory();
            List<string> errors = new List<string>();
            int assertions = 0;

            foreach (string path in Directory.EnumerateFileSystemEntries(assets, "*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                assertions++;
                if (!File.Exists(path + ".meta"))
                    errors.Add("Missing sidecar metadata: " + Relative(assets, path));
            }

            Dictionary<string, string> owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string metaPath in Directory.EnumerateFiles(assets, "*.meta", SearchOption.AllDirectories))
            {
                string assetPath = metaPath.Substring(0, metaPath.Length - ".meta".Length);
                assertions++;
                if (!File.Exists(assetPath) && !Directory.Exists(assetPath))
                    errors.Add("Orphan metadata: " + Relative(assets, metaPath));

                Match match = MetaGuid.Match(File.ReadAllText(metaPath));
                assertions++;
                if (!match.Success)
                {
                    errors.Add("Metadata has no canonical GUID: " + Relative(assets, metaPath));
                    continue;
                }

                string guid = match.Groups[1].Value;
                string relative = Relative(assets, assetPath);
                string existing;
                assertions++;
                if (owners.TryGetValue(guid, out existing))
                    errors.Add("Duplicate GUID " + guid + ": " + existing + " and " + relative);
                else
                    owners.Add(guid, relative);
            }

            foreach (string path in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
            {
                if (!SerializedExtensions.Contains(Path.GetExtension(path)))
                    continue;

                foreach (Match match in SerializedGuid.Matches(File.ReadAllText(path)))
                {
                    string guid = match.Groups[1].Value;
                    assertions++;
                    if (!owners.ContainsKey(guid) && !ExternalUnityGuids.Contains(guid))
                        errors.Add("Unresolved GUID " + guid + " in " + Relative(assets, path));
                }
            }

            if (errors.Count != 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Distinct().Take(50).ToArray()));

            Console.WriteLine("Unity asset reference contract tests passed: " + assertions +
                              " checks, " + owners.Count + " GUID owners");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static string FindAssetsDirectory()
    {
        string found = FindAssetsDirectoryFrom(new DirectoryInfo(Directory.GetCurrentDirectory()));
        if (found != null)
            return found;

        found = FindAssetsDirectoryFrom(new DirectoryInfo(AppContext.BaseDirectory));
        if (found != null)
            return found;

        throw new DirectoryNotFoundException("Could not locate source/Assets from the working or executable directory.");
    }

    private static string FindAssetsDirectoryFrom(DirectoryInfo directory)
    {
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "source", "Assets");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    private static string Relative(string assets, string path)
    {
        return Path.GetRelativePath(assets, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
