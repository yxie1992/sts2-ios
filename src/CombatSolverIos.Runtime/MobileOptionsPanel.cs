using Godot;

namespace CombatSolver.Mobile;

/// <summary>Built-in controls only; updates immutable options for the next search.</summary>
internal sealed class MobileOptionsPanel
{
    private MobileSolverOptions _options;
    private readonly Action<MobileSolverOptions> _changed;
    private readonly OptionButton _preset = Choice();
    private readonly OptionButton _potions = Choice();
    private readonly SpinBox _seconds = Number(1, 600, 1);
    private readonly SpinBox _nodes = Number(100, 1_000_000, 100);
    private readonly SpinBox _beam = Number(4, 512, 1);
    private readonly SpinBox _workers = Number(1, MobileSolverOptions.MaximumWorkers, 1);
    private readonly Label _summary;
    private readonly Action<bool> _setExpanded;
    private bool _refreshing;

    internal VBoxContainer Root { get; } = new();
    internal bool Expanded { get; private set; }
    internal event Action<bool>? ExpandedChanged;

    internal MobileOptionsPanel(MobileSolverOptions options, Action<MobileSolverOptions> changed)
    {
        _options = options.Validated();
        _changed = changed;
        Root.AddThemeConstantOverride("separation", 8);
        var toggle = new Button { Text = "Options ▸", CustomMinimumSize = new Vector2(0, 78), FocusMode = Control.FocusModeEnum.None };
        toggle.AddThemeFontSizeOverride("font_size", 25);
        Root.AddChild(toggle);
        var scroll = new ScrollContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 330),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        Root.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(body);
        foreach (string name in new[] { "Mobile · 5s / 3k", "Low · 60s / 60k", "Medium · 120s / 120k", "High · 180s / 250k", "Very high · 300s / 500k", "Custom" })
            _preset.AddItem(name);
        foreach (string name in new[] { "None", "Smart", "Require at least one" }) _potions.AddItem(name);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 10);
        body.AddChild(grid);
        Row(grid, "Preset", _preset);
        Row(grid, "Seconds", _seconds);
        Row(grid, "Nodes", _nodes);
        Row(grid, "Beam width", _beam);
        Row(grid, "Workers", _workers);
        Row(grid, "Potions", _potions);
        _summary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(430, 0),
        };
        _summary.AddThemeFontSizeOverride("font_size", 21);
        body.AddChild(_summary);
        _setExpanded = expanded =>
        {
            Expanded = expanded;
            scroll.Visible = Expanded;
            toggle.Text = Expanded ? "Options ▾" : "Options ▸";
            ExpandedChanged?.Invoke(Expanded);
        };
        toggle.Pressed += () => _setExpanded(!Expanded);
        _preset.ItemSelected += index => Update(_options with { Preset = (MobilePreset)index });
        _seconds.ValueChanged += value => Custom(o => o with { Seconds = (int)value });
        _nodes.ValueChanged += value => Custom(o => o with { Nodes = (int)value });
        _beam.ValueChanged += value => Custom(o => o with { BeamWidth = (int)value });
        _workers.ValueChanged += value => Update(_options with { Workers = (int)value });
        _potions.ItemSelected += index => Update(_options with { Potions = (SolverPotionPolicy)index });
        Refresh();
    }

    internal void Collapse() => _setExpanded(false);

    private void Custom(Func<MobileSolverOptions, MobileSolverOptions> update)
    {
        if (_refreshing) return;
        SolverSearchProfile prior = _options.Profile();
        Update(update(_options with
        {
            Preset = MobilePreset.Custom,
            Seconds = prior.SoftTimeBudgetMilliseconds / 1_000,
            Nodes = prior.MaxExpandedNodes,
            BeamWidth = prior.BeamWidth,
        }));
    }

    private void Update(MobileSolverOptions options)
    {
        if (_refreshing) return;
        _options = options.Validated();
        Refresh();
        _changed(_options);
    }

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            SolverSearchProfile profile = _options.Profile();
            _preset.Select((int)_options.Preset);
            _seconds.Value = profile.SoftTimeBudgetMilliseconds / 1_000;
            _nodes.Value = profile.MaxExpandedNodes;
            _beam.Value = profile.BeamWidth;
            _workers.Value = _options.Workers;
            _potions.Select((int)_options.Potions);
            _summary.Text = "Changes apply to the next search. Larger budgets and more workers use more memory. Search stops early if memory runs low.";
        }
        finally { _refreshing = false; }
    }

    private static void Row(GridContainer grid, string text, Control value)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        grid.AddChild(label);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(value);
    }

    private static OptionButton Choice()
    {
        var choice = new OptionButton { CustomMinimumSize = new Vector2(270, 74) };
        choice.AddThemeFontSizeOverride("font_size", 24);
        return choice;
    }

    private static SpinBox Number(double minimum, double maximum, double step)
    {
        var number = new SpinBox
        {
            MinValue = minimum, MaxValue = maximum, Step = step,
            CustomMinimumSize = new Vector2(270, 74),
        };
        number.AddThemeFontSizeOverride("font_size", 24);
        return number;
    }
}
