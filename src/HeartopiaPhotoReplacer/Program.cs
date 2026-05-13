using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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

    public string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HeartopiaPhotoReplacer");

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public string Workspace { get; private set; } = string.Empty;
    public string ImageDirectory { get; private set; } = string.Empty;
    public string BackupDirectory { get; private set; } = string.Empty;
    public string LogDirectory { get; private set; } = string.Empty;
    public string PhotoDirectory { get; private set; } = DefaultPhotoDirectory();

    public bool Initialize()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var config = LoadConfig();

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
        Directory.CreateDirectory(ImageDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
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
            PhotoDir = PhotoDirectory
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

        var backupPath = Path.Combine(BackupDirectory, $"{targetId}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backupPath);

        var plans = targetFiles.Select(file => new ReplacementPlan(
            file,
            Path.Combine(backupPath, Path.GetFileName(file.Path)),
            file.Path + $".replace_{Guid.NewGuid():N}.tmp",
            EncryptBytes(ConvertToJpegBytes(sourceImage, file.Width, file.Height, 98))))
            .ToArray();

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
                log($"Replaced {Path.GetFileName(plan.File.Path)} ({plan.File.Width}x{plan.File.Height}).");
            }

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
}

internal sealed class MainForm : Form
{
    private readonly ReplacerApp _app;
    private readonly TextBox _workspaceBox = new();
    private readonly TextBox _photoCacheBox = new();
    private readonly ComboBox _sourceCombo = new();
    private readonly PictureBox _sourcePreview = new();
    private readonly PictureBox _targetPreview = new();
    private readonly DataGridView _photoGrid = new();
    private readonly TextBox _logBox = new();

    public MainForm(ReplacerApp app)
    {
        _app = app;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Heartopia Photo Replacer";
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
        _workspaceBox.Size = new Size(590, 24);
        _workspaceBox.ReadOnly = true;
        _workspaceBox.Text = _app.Workspace;
        Controls.Add(_workspaceBox);

        var changeWorkspace = new Button
        {
            Text = "Change",
            Location = new Point(695, 10),
            Size = new Size(80, 28)
        };
        changeWorkspace.Click += (_, _) => ChangeWorkspace();
        Controls.Add(changeWorkspace);

        var openImages = new Button
        {
            Text = "Open Images",
            Location = new Point(785, 10),
            Size = new Size(105, 28)
        };
        openImages.Click += (_, _) => OpenFolder(_app.ImageDirectory);
        Controls.Add(openImages);

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
            Location = new Point(795, 42),
            Size = new Size(105, 28)
        };
        changeCache.Click += (_, _) => ChangeCache();
        Controls.Add(changeCache);

        BuildSourceGroup();
        BuildTargetGroup();
        BuildPhotoGrid();
        BuildLogBox();

        Load += (_, _) =>
        {
            Log($"Workspace: {_app.Workspace}");
            Log($"Image folder: {_app.ImageDirectory}");
            Log($"Game photo cache: {_app.PhotoDirectory}");
            RefreshImages();
            RefreshPhotos();
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

        var hint = new Label
        {
            Text = "Tip: take a new photo in-game, then click Refresh Photos and select the newest ID.",
            Location = new Point(12, 185),
            Size = new Size(430, 35)
        };
        group.Controls.Add(hint);
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
        _photoCacheBox.Text = _app.PhotoDirectory;
        Log($"Photo cache selected: {_app.PhotoDirectory}");
        RefreshPhotos();
    }

    private void ChangeCache()
    {
        var selected = _app.PromptForVerifiedPhotoCache();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        _app.SetPhotoDirectory(selected);
        _photoCacheBox.Text = _app.PhotoDirectory;
        Log($"Photo cache changed: {_app.PhotoDirectory}");
        RefreshPhotos();
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
            return;
        }

        var path = _app.SmallestPreviewPath(id);
        if (string.IsNullOrWhiteSpace(path))
        {
            ReplacePicture(_targetPreview, null);
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
            MessageBox.Show(
                $"Replace complete.\nBackup: {backup}\n\nRefresh the album or restart the game if it still shows the old image.",
                "Complete");
        }
        catch (Exception ex)
        {
            ShowError("Replace failed", ex);
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
}

internal sealed record ReplacementPlan(PhotoFile File, string BackupPath, string TempPath, byte[] EncryptedBytes);

internal sealed record PhotoFile(string Id, int Width, int Height, string Path, DateTime LastWriteTime);

internal sealed record PhotoGroup(string Id, int FileCount, DateTime LastWriteTime, string Sizes);
