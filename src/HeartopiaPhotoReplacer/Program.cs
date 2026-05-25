using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ImageEncoder = System.Drawing.Imaging.Encoder;

namespace HeartopiaPhotoReplacer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var app = new ReplacerApp();
        if (!app.Initialize())
        {
            return;
        }

        Application.Run(new MainForm(app));
    }
}

internal sealed class ReplacerApp
{
    private static readonly Regex PhotoFileRegex = new(@"^(\d+)_(\d+)_(\d+)\.jpg$", RegexOptions.Compiled);
    private static readonly Regex BackupSnapshotRegex = new(@"^(\d+)_\d{8}_\d{6}$", RegexOptions.Compiled);
    private static readonly Regex RestorePointRegex = new(@"^restorepoint_(\d+)_\d{8}_\d{6}$", RegexOptions.Compiled);
    private const int DefaultBackupRetentionCount = 20;
    private const int DefaultBackupRetentionDays = 30;
    private const int RestorePointRetentionCount = 10;

    private static readonly byte[] Key =
    [
        0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD, 0xEF,
        0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10,
        0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCD, 0xEF,
        0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10
    ];

    private static readonly byte[] Iv =
    [
        0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
        0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10
    ];

    public string ConfigDirectory { get; }

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public string Workspace { get; private set; } = string.Empty;
    public string ImageDirectory { get; private set; } = string.Empty;
    public string BackupDirectory { get; private set; } = string.Empty;
    public string LogDirectory { get; private set; } = string.Empty;
    public string SupportBundleDirectory { get; private set; } = string.Empty;
    public string PhotoDirectory { get; private set; } = DefaultPhotoDirectory();
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    public int BackupRetentionCount { get; private set; } = DefaultBackupRetentionCount;
    public int BackupRetentionDays { get; private set; } = DefaultBackupRetentionDays;
    public string NoticeAcceptedVersion { get; private set; } = string.Empty;

    public ReplacerApp(string? configDirectoryOverride = null)
    {
        ConfigDirectory = Path.GetFullPath(configDirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HeartopiaPhotoReplacer"));
    }

    public bool Initialize()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var config = LoadConfig();
        BackupRetentionCount = NormalizeBackupRetentionCount(config.BackupRetentionCount);
        BackupRetentionDays = NormalizeBackupRetentionDays(config.BackupRetentionDays);
        NoticeAcceptedVersion = config.NoticeAcceptedVersion?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(config.Workspace) && Directory.Exists(config.Workspace))
        {
            SetWorkspace(config.Workspace);
        }
        else
        {
            var selected = PromptForWorkspace();
            if (string.IsNullOrWhiteSpace(selected))
            {
                return false;
            }

            SetWorkspace(selected);
        }

        if (!string.IsNullOrWhiteSpace(config.PhotoDir) && IsPhotoCache(config.PhotoDir))
        {
            PhotoDirectory = Path.GetFullPath(config.PhotoDir);
        }
        else
        {
            var detected = DetectPhotoCache(interactive: true);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                PhotoDirectory = Path.GetFullPath(detected);
            }
        }

        SaveConfig();
        return true;
    }

    public void SetWorkspace(string path)
    {
        Directory.CreateDirectory(path);
        Workspace = Path.GetFullPath(path);
        ImageDirectory = Path.Combine(Workspace, "ReplacementImages");
        BackupDirectory = Path.Combine(Workspace, "Backups");
        LogDirectory = Path.Combine(Workspace, "Logs");
        SupportBundleDirectory = Path.Combine(Workspace, "SupportBundles");
        Directory.CreateDirectory(ImageDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(SupportBundleDirectory);
    }

    public void SetPhotoDirectory(string path)
    {
        PhotoDirectory = Path.GetFullPath(path);
        SaveConfig();
    }

    public void SaveConfig()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var config = new AppConfig
        {
            Workspace = Workspace,
            PhotoDir = PhotoDirectory,
            BackupRetentionCount = BackupRetentionCount,
            BackupRetentionDays = BackupRetentionDays,
            NoticeAcceptedVersion = NoticeAcceptedVersion
        };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public AppConfig LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public string? PromptForWorkspace()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select workspace folder for Heartopia Photo Replacer",
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(Workspace) ? Workspace : AppContext.BaseDirectory
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PromptForPhotoCache()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Heartopia ScreenCapture\\Photo cache folder",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(PhotoDirectory)
                ? PhotoDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PromptForVerifiedPhotoCache()
    {
        while (true)
        {
            var selected = PromptForPhotoCache();
            if (string.IsNullOrWhiteSpace(selected))
            {
                return null;
            }

            if (IsPhotoCache(selected))
            {
                return selected;
            }

            MessageBox.Show(
                "The selected folder does not look like a Heartopia ScreenCapture\\Photo cache.\nChoose the real cache folder to avoid writing into the wrong location.",
                "Invalid photo cache folder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    public string? DetectPhotoCache(bool interactive)
    {
        var caches = FindPhotoCaches().ToArray();
        if (caches.Length == 1)
        {
            return caches[0];
        }

        if (caches.Length > 1)
        {
            return ChooseCache(caches);
        }

        if (!interactive)
        {
            return null;
        }

        MessageBox.Show(
            "No Heartopia photo cache was detected automatically.\nPlease select the ScreenCapture\\Photo folder manually.",
            "Photo cache not found",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return PromptForVerifiedPhotoCache();
    }

    public bool HasAcceptedCurrentNotice()
    {
        return string.Equals(NoticeAcceptedVersion, AppVersion, StringComparison.OrdinalIgnoreCase);
    }

    public void MarkCurrentNoticeAccepted()
    {
        NoticeAcceptedVersion = AppVersion;
        SaveConfig();
    }

    public void UpdateBackupPolicy(int retentionCount, int retentionDays)
    {
        BackupRetentionCount = NormalizeBackupRetentionCount(retentionCount);
        BackupRetentionDays = NormalizeBackupRetentionDays(retentionDays);
        SaveConfig();
    }

    public IEnumerable<string> FindPhotoCaches()
    {
        var localLow = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData",
            "LocalLow");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(localLow))
        {
            yield break;
        }

        var known = Path.Combine(localLow, "xd", "Heartopia", "ScreenCapture", "Photo");
        if (IsPhotoCache(known) && seen.Add(Path.GetFullPath(known)))
        {
            yield return Path.GetFullPath(known);
        }

        foreach (var dir in EnumerateDirectoriesSafe(localLow))
        {
            if (!dir.EndsWith(Path.Combine("ScreenCapture", "Photo"), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsPhotoCache(dir))
            {
                var full = Path.GetFullPath(dir);
                if (seen.Add(full))
                {
                    yield return full;
                }
            }
        }
    }

    public bool IsPhotoCache(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(path)
                .Select(Path.GetFileName)
                .Any(name => name is not null &&
                             Regex.IsMatch(name, @"^\d+_(256_144|512_288|1564_880|1920_1080|400_400)\.jpg$"));
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetReplacementImages()
    {
        if (!Directory.Exists(ImageDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(ImageDirectory)
            .Where(file =>
            {
                var ext = Path.GetExtension(file);
                return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTime)
            .ToArray();
    }

    public IReadOnlyList<PhotoGroup> GetPhotoGroups()
    {
        if (!Directory.Exists(PhotoDirectory))
        {
            return [];
        }

        var items = new List<PhotoFile>();
        foreach (var file in Directory.EnumerateFiles(PhotoDirectory, "*.jpg"))
        {
            var name = Path.GetFileName(file);
            var match = PhotoFileRegex.Match(name);
            if (!match.Success)
            {
                continue;
            }

            items.Add(new PhotoFile(
                match.Groups[1].Value,
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value),
                file,
                File.GetLastWriteTime(file)));
        }

        return items
            .GroupBy(item => item.Id)
            .Select(group => new PhotoGroup(
                group.Key,
                group.Count(),
                group.Max(item => item.LastWriteTime),
                string.Join(", ", group.OrderBy(item => item.Width).ThenBy(item => item.Height)
                    .Select(item => $"{item.Width}x{item.Height}"))))
            .OrderByDescending(group => group.LastWriteTime)
            .ToArray();
    }

    public IReadOnlyList<PhotoFile> GetFilesForPhotoId(string id)
    {
        if (!Directory.Exists(PhotoDirectory))
        {
            return [];
        }

        var files = new List<PhotoFile>();
        foreach (var file in Directory.EnumerateFiles(PhotoDirectory, $"{id}_*.jpg"))
        {
            var name = Path.GetFileName(file);
            var match = PhotoFileRegex.Match(name);
            if (!match.Success || match.Groups[1].Value != id)
            {
                continue;
            }

            files.Add(new PhotoFile(
                id,
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value),
                file,
                File.GetLastWriteTime(file)));
        }

        return files.OrderBy(file => file.Width).ThenBy(file => file.Height).ToArray();
    }

    public IReadOnlyList<BackupSnapshot> GetBackupsForPhotoId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !Directory.Exists(BackupDirectory))
        {
            return [];
        }

        return Directory.GetDirectories(BackupDirectory, $"{id}_*")
            .Select(path => new DirectoryInfo(path))
            .Where(dir => !dir.Name.StartsWith("restorepoint_", StringComparison.OrdinalIgnoreCase))
            .Where(dir => dir.EnumerateFiles("*.jpg").Any(file =>
            {
                var match = PhotoFileRegex.Match(file.Name);
                return match.Success && match.Groups[1].Value == id;
            }))
            .OrderByDescending(dir => dir.Name)
            .Select(dir => new BackupSnapshot(dir.Name, dir.FullName, dir.LastWriteTime))
            .ToArray();
    }

    public BackupCleanupResult CleanupBackups(Action<string>? log = null)
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return new BackupCleanupResult(0, 0, 0, "Backup folder not found.");
        }

        var deletedDirectoryCount = 0;
        var deletedFileCount = 0;
        long reclaimedBytes = 0;
        var cutoff = BackupRetentionDays > 0
            ? DateTime.Now.AddDays(-BackupRetentionDays)
            : (DateTime?)null;

        var root = new DirectoryInfo(BackupDirectory);
        var directories = root.EnumerateDirectories().ToArray();
        var toDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var snapshotsByPhotoId = directories
            .Select(dir => new { Directory = dir, Match = BackupSnapshotRegex.Match(dir.Name) })
            .Where(item => item.Match.Success)
            .GroupBy(item => item.Match.Groups[1].Value, StringComparer.OrdinalIgnoreCase);

        foreach (var group in snapshotsByPhotoId)
        {
            var ordered = group
                .Select(item => item.Directory)
                .OrderByDescending(dir => dir.LastWriteTime)
                .ToArray();

            if (BackupRetentionCount > 0)
            {
                foreach (var extra in ordered.Skip(BackupRetentionCount))
                {
                    toDelete.Add(extra.FullName);
                }
            }

            if (cutoff is not null)
            {
                foreach (var expired in ordered.Where(dir => dir.LastWriteTime < cutoff.Value))
                {
                    toDelete.Add(expired.FullName);
                }
            }
        }

        var restorePoints = directories
            .Where(dir => RestorePointRegex.IsMatch(dir.Name))
            .OrderByDescending(dir => dir.LastWriteTime)
            .ToArray();

        foreach (var extra in restorePoints.Skip(RestorePointRetentionCount))
        {
            toDelete.Add(extra.FullName);
        }

        if (cutoff is not null)
        {
            foreach (var expired in restorePoints.Where(dir => dir.LastWriteTime < cutoff.Value))
            {
                toDelete.Add(expired.FullName);
            }
        }

        foreach (var path in toDelete)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                if (!dir.Exists)
                {
                    continue;
                }

                var files = dir.EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
                reclaimedBytes += files.Sum(file => file.Length);
                deletedFileCount += files.Length;
                dir.Delete(recursive: true);
                deletedDirectoryCount += 1;
                log?.Invoke($"Deleted backup snapshot: {dir.Name}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Backup cleanup warning for {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        var summary = deletedDirectoryCount == 0
            ? $"No backup cleanup required. Policy: keep newest {BackupRetentionCount} snapshots per photo ID and {BackupRetentionDays} days of history."
            : $"Deleted {deletedDirectoryCount} backup folders and {deletedFileCount} files, reclaimed {FormatBytes(reclaimedBytes)}.";

        return new BackupCleanupResult(deletedDirectoryCount, deletedFileCount, reclaimedBytes, summary);
    }

    public CacheCompatibilityResult ProbeCurrentCacheCompatibility()
    {
        if (!IsPhotoCache(PhotoDirectory))
        {
            return new CacheCompatibilityResult(
                false,
                "The selected folder is not a verified Heartopia Photo cache. Select the real ScreenCapture\\Photo cache first.",
                0);
        }

        var candidates = Directory.EnumerateFiles(PhotoDirectory, "*.jpg")
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name is not null && PhotoFileRegex.IsMatch(name);
            })
            .Take(5)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new CacheCompatibilityResult(
                false,
                "No photo cache samples were found yet. Take a new in-game photo first, then run the compatibility probe again.",
                0);
        }

        foreach (var candidate in candidates)
        {
            try
            {
                ValidateEncryptedPhotoFile(candidate);
            }
            catch (Exception ex)
            {
                return new CacheCompatibilityResult(
                    false,
                    $"Cache compatibility failed on {Path.GetFileName(candidate)}. The game cache format or encryption may have changed.\nReason: {ex.Message}",
                    0);
            }
        }

        return new CacheCompatibilityResult(
            true,
            $"Compatibility probe passed. Checked {candidates.Length} encrypted cache files in the selected Heartopia Photo cache.",
            candidates.Length);
    }

    public string ImportImage(string sourcePath)
    {
        Directory.CreateDirectory(ImageDirectory);
        var dest = Path.Combine(ImageDirectory, Path.GetFileName(sourcePath));
        var sourceFull = Path.GetFullPath(sourcePath);

        if (File.Exists(dest) && !Path.GetFullPath(dest).Equals(sourceFull, StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var ext = Path.GetExtension(sourcePath);
            dest = Path.Combine(ImageDirectory, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
        }

        if (!File.Exists(dest) || !Path.GetFullPath(dest).Equals(sourceFull, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, dest, overwrite: true);
        }

        return dest;
    }

    public string ReplacePhoto(string sourceImage, string targetId, Action<string> log)
    {
        if (!File.Exists(sourceImage))
        {
            throw new FileNotFoundException("Replacement image was not found.", sourceImage);
        }

        if (!IsPhotoCache(PhotoDirectory))
        {
            throw new InvalidOperationException("The selected photo cache folder is no longer valid. Re-select the real Heartopia Photo cache before replacing files.");
        }

        var targetFiles = GetFilesForPhotoId(targetId);
        if (targetFiles.Count == 0)
        {
            throw new InvalidOperationException($"No target files found for photo ID {targetId}.");
        }

        foreach (var file in targetFiles)
        {
            try
            {
                ValidateEncryptedPhotoFileMatchesName(file.Path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"The selected photo cache no longer matches the supported encryption or image format.\nRun the compatibility probe before replacing files.\nReason: {ex.Message}",
                    ex);
            }
        }

        var backupPath = Path.Combine(BackupDirectory, $"{targetId}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backupPath);

        var plans = targetFiles.Select(file => new ReplacementPlan(
            file,
            Path.Combine(backupPath, Path.GetFileName(file.Path)),
            file.Path + $".replace_{Guid.NewGuid():N}.tmp",
            EncryptBytes(ConvertToJpegBytes(sourceImage, file.Width, file.Height, 98))))
            .ToArray();

        EnsureWritableFiles(targetFiles.Select(file => file.Path), "replace");
        EnsureAvailableFreeSpace(
            new StorageRequirement(BackupDirectory, targetFiles.Sum(file => new FileInfo(file.Path).Length), "backup storage"),
            new StorageRequirement(PhotoDirectory, plans.Sum(plan => (long)plan.EncryptedBytes.Length), "temporary cache writes"));

        try
        {
            foreach (var plan in plans)
            {
                File.Copy(plan.File.Path, plan.BackupPath, overwrite: true);
            }

            foreach (var plan in plans)
            {
                File.WriteAllBytes(plan.TempPath, plan.EncryptedBytes);
            }

            foreach (var plan in plans)
            {
                File.Replace(plan.TempPath, plan.File.Path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                ValidateEncryptedPhotoFileMatchesName(plan.File.Path);
                log($"Replaced {Path.GetFileName(plan.File.Path)} ({plan.File.Width}x{plan.File.Height}).");
            }

            var cleanup = CleanupBackups(log);
            log(cleanup.Summary);

            return backupPath;
        }
        catch (Exception ex)
        {
            SafeRollback(plans, log);
            throw new InvalidOperationException(
                $"Replace failed and the original cache files were restored from backup.\nBackup: {backupPath}\nReason: {ex.Message}",
                ex);
        }
        finally
        {
            CleanupTemps(plans);
        }
    }

    public string RestoreLatestBackup(string targetId, Action<string> log)
    {
        var latest = GetBackupsForPhotoId(targetId).FirstOrDefault();
        if (latest is null)
        {
            throw new InvalidOperationException($"No backup folder was found for photo ID {targetId}.");
        }

        return RestoreBackupSnapshot(targetId, latest.DirectoryPath, log);
    }

    public string RestoreBackupSnapshot(string targetId, string backupDirectoryPath, Action<string> log)
    {
        if (!IsPhotoCache(PhotoDirectory))
        {
            throw new InvalidOperationException("The selected photo cache folder is no longer valid. Re-select the real Heartopia Photo cache before restoring files.");
        }

        if (string.IsNullOrWhiteSpace(backupDirectoryPath) || !Directory.Exists(backupDirectoryPath))
        {
            throw new InvalidOperationException("The selected backup folder no longer exists.");
        }

        var snapshot = new DirectoryInfo(backupDirectoryPath);

        var backupFiles = Directory.EnumerateFiles(snapshot.FullName, "*.jpg")
            .Where(path =>
            {
                var match = PhotoFileRegex.Match(Path.GetFileName(path));
                return match.Success && match.Groups[1].Value == targetId;
            })
            .OrderBy(path => path)
            .ToArray();

        if (backupFiles.Length == 0)
        {
            throw new InvalidOperationException($"Backup folder {snapshot.Name} does not contain any matching photo cache files.");
        }

        foreach (var backupFile in backupFiles)
        {
            ValidateEncryptedPhotoFileMatchesName(backupFile);
        }

        var restorePlans = backupFiles.Select(path =>
        {
            var (_, width, height) = ParsePhotoFileName(path);
            var destinationPath = Path.Combine(PhotoDirectory, Path.GetFileName(path));
            return new RestorePlan(
                targetId,
                width,
                height,
                path,
                destinationPath,
                destinationPath + $".restore_{Guid.NewGuid():N}.tmp",
                File.Exists(destinationPath));
        }).ToArray();

        var restorePointPath = Path.Combine(BackupDirectory, $"restorepoint_{targetId}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(restorePointPath);

        EnsureWritableFiles(
            restorePlans.Where(plan => plan.DestinationExists).Select(plan => plan.DestinationPath),
            "restore");
        EnsureAvailableFreeSpace(
            new StorageRequirement(BackupDirectory, restorePlans.Where(plan => plan.DestinationExists).Sum(plan => new FileInfo(plan.DestinationPath).Length), "restore-point backup"),
            new StorageRequirement(PhotoDirectory, restorePlans.Sum(plan => new FileInfo(plan.BackupFilePath).Length), "temporary restore writes"));

        try
        {
            foreach (var plan in restorePlans.Where(plan => plan.DestinationExists))
            {
                File.Copy(plan.DestinationPath, Path.Combine(restorePointPath, Path.GetFileName(plan.DestinationPath)), overwrite: true);
            }

            foreach (var plan in restorePlans)
            {
                File.Copy(plan.BackupFilePath, plan.TempPath, overwrite: true);
            }

            foreach (var plan in restorePlans)
            {
                if (plan.DestinationExists)
                {
                    File.Replace(plan.TempPath, plan.DestinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(plan.TempPath, plan.DestinationPath);
                }

                ValidateEncryptedPhotoFileMatchesName(plan.DestinationPath);
                log($"Restored {Path.GetFileName(plan.DestinationPath)} from {snapshot.Name}.");
            }

            var cleanup = CleanupBackups(log);
            log(cleanup.Summary);

            return snapshot.FullName;
        }
        catch (Exception ex)
        {
            SafeRestoreRollback(restorePlans, restorePointPath, log);
            throw new InvalidOperationException(
                $"Restore failed and the current cache files were rolled back.\nRestore point: {restorePointPath}\nReason: {ex.Message}",
                ex);
        }
        finally
        {
            CleanupRestoreTemps(restorePlans);
        }
    }

    public Image? LoadPlainImagePreview(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    public Image? LoadEncryptedImagePreview(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(path);
        var plain = DecryptBytes(encrypted);
        using var stream = new MemoryStream(plain);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    public string? SmallestPreviewPath(string id)
    {
        return GetFilesForPhotoId(id).OrderBy(file => file.Width * file.Height).FirstOrDefault()?.Path;
    }

    private static string DefaultPhotoDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData",
            "LocalLow",
            "xd",
            "Heartopia",
            "ScreenCapture",
            "Photo");
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                yield return child;
                pending.Push(child);
            }
        }
    }

    private static string? ChooseCache(IReadOnlyList<string> caches)
    {
        using var form = new Form
        {
            Text = "Select Photo Cache",
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(760, 330),
            MinimumSize = new Size(700, 280)
        };

        var label = new Label
        {
            Text = "Multiple photo cache folders were found. Select the one to use.",
            Location = new Point(12, 12),
            Size = new Size(700, 24)
        };
        form.Controls.Add(label);

        var list = new ListBox
        {
            Location = new Point(12, 42),
            Size = new Size(715, 190)
        };
        foreach (var cache in caches)
        {
            list.Items.Add(cache);
        }

        list.SelectedIndex = 0;
        form.Controls.Add(list);

        var ok = new Button
        {
            Text = "Use Selected",
            Location = new Point(515, 242),
            Size = new Size(105, 32),
            DialogResult = DialogResult.OK
        };
        form.AcceptButton = ok;
        form.Controls.Add(ok);

        var cancel = new Button
        {
            Text = "Cancel",
            Location = new Point(625, 242),
            Size = new Size(100, 32),
            DialogResult = DialogResult.Cancel
        };
        form.CancelButton = cancel;
        form.Controls.Add(cancel);

        return form.ShowDialog() == DialogResult.OK && list.SelectedItem is string selected ? selected : null;
    }

    private static Aes NewAes()
    {
        var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

    private static byte[] EncryptBytes(byte[] bytes)
    {
        using var aes = NewAes();
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
    }

    private static byte[] DecryptBytes(byte[] bytes)
    {
        using var aes = NewAes();
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(bytes, 0, bytes.Length);
    }

    private static Size ValidateEncryptedPhotoFile(string path)
    {
        var encrypted = File.ReadAllBytes(path);
        var plain = DecryptBytes(encrypted);
        using var stream = new MemoryStream(plain);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return new Size(image.Width, image.Height);
    }

    private static void ValidateEncryptedPhotoFileMatchesName(string path)
    {
        var (_, width, height) = ParsePhotoFileName(path);
        var size = ValidateEncryptedPhotoFile(path);
        EnsureExpectedSize(size, width, height, Path.GetFileName(path));
    }

    internal static byte[] EncodeReplacementImageForCache(string sourcePath, int width, int height, int quality = 98)
    {
        return EncryptBytes(ConvertToJpegBytes(sourcePath, width, height, quality));
    }

    private static byte[] ConvertToJpegBytes(string sourcePath, int width, int height, int quality)
    {
        using var source = Image.FromFile(sourcePath);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Black);
            graphics.DrawImage(source, 0, 0, width, height);
        }

        var codec = ImageCodecInfo.GetImageEncoders().First(item => item.MimeType == "image/jpeg");
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(ImageEncoder.Quality, (long)quality);
        using var stream = new MemoryStream();
        bitmap.Save(stream, codec, encoderParams);
        return stream.ToArray();
    }

    private static void SafeRollback(IEnumerable<ReplacementPlan> plans, Action<string> log)
    {
        foreach (var plan in plans)
        {
            try
            {
                if (File.Exists(plan.BackupPath))
                {
                    File.Copy(plan.BackupPath, plan.File.Path, overwrite: true);
                    log($"Restored {Path.GetFileName(plan.File.Path)} from backup.");
                }
            }
            catch (Exception rollbackEx)
            {
                log($"Rollback warning for {Path.GetFileName(plan.File.Path)}: {rollbackEx.Message}");
            }
        }
    }

    private static void CleanupTemps(IEnumerable<ReplacementPlan> plans)
    {
        foreach (var plan in plans)
        {
            try
            {
                if (File.Exists(plan.TempPath))
                {
                    File.Delete(plan.TempPath);
                }
            }
            catch
            {
                // Temp cleanup must not hide the real result.
            }
        }
    }

    private static void SafeRestoreRollback(IEnumerable<RestorePlan> plans, string restorePointPath, Action<string> log)
    {
        foreach (var plan in plans)
        {
            try
            {
                if (plan.DestinationExists)
                {
                    var restorePointFile = Path.Combine(restorePointPath, Path.GetFileName(plan.DestinationPath));
                    if (File.Exists(restorePointFile))
                    {
                        File.Copy(restorePointFile, plan.DestinationPath, overwrite: true);
                        log($"Rolled back {Path.GetFileName(plan.DestinationPath)} from restore point.");
                    }
                }
                else if (File.Exists(plan.DestinationPath))
                {
                    File.Delete(plan.DestinationPath);
                    log($"Removed partially restored file {Path.GetFileName(plan.DestinationPath)}.");
                }
            }
            catch (Exception rollbackEx)
            {
                log($"Restore rollback warning for {Path.GetFileName(plan.DestinationPath)}: {rollbackEx.Message}");
            }
        }
    }

    private static void CleanupRestoreTemps(IEnumerable<RestorePlan> plans)
    {
        foreach (var plan in plans)
        {
            try
            {
                if (File.Exists(plan.TempPath))
                {
                    File.Delete(plan.TempPath);
                }
            }
            catch
            {
                // Temp cleanup must not hide the real result.
            }
        }
    }

    private static void EnsureExpectedSize(Size actual, int expectedWidth, int expectedHeight, string label)
    {
        if (actual.Width != expectedWidth || actual.Height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"{label} produced {actual.Width}x{actual.Height}, expected {expectedWidth}x{expectedHeight}.");
        }
    }

    private static (string Id, int Width, int Height) ParsePhotoFileName(string path)
    {
        var match = PhotoFileRegex.Match(Path.GetFileName(path));
        if (!match.Success)
        {
            throw new InvalidOperationException($"Unsupported cache file name: {Path.GetFileName(path)}");
        }

        return (
            match.Groups[1].Value,
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
    }

    private static void EnsureWritableFiles(IEnumerable<string> paths, string operationName)
    {
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new FileNotFoundException($"Required file for {operationName} was not found.", path);
            }

            if (info.IsReadOnly)
            {
                throw new InvalidOperationException($"Cannot {operationName} because the file is read-only: {info.Name}");
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot {operationName} because the file is currently in use: {info.Name}\nClose the game album or any tool using that cache file, then try again.",
                    ex);
            }
        }
    }

    private static void EnsureAvailableFreeSpace(params StorageRequirement[] requirements)
    {
        var grouped = requirements
            .Where(item => item.RequiredBytes > 0)
            .GroupBy(item => Path.GetPathRoot(Path.GetFullPath(item.Path)) ?? item.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var requiredBytes = group.Sum(item => item.RequiredBytes);
            var margin = Math.Max(8L * 1024 * 1024, requiredBytes / 4);
            var drive = new DriveInfo(group.Key);
            if (drive.AvailableFreeSpace < requiredBytes + margin)
            {
                var purpose = string.Join(", ", group.Select(item => item.Purpose));
                throw new InvalidOperationException(
                    $"Not enough free space on drive {drive.Name} for {purpose}.\nRequired: {FormatBytes(requiredBytes + margin)}\nAvailable: {FormatBytes(drive.AvailableFreeSpace)}");
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.00} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.00} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0.00} KB";
        }

        return $"{bytes} B";
    }

    private static int NormalizeBackupRetentionCount(int? value)
    {
        if (value is null || value <= 0)
        {
            return DefaultBackupRetentionCount;
        }

        return Math.Min(value.Value, 200);
    }

    private static int NormalizeBackupRetentionDays(int? value)
    {
        if (value is null || value <= 0)
        {
            return DefaultBackupRetentionDays;
        }

        return Math.Min(value.Value, 3650);
    }
}

internal sealed class MainForm : Form
{
    private const string UsageNoticeText =
        "This tool only replaces Heartopia photo cache files under AppData\\LocalLow.\n\n" +
        "It does not modify the game install folder. Always keep backups, stop immediately if the compatibility probe fails, " +
        "and review each release after a game update before replacing cache files again.";

    private readonly ReplacerApp _app;
    private readonly TextBox _workspaceBox = new();
    private readonly TextBox _photoCacheBox = new();
    private readonly ComboBox _sourceCombo = new();
    private readonly PictureBox _sourcePreview = new();
    private readonly PictureBox _targetPreview = new();
    private readonly DataGridView _photoGrid = new();
    private readonly TextBox _logBox = new();
    private readonly Label _healthSummaryLabel = new();
    private bool _probePassedForCurrentCache;
    private bool _probeFailedForCurrentCache;
    private string _probedCacheDirectory = string.Empty;

    public MainForm(ReplacerApp app)
    {
        _app = app;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = $"Heartopia Photo Replacer v{_app.AppVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(980, 735);
        MinimumSize = new Size(900, 660);

        Controls.Add(new Label
        {
            Text = "Workspace",
            Location = new Point(12, 15),
            Size = new Size(80, 22)
        });

        _workspaceBox.Location = new Point(95, 12);
        _workspaceBox.Size = new Size(545, 24);
        _workspaceBox.ReadOnly = true;
        _workspaceBox.Text = _app.Workspace;
        Controls.Add(_workspaceBox);

        var changeWorkspace = new Button
        {
            Text = "Change",
            Location = new Point(650, 10),
            Size = new Size(78, 28)
        };
        changeWorkspace.Click += (_, _) => ChangeWorkspace();
        Controls.Add(changeWorkspace);

        var openImages = new Button
        {
            Text = "Open Images",
            Location = new Point(735, 10),
            Size = new Size(98, 28)
        };
        openImages.Click += (_, _) => OpenFolder(_app.ImageDirectory);
        Controls.Add(openImages);

        var safetySupport = new Button
        {
            Text = "Safety && Support",
            Location = new Point(840, 10),
            Size = new Size(110, 28)
        };
        safetySupport.Click += (_, _) => OpenSafetySupport();
        Controls.Add(safetySupport);

        Controls.Add(new Label
        {
            Text = "Photo cache",
            Location = new Point(12, 47),
            Size = new Size(80, 22)
        });

        _photoCacheBox.Location = new Point(95, 44);
        _photoCacheBox.Size = new Size(590, 24);
        _photoCacheBox.ReadOnly = true;
        _photoCacheBox.Text = _app.PhotoDirectory;
        Controls.Add(_photoCacheBox);

        var autoDetect = new Button
        {
            Text = "Auto Detect",
            Location = new Point(695, 42),
            Size = new Size(90, 28)
        };
        autoDetect.Click += (_, _) => AutoDetectCache();
        Controls.Add(autoDetect);

        var changeCache = new Button
        {
            Text = "Change Cache",
            Location = new Point(865, 42),
            Size = new Size(85, 28)
        };
        changeCache.Click += (_, _) => ChangeCache();
        Controls.Add(changeCache);

        var probeCompatibility = new Button
        {
            Text = "Probe",
            Location = new Point(790, 42),
            Size = new Size(70, 28)
        };
        probeCompatibility.Click += (_, _) => ProbeCompatibility();
        Controls.Add(probeCompatibility);

        BuildSourceGroup();
        BuildTargetGroup();
        BuildPhotoGrid();
        BuildLogBox();

        Load += (_, _) =>
        {
            if (!EnsureUsageNoticeAccepted())
            {
                BeginInvoke(new Action(Close));
                return;
            }

            InvalidateProbe();
            Log($"Workspace: {_app.Workspace}");
            Log($"Image folder: {_app.ImageDirectory}");
            Log($"Game photo cache: {_app.PhotoDirectory}");
            Log($"Version: {_app.AppVersion}");
            RefreshImages();
            RefreshPhotos();
            UpdateHealthSummary();
        };

        FormClosed += (_, _) =>
        {
            ReplacePicture(_sourcePreview, null);
            ReplacePicture(_targetPreview, null);
        };
    }

    private void BuildSourceGroup()
    {
        var group = new GroupBox
        {
            Text = "1. Replacement image",
            Location = new Point(12, 82),
            Size = new Size(455, 230)
        };
        Controls.Add(group);

        _sourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _sourceCombo.Location = new Point(12, 28);
        _sourceCombo.Size = new Size(315, 24);
        _sourceCombo.SelectedIndexChanged += (_, _) => UpdateSourcePreview();
        group.Controls.Add(_sourceCombo);

        var import = new Button
        {
            Text = "Import",
            Location = new Point(335, 26),
            Size = new Size(90, 28)
        };
        import.Click += (_, _) => ImportImage();
        group.Controls.Add(import);

        var refresh = new Button
        {
            Text = "Refresh",
            Location = new Point(335, 60),
            Size = new Size(90, 28)
        };
        refresh.Click += (_, _) => RefreshImages();
        group.Controls.Add(refresh);

        _sourcePreview.BorderStyle = BorderStyle.FixedSingle;
        _sourcePreview.SizeMode = PictureBoxSizeMode.Zoom;
        _sourcePreview.Location = new Point(12, 60);
        _sourcePreview.Size = new Size(300, 150);
        group.Controls.Add(_sourcePreview);
    }

    private void BuildTargetGroup()
    {
        var group = new GroupBox
        {
            Text = "2. Game photo target",
            Location = new Point(485, 82),
            Size = new Size(465, 230)
        };
        Controls.Add(group);

        _targetPreview.BorderStyle = BorderStyle.FixedSingle;
        _targetPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _targetPreview.Location = new Point(12, 28);
        _targetPreview.Size = new Size(300, 150);
        group.Controls.Add(_targetPreview);

        var refresh = new Button
        {
            Text = "Refresh Photos",
            Location = new Point(325, 28),
            Size = new Size(115, 30)
        };
        refresh.Click += (_, _) => RefreshPhotos();
        group.Controls.Add(refresh);

        var replace = new Button
        {
            Text = "Replace Selected",
            Location = new Point(325, 65),
            Size = new Size(115, 42)
        };
        replace.Click += (_, _) => ReplaceSelected();
        group.Controls.Add(replace);

        var restoreLatest = new Button
        {
            Text = "Restore Latest",
            Location = new Point(325, 113),
            Size = new Size(115, 30)
        };
        restoreLatest.Click += (_, _) => RestoreLatestBackup();
        group.Controls.Add(restoreLatest);

        var restoreHistory = new Button
        {
            Text = "History...",
            Location = new Point(325, 149),
            Size = new Size(115, 30)
        };
        restoreHistory.Click += (_, _) => RestoreFromHistory();
        group.Controls.Add(restoreHistory);

        _healthSummaryLabel.Location = new Point(12, 185);
        _healthSummaryLabel.Size = new Size(430, 35);
        _healthSummaryLabel.Text = "Health: waiting for photo cache scan.";
        group.Controls.Add(_healthSummaryLabel);
    }

    private void BuildPhotoGrid()
    {
        _photoGrid.Location = new Point(12, 324);
        _photoGrid.Size = new Size(938, 250);
        _photoGrid.ReadOnly = true;
        _photoGrid.AllowUserToAddRows = false;
        _photoGrid.AllowUserToDeleteRows = false;
        _photoGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _photoGrid.MultiSelect = false;
        _photoGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _photoGrid.Columns.Add("Id", "Photo ID");
        _photoGrid.Columns.Add("FileCount", "Files");
        _photoGrid.Columns.Add("LastWriteTime", "Last Modified");
        _photoGrid.Columns.Add("Sizes", "Sizes");
        _photoGrid.Columns["Id"]!.FillWeight = 35;
        _photoGrid.Columns["FileCount"]!.FillWeight = 12;
        _photoGrid.Columns["LastWriteTime"]!.FillWeight = 25;
        _photoGrid.Columns["Sizes"]!.FillWeight = 55;
        _photoGrid.SelectionChanged += (_, _) => UpdateTargetPreview();
        Controls.Add(_photoGrid);
    }

    private void BuildLogBox()
    {
        _logBox.Location = new Point(12, 586);
        _logBox.Size = new Size(938, 95);
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        Controls.Add(_logBox);
    }

    private void ChangeWorkspace()
    {
        var selected = _app.PromptForWorkspace();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        _app.SetWorkspace(selected);
        _app.SaveConfig();
        _workspaceBox.Text = _app.Workspace;
        Log($"Workspace changed: {_app.Workspace}");
        RefreshImages();
        UpdateHealthSummary();
    }

    private void AutoDetectCache()
    {
        Log("Scanning AppData\\LocalLow for photo cache folders...");
        var detected = _app.DetectPhotoCache(interactive: true);
        if (string.IsNullOrWhiteSpace(detected))
        {
            return;
        }

        _app.SetPhotoDirectory(detected);
        InvalidateProbe();
        _photoCacheBox.Text = _app.PhotoDirectory;
        Log($"Photo cache selected: {_app.PhotoDirectory}");
        RefreshPhotos();
        UpdateHealthSummary();
    }

    private void ChangeCache()
    {
        var selected = _app.PromptForVerifiedPhotoCache();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        _app.SetPhotoDirectory(selected);
        InvalidateProbe();
        _photoCacheBox.Text = _app.PhotoDirectory;
        Log($"Photo cache changed: {_app.PhotoDirectory}");
        RefreshPhotos();
        UpdateHealthSummary();
    }

    private void ProbeCompatibility()
    {
        var result = RunCompatibilityProbe();
        Log(result.Message);
        UpdateHealthSummary();
        MessageBox.Show(
            result.Message,
            result.IsCompatible ? "Compatibility OK" : "Compatibility Warning",
            MessageBoxButtons.OK,
            result.IsCompatible ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ImportImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            var imported = _app.ImportImage(dialog.FileName);
            RefreshImages();
            _sourceCombo.SelectedItem = imported;
            Log($"Imported image: {imported}");
        }
        catch (Exception ex)
        {
            ShowError("Import failed", ex);
        }
    }

    private void RefreshImages()
    {
        _sourceCombo.Items.Clear();
        foreach (var image in _app.GetReplacementImages())
        {
            _sourceCombo.Items.Add(image);
        }

        if (_sourceCombo.Items.Count > 0)
        {
            _sourceCombo.SelectedIndex = 0;
        }
        else
        {
            ReplacePicture(_sourcePreview, null);
        }
    }

    private void RefreshPhotos()
    {
        _photoGrid.Rows.Clear();
        foreach (var group in _app.GetPhotoGroups())
        {
            var row = _photoGrid.Rows[_photoGrid.Rows.Add()];
            row.Cells["Id"].Value = group.Id;
            row.Cells["FileCount"].Value = group.FileCount;
            row.Cells["LastWriteTime"].Value = group.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            row.Cells["Sizes"].Value = group.Sizes;
        }

        if (_photoGrid.Rows.Count > 0)
        {
            _photoGrid.Rows[0].Selected = true;
            _photoGrid.CurrentCell = _photoGrid.Rows[0].Cells["Id"];
            UpdateTargetPreview();
        }
        else
        {
            ReplacePicture(_targetPreview, null);
        }

        Log($"Loaded {_photoGrid.Rows.Count} photo IDs.");
        UpdateHealthSummary();
    }

    private void UpdateSourcePreview()
    {
        var path = SelectedSourcePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            ReplacePicture(_sourcePreview, null);
            return;
        }

        try
        {
            ReplacePicture(_sourcePreview, _app.LoadPlainImagePreview(path));
        }
        catch (Exception ex)
        {
            Log($"Source preview failed: {ex.Message}");
            ReplacePicture(_sourcePreview, null);
        }
    }

    private void UpdateTargetPreview()
    {
        var id = SelectedPhotoId();
        if (string.IsNullOrWhiteSpace(id))
        {
            ReplacePicture(_targetPreview, null);
            UpdateHealthSummary();
            return;
        }

        var path = _app.SmallestPreviewPath(id);
        if (string.IsNullOrWhiteSpace(path))
        {
            ReplacePicture(_targetPreview, null);
            UpdateHealthSummary();
            return;
        }

        try
        {
            ReplacePicture(_targetPreview, _app.LoadEncryptedImagePreview(path));
        }
        catch (Exception ex)
        {
            Log($"Target preview failed: {ex.Message}");
            ReplacePicture(_targetPreview, null);
        }

        UpdateHealthSummary();
    }

    private void ReplaceSelected()
    {
        var source = SelectedSourcePath();
        var id = SelectedPhotoId();

        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            MessageBox.Show("Select a replacement image first.", "Missing image");
            return;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show("Select a game photo ID first.", "Missing target");
            return;
        }

        if (!HasCurrentProbePass())
        {
            var answer = MessageBox.Show(
                "You must pass a compatibility probe on the current photo cache before replacing files.\nRun Probe now?",
                "Probe required",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (answer != DialogResult.OK)
            {
                return;
            }

            var probeResult = RunCompatibilityProbe();
            Log(probeResult.Message);
            if (!probeResult.IsCompatible)
            {
                MessageBox.Show(probeResult.Message, "Compatibility Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var confirm = MessageBox.Show(
            $"Replace photo ID {id} with:\n{source}\n\nThe current game cache files will be backed up first.",
            "Confirm replace",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        try
        {
            var backup = _app.ReplacePhoto(source, id, Log);
            Log($"Backup saved: {backup}");
            RefreshPhotos();
            UpdateHealthSummary();
            MessageBox.Show(
                $"Replace complete.\nBackup: {backup}\n\nRefresh the album or restart the game if it still shows the old image.",
                "Complete");
        }
        catch (Exception ex)
        {
            ShowError("Replace failed", ex);
        }
    }

    private void RestoreLatestBackup()
    {
        var id = SelectedPhotoId();
        if (string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show("Select a game photo ID first.", "Missing target");
            return;
        }

        var backups = _app.GetBackupsForPhotoId(id);
        if (backups.Count == 0)
        {
            MessageBox.Show($"No backup was found yet for photo ID {id}.", "No backup");
            return;
        }

        var latest = backups[0];
        var confirm = MessageBox.Show(
            $"Restore the latest backup for photo ID {id}?\n\nBackup folder:\n{latest.DirectoryName}",
            "Confirm restore",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        try
        {
            var restored = _app.RestoreLatestBackup(id, Log);
            RefreshPhotos();
            UpdateTargetPreview();
            UpdateHealthSummary();
            MessageBox.Show(
                $"Restore complete.\nBackup used: {restored}",
                "Restore complete");
        }
        catch (Exception ex)
        {
            ShowError("Restore failed", ex);
        }
    }

    private void RestoreFromHistory()
    {
        var id = SelectedPhotoId();
        if (string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show("Select a game photo ID first.", "Missing target");
            return;
        }

        var backups = _app.GetBackupsForPhotoId(id);
        if (backups.Count == 0)
        {
            MessageBox.Show($"No backup was found yet for photo ID {id}.", "No backup");
            return;
        }

        var selected = ChooseBackupSnapshot(id, backups);
        if (selected is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Restore photo ID {id} from this backup?\n\n{selected.DirectoryName}",
            "Confirm restore from history",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        try
        {
            var restored = _app.RestoreBackupSnapshot(id, selected.DirectoryPath, Log);
            RefreshPhotos();
            UpdateTargetPreview();
            UpdateHealthSummary();
            MessageBox.Show(
                $"Restore complete.\nBackup used: {restored}",
                "Restore complete");
        }
        catch (Exception ex)
        {
            ShowError("Restore failed", ex);
        }
    }

    private string? SelectedSourcePath()
    {
        return _sourceCombo.SelectedItem as string;
    }

    private string? SelectedPhotoId()
    {
        return _photoGrid.CurrentRow?.Cells["Id"].Value as string;
    }

    private CacheCompatibilityResult RunCompatibilityProbe()
    {
        var result = _app.ProbeCurrentCacheCompatibility();
        if (result.IsCompatible)
        {
            _probePassedForCurrentCache = true;
            _probeFailedForCurrentCache = false;
            _probedCacheDirectory = _app.PhotoDirectory;
        }
        else
        {
            _probePassedForCurrentCache = false;
            _probeFailedForCurrentCache = true;
            _probedCacheDirectory = _app.PhotoDirectory;
        }

        return result;
    }

    private BackupSnapshot? ChooseBackupSnapshot(string photoId, IReadOnlyList<BackupSnapshot> backups)
    {
        using var form = new Form
        {
            Text = $"Backup History - {photoId}",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 360),
            MinimumSize = new Size(700, 320)
        };

        var label = new Label
        {
            Text = "Choose the backup snapshot to restore for this photo ID.",
            Location = new Point(12, 12),
            Size = new Size(710, 24)
        };
        form.Controls.Add(label);

        var list = new ListBox
        {
            Location = new Point(12, 42),
            Size = new Size(715, 220),
            DisplayMember = nameof(BackupSnapshotDisplay.DisplayText)
        };
        foreach (var backup in backups)
        {
            list.Items.Add(new BackupSnapshotDisplay(
                backup,
                $"{backup.DirectoryName}  |  {backup.LastWriteTime:yyyy-MM-dd HH:mm:ss}"));
        }

        list.SelectedIndex = 0;
        form.Controls.Add(list);

        var restore = new Button
        {
            Text = "Restore Selected",
            Location = new Point(500, 275),
            Size = new Size(110, 32),
            DialogResult = DialogResult.OK
        };
        form.AcceptButton = restore;
        form.Controls.Add(restore);

        var cancel = new Button
        {
            Text = "Cancel",
            Location = new Point(620, 275),
            Size = new Size(100, 32),
            DialogResult = DialogResult.Cancel
        };
        form.CancelButton = cancel;
        form.Controls.Add(cancel);

        return form.ShowDialog(this) == DialogResult.OK
               && list.SelectedItem is BackupSnapshotDisplay selected
            ? selected.Snapshot
            : null;
    }

    private bool HasCurrentProbePass()
    {
        return _probePassedForCurrentCache
               && string.Equals(_probedCacheDirectory, _app.PhotoDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private void InvalidateProbe()
    {
        _probePassedForCurrentCache = false;
        _probeFailedForCurrentCache = false;
        _probedCacheDirectory = string.Empty;
    }

    private void UpdateHealthSummary()
    {
        var cacheState = _app.IsPhotoCache(_app.PhotoDirectory) ? "Verified" : "Invalid";
        var probeState = HasCurrentProbePass() ? "Passed" : "Required";
        var selectedId = SelectedPhotoId();
        var backupCount = string.IsNullOrWhiteSpace(selectedId)
            ? "n/a"
            : _app.GetBackupsForPhotoId(selectedId).Count.ToString();
        var diskSummary = BuildDiskSummary();
        var compatibilityState = BuildCompatibilityState();

        _healthSummaryLabel.Text =
            $"Compatibility: {compatibilityState} | Cache: {cacheState} | Probe: {probeState}\nBackups: {backupCount} | Policy: {_app.BackupRetentionCount} snapshots / {_app.BackupRetentionDays} days | Disk: {diskSummary}";
    }

    private string BuildDiskSummary()
    {
        try
        {
            var paths = new[] { _app.PhotoDirectory, _app.BackupDirectory }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetPathRoot(Path.GetFullPath(path)) ?? path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0)
            {
                return "n/a";
            }

            return string.Join(" | ", paths.Select(root =>
            {
                var drive = new DriveInfo(root);
                return $"{drive.Name.TrimEnd('\\')}: {FormatBytesForUi(drive.AvailableFreeSpace)} free";
            }));
        }
        catch
        {
            return "unknown";
        }
    }

    private static string FormatBytesForUi(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.0} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.0} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0.0} KB";
        }

        return $"{bytes} B";
    }

    private bool EnsureUsageNoticeAccepted()
    {
        if (_app.HasAcceptedCurrentNotice())
        {
            return true;
        }

        var answer = MessageBox.Show(
            UsageNoticeText + "\n\nSelect OK to confirm that you understand these limits before using the tool.",
            $"Usage notice - v{_app.AppVersion}",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.OK)
        {
            return false;
        }

        _app.MarkCurrentNoticeAccepted();
        return true;
    }

    private string BuildCompatibilityState()
    {
        if (!_app.IsPhotoCache(_app.PhotoDirectory))
        {
            return "Blocked";
        }

        if (_probeFailedForCurrentCache
            && string.Equals(_probedCacheDirectory, _app.PhotoDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return "Unsupported";
        }

        return HasCurrentProbePass() ? "Ready" : "Probe required";
    }

    private void OpenSafetySupport()
    {
        using var form = new Form
        {
            Text = "Safety and Support",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(620, 420),
            MinimumSize = new Size(580, 390)
        };

        form.Controls.Add(new Label
        {
            Text = $"Version: {_app.AppVersion} | Compatibility: {BuildCompatibilityState()}",
            Location = new Point(12, 12),
            Size = new Size(570, 24)
        });

        form.Controls.Add(new Label
        {
            Text = "Backup retention policy",
            Location = new Point(12, 48),
            Size = new Size(180, 22)
        });

        form.Controls.Add(new Label
        {
            Text = "Keep newest snapshots per photo ID",
            Location = new Point(12, 80),
            Size = new Size(240, 22)
        });

        var retentionCount = new NumericUpDown
        {
            Location = new Point(260, 78),
            Size = new Size(80, 24),
            Minimum = 1,
            Maximum = 200,
            Value = _app.BackupRetentionCount
        };
        form.Controls.Add(retentionCount);

        form.Controls.Add(new Label
        {
            Text = "Keep backups for days",
            Location = new Point(12, 112),
            Size = new Size(240, 22)
        });

        var retentionDays = new NumericUpDown
        {
            Location = new Point(260, 110),
            Size = new Size(80, 24),
            Minimum = 1,
            Maximum = 3650,
            Value = _app.BackupRetentionDays
        };
        form.Controls.Add(retentionDays);

        var applyPolicy = new Button
        {
            Text = "Apply Policy",
            Location = new Point(360, 78),
            Size = new Size(105, 28)
        };
        applyPolicy.Click += (_, _) =>
        {
            _app.UpdateBackupPolicy((int)retentionCount.Value, (int)retentionDays.Value);
            UpdateHealthSummary();
            MessageBox.Show(
                $"Backup policy updated.\nKeep newest {_app.BackupRetentionCount} snapshots per photo ID for {_app.BackupRetentionDays} days.",
                "Backup policy saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        form.Controls.Add(applyPolicy);

        var cleanupNow = new Button
        {
            Text = "Cleanup Backups Now",
            Location = new Point(360, 110),
            Size = new Size(150, 28)
        };
        cleanupNow.Click += (_, _) =>
        {
            try
            {
                var result = _app.CleanupBackups(Log);
                UpdateHealthSummary();
                MessageBox.Show(result.Summary, "Backup cleanup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError("Backup cleanup failed", ex);
            }
        };
        form.Controls.Add(cleanupNow);

        var exportBundle = new Button
        {
            Text = "Export Support Bundle",
            Location = new Point(12, 160),
            Size = new Size(160, 32)
        };
        exportBundle.Click += (_, _) => ExportSupportBundle();
        form.Controls.Add(exportBundle);

        var openBackupFolder = new Button
        {
            Text = "Open Backup Folder",
            Location = new Point(182, 160),
            Size = new Size(140, 32)
        };
        openBackupFolder.Click += (_, _) => OpenFolder(_app.BackupDirectory);
        form.Controls.Add(openBackupFolder);

        var viewNotice = new Button
        {
            Text = "View Notice",
            Location = new Point(332, 160),
            Size = new Size(110, 32)
        };
        viewNotice.Click += (_, _) =>
        {
            MessageBox.Show(UsageNoticeText, $"Usage notice - v{_app.AppVersion}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        form.Controls.Add(viewNotice);

        var healthBox = new TextBox
        {
            Location = new Point(12, 210),
            Size = new Size(575, 150),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text =
                $"Health summary:{Environment.NewLine}{_healthSummaryLabel.Text}{Environment.NewLine}{Environment.NewLine}" +
                $"Workspace: {_app.Workspace}{Environment.NewLine}" +
                $"Photo cache: {_app.PhotoDirectory}{Environment.NewLine}" +
                $"Support bundles: {_app.SupportBundleDirectory}{Environment.NewLine}" +
                $"Selected photo ID: {SelectedPhotoId() ?? "n/a"}{Environment.NewLine}" +
                $"Log file: {Path.Combine(_app.LogDirectory, "replacer.log")}"
        };
        form.Controls.Add(healthBox);

        form.ShowDialog(this);
    }

    private void ExportSupportBundle()
    {
        try
        {
            Directory.CreateDirectory(_app.SupportBundleDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var stagingDir = Path.Combine(_app.SupportBundleDirectory, $"support_bundle_{stamp}");
            Directory.CreateDirectory(stagingDir);

            var diagnostics = new
            {
                appVersion = _app.AppVersion,
                exportedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                exportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                workspace = _app.Workspace,
                photoCache = _app.PhotoDirectory,
                imageDirectory = _app.ImageDirectory,
                backupDirectory = _app.BackupDirectory,
                logDirectory = _app.LogDirectory,
                supportBundleDirectory = _app.SupportBundleDirectory,
                cacheVerified = _app.IsPhotoCache(_app.PhotoDirectory),
                compatibilityState = BuildCompatibilityState(),
                currentProbePassed = HasCurrentProbePass(),
                selectedPhotoId = SelectedPhotoId(),
                selectedPhotoBackupCount = string.IsNullOrWhiteSpace(SelectedPhotoId()) ? 0 : _app.GetBackupsForPhotoId(SelectedPhotoId()!).Count,
                backupRetentionCount = _app.BackupRetentionCount,
                backupRetentionDays = _app.BackupRetentionDays,
                healthSummary = _healthSummaryLabel.Text,
                diskSummary = BuildDiskSummary(),
                environment = new
                {
                    os = Environment.OSVersion.VersionString,
                    dotnet = Environment.Version.ToString(),
                    machineName = Environment.MachineName
                }
            };

            File.WriteAllText(
                Path.Combine(stagingDir, "diagnostics.json"),
                JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }));

            File.WriteAllText(Path.Combine(stagingDir, "health_summary.txt"), _healthSummaryLabel.Text);

            if (File.Exists(_app.ConfigPath))
            {
                File.Copy(_app.ConfigPath, Path.Combine(stagingDir, "config.json"), overwrite: true);
            }

            var logPath = Path.Combine(_app.LogDirectory, "replacer.log");
            if (File.Exists(logPath))
            {
                File.Copy(logPath, Path.Combine(stagingDir, "replacer.log"), overwrite: true);
            }

            foreach (var fileName in new[] { "README.md", "CHANGELOG.md", "NOTICE.txt", "EULA.txt" })
            {
                var source = ResolvePackageFile(fileName);
                if (source is not null)
                {
                    File.Copy(source, Path.Combine(stagingDir, fileName), overwrite: true);
                }
            }

            var zipPath = Path.Combine(_app.SupportBundleDirectory, $"HeartopiaPhotoReplacer_support_{stamp}.zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(stagingDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            Directory.Delete(stagingDir, recursive: true);
            Log($"Support bundle exported: {zipPath}");

            MessageBox.Show(
                $"Support bundle exported.\n{zipPath}",
                "Support bundle ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("Support bundle export failed", ex);
        }
    }

    private static string? ResolvePackageFile(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", fileName)
        };

        return candidates
            .Select(path => Path.GetFullPath(path))
            .FirstOrDefault(File.Exists);
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logBox.AppendText(line + Environment.NewLine);

        try
        {
            if (!string.IsNullOrWhiteSpace(_app.LogDirectory))
            {
                Directory.CreateDirectory(_app.LogDirectory);
                File.AppendAllText(Path.Combine(_app.LogDirectory, "replacer.log"), line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must not block replacement.
        }
    }

    private static void ReplacePicture(PictureBox box, Image? image)
    {
        var old = box.Image;
        box.Image = image;
        old?.Dispose();
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
    }

    private static void ShowError(string title, Exception ex)
    {
        MessageBox.Show(ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

internal sealed class AppConfig
{
    public string? Workspace { get; set; }
    public string? PhotoDir { get; set; }
    public int? BackupRetentionCount { get; set; }
    public int? BackupRetentionDays { get; set; }
    public string? NoticeAcceptedVersion { get; set; }
}

internal sealed record CacheCompatibilityResult(bool IsCompatible, string Message, int CheckedFileCount);

internal sealed record BackupCleanupResult(int DeletedDirectoryCount, int DeletedFileCount, long ReclaimedBytes, string Summary);

internal sealed record ReplacementPlan(PhotoFile File, string BackupPath, string TempPath, byte[] EncryptedBytes);

internal sealed record RestorePlan(
    string PhotoId,
    int Width,
    int Height,
    string BackupFilePath,
    string DestinationPath,
    string TempPath,
    bool DestinationExists);

internal sealed record BackupSnapshot(string DirectoryName, string DirectoryPath, DateTime LastWriteTime);

internal sealed record BackupSnapshotDisplay(BackupSnapshot Snapshot, string DisplayText);

internal sealed record StorageRequirement(string Path, long RequiredBytes, string Purpose);

internal sealed record PhotoFile(string Id, int Width, int Height, string Path, DateTime LastWriteTime);

internal sealed record PhotoGroup(string Id, int FileCount, DateTime LastWriteTime, string Sizes);
