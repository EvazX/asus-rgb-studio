using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new FxDeckForm());

internal sealed class FxDeckForm : Form
{
    private static readonly string BaseDir = ResolveBaseDir();
    private static readonly string StateFile = Path.Combine(BaseDir, "rgb_intensity.txt");
    private static readonly string ProcessStateFile = Path.Combine(BaseDir, "rgb_effect_pids.txt");

    private readonly List<EffectDef> _effects = EffectCatalog.Build();
    private readonly List<FilterChip> _chips = [];
    private readonly List<FxTile> _tiles = [];
    private readonly FlowLayoutPanel _chipFlow;
    private readonly FlowLayoutPanel _effectFlow;
    private readonly LivePreview _heroPreview;
    private readonly Label _activeName;
    private readonly Label _activeDetail;
    private readonly Label _statusLabel;
    private readonly Label _intensityValue;
    private readonly TrackBar _intensityTrack;
    private readonly System.Windows.Forms.Timer _watchdog;

    private Process? _currentProcess;
    private EffectDef? _currentEffect;
    private string _activeFilter = "Tous";
    private bool _keepEffectAlive;
    private DateTime _lastRestartUtc = DateTime.MinValue;
    private int _intensityPercent = 100;

    public FxDeckForm()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var width = Math.Clamp(workingArea.Width / 4, 480, 560);
        var height = Math.Max(720, workingArea.Height - 36);

        Text = "ASUS Keyboard FX + Ambilight";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(workingArea.Right - width - 10, workingArea.Top + 8);
        Size = new Size(width, height);
        MinimumSize = new Size(440, 680);
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        BackColor = Theme.Deep;
        ForeColor = Theme.Text;

        _intensityPercent = ReadIntensityPercent();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 14),
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        root.Controls.Add(new HeroHeader(), 0, 0);

        var activePanel = BuildActivePanel();
        _heroPreview = new LivePreview(EffectCatalog.Placeholder)
        {
            Dock = DockStyle.Right,
            Width = 156,
            Margin = new Padding(8)
        };
        _activeName = new Label
        {
            Text = "Aucun effet",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI Variable Display", 18f, FontStyle.Bold),
            ForeColor = Color.White
        };
        _activeDetail = new Label
        {
            Text = "Selectionne un rendu, l'app garde le moteur vivant en arriere-plan.",
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.Muted
        };
        activePanel.Controls.Add(_heroPreview);
        activePanel.Controls.Add(_activeDetail);
        activePanel.Controls.Add(_activeName);
        root.Controls.Add(activePanel, 0, 1);

        var controls = BuildControlsPanel();
        _chipFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(2, 2, 2, 0)
        };
        _intensityTrack = new TrackBar
        {
            Dock = DockStyle.Top,
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10,
            Value = _intensityPercent,
            BackColor = Theme.Card
        };
        _intensityTrack.Scroll += (_, _) => SetIntensity(_intensityTrack.Value);
        _intensityValue = new Label
        {
            Text = $"{_intensityPercent}%",
            Dock = DockStyle.Right,
            Width = 52,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleRight
        };
        var intensityTitle = new Label
        {
            Text = "Intensite live",
            Dock = DockStyle.Left,
            Width = 112,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Theme.Soft
        };
        var intensityLine = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(2, 4, 2, 0) };
        intensityLine.Controls.Add(_intensityValue);
        intensityLine.Controls.Add(intensityTitle);
        controls.Controls.Add(_intensityTrack);
        controls.Controls.Add(intensityLine);
        controls.Controls.Add(_chipFlow);
        root.Controls.Add(controls, 0, 2);

        _effectFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 6, 4, 8)
        };
        root.Controls.Add(_effectFlow, 0, 3);

        var footer = BuildFooter();
        _statusLabel = new Label
        {
            Text = "Pret",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.Cyan,
            TextAlign = ContentAlignment.MiddleLeft
        };
        footer.Controls.Add(_statusLabel);
        root.Controls.Add(footer, 0, 4);

        BuildFilters();
        PopulateEffects();
        CleanupTrackedProcesses();
        WriteIntensityState();
        SetStatus("Nouvelle interface chargee");

        _watchdog = new System.Windows.Forms.Timer { Interval = 1400 };
        _watchdog.Tick += (_, _) => WatchdogTick();
        _watchdog.Start();

        Resize += (_, _) => ResizeTiles();
        FormClosing += (_, _) =>
        {
            _keepEffectAlive = false;
            StopCurrentEffect();
        };
    }

    private Panel BuildActivePanel()
    {
        var panel = new GlowPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 18, 12),
            Main = Color.FromArgb(15, 23, 42),
            Accent = Color.FromArgb(8, 145, 178)
        };
        return panel;
    }

    private Panel BuildControlsPanel()
    {
        return new GlowPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8),
            Main = Color.FromArgb(10, 15, 23),
            Accent = Color.FromArgb(234, 88, 12)
        };
    }

    private Panel BuildFooter()
    {
        var footer = new GlowPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 8),
            Main = Color.FromArgb(13, 18, 28),
            Accent = Color.FromArgb(51, 65, 85)
        };

        var stopButton = MakeButton("Stop", Color.FromArgb(127, 29, 29), (_, _) => StopCurrentEffect());
        stopButton.Dock = DockStyle.Right;

        var patternsButton = MakeButton("Patterns", Color.FromArgb(22, 78, 99), (_, _) => OpenPatterns());
        patternsButton.Dock = DockStyle.Right;
        patternsButton.Margin = new Padding(0, 0, 8, 0);

        footer.Controls.Add(stopButton);
        footer.Controls.Add(patternsButton);
        return footer;
    }

    private void BuildFilters()
    {
        var filters = new[] { "Tous" }
            .Concat(_effects.Select(e => e.Group).Distinct())
            .ToArray();

        foreach (var filter in filters)
        {
            var chip = new FilterChip(filter) { Active = filter == _activeFilter };
            chip.Click += (_, _) =>
            {
                _activeFilter = filter;
                foreach (var item in _chips)
                {
                    item.Active = item.Text == _activeFilter;
                }
                PopulateEffects();
            };
            _chips.Add(chip);
            _chipFlow.Controls.Add(chip);
        }
    }

    private void PopulateEffects()
    {
        _effectFlow.SuspendLayout();
        _effectFlow.Controls.Clear();
        _tiles.Clear();

        var selected = _activeFilter == "Tous"
            ? _effects
            : _effects.Where(effect => effect.Group == _activeFilter);

        foreach (var effect in selected)
        {
            var tile = new FxTile(effect)
            {
                Width = TileWidth(),
                PreviewIntensity = _intensityPercent / 100.0
            };
            tile.ApplyRequested += (_, selectedEffect) => ApplyEffect(selectedEffect);
            tile.Active = _currentEffect?.Name == effect.Name;
            _tiles.Add(tile);
            _effectFlow.Controls.Add(tile);
        }

        _effectFlow.ResumeLayout();
    }

    private int TileWidth() => Math.Max(390, ClientSize.Width - 56);

    private void ResizeTiles()
    {
        foreach (var tile in _tiles)
        {
            tile.Width = TileWidth();
        }
    }

    private void ApplyEffect(EffectDef effect)
    {
        StopCurrentEffect(false);
        CleanupTrackedProcesses();
        _currentEffect = effect;
        _keepEffectAlive = true;
        StartEffect(effect);
        UpdateActiveEffect(effect);
    }

    private void UpdateActiveEffect(EffectDef effect)
    {
        _activeName.Text = effect.Name;
        _activeDetail.Text = $"{effect.Group} - {effect.Description}";
        _heroPreview.Effect = effect;
        HighlightCurrentEffect();
    }

    private void HighlightCurrentEffect()
    {
        foreach (var tile in _tiles)
        {
            tile.Active = _currentEffect?.Name == tile.Effect.Name;
        }
    }

    private void StartEffect(EffectDef effect)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = effect.FileName,
            Arguments = effect.Arguments,
            WorkingDirectory = BaseDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var intensity = (_intensityPercent / 100.0).ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["RGB_STATE_FILE"] = StateFile;
        if (string.Equals(effect.FileName, "python", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["RGB_INTENSITY"] = intensity;
        }
        else if (!effect.Arguments.Contains("--benchmark", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Arguments = $"{effect.Arguments} --intensity-boost {intensity}";
        }

        try
        {
            _currentProcess = Process.Start(startInfo);
            TrackCurrentProcess();
            SetStatus(_currentProcess is null ? $"Echec: {effect.Name}" : $"En cours: {effect.Name}");
        }
        catch (Exception ex)
        {
            _currentProcess = null;
            SetStatus($"Impossible de lancer {effect.Name}: {ex.Message}");
        }
    }

    private void WatchdogTick()
    {
        _heroPreview.Intensity = _intensityPercent / 100.0;
        foreach (var tile in _tiles)
        {
            tile.PreviewIntensity = _intensityPercent / 100.0;
        }

        if (!_keepEffectAlive || _currentEffect is null)
        {
            return;
        }

        if (_currentProcess is { HasExited: false })
        {
            return;
        }

        if ((DateTime.UtcNow - _lastRestartUtc).TotalSeconds < 2)
        {
            return;
        }

        _lastRestartUtc = DateTime.UtcNow;
        StartEffect(_currentEffect);
    }

    private void StopCurrentEffect(bool restoreWhite = true)
    {
        _keepEffectAlive = false;
        if (_currentProcess is { HasExited: false })
        {
            try
            {
                _currentProcess.Kill(entireProcessTree: true);
                _currentProcess.WaitForExit(650);
            }
            catch
            {
                // Best effort: the device will be restored by set_white below.
            }
        }

        _currentProcess = null;
        _currentEffect = null;
        _activeName.Text = "Aucun effet";
        _activeDetail.Text = "Le clavier revient en blanc quand on arrete.";
        _heroPreview.Effect = EffectCatalog.Placeholder;
        HighlightCurrentEffect();
        ClearTrackedProcesses();

        if (restoreWhite)
        {
            RestoreWhite();
            SetStatus("Effets arretes - blanc applique");
        }
    }

    private void RestoreWhite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "set_white.py --red 255 --green 255 --blue 255",
                WorkingDirectory = BaseDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment = { ["RGB_STATE_FILE"] = StateFile }
            })?.Dispose();
        }
        catch (Exception ex)
        {
            SetStatus($"Blanc non applique: {ex.Message}");
        }
    }

    private void SetIntensity(int percent)
    {
        _intensityPercent = Math.Clamp(percent, 0, 100);
        _intensityValue.Text = $"{_intensityPercent}%";
        _heroPreview.Intensity = _intensityPercent / 100.0;
        foreach (var tile in _tiles)
        {
            tile.PreviewIntensity = _intensityPercent / 100.0;
        }
        WriteIntensityState();
        SetStatus($"Intensite: {_intensityPercent}%");
    }

    private int ReadIntensityPercent()
    {
        try
        {
            if (!File.Exists(StateFile))
            {
                return 100;
            }

            var raw = File.ReadAllText(StateFile).Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return Math.Clamp((int)Math.Round(value * 100), 0, 100);
            }
        }
        catch
        {
            // Fall back to full brightness.
        }

        return 100;
    }

    private void WriteIntensityState()
    {
        try
        {
            File.WriteAllText(StateFile, (_intensityPercent / 100.0).ToString("0.###", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            SetStatus($"Intensite non sauvegardee: {ex.Message}");
        }
    }

    private void TrackCurrentProcess()
    {
        if (_currentProcess is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(ProcessStateFile, _currentProcess.Id.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            // Tracking is only a convenience for cleanup.
        }
    }

    private void CleanupTrackedProcesses()
    {
        if (!File.Exists(ProcessStateFile))
        {
            return;
        }

        try
        {
            var ids = File.ReadAllText(ProcessStateFile)
                .Split([',', ';', '\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1)
                .Where(id => id > 0);

            foreach (var id in ids)
            {
                try
                {
                    var process = Process.GetProcessById(id);
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Already gone or not ours anymore.
                }
            }
        }
        finally
        {
            ClearTrackedProcesses();
        }
    }

    private void ClearTrackedProcesses()
    {
        try
        {
            if (File.Exists(ProcessStateFile))
            {
                File.Delete(ProcessStateFile);
            }
        }
        catch
        {
            // Not worth interrupting the UI for a stale pid file.
        }
    }

    private void OpenPatterns()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "latency_probe.py --pattern",
                WorkingDirectory = BaseDir,
                UseShellExecute = false,
                CreateNoWindow = false,
                Environment = { ["RGB_STATE_FILE"] = StateFile }
            })?.Dispose();
            SetStatus("Patterns de test lances");
        }
        catch (Exception ex)
        {
            SetStatus($"Patterns indisponibles: {ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = text;
        }
    }

    private static Button MakeButton(string text, Color color, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Width = 92,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += click;
        return button;
    }

    private static string ResolveBaseDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "effects_common.py")) && File.Exists(Path.Combine(dir.FullName, "set_white.py")))
            {
                return dir.FullName;
            }
        }

        return @"D:\asus-ambient-led";
    }
}

internal sealed class HeroHeader : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly DateTime _started = DateTime.UtcNow;

    public HeroHeader()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 26);
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(12, 18, 30), Color.FromArgb(32, 18, 12), LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillPath(bg, path);

        var t = (DateTime.UtcNow - _started).TotalSeconds;
        DrawOrbit(e.Graphics, Width - 76, Height / 2f, 30, t, Theme.Orange);
        DrawOrbit(e.Graphics, Width - 76, Height / 2f, 20, -t * 1.35, Theme.Cyan);

        using var title = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold);
        using var subtitle = new Font("Segoe UI", 9.5f);
        using var titleBrush = new SolidBrush(Color.White);
        using var subtitleBrush = new SolidBrush(Theme.Muted);
        e.Graphics.DrawString("ASUS Keyboard FX", title, titleBrush, 18, 14);
        e.Graphics.DrawString("Effets 4 zones + Ambilight", subtitle, subtitleBrush, 20, 51);
    }

    private static void DrawOrbit(Graphics graphics, float cx, float cy, float radius, double time, Color color)
    {
        using var pen = new Pen(Color.FromArgb(150, color), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var dot = new SolidBrush(color);
        graphics.DrawArc(pen, cx - radius, cy - radius, radius * 2, radius * 2, (float)(time * 130), 95);
        var angle = time * 2.2;
        graphics.FillEllipse(dot, cx + (float)Math.Cos(angle) * radius - 4, cy + (float)Math.Sin(angle) * radius - 4, 8, 8);
    }
}

internal sealed class FilterChip : Button
{
    private bool _active;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            BackColor = value ? Theme.Cyan : Color.FromArgb(20, 27, 39);
            ForeColor = value ? Color.FromArgb(5, 12, 18) : Theme.Text;
            Invalidate();
        }
    }

    public FilterChip(string text)
    {
        Text = text;
        AutoSize = true;
        Height = 31;
        Padding = new Padding(12, 0, 12, 0);
        Margin = new Padding(0, 3, 8, 4);
        FlatStyle = FlatStyle.Flat;
        Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        FlatAppearance.BorderSize = 0;
    }
}

internal sealed class FxTile : Control
{
    private readonly LivePreview _preview;
    private readonly Button _applyButton;
    private bool _active;

    public event EventHandler<EffectDef>? ApplyRequested;
    public EffectDef Effect { get; }

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            _applyButton.Text = value ? "Actif" : "Lancer";
            _applyButton.BackColor = value ? Theme.Cyan : Color.FromArgb(30, 41, 59);
            _applyButton.ForeColor = value ? Color.FromArgb(6, 14, 20) : Color.White;
            Invalidate();
        }
    }

    public double PreviewIntensity
    {
        get => _preview.Intensity;
        set => _preview.Intensity = value;
    }

    public FxTile(EffectDef effect)
    {
        Effect = effect;
        Height = 98;
        Margin = new Padding(0, 0, 0, 10);
        DoubleBuffered = true;
        BackColor = Theme.Deep;

        _preview = new LivePreview(effect)
        {
            Dock = DockStyle.Right,
            Width = 128,
            Margin = new Padding(8)
        };

        _applyButton = new Button
        {
            Text = "Lancer",
            Width = 78,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
        };
        _applyButton.FlatAppearance.BorderSize = 0;
        _applyButton.Click += (_, _) => ApplyRequested?.Invoke(this, Effect);

        var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 13, 10, 10) };
        var title = new Label
        {
            Text = effect.Name,
            Dock = DockStyle.Top,
            Height = 23,
            Font = new Font("Segoe UI Semibold", 12.8f, FontStyle.Bold),
            ForeColor = Color.White
        };
        var meta = new Label
        {
            Text = effect.Group,
            Dock = DockStyle.Top,
            Height = 18,
            Font = new Font("Segoe UI", 8.8f),
            ForeColor = effect.Accent
        };
        var desc = new Label
        {
            Text = effect.Description,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.Muted
        };
        var buttonHost = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        buttonHost.Controls.Add(_applyButton);
        _applyButton.Dock = DockStyle.Left;

        textHost.Controls.Add(desc);
        textHost.Controls.Add(meta);
        textHost.Controls.Add(title);
        textHost.Controls.Add(buttonHost);
        Controls.Add(textHost);
        Controls.Add(_preview);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 22);
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(11, 17, 27), Color.FromArgb(7, 11, 17), LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(_active ? Effect.Accent : Color.FromArgb(28, 38, 52), _active ? 2.0f : 1f);
        using var glow = new SolidBrush(Color.FromArgb(_active ? 62 : 22, Effect.Accent));
        e.Graphics.FillPath(bg, path);
        e.Graphics.FillEllipse(glow, Width - 178, 16, 18, 18);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class LivePreview : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public EffectDef Effect { get; set; }
    public double Intensity { get; set; } = 1.0;

    public LivePreview(EffectDef effect)
    {
        Effect = effect;
        DoubleBuffered = true;
        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 18);
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(7, 12, 18), Color.FromArgb(16, 24, 36), LinearGradientMode.Vertical);
        e.Graphics.FillPath(bg, path);

        var elapsed = (DateTime.UtcNow - _startedAt).TotalSeconds;
        var zones = Effect.Frame(elapsed, Intensity);
        DrawKeyboard(e.Graphics, new RectangleF(12, 16, Width - 24, Height * 0.48f), zones);
        DrawBar(e.Graphics, new RectangleF(16, Height - 27, Width - 32, 10), zones);
    }

    private static void DrawKeyboard(Graphics graphics, RectangleF rect, IReadOnlyList<Color> colors)
    {
        using var shell = new SolidBrush(Color.FromArgb(24, 32, 46));
        using var path = Ui.RoundRect(rect, 14);
        graphics.FillPath(shell, path);

        var zoneWidth = rect.Width / 4f;
        for (var zone = 0; zone < 4; zone++)
        {
            var zoneRect = new RectangleF(rect.X + zone * zoneWidth + 3, rect.Y + 3, zoneWidth - 6, rect.Height - 6);
            using var zoneBrush = new SolidBrush(colors[zone]);
            using var zonePath = Ui.RoundRect(zoneRect, 9);
            graphics.FillPath(zoneBrush, zonePath);

            using var keyBrush = new SolidBrush(Color.FromArgb(45, 5, 9, 14));
            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 3; col++)
                {
                    var key = new RectangleF(
                        zoneRect.X + col * zoneRect.Width / 3f + 2,
                        zoneRect.Y + row * zoneRect.Height / 3f + 2,
                        zoneRect.Width / 3f - 4,
                        zoneRect.Height / 3f - 4);
                    using var keyPath = Ui.RoundRect(key, 3);
                    graphics.FillPath(keyBrush, keyPath);
                }
            }
        }
    }

    private static void DrawBar(Graphics graphics, RectangleF rect, IReadOnlyList<Color> colors)
    {
        var zoneWidth = rect.Width / 4f;
        for (var zone = 0; zone < 4; zone++)
        {
            var zoneRect = new RectangleF(rect.X + zone * zoneWidth, rect.Y, zoneWidth + 1, rect.Height);
            using var brush = new SolidBrush(colors[zone]);
            graphics.FillRectangle(brush, zoneRect);
        }

        using var border = new Pen(Color.FromArgb(100, 255, 255, 255), 1);
        using var path = Ui.RoundRect(rect, 5);
        graphics.DrawPath(border, path);
    }
}

internal sealed class GlowPanel : Panel
{
    public Color Main { get; set; } = Theme.Card;
    public Color Accent { get; set; } = Theme.Cyan;

    public GlowPanel()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 24);
        using var bg = new LinearGradientBrush(ClientRectangle, Main, Color.FromArgb(8, 12, 18), LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(Color.FromArgb(40, Accent), 1);
        using var glow = new SolidBrush(Color.FromArgb(24, Accent));
        e.Graphics.FillPath(bg, path);
        e.Graphics.FillEllipse(glow, Width - 120, -70, 180, 180);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed record EffectDef(
    string Name,
    string Group,
    string Description,
    string FileName,
    string Arguments,
    Color Accent,
    Func<double, double, Color[]> Frame);

internal static class EffectCatalog
{
    public static readonly EffectDef Placeholder = Make(
        "Apercu",
        "Studio",
        "Preview",
        "python",
        "set_white.py",
        ["#38bdf8", "#f97316", "#22c55e", "#f43f5e"],
        Ui.SlowOrbit);

    public static List<EffectDef> Build() =>
    [
        Make("Ambilight Reactif", "Live", "Capture le bas de l'ecran, rapide pour jeux et videos.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --fps 32 --threshold 2 --vertical-bias 0.30 --saturation-boost 3.4 --value-boost 0.95 --smoothing 0.12", ["#38bdf8","#1d4ed8","#7c3aed","#f472b6"], Ui.SlowOrbit),
        Make("Screen Dominance", "Live", "Prend les couleurs fortes de l'image entiere au lieu du gris moyen.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --screen-mode vibrant --fps 28 --threshold 2 --samples-x 8 --samples-y 5 --saturation-boost 3.8 --value-boost 0.95 --neutral-threshold 24 --color-bias 4.6 --smoothing 0.20", ["#f97316","#22d3ee","#a855f7","#22c55e"], Ui.Prism),
        Make("Mirror", "Live", "Reflet gauche-droite de l'ecran entier sur les 4 zones.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --mirror --screen-mode full --fps 30 --threshold 2 --samples-x 8 --samples-y 5 --saturation-boost 3.2 --value-boost 0.9 --smoothing 0.18", ["#2563eb","#06b6d4","#ec4899","#f97316"], Ui.Mirror),
        Make("Cinema Glow", "Live", "Ambilight plus doux, stable, parfait pour films et YouTube.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --screen-mode vibrant --fps 22 --threshold 4 --samples-x 8 --samples-y 5 --saturation-boost 2.7 --value-boost 0.78 --neutral-threshold 32 --color-bias 3.0 --smoothing 0.38", ["#f59e0b","#ef4444","#38bdf8","#1e293b"], Ui.Breath),
        Make("Audio Pulse", "Live", "Reagit au son du PC, plutot pour musique et videos.", "dotnet", @".\csharp-audio\bin\Release\net8.0-windows\AudioReactive.dll --style fire", ["#ff5a14","#ffb21e","#fff0b8","#b91c1c"], Ui.Breath),

        Make("K2000", "Classiques", "Scanner rouge propre et lisible.", "python", "k2000.py --speed 0.12", ["#ff3b30"], Ui.Scanner),
        Make("Police", "Classiques", "Gyrophare rouge et bleu.", "python", "police.py", ["#ef4444","#2563eb"], Ui.Police),
        Make("Prism Flow", "Classiques", "Arc-en-ciel fluide, moins agressif.", "python", "prism_flow.py", ["#ff4646","#ffb43c","#ffe65a","#46dc78"], Ui.Prism),
        Make("Stack Fall", "Classiques", "Empilement gauche-droite facon Tetris.", "python", "stack_fall.py", ["#22c55e","#facc15","#fb7185","#38bdf8"], Ui.StackRows),

        Make("Cyberpunk", "Neon", "Cyan, magenta et violet tres lumineux.", "python", "cyberpunk.py", ["#00eaff","#ff3bf4","#7c3aed","#ffffff"], Ui.Neon),
        Make("Neon Comet", "Neon", "Comete blanche avec trainee cyan magenta.", "python", "neon_comet.py", ["#ffffff","#00eaff","#ff2cf0","#7c3aed"], Ui.Comet),
        Make("Matrix Rain", "Neon", "Pluie digitale laterale sur les 4 zones.", "python", "matrix_rain.py", ["#22ff6e","#beff5a","#003010","#001208"], Ui.Matrix),
        Make("Acid Pulse", "Neon", "Vert toxique plus agressif.", "python", "acid_pulse.py", ["#84cc16","#22c55e","#14532d","#d9f99d"], Ui.Breath),

        Make("Lava Wave", "Atmosphere", "Chaud, braise et vague orange.", "python", "lava_wave.py", ["#ff461e","#ffaa1e","#fff5b4","#b91c1c"], Ui.Breath),
        Make("Aurora Drift", "Atmosphere", "Cyan, vert et violet, plus doux.", "python", "aurora_drift.py", ["#46dcff","#50ffaa","#966eff","#e0fbff"], Ui.SlowOrbit),
        Make("Deep Ocean", "Atmosphere", "Bleu profond avec reflets froids.", "python", "deep_ocean.py", ["#1446b4","#28b4e6","#d2f0ff","#0f172a"], Ui.Wave),
        Make("Storm Mode", "Atmosphere", "Orage bleu avec flashs blancs.", "python", "storm_mode.py", ["#1d4ed8","#93c5fd","#f8fafc","#0f172a"], Ui.Storm),
        Make("Solar Flare", "Atmosphere", "Eruption solaire chaude avec flash central.", "python", "solar_flare.py", ["#ffce54","#ff681f","#b91c1c","#fff6c8"], Ui.Solar),

        Make("Radar Sweep", "Jeux", "Balayage tactique vert cyan.", "python", "radar_sweep.py", ["#32ffa0","#00beff","#001218","#0f766e"], Ui.Radar),
        Make("Factory Core", "Jeux", "Orange industriel net, bon en fond de jeu.", "python", "factory_core.py", ["#f97316","#facc15","#475569","#f8fafc"], Ui.Wave),
        Make("DedSec Glitch", "Jeux", "Hacking cyan rouge avec alternance brutale.", "python", "dedsec_glitch.py", ["#06b6d4","#ef4444","#111827","#f8fafc"], Ui.Police)
    ];

    private static EffectDef Make(string name, string group, string description, string fileName, string arguments, string[] palette, Func<Color[], double, double, Color[]> frame)
    {
        var colors = palette.Select(ColorTranslator.FromHtml).ToArray();
        return new EffectDef(name, group, description, fileName, arguments, colors[0], (t, i) => frame(colors, t, i));
    }
}

internal static class Ui
{
    public static GraphicsPath RoundRect(RectangleF rect, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color[] Breath(Color[] colors, double t, double intensity)
    {
        var pulse = 0.34 + ((Math.Sin(t * 2.5) + 1.0) / 2.0) * 0.66;
        return Enumerable.Range(0, 4)
            .Select(i => Scale(colors[i % colors.Length], Math.Min(1.0, pulse * (0.82 + i * 0.05)) * intensity))
            .ToArray();
    }

    public static Color[] Scanner(Color[] colors, double t, double intensity)
    {
        var pos = ((Math.Sin(t * 2.4) + 1.0) / 2.0) * 3.0;
        return Enumerable.Range(0, 4)
            .Select(i => Scale(colors[0], Math.Max(0.08, 1.0 - Math.Abs(i - pos) * 0.62) * intensity))
            .ToArray();
    }

    public static Color[] Police(Color[] colors, double t, double intensity)
    {
        var phase = (int)(t * 5.5) % 4;
        return Enumerable.Range(0, 4).Select(i =>
        {
            var left = i < 2;
            var active = left ? phase is 0 or 1 : phase is 2 or 3;
            return Scale(left ? colors[0] : colors[Math.Min(1, colors.Length - 1)], (active ? 1.0 : 0.16) * intensity);
        }).ToArray();
    }

    public static Color[] Mirror(Color[] colors, double t, double intensity)
    {
        var shift = (Math.Sin(t * 1.5) + 1.0) / 2.0;
        return Enumerable.Range(0, 4)
            .Select(i => Blend(colors[i % colors.Length], colors[(i + 1) % colors.Length], shift, intensity))
            .ToArray();
    }

    public static Color[] Prism(Color[] colors, double t, double intensity)
    {
        return Enumerable.Range(0, 4)
            .Select(i => Blend(colors[(i + (int)t) % colors.Length], colors[(i + 1 + (int)t) % colors.Length], t % 1.0, intensity))
            .ToArray();
    }

    public static Color[] SlowOrbit(Color[] colors, double t, double intensity)
    {
        return Enumerable.Range(0, 4).Select(i =>
        {
            var ratio = (Math.Sin(t * 1.25 + i * 0.9) + 1.0) / 2.0;
            return Blend(colors[i % colors.Length], colors[(i + 1) % colors.Length], ratio, intensity);
        }).ToArray();
    }

    public static Color[] StackRows(Color[] colors, double t, double intensity)
    {
        var step = ((int)(t * 3.0)) % 8;
        var fillCount = (step % 4) + 1;
        var reverse = step >= 4;
        return Enumerable.Range(0, 4).Select(i =>
        {
            var active = reverse ? i >= 4 - fillCount : i < fillCount;
            return Scale(colors[(i + step) % colors.Length], (active ? 1.0 : 0.13) * intensity);
        }).ToArray();
    }

    public static Color[] Neon(Color[] colors, double t, double intensity)
    {
        return Enumerable.Range(0, 4).Select(i =>
        {
            var flicker = ((int)(t * 10 + i * 2) % 7 == 0) ? 1.0 : 0.48 + (Math.Sin(t * 3.0 + i) + 1.0) * 0.26;
            return Scale(colors[i % colors.Length], flicker * intensity);
        }).ToArray();
    }

    public static Color[] Comet(Color[] colors, double t, double intensity)
    {
        var pos = ((Math.Sin(t * 2.8) + 1.0) / 2.0) * 3.0;
        return Enumerable.Range(0, 4).Select(i =>
        {
            var distance = Math.Abs(i - pos);
            var color = distance < 0.5 ? colors[0] : colors[(i % (colors.Length - 1)) + 1];
            return Scale(color, Math.Max(0.12, 1.0 - distance * 0.55) * intensity);
        }).ToArray();
    }

    public static Color[] Matrix(Color[] colors, double t, double intensity)
    {
        return Enumerable.Range(0, 4).Select(i =>
        {
            var glitch = ((int)(t * 12 + i * 5) % 11) == 0;
            var factor = glitch ? 1.0 : 0.2 + ((Math.Sin(t * 4.4 - i * 1.1) + 1.0) / 2.0) * 0.56;
            return Scale(colors[glitch ? 1 : 0], factor * intensity);
        }).ToArray();
    }

    public static Color[] Wave(Color[] colors, double t, double intensity)
    {
        return Enumerable.Range(0, 4)
            .Select(i => Scale(colors[i % colors.Length], (0.35 + ((Math.Sin(t * 1.9 + i * 0.8) + 1.0) / 2.0) * 0.62) * intensity))
            .ToArray();
    }

    public static Color[] Storm(Color[] colors, double t, double intensity)
    {
        var flash = ((int)(t * 8) % 23) is 0 or 1;
        return Enumerable.Range(0, 4)
            .Select(i => Scale(flash && i is 1 or 2 ? colors[2] : colors[i % colors.Length], (flash ? 1.0 : 0.28 + i * 0.12) * intensity))
            .ToArray();
    }

    public static Color[] Solar(Color[] colors, double t, double intensity)
    {
        var flare = ((int)(t * 5) % 19) < 2;
        return Enumerable.Range(0, 4).Select(i =>
        {
            var color = flare && i is 1 or 2 ? colors[3] : BlendRaw(colors[2], colors[0], (Math.Sin(t * 1.8 + i) + 1.0) / 2.0);
            return Scale(color, (flare ? 1.0 : 0.48 + i * 0.08) * intensity);
        }).ToArray();
    }

    public static Color[] Radar(Color[] colors, double t, double intensity)
    {
        var pos = ((Math.Sin(t * 2.0) + 1.0) / 2.0) * 3.0;
        return Enumerable.Range(0, 4)
            .Select(i => Scale(BlendRaw(colors[0], colors[1], i / 3.0), Math.Max(0.13, 1.0 - Math.Abs(i - pos) * 0.48) * intensity))
            .ToArray();
    }

    private static Color Scale(Color color, double factor) =>
        Color.FromArgb(
            Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.G * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.B * factor), 0, 255));

    private static Color Blend(Color left, Color right, double ratio, double intensity) => Scale(BlendRaw(left, right, ratio), intensity);

    private static Color BlendRaw(Color left, Color right, double ratio)
    {
        var r = Math.Clamp(ratio, 0.0, 1.0);
        return Color.FromArgb(
            (int)Math.Round(left.R + (right.R - left.R) * r),
            (int)Math.Round(left.G + (right.G - left.G) * r),
            (int)Math.Round(left.B + (right.B - left.B) * r));
    }
}

internal static class Theme
{
    public static readonly Color Deep = Color.FromArgb(6, 10, 16);
    public static readonly Color Card = Color.FromArgb(13, 18, 28);
    public static readonly Color Text = Color.FromArgb(241, 245, 249);
    public static readonly Color Muted = Color.FromArgb(148, 163, 184);
    public static readonly Color Soft = Color.FromArgb(191, 219, 254);
    public static readonly Color Cyan = Color.FromArgb(34, 211, 238);
    public static readonly Color Orange = Color.FromArgb(249, 115, 22);
}
