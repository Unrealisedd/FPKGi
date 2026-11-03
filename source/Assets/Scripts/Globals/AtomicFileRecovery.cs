using System;
using System.IO;

public enum AtomicRecoverySource
{
    None,
    Primary,
    Backup,
    Temporary
}

public static class AtomicFileRecovery
{
    public static bool HasRecoveryArtifacts(string filePath)
    {
        if (String.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", "filePath");

        string fullPath = Path.GetFullPath(filePath);
        return File.Exists(fullPath + ".bak") || File.Exists(fullPath + ".tmp");
    }

    public static AtomicRecoverySource Recover(string filePath)
    {
        if (String.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", "filePath");

        string fullPath = Path.GetFullPath(filePath);
        if (File.Exists(fullPath))
            return AtomicRecoverySource.Primary;

        string backupPath = fullPath + ".bak";
        if (File.Exists(backupPath))
        {
            File.Move(backupPath, fullPath);
            return AtomicRecoverySource.Backup;
        }

        string temporaryPath = fullPath + ".tmp";
        if (File.Exists(temporaryPath))
        {
            File.Move(temporaryPath, fullPath);
            return AtomicRecoverySource.Temporary;
        }

        return AtomicRecoverySource.None;
    }
}
