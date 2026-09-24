using System.Windows;
using System.Windows.Controls;
using DsxLite.Core.DualSense;

namespace DsxLite.App;

/// <summary>
/// Editor for one adaptive trigger: effect mode dropdown plus dynamically
/// generated parameter sliders.
/// </summary>
public partial class TriggerEffectControl : UserControl
{
    private sealed record ModeEntry(TriggerEffectMode Mode, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly ModeEntry[] Modes =
    [
        new(TriggerEffectMode.Off, "关闭"),
        new(TriggerEffectMode.ContinuousResistance, "连续阻力"),
        new(TriggerEffectMode.SectionResistance, "分段阻力"),
        new(TriggerEffectMode.Feedback, "触感反馈"),
        new(TriggerEffectMode.Weapon, "枪械扳机"),
        new(TriggerEffectMode.Vibration, "振动"),
        new(TriggerEffectMode.Bow, "弓弦"),
        new(TriggerEffectMode.Galloping, "马蹄"),
        new(TriggerEffectMode.Machine, "机械"),
    ];

    private readonly List<(TextBlock Label, Slider Slider, TextBlock Value)> _paramRows = [];

    /// <summary>Raised when the user clicks 应用/关闭效果 with the effect to send.</summary>
    public event EventHandler<TriggerEffect>? EffectRequested;

    public TriggerEffectControl()
    {
        InitializeComponent();
        ModeCombo.ItemsSource = Modes;
        ModeCombo.SelectedIndex = 0;
    }

    private TriggerEffectMode SelectedMode =>
        ModeCombo.SelectedItem is ModeEntry entry ? entry.Mode : TriggerEffectMode.Off;

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
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
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var label = new TextBlock
            {
                Text = param.Label,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var slider = new Slider
            {
                Minimum = param.Min,
                Maximum = param.Max,
                Value = param.Default,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var value = new TextBlock
            {
                Text = param.Default.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
            };

            slider.ValueChanged += (_, args) => value.Text = ((int)args.NewValue).ToString();

            Grid.SetColumn(label, 0);
            Grid.SetColumn(slider, 1);
            Grid.SetColumn(value, 2);
            grid.Children.Add(label);
            grid.Children.Add(slider);
            grid.Children.Add(value);
            ParamsPanel.Children.Add(grid);
            _paramRows.Add((label, slider, value));
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
