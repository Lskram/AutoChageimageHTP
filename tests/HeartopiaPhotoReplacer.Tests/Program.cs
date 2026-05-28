using System.Drawing;
using HeartopiaPhotoReplacer;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RunAll();
            Console.WriteLine("HeartopiaPhotoReplacer.Tests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("HeartopiaPhotoReplacer.Tests: FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunAll()
    {
        var root = Path.Combine(Path.GetTempPath(), $"HeartopiaPhotoReplacerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var workspace = Path.Combine(root, "workspace");
            var photoCache = Path.Combine(root, "cache");
            var configRoot = Path.Combine(root, "config");
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(photoCache);
            Directory.CreateDirectory(configRoot);

            var originalImage = Path.Combine(root, "original.png");
            var replacementImage = Path.Combine(root, "replacement.png");
            CreateImage(originalImage, Color.OrangeRed);
            CreateImage(replacementImage, Color.DodgerBlue);

            const string photoId = "123456789012345678";
            CreateEncryptedPhotoCache(photoCache, photoId, originalImage, 256, 144);
            CreateEncryptedPhotoCache(photoCache, photoId, originalImage, 512, 288);
            CreateEncryptedPhotoCache(photoCache, photoId, originalImage, 1920, 1080, 1452, 817);

            const string newFormatPhotoId = "normal+tvn5qeg+TakePhoto+134160780319712106.png";
            CreateEncryptedPhotoCache(photoCache, newFormatPhotoId, originalImage, 256, 144);

            var app = new ReplacerApp(configRoot);
            app.SetWorkspace(workspace);
            app.SetPhotoDirectory(photoCache);
            app.UpdateBackupPolicy(1, 30);

            Assert(app.IsPhotoCache(photoCache), "Expected generated cache directory to be recognized.");
            Assert(app.GetPhotoGroups().Count == 2, "Expected two generated photo groups.");
            Assert(app.GetFilesForPhotoId(newFormatPhotoId).Count == 1, "Expected the new filename format to be grouped by its full prefix.");

            var probe = app.ProbeCurrentCacheCompatibility();
            Assert(probe.IsCompatible, $"Expected compatibility probe to pass, got: {probe.Message}");

            var targetFiles = app.GetFilesForPhotoId(photoId);
            Assert(targetFiles.Count == 3, "Expected three target cache files.");

            var originalBytes = targetFiles.ToDictionary(
                item => Path.GetFileName(item.Path),
                item => File.ReadAllBytes(item.Path),
                StringComparer.OrdinalIgnoreCase);

            var backupPath = app.ReplacePhoto(replacementImage, photoId, _ => { });
            Assert(Directory.Exists(backupPath), "Expected replacement to create a backup directory.");

            var updatedBytes = File.ReadAllBytes(targetFiles[0].Path);
            Assert(!updatedBytes.SequenceEqual(originalBytes[Path.GetFileName(targetFiles[0].Path)]), "Expected replacement to change cache bytes.");
            Assert(app.GetBackupsForPhotoId(photoId).Count == 1, "Expected one backup snapshot after replacement.");

            using (var mismatchedPreview = app.LoadEncryptedImagePreview(Path.Combine(photoCache, $"{photoId}_1920_1080.jpg")))
            {
                Assert(mismatchedPreview is not null, "Expected mismatched-name cache preview to load after replacement.");
                Assert(mismatchedPreview!.Width == 1452 && mismatchedPreview.Height == 817, "Expected replacement to preserve the actual decrypted cache size, not the size in the filename.");
            }

            var newFormatBackupPath = app.ReplacePhoto(replacementImage, newFormatPhotoId, _ => { });
            Assert(Directory.Exists(newFormatBackupPath), "Expected replacement to support the new cache filename format.");
            Assert(app.GetBackupsForPhotoId(newFormatPhotoId).Count == 1, "Expected a backup snapshot for the new filename format.");

            var restoredPath = app.RestoreLatestBackup(photoId, _ => { });
            Assert(string.Equals(restoredPath, backupPath, StringComparison.OrdinalIgnoreCase), "Expected latest restore to use the first backup snapshot.");

            foreach (var file in app.GetFilesForPhotoId(photoId))
            {
                var expected = originalBytes[Path.GetFileName(file.Path)];
                var actual = File.ReadAllBytes(file.Path);
                Assert(actual.SequenceEqual(expected), $"Expected restore to bring back original bytes for {Path.GetFileName(file.Path)}.");
            }

            CreateSnapshotDirectory(app.BackupDirectory, $"{photoId}_20000101_000000", targetFiles.Select(item => item.Path), DateTime.Now.AddDays(-400));
            CreateSnapshotDirectory(app.BackupDirectory, $"{photoId}_20010101_000000", targetFiles.Select(item => item.Path), DateTime.Now.AddDays(-200));

            var cleanup = app.CleanupBackups();
            Assert(cleanup.DeletedDirectoryCount >= 2, "Expected backup cleanup to remove expired snapshots.");
            Assert(app.GetBackupsForPhotoId(photoId).Count <= 1, "Expected backup cleanup to keep at most one current snapshot.");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup for test temp data.
            }
        }
    }

    private static void CreateImage(string path, Color color)
    {
        using var bitmap = new Bitmap(640, 360);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path);
    }

    private static void CreateEncryptedPhotoCache(
        string photoCache,
        string photoId,
        string sourceImage,
        int fileNameWidth,
        int fileNameHeight,
        int? actualWidth = null,
        int? actualHeight = null)
    {
        var encrypted = ReplacerApp.EncodeReplacementImageForCache(
            sourceImage,
            actualWidth ?? fileNameWidth,
            actualHeight ?? fileNameHeight);
        File.WriteAllBytes(Path.Combine(photoCache, $"{photoId}_{fileNameWidth}_{fileNameHeight}.jpg"), encrypted);
    }

    private static void CreateSnapshotDirectory(string backupRoot, string directoryName, IEnumerable<string> sourceFiles, DateTime lastWriteTime)
    {
        var dir = Path.Combine(backupRoot, directoryName);
        Directory.CreateDirectory(dir);
        foreach (var file in sourceFiles)
        {
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
        }

        Directory.SetLastWriteTime(dir, lastWriteTime);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
