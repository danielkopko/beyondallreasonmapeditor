using System.ComponentModel;
using System.Globalization;
using MapCore.Structures;
using static MapEditor.PanelLayout;

namespace MapEditor;

/// <summary>
/// What a guardian tower does in one region of its zone (the band, inside, or outside): which teams it acts on (all,
/// or BAR's lobby teams 1 to 8), after how long there, and which unit types it destroys. Each change raises
/// <see cref="RuleChanged"/> with the whole rule.
/// </summary>
sealed class GuardianRegionEditor : BarGroupBox
{
    readonly Label about = Hint("");
    readonly CheckBox allTeams = new() { Text = "All", AutoSize = true, Margin = new Padding(3, 3, 6, 3) };
    readonly CheckBox[] teams = Enumerable.Range(1, GuardianRule.MaxTeam)
        .Select(t => new CheckBox { Text = t.ToString(CultureInfo.InvariantCulture), AutoSize = true, Margin = new Padding(0, 3, 6, 3) }).ToArray();
    readonly NumericUpDown delay = Number(0, (decimal)GuardianRule.MaxDelay, 0, 1, 5);
    readonly Button choose = new() { Text = "Choose…", AutoSize = true };
    readonly Label targetsText = new() { AutoSize = true, MaximumSize = new Size(230, 0), Margin = new Padding(3, 4, 3, 2) };
    readonly Label note = Hint("");
    readonly TableLayoutPanel grid;
    GuardianRule shown = GuardianRule.None;
    bool syncing;

    // A group of its own within the tower's group, laid out as every panel's groups are (PanelLayout.Group).
    public GuardianRegionEditor(GuardianRegion region)
    {
        Which = region;
        Minor = true;
        AutoSize = true;
        Dock = DockStyle.Fill;
        Padding = new Padding(4);
        grid = Grid();
        (Text, about.Text) = region switch
        {
            GuardianRegion.Inside => ("Inside the path", "Inside a closed path, beyond the band."),
            GuardianRegion.Outside => ("Outside the path", "Everywhere else on the map beyond the band, other towers' zones included."),
            _ => ("Crossing the path (the band)", "Within the distance of the path."),
        };
        AddWide(grid, about);
        // Teams on a row of their own: All, then BAR's lobby teams 1 to 8.
        var teamRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty, MaximumSize = new Size(230, 0) };
        teamRow.Controls.Add(new Label { Text = "Teams", AutoSize = true, Margin = new Padding(3, 6, 6, 3) });
        teamRow.Controls.Add(allTeams);
        teamRow.Controls.AddRange(teams);
        AddWide(grid, teamRow);
        AddRow(grid, "Delay", delay, "s");
        AddRow(grid, "Destroys", choose, "");
        AddWide(grid, targetsText);
        AddWide(grid, note);
        Controls.Add(grid);
        new ToolTip().SetToolTip(delay, "How long a unit may stay in this region before it is doomed: 0 is at once. Leaving starts the count again.");

        allTeams.CheckedChanged += (_, _) =>
        {
            foreach (var box in teams)
                box.Enabled = !allTeams.Checked && Enabled;
            Changed();
        };
        foreach (var box in teams)
            box.CheckedChanged += (_, _) => Changed();
        delay.ValueChanged += (_, _) => Changed();
        choose.Click += (_, _) => Choose();
    }

    /// <summary>The region it sets: band, inside or outside.</summary>
    public GuardianRegion Which { get; }

    /// <summary>BAR's standard units, for the targets dialog (null: none beside the editor).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UnitCatalog? Catalog { get; set; }

    /// <summary>The tower's name, for the targets dialog's title.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TowerName { get; set; } = "";

    /// <summary>The rule as set here changed (teams, delay or targets).</summary>
    public event EventHandler<GuardianRule>? RuleChanged;

    /// <summary>The rule as the controls have it.</summary>
    internal GuardianRule Rule => new(shown.Targets, allTeams.Checked ? null : teams.Where(b => b.Checked).Select(b => int.Parse(b.Text, CultureInfo.InvariantCulture)).ToList(),
        (double)delay.Value);

    internal string TargetsText => targetsText.Text;
    internal string NoteText => note.Text;

    /// <summary>Shows a tower's rule for this region; <paramref name="why"/>, when given, says why it doesn't act (an open path's inside).</summary>
    public void Show(GuardianRule rule, string? why = null)
    {
        syncing = true;
        shown = rule;
        allTeams.Checked = rule.AllTeams;
        foreach (var box in teams)
        {
            box.Checked = rule.Teams?.Contains(int.Parse(box.Text, CultureInfo.InvariantCulture)) == true;
            box.Enabled = !rule.AllTeams;
        }
        delay.Value = Math.Clamp((decimal)rule.DelaySeconds, delay.Minimum, delay.Maximum);
        targetsText.Text = rule.Targets.Count == 0 ? "Nothing: every unit passes." : AssetsPanel.TargetsSummary(rule.Targets, Catalog) + Teams(rule) + Delay(rule);
        note.Text = why ?? "";
        choose.Enabled = Catalog != null;
        syncing = false;
    }

    static string Teams(GuardianRule rule) => rule.AllTeams ? ", of every team" : rule.Teams!.Count == 0 ? ", of no team (so nothing)" : $", of team{(rule.Teams.Count == 1 ? "" : "s")} {string.Join(", ", rule.Teams)}";

    static string Delay(GuardianRule rule) => rule.DelaySeconds > 0
        ? string.Create(CultureInfo.CurrentCulture, $", after {rule.DelaySeconds:0.#} s there.")
        : ", at once.";

    void Changed()
    {
        if (syncing)
            return;
        var rule = Rule;
        if (!rule.Same(shown))
            RuleChanged?.Invoke(this, rule);
    }

    void Choose()
    {
        if (Catalog == null)
            return;
        using var dialog = new TargetsDialog(Catalog, shown.Targets, $"{TowerName} ({Text.ToLowerInvariant()})");
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            SetTargets(dialog.Chosen);
    }

    /// <summary>New targets (as OK in the dialog gives them), keeping the teams and delay.</summary>
    internal void SetTargets(IReadOnlyList<string> targets)
    {
        var rule = Rule with { Targets = targets };
        if (!rule.Same(shown))
            RuleChanged?.Invoke(this, rule);
    }

    /// <summary>For the self-test: ticks the teams (null: All) and sets the delay through the controls, as one change.</summary>
    internal void SetTeamsAndDelay(IReadOnlyList<int>? lobbyTeams, double seconds)
    {
        syncing = true;
        allTeams.Checked = lobbyTeams == null;
        foreach (var box in teams)
            box.Checked = lobbyTeams?.Contains(int.Parse(box.Text, CultureInfo.InvariantCulture)) == true;
        delay.Value = (decimal)seconds;
        syncing = false;
        Changed();
    }

    /// <summary>For the self-test: ticks or unticks one lobby team's box, as a click does.</summary>
    internal void ClickTeam(int lobbyTeam) => teams[lobbyTeam - 1].Checked = !teams[lobbyTeam - 1].Checked;
    internal void ClickAll() => allTeams.Checked = !allTeams.Checked;
}
