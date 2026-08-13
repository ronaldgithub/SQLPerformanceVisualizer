using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.Controls;

/// <summary>
/// Native, dependency-free animated diagram of a showplan operator tree: leaves (scans) continuously
/// spawn particles that flow rightward along the tree, getting discarded or passed on at each operator
/// based on that operator's own actual-vs-child-rows ratio, finally landing in a "rows out" terminal box.
/// Illustrative (like the "database animation" style), not a literal per-row simulation.
/// </summary>
public class ExecutionPlanAnimationControl : ContentControl
{
    public static readonly StyledProperty<PlanOperatorNode?> PlanProperty =
        AvaloniaProperty.Register<ExecutionPlanAnimationControl, PlanOperatorNode?>(nameof(Plan));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<ExecutionPlanAnimationControl, bool>(
            nameof(IsPlaying), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<ExecutionPlanAnimationControl, bool>(
            nameof(IsPaused), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsSerialPlanProperty =
        AvaloniaProperty.Register<ExecutionPlanAnimationControl, bool>(nameof(IsSerialPlan), defaultValue: true);

    public PlanOperatorNode? Plan
    {
        get => GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>True while a playback session is frozen mid-animation (timer stopped, state kept) rather than idle/finished.</summary>
    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    /// <summary>
    /// Whether the plan ran at DOP 1. The self-elapsed-time-by-operator bar only means something on a
    /// serial plan — on a parallel plan, operator self-times overlap across threads and don't sum cleanly.
    /// </summary>
    public bool IsSerialPlan
    {
        get => GetValue(IsSerialPlanProperty);
        set => SetValue(IsSerialPlanProperty, value);
    }

    private static readonly CultureInfo Nl = new("nl-NL");

    private const double BaseRowHeight = 108;
    private const double ColWidth = 230;
    private const double BoxWidth = 190;
    private const double BoxHeight = 74;
    private const double AnimDurationMs = 9000;
    private const double PixelsPerMs = 0.22;
    private const double ParticleRadius = 5;

    private const double ThreadGridWidth = BoxWidth; // sits directly under the box, same width
    private const double ThreadGridGapBelow = 6;
    private const double ThreadGridHeaderHeight = 20;
    private const double ThreadGridRowHeight = 15;

    private static readonly IBrush BoxBg = Brush.Parse("#252525");
    private static readonly IBrush BoxBorder = Brush.Parse("#4A4A4A");
    private static readonly IBrush RootBorder = Brush.Parse("#5B8DEF");
    private static readonly IBrush RejectBrush = Brush.Parse("#E05C5C");
    private static readonly IBrush EdgeBrush = Brush.Parse("#555555");
    private static readonly IBrush TitleBrush = Brush.Parse("#FFFFFF");
    private static readonly IBrush MetaBrush = Brush.Parse("#9A9A9A");
    private static readonly IBrush ObjectBrush = Brush.Parse("#CE9178");
    private static readonly IBrush ParticleBrush = Brush.Parse("#5B8DEF");
    private static readonly FontFamily MonoFont = new("Consolas,monospace");

    private static readonly IBrush GridPanelBg = Brush.Parse("#1E1E1E");
    private static readonly IBrush GridHeaderBg = Brush.Parse("#33456B");
    private static readonly IBrush ThreadLabelBrush = Brush.Parse("#7FB3D5");

    private static readonly IBrush[] SelfTimePalette =
    [
        Brush.Parse("#1F6F8B"), // teal
        Brush.Parse("#93641B"), // amber
        Brush.Parse("#6A5A8A"), // purple
        Brush.Parse("#3D7A4F"), // green
        Brush.Parse("#8A4A4A"), // brick
        Brush.Parse("#4A6A8A"), // steel blue
        Brush.Parse("#7A5A2A"), // brown
        Brush.Parse("#5A7A7A"), // slate
    ];

    private Canvas? _canvas;
    private TextBlock? _elapsedText;
    private TextBlock? _scannedText;
    private TextBlock? _outText;

    private readonly Dictionary<PlanOperatorNode, Point> _outAnchors = new();
    private readonly Dictionary<PlanOperatorNode, Point> _inAnchors = new();
    private readonly Dictionary<PlanOperatorNode, PlanOperatorNode?> _parentOf = new();
    private readonly Dictionary<PlanOperatorNode, Border> _boxes = new();
    private readonly Dictionary<PlanOperatorNode, double> _flashUntilMs = new();
    private readonly List<LeafSpawner> _leafSpawners = new();
    private readonly List<Particle> _particles = new();
    private readonly List<(TextBlock Text, long Final)> _threadValueBlocks = new();

    private PlanOperatorNode? _root;
    private Point _outputAnchor;
    private double _maxLeafSelf = 1;
    private long _totalLeafRows;
    private double _totalElapsedTarget;
    private int _nextLeafRow;

    private DispatcherTimer? _timer;
    private DateTime _playStart;
    private DateTime? _pausedAt;
    private bool _internalUpdate;
    private readonly Random _rng = new();

    private sealed class LeafSpawner
    {
        public PlanOperatorNode Node = null!;
        public double NextSpawnMs;
        public double IntervalMs;
    }

    private sealed class Particle
    {
        public Ellipse Visual = null!;
        public Point Start;
        public Point End;
        public double StartMs;
        public double DurationMs;
        public PlanOperatorNode? ArrivingAt;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_internalUpdate) return;

        if (change.Property == PlanProperty || change.Property == IsSerialPlanProperty)
        {
            if (IsPlaying || IsPaused) SetPlaybackState(playing: false, paused: false);
            Rebuild();
        }
        else if (change.Property == IsPlayingProperty)
        {
            if (IsPlaying)
            {
                if (IsPaused) Resume(); else StartPlayback();
            }
            else
            {
                Pause();
            }
        }
    }

    /// <summary>Sets both playback bindable properties at once without re-triggering this control's own reaction.</summary>
    private void SetPlaybackState(bool playing, bool paused)
    {
        _internalUpdate = true;
        SetCurrentValue(IsPlayingProperty, playing);
        SetCurrentValue(IsPausedProperty, paused);
        _internalUpdate = false;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    private void Rebuild()
    {
        _timer?.Stop();
        _outAnchors.Clear();
        _inAnchors.Clear();
        _parentOf.Clear();
        _boxes.Clear();
        _flashUntilMs.Clear();
        _leafSpawners.Clear();
        _particles.Clear();
        _threadValueBlocks.Clear();

        _root = Plan;
        if (_root is null)
        {
            Content = null;
            return;
        }

        var col = new Dictionary<PlanOperatorNode, int>();
        var row = new Dictionary<PlanOperatorNode, double>();
        _nextLeafRow = 0;
        AssignColumns(_root, col);
        AssignRows(_root, row);

        var allNodes = Flatten(_root).ToList();
        var maxCol = col.Values.Max();

        const double padX = 16, padY = 16;

        // Rows grow taller than the default when a node in them carries a thread-breakdown grid
        // below its box, so the grid has room without overlapping the row beneath it.
        var maxGridHeight = allNodes.Count == 0 ? 0 : allNodes.Max(ThreadGridHeightFor);
        var rowHeight = maxGridHeight <= 0 ? BaseRowHeight : Math.Max(BaseRowHeight, BoxHeight + ThreadGridGapBelow + maxGridHeight + 16);

        var leafCount = Math.Max(1, _nextLeafRow);
        var canvas = new Canvas
        {
            Width = padX * 2 + (maxCol + 2) * ColWidth,
            Height = padY * 2 + leafCount * rowHeight,
            Background = Brushes.Transparent,
        };

        var leaves = new List<PlanOperatorNode>();
        var edges = new List<(PlanOperatorNode Child, PlanOperatorNode Parent)>();

        void Walk(PlanOperatorNode node, PlanOperatorNode? parent)
        {
            _parentOf[node] = parent;
            var x = padX + col[node] * ColWidth;
            var y = padY + row[node] * rowHeight;

            var box = BuildBox(node);
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            canvas.Children.Add(box);
            _boxes[node] = box;

            _inAnchors[node] = new Point(x, y + BoxHeight / 2);
            _outAnchors[node] = new Point(x + BoxWidth, y + BoxHeight / 2);

            if (node.ThreadRows.Count > 1)
            {
                var gridPanel = BuildThreadGrid(node);
                Canvas.SetLeft(gridPanel, x);
                Canvas.SetTop(gridPanel, y + BoxHeight + ThreadGridGapBelow);
                canvas.Children.Add(gridPanel);
            }

            if (node.Children.Count == 0) leaves.Add(node);
            foreach (var child in node.Children)
            {
                Walk(child, node);
                edges.Add((child, node));
            }
        }
        Walk(_root, null);

        var outX = padX + (maxCol + 1) * ColWidth;
        var outY = padY + row[_root] * rowHeight;
        var outBox = BuildOutputBox(_root);
        Canvas.SetLeft(outBox, outX);
        Canvas.SetTop(outBox, outY);
        canvas.Children.Add(outBox);
        _outputAnchor = new Point(outX, outY + BoxHeight / 2);

        var insertAt = 0;
        foreach (var (child, parent) in edges)
            canvas.Children.Insert(insertAt++, MakeEdgeLine(_outAnchors[child], _inAnchors[parent]));
        canvas.Children.Insert(insertAt, MakeEdgeLine(_outAnchors[_root], _outputAnchor));

        _maxLeafSelf = leaves.Count == 0 ? 1 : leaves.Max(l => Math.Max(l.SelfElapsedMs, 0.01));
        foreach (var leaf in leaves)
            _leafSpawners.Add(new LeafSpawner { Node = leaf, IntervalMs = ComputeSpawnInterval(leaf), NextSpawnMs = 0 });

        _totalLeafRows = leaves.Sum(l => l.RowsForDisplay);
        _totalElapsedTarget = _root.ActualElapsedMs ?? _root.EstimatedSubtreeCost;

        _canvas = canvas;

        var statsBar = BuildStatsBar();
        var dock = new DockPanel();
        DockPanel.SetDock(statsBar, Dock.Top);
        dock.Children.Add(statsBar);

        var selfTimeBar = BuildSelfTimeBar(_root, IsSerialPlan);
        if (selfTimeBar is not null)
        {
            DockPanel.SetDock(selfTimeBar, Dock.Top);
            dock.Children.Add(selfTimeBar);
        }

        dock.Children.Add(canvas);
        Content = dock;

        ShowFinalStatics();
    }

    private static Control? BuildSelfTimeBar(PlanOperatorNode root, bool isSerial)
    {
        if (!isSerial)
        {
            return new TextBlock
            {
                Text = "Self-elapsed-time breakdown hidden — this plan ran parallel (DOP > 1), " +
                       "where operator self-times overlap across threads and don't sum to a clean total.",
                Foreground = MetaBrush, FontSize = 11, FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12),
            };
        }

        var flat = new List<PlanOperatorNode>();
        void Collect(PlanOperatorNode n)
        {
            foreach (var c in n.Children) Collect(c);
            flat.Add(n);
        }
        Collect(root);

        var totalSelf = flat.Sum(n => n.SelfElapsedMs);
        if (totalSelf <= 0) return null;
        flat = flat.Where(n => n.SelfElapsedMs / totalSelf > 0.005).ToList();
        if (flat.Count == 0) return null;

        var bar = new Grid { Height = 28, ClipToBounds = true };
        var legend = new WrapPanel { Margin = new Thickness(4, 8, 4, 0) };

        for (var i = 0; i < flat.Count; i++)
        {
            var node = flat[i];
            var brush = SelfTimePalette[i % SelfTimePalette.Length];
            var label = ShortLabel(node);

            bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(node.SelfElapsedMs, 0.01), GridUnitType.Star)));
            var segment = new Border
            {
                Background = brush,
                Child = new TextBlock
                {
                    Text = $"{label} · {node.SelfElapsedMs.ToString("N0", Nl)} ms",
                    Foreground = Brushes.White, FontSize = 10, FontFamily = MonoFont,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            Grid.SetColumn(segment, i);
            bar.Children.Add(segment);

            var legendItem = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 16, 4) };
            legendItem.Children.Add(new Border { Background = brush, Width = 9, Height = 9, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            legendItem.Children.Add(new TextBlock { Text = label, Foreground = MetaBrush, FontSize = 10, FontFamily = MonoFont });
            legend.Children.Add(legendItem);
        }

        var outer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        outer.Children.Add(new TextBlock
        {
            Text = "SELF ELAPSED TIME, BY OPERATOR", Foreground = MetaBrush, FontSize = 10,
            FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0, 0, 0),
        });
        outer.Children.Add(new Border
        {
            BorderBrush = BoxBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            ClipToBounds = true, Child = bar,
        });
        outer.Children.Add(legend);
        return outer;
    }

    private static string ShortLabel(PlanOperatorNode node) =>
        node.ObjectLabel is null ? node.PhysicalOp : $"{node.PhysicalOp}, {node.ObjectLabel.Split(" AS ")[0]}";

    private static IEnumerable<PlanOperatorNode> Flatten(PlanOperatorNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }

    private static double ThreadGridHeightFor(PlanOperatorNode node) =>
        node.ThreadRows.Count <= 1 ? 0 : ThreadGridHeaderHeight + node.ThreadRows.Count * ThreadGridRowHeight;

    private static int AssignColumns(PlanOperatorNode node, Dictionary<PlanOperatorNode, int> col)
    {
        if (node.Children.Count == 0)
        {
            col[node] = 0;
            return 0;
        }
        var maxChildCol = node.Children.Select(c => AssignColumns(c, col)).Max();
        var myCol = maxChildCol + 1;
        col[node] = myCol;
        return myCol;
    }

    private double AssignRows(PlanOperatorNode node, Dictionary<PlanOperatorNode, double> row)
    {
        if (node.Children.Count == 0)
        {
            double r = _nextLeafRow++;
            row[node] = r;
            return r;
        }
        var avg = node.Children.Select(c => AssignRows(c, row)).Average();
        row[node] = avg;
        return avg;
    }

    private Border BuildBox(PlanOperatorNode node)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(10, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = node.PhysicalOp, Foreground = TitleBrush, FontWeight = FontWeight.SemiBold,
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
        if (!string.Equals(node.PhysicalOp, node.LogicalOp, StringComparison.OrdinalIgnoreCase))
            stack.Children.Add(new TextBlock { Text = node.LogicalOp, Foreground = MetaBrush, FontSize = 10 });
        if (node.ObjectLabel is not null)
            stack.Children.Add(new TextBlock
            {
                Text = node.ObjectLabel, Foreground = ObjectBrush, FontFamily = MonoFont,
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
            });
        stack.Children.Add(new TextBlock
        {
            Text = $"{node.RowsLabel} · {node.SelfTimeLabel}", Foreground = MetaBrush,
            FontFamily = MonoFont, FontSize = 9,
        });

        return new Border
        {
            Width = BoxWidth, Height = BoxHeight,
            Background = BoxBg,
            BorderBrush = NormalBorderBrush(node),
            BorderThickness = new Thickness(node == _root ? 2 : 1),
            CornerRadius = new CornerRadius(4),
            Child = stack,
        };
    }

    private IBrush NormalBorderBrush(PlanOperatorNode node) =>
        node == _root ? RootBorder : BoxBorder;

    private Border BuildOutputBox(PlanOperatorNode root)
    {
        var stack = new StackPanel
        {
            Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new TextBlock
        {
            Text = "ROWS OUT", Foreground = MetaBrush, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = FormatCount(root.RowsForDisplay), Foreground = TitleBrush, FontSize = 22,
            FontWeight = FontWeight.Bold, FontFamily = MonoFont, HorizontalAlignment = HorizontalAlignment.Center,
        });
        return new Border
        {
            Width = 150, Height = BoxHeight,
            Background = BoxBg, BorderBrush = RootBorder, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4), Child = stack,
        };
    }

    private static Line MakeEdgeLine(Point from, Point to) => new()
    {
        StartPoint = from, EndPoint = to,
        Stroke = EdgeBrush, StrokeThickness = 1.5,
        StrokeDashArray = new AvaloniaList<double> { 4, 3 },
    };

    /// <summary>
    /// Small property-grid-style panel beside a parallel operator's box, one row per worker thread —
    /// same shape as SSMS's "Actual Number of Rows" thread breakdown. Values start each Play at 0 and
    /// count up to their real totals in <see cref="OnTick"/>, on the same clock as the top stats.
    /// </summary>
    private Border BuildThreadGrid(PlanOperatorNode node)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(ThreadGridHeaderHeight, GridUnitType.Pixel)));
        for (var i = 0; i < node.ThreadRows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(ThreadGridRowHeight, GridUnitType.Pixel)));

        void AddRow(int r, string label, IBrush labelBrush, TextBlock valueBlock, IBrush? rowBg)
        {
            if (rowBg is not null)
            {
                var bg = new Border { Background = rowBg };
                Grid.SetRow(bg, r);
                Grid.SetColumnSpan(bg, 2);
                grid.Children.Add(bg);
            }

            var labelText = new TextBlock
            {
                Text = label, Foreground = labelBrush, FontSize = 9, FontFamily = MonoFont,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0),
            };
            Grid.SetRow(labelText, r);
            Grid.SetColumn(labelText, 0);
            grid.Children.Add(labelText);

            valueBlock.HorizontalAlignment = HorizontalAlignment.Right;
            valueBlock.VerticalAlignment = VerticalAlignment.Center;
            valueBlock.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetRow(valueBlock, r);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
        }

        var headerValue = new TextBlock { Foreground = TitleBrush, FontSize = 9, FontWeight = FontWeight.SemiBold, FontFamily = MonoFont };
        AddRow(0, "ALL THREADS", TitleBrush, headerValue, GridHeaderBg);
        _threadValueBlocks.Add((headerValue, node.RowsForDisplay));

        for (var i = 0; i < node.ThreadRows.Count; i++)
        {
            var t = node.ThreadRows[i];
            var valueText = new TextBlock { Foreground = MetaBrush, FontSize = 9, FontFamily = MonoFont };
            AddRow(i + 1, $"Thread {t.ThreadId}", ThreadLabelBrush, valueText, null);
            _threadValueBlocks.Add((valueText, t.ActualRows));
        }

        return new Border
        {
            Width = ThreadGridWidth,
            Background = GridPanelBg,
            BorderBrush = BoxBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), ClipToBounds = true,
            Child = grid,
        };
    }

    private StackPanel BuildStatsBar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, Margin = new Thickness(4, 0, 4, 10) };
        panel.Children.Add(StatBlock("ELAPSED", out _elapsedText));
        panel.Children.Add(StatBlock("ROWS SCANNED", out _scannedText));
        panel.Children.Add(StatBlock("ROWS OUT", out _outText));
        return panel;
    }

    private static StackPanel StatBlock(string label, out TextBlock valueBlock)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = label, Foreground = MetaBrush, FontSize = 10, FontWeight = FontWeight.SemiBold });
        valueBlock = new TextBlock { Text = "—", Foreground = TitleBrush, FontSize = 16, FontFamily = MonoFont };
        stack.Children.Add(valueBlock);
        return stack;
    }

    private double ComputeSpawnInterval(PlanOperatorNode leaf)
    {
        var self = Math.Max(leaf.SelfElapsedMs, 0.01);
        var raw = 1400.0 * (_maxLeafSelf / self);
        return Math.Clamp(raw, 450, 2600);
    }

    private void ShowFinalStatics()
    {
        if (_root is null) return;
        if (_elapsedText is not null) _elapsedText.Text = FormatElapsed(_totalElapsedTarget, _root.HasActuals);
        if (_scannedText is not null) _scannedText.Text = FormatCount(_totalLeafRows);
        if (_outText is not null) _outText.Text = FormatCount(_root.RowsForDisplay);
        foreach (var (text, final) in _threadValueBlocks) text.Text = FormatCount(final);
    }

    private void StartPlayback()
    {
        if (_root is null || _canvas is null) return;

        foreach (var p in _particles) _canvas.Children.Remove(p.Visual);
        _particles.Clear();
        _flashUntilMs.Clear();
        foreach (var sp in _leafSpawners) sp.NextSpawnMs = 0;
        _pausedAt = null;

        if (_elapsedText is not null) _elapsedText.Text = FormatElapsed(0, _root.HasActuals);
        if (_scannedText is not null) _scannedText.Text = FormatCount(0);
        foreach (var (text, _) in _threadValueBlocks) text.Text = FormatCount(0);

        _playStart = DateTime.Now;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Normal, OnTick);
        _timer.Start();
        SetPlaybackState(playing: true, paused: false);
    }

    /// <summary>Freezes the animation exactly where it is — particles, counters, everything stays put.</summary>
    private void Pause()
    {
        _timer?.Stop();
        _pausedAt = DateTime.Now;
        SetPlaybackState(playing: false, paused: true);
    }

    /// <summary>Continues from a paused state, shifting the internal clock forward so nothing jumps.</summary>
    private void Resume()
    {
        if (_timer is null) { StartPlayback(); return; }
        if (_pausedAt is not null)
        {
            _playStart += DateTime.Now - _pausedAt.Value;
            _pausedAt = null;
        }
        _timer.Start();
        SetPlaybackState(playing: true, paused: false);
    }

    /// <summary>Playback reached the end of its own timeline (not user-paused) — settle on the real final values.</summary>
    private void FinishNaturally()
    {
        _timer?.Stop();
        _pausedAt = null;
        ShowFinalStatics();
        SetPlaybackState(playing: false, paused: false);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_root is null || _canvas is null) return;

        var nowMs = (DateTime.Now - _playStart).TotalMilliseconds;
        var fraction = Math.Min(1, nowMs / AnimDurationMs);

        if (_elapsedText is not null) _elapsedText.Text = FormatElapsed(fraction * _totalElapsedTarget, _root.HasActuals);
        if (_scannedText is not null) _scannedText.Text = FormatCount((long)(fraction * _totalLeafRows));
        foreach (var (text, final) in _threadValueBlocks) text.Text = FormatCount((long)(fraction * final));

        if (fraction < 1)
        {
            foreach (var sp in _leafSpawners)
            {
                if (nowMs < sp.NextSpawnMs) continue;
                SpawnParticle(sp.Node, nowMs);
                sp.NextSpawnMs = nowMs + sp.IntervalMs * (0.85 + _rng.NextDouble() * 0.3);
            }
        }

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            var t = (nowMs - p.StartMs) / p.DurationMs;
            if (t < 1)
            {
                var pos = Lerp(p.Start, p.End, t);
                Canvas.SetLeft(p.Visual, pos.X - ParticleRadius);
                Canvas.SetTop(p.Visual, pos.Y - ParticleRadius);
                continue;
            }

            _particles.RemoveAt(i);
            var arrivedAt = p.ArrivingAt;
            if (arrivedAt is null)
            {
                _canvas.Children.Remove(p.Visual);
                continue;
            }

            if (_rng.NextDouble() >= ComputePassRatio(arrivedAt))
            {
                FlashReject(arrivedAt, nowMs);
                _canvas.Children.Remove(p.Visual);
                continue;
            }

            var nextTarget = _parentOf[arrivedAt];
            var start = _outAnchors[arrivedAt];
            var end = nextTarget is null ? _outputAnchor : _inAnchors[nextTarget];
            var duration = Math.Clamp(Distance(start, end) / PixelsPerMs, 500, 2500);

            p.Start = start;
            p.End = end;
            p.StartMs = nowMs;
            p.DurationMs = duration;
            p.ArrivingAt = nextTarget;
            Canvas.SetLeft(p.Visual, start.X - ParticleRadius);
            Canvas.SetTop(p.Visual, start.Y - ParticleRadius);
            _particles.Add(p);
        }

        if (_flashUntilMs.Count > 0)
        {
            List<PlanOperatorNode>? expired = null;
            foreach (var (node, until) in _flashUntilMs)
            {
                if (nowMs < until) continue;
                if (_boxes.TryGetValue(node, out var box))
                    box.BorderBrush = NormalBorderBrush(node);
                (expired ??= new List<PlanOperatorNode>()).Add(node);
            }
            if (expired is not null)
                foreach (var n in expired) _flashUntilMs.Remove(n);
        }

        if (fraction >= 1 && _particles.Count == 0)
            FinishNaturally();
    }

    private void SpawnParticle(PlanOperatorNode leaf, double nowMs)
    {
        if (_canvas is null) return;
        var ellipse = new Ellipse { Width = ParticleRadius * 2, Height = ParticleRadius * 2, Fill = ParticleBrush };
        var start = _outAnchors[leaf];
        var target = _parentOf[leaf];
        var end = target is null ? _outputAnchor : _inAnchors[target];
        Canvas.SetLeft(ellipse, start.X - ParticleRadius);
        Canvas.SetTop(ellipse, start.Y - ParticleRadius);
        _canvas.Children.Add(ellipse);
        var duration = Math.Clamp(Distance(start, end) / PixelsPerMs, 500, 2500);
        _particles.Add(new Particle { Visual = ellipse, Start = start, End = end, StartMs = nowMs, DurationMs = duration, ArrivingAt = target });
    }

    private void FlashReject(PlanOperatorNode node, double nowMs)
    {
        _flashUntilMs[node] = nowMs + 260;
        if (_boxes.TryGetValue(node, out var box))
            box.BorderBrush = RejectBrush;
    }

    /// <summary>
    /// Chance a particle arriving at <paramref name="node"/> continues on rather than being discarded there —
    /// this node's own rows vs. the rows its children fed in. A true zero stays zero (e.g. an anti-join that
    /// matched everything); any other selectivity gets a visible floor so rare survivors aren't invisible.
    /// </summary>
    private static double ComputePassRatio(PlanOperatorNode node)
    {
        if (node.Children.Count == 0) return 1.0;
        var childSum = node.Children.Sum(c => c.RowsForDisplay);
        if (childSum <= 0) return 0.0;
        var raw = Math.Clamp((double)node.RowsForDisplay / childSum, 0.0, 1.0);
        return raw <= 0 ? 0.0 : Math.Max(raw, 0.08);
    }

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Distance(Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string FormatCount(long n) => n.ToString("N0", Nl);

    private static string FormatElapsed(double value, bool hasActuals) =>
        hasActuals ? $"{value.ToString("N0", Nl)} ms" : $"{value.ToString("N2", Nl)} cost";
}
