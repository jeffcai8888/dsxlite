using System.Windows;
using DsxLite.Core.DualSense;
using UserControl = System.Windows.Controls.UserControl;

namespace DsxLite.App;

/// <summary>
/// Editor for one adaptive trigger: effect mode dropdown plus dynamically
/// generated parameter sliders. Rebuilds its labels on language change.
/// </summary>
public partial class TriggerEffectControl : UserControl
{
    private sealed record ModeEntry(TriggerEffectMode Mode, string NameKey)
    {
        public override string ToString() => Localization.Get(NameKey);
    }

    private static readonly ModeEntry[] Modes =
    [
        new(TriggerEffectMode.Off, "TrigOff"),
        new(TriggerEffectMode.ContinuousResistance, "TrigContinuous"),
        new(TriggerEffectMode.SectionResistance, "TrigSection"),
        new(TriggerEffectMode.Feedback, "TrigFeedback"),
        new(TriggerEffectMode.Weapon, "TrigWeapon"),
        new(TriggerEffectMode.Vibration, "TrigVibration"),
        new(TriggerEffectMode.Bow, "TrigBow"),
        new(TriggerEffectMode.Galloping, "TrigGalloping"),
        new(TriggerEffectMode.Machine, "TrigMachine"),
    ];

    private readonly List<(System.Windows.Controls.TextBlock Label, string LabelKey, System.Windows.Controls.Slider Slider, System.Windows.Controls.TextBlock Value)> _paramRows = [];

    /// <summary>Raised when the user clicks apply/off with the effect to send.</summary>
    public event EventHandler<TriggerEffect>? EffectRequested;

    public TriggerEffectControl()
    {
        InitializeComponent();
        ModeCombo.ItemsSource = Modes;
        ModeCombo.SelectedIndex = 0;
        Localization.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // ComboBox caches ToString() of items; force a rebuild keeping the selection.
        int index = ModeCombo.SelectedIndex;
        ModeCombo.ItemsSource = null;
        ModeCombo.ItemsSource = Modes;
        ModeCombo.SelectedIndex = index >= 0 ? index : 0;
        // SelectionChanged only fires when the index actually changed; rebuild params either way.
        RebuildParams();
    }

    private TriggerEffectMode SelectedMode =>
        ModeCombo.SelectedItem is ModeEntry entry ? entry.Mode : TriggerEffectMode.Off;

    private void OnModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RebuildParams();
    }

    private void RebuildParams()
    {
        ParamsPanel.Children.Clear();
        _paramRows.Clear();

        TriggerParamSpec[] spec = TriggerEffect.GetParamSpec(SelectedMode);
        foreach (TriggerParamSpec param in spec)
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(36) });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = Localization.Get(param.Label), // Core labels are keyed by their Chinese text
                VerticalAlignment = VerticalAlignment.Center,
            };
            var slider = new System.Windows.Controls.Slider
            {
                Minimum = param.Min,
                Maximum = param.Max,
                Value = param.Default,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var value = new System.Windows.Controls.TextBlock
            {
                Text = param.Default.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
            };

            slider.ValueChanged += (_, args) => value.Text = ((int)args.NewValue).ToString();

            System.Windows.Controls.Grid.SetColumn(label, 0);
            System.Windows.Controls.Grid.SetColumn(slider, 1);
            System.Windows.Controls.Grid.SetColumn(value, 2);
            grid.Children.Add(label);
            grid.Children.Add(slider);
            grid.Children.Add(value);
            ParamsPanel.Children.Add(grid);
            _paramRows.Add((label, param.Label, slider, value));
        }
    }

    private TriggerEffect BuildEffect()
    {
        TriggerEffectMode mode = SelectedMode;
        int[] values = _paramRows.Select(r => (int)r.Slider.Value).ToArray();
        var effect = new TriggerEffect { Mode = mode };
        for (int i = 0; i < values.Length && i < 9; i++)
            effect.Params[i] = (byte)Math.Clamp(values[i], 0, 255);
        return effect;
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e) =>
        EffectRequested?.Invoke(this, BuildEffect());

    private void OnOffClicked(object sender, RoutedEventArgs e)
    {
        ModeCombo.SelectedIndex = 0;
        EffectRequested?.Invoke(this, TriggerEffect.Off());
    }
}
