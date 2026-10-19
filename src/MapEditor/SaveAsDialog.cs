namespace MapEditor;

/// <summary>
/// Asks for the new map's name, version and archive file. The name and version may not both stay as they were
/// when the map was opened: BAR would treat the edited map as the original (PLAN.md rule 2).
/// </summary>
sealed class SaveAsDialog : Form
{
    readonly TextBox nameBox = new() { Width = 320 };
    readonly TextBox versionBox = new() { Width = 320 };
    readonly TextBox fileBox = new() { Width = 320 };
    readonly Label problem = new() { AutoSize = true, ForeColor = BarTheme.Error, MaximumSize = new Size(440, 0) };
    readonly Button ok = new() { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
    readonly CheckBox copyToMaps = new() { Text = "Also copy it into BAR's maps folder:", AutoSize = true };
    readonly TextBox mapsBox = new() { Width = 320 };
    readonly string? loadedName, loadedVersion, sourceArchive, lastSaved;
    bool fileEdited;

    /// <param name="lastSaved">The archive this session last saved, if any: the suggested file starts in its folder.</param>
    /// <param name="mapsFolder">BAR's maps folder as remembered (or found), and whether the copy was on last time.</param>
    public SaveAsDialog(string? name, string? version, string? loadedName, string? loadedVersion, string? sourceArchive, string? lastSaved = null,
        string? mapsFolder = null, bool copy = false)
    {
        this.lastSaved = lastSaved;
        this.loadedName = loadedName;
        this.loadedVersion = loadedVersion;
        this.sourceArchive = sourceArchive;

        Text = "Save As";
        Font = BarTheme.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => Browse();
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        grid.Controls.Add(new Label { Text = "Map name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(nameBox, 1, 0);
        grid.Controls.Add(new Label { Text = "Version:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(versionBox, 1, 1);
        grid.Controls.Add(new Label { Text = "Archive file:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(fileBox, 1, 2);
        grid.Controls.Add(browse, 2, 2);
        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = BarTheme.Dim,
            Text = "BAR knows a map by its name and version, so at least one must differ from the original. " +
                   "Use .sd7 (7-Zip) or .sdz (zip).",
        };
        grid.Controls.Add(hint, 1, 3);
        grid.Controls.Add(copyToMaps, 1, 4);
        var browseMaps = new Button { Text = "Browse…", AutoSize = true };
        browseMaps.Click += (_, _) => BrowseMaps();
        grid.Controls.Add(new Label { Text = "Maps folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        grid.Controls.Add(mapsBox, 1, 5);
        grid.Controls.Add(browseMaps, 2, 5);
        grid.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = BarTheme.Dim,
            Text = "The folder is checked first for another map with the same name and version. Nothing there is replaced without asking.",
        }, 1, 6);
        grid.Controls.Add(problem, 1, 7);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        grid.Controls.Add(buttons, 1, 8);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;

        mapsBox.Text = mapsFolder ?? "";
        copyToMaps.Checked = copy && !string.IsNullOrEmpty(mapsFolder);
        copyToMaps.CheckedChanged += (_, _) => Validate();
        mapsBox.TextChanged += (_, _) => Validate();
        nameBox.Text = name ?? "";
        versionBox.Text = name == loadedName && version == loadedVersion ? $"{version}-edit1" : version ?? "";
        fileBox.Text = SuggestedPath();
        nameBox.TextChanged += (_, _) => NameOrVersionChanged();
        versionBox.TextChanged += (_, _) => NameOrVersionChanged();
        fileBox.TextChanged += (_, _) =>
        {
            fileEdited = fileBox.Focused || fileEdited;
            Validate();
        };
        Validate();
        BarTheme.Apply(this);
    }

    public string MapName => nameBox.Text.Trim();
    public string Version => versionBox.Text.Trim();
    public string ArchivePath => fileBox.Text.Trim();

    /// <summary>The maps folder to copy into, or null when the copy is off.</summary>
    public string? CopyTo => copyToMaps.Checked ? mapsBox.Text.Trim() : null;

    public string MapsFolder => mapsBox.Text.Trim();

    void NameOrVersionChanged()
    {
        if (!fileEdited)
            fileBox.Text = SuggestedPath();
        Validate();
    }

    new void Validate()
    {
        var why = Problem();
        problem.Text = why ?? "";
        ok.Enabled = why == null;
    }

    string? Problem() =>
        Check(MapName, Version, loadedName, loadedVersion, ArchivePath, sourceArchive) ??
        (copyToMaps.Checked && !Directory.Exists(MapsFolder) ? "Choose BAR's maps folder (it must exist), or untick the copy." : null);

    void BrowseMaps()
    {
        using var dialog = new FolderBrowserDialog { Description = "BAR's maps folder", UseDescriptionForTitle = true, SelectedPath = MapsFolder };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            mapsBox.Text = dialog.SelectedPath;
            copyToMaps.Checked = true;
        }
    }

    /// <summary>Why these Save As values are not allowed, or null if they are.</summary>
    public static string? Check(string name, string version, string? loadedName, string? loadedVersion, string archivePath, string? sourceArchive)
    {
        name = name.Trim();
        version = version.Trim();
        if (name.Length == 0)
            return "Enter a map name.";
        if (string.Equals(name, loadedName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(version, loadedVersion?.Trim() ?? "", StringComparison.OrdinalIgnoreCase))
            return $"\"{name} {version}\" is the original map's name and version. Change the name or the version.";
        if (archivePath.Length == 0)
            return "Choose an archive file.";
        var extension = Path.GetExtension(archivePath).ToLowerInvariant();
        if (extension is not (".sd7" or ".sdz"))
            return "The archive must end in .sd7 or .sdz.";
        try
        {
            if (!Path.IsPathFullyQualified(archivePath))
                return "Give the full path of the archive file.";
            if (sourceArchive != null && MapCore.FileSystemUtil.SamePath(archivePath, sourceArchive))
                return "That is the archive the map was opened from. Save under a new file name.";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That is not a valid file path.";
        }
        return null;
    }

    void Browse()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Spring map, 7-Zip (*.sd7)|*.sd7|Spring map, zip (*.sdz)|*.sdz",
            FileName = Path.GetFileName(ArchivePath),
            InitialDirectory = SafeDirectory(ArchivePath),
            OverwritePrompt = false, // asked once, by the main window, when saving
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            fileEdited = true;
            fileBox.Text = dialog.FileName;
        }
    }

    string SuggestedPath()
    {
        var current = fileBox.Text.Length > 0 ? fileBox.Text : lastSaved ?? sourceArchive ?? "";
        var folder = SafeDirectory(current);
        var extension = Path.GetExtension(current);
        if (extension is not (".sd7" or ".sdz"))
            extension = ".sd7";
        var stem = Slug(MapName) + (Version.Length > 0 ? "_" + Slug(Version) : "");
        return Path.Combine(folder, (stem.Length > 0 ? stem : "map") + extension);
    }

    static string SafeDirectory(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                return dir;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>Lower case, with anything but letters, digits, '.', '-' and '_' turned into '_'.</summary>
    static string Slug(string text)
    {
        var chars = text.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        return string.Join("_", new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
    }
}
