using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new RightDockStudioForm());

internal sealed class RightDockStudioForm : Form
{
    private readonly string _baseDir = @"D:\asus-ambient-led";
    private readonly string _stateFile = @"D:\asus-ambient-led\rgb_intensity.txt";
    private readonly string _processStateFile = @"D:\asus-ambient-led\rgb_effect_pids.txt";
    private readonly List<EffectDef> _effects = EffectCatalog.Build();
    private readonly Label _statusLabel;
    private readonly Label _intensityValueLabel;
    private readonly TrackBar _intensityTrack;
    private readonly FlowLayoutPanel _effectsFlow;
    private readonly System.Windows.Forms.Timer _watchdog;

    private readonly List<EffectCard> _cards = [];
    private Process? _currentProcess;
    private EffectDef? _currentEffect;
    private bool _keepEffectAlive;
    private DateTime _lastRestartUtc = DateTime.MinValue;
    private int _intensityPercent = 100;

    public RightDockStudioForm()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var width = Math.Max(430, workingArea.Width / 5);
        var height = Math.Max(680, workingArea.Height - 36);

        Text = "ASUS RGB Studio";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(workingArea.Right - width - 8, workingArea.Top + 8);
        Size = new Size(width, height);
        MinimumSize = new Size(420, 640);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(8, 12, 18);
        ForeColor = Color.FromArgb(241, 245, 249);

        _intensityPercent = ReadIntensityPercent();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 12, 14, 14),
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var header = BuildHeader();
        var controls = BuildControlsRow();
        _effectsFlow = BuildEffectsFlow();
        var footer = BuildFooter();

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(controls, 0, 1);
        root.Controls.Add(_effectsFlow, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        _statusLabel = new Label
        {
            Text = "Pret",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(125, 211, 252),
            TextAlign = ContentAlignment.MiddleLeft
        };
        footer.Controls.Add(_statusLabel);

        _intensityValueLabel = new Label
        {
            Text = $"{_intensityPercent}%",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(226, 232, 240),
            Margin = new Padding(8, 8, 0, 0)
        };

        _intensityTrack = new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10,
            Value = _intensityPercent,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        _intensityTrack.Scroll += (_, _) => SetIntensity(_intensityTrack.Value);

        controls.Controls.Add(new Label
        {
            Text = "Intensite",
            Dock = DockStyle.Left,
            Width = 76,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.FromArgb(191, 219, 254),
            TextAlign = ContentAlignment.MiddleLeft
        });
        controls.Controls.Add(_intensityTrack);
        controls.Controls.Add(_intensityValueLabel);

        PopulateEffects();
        CleanupTrackedProcesses();
        WriteIntensityState();
        SetStatus("Choisis un rendu dans la colonne");

        _watchdog = new System.Windows.Forms.Timer { Interval = 1500 };
        _watchdog.Tick += (_, _) => WatchdogTick();
        _watchdog.Start();

        FormClosing += (_, _) =>
        {
            _keepEffectAlive = false;
            StopCurrentEffect();
        };
    }

    private Control BuildHeader()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 12) };
        host.Paint += PaintCard;

        var orbit = new OrbitVisualizer
        {
            Dock = DockStyle.Right,
            Width = 86,
            Accent = Color.FromArgb(56, 189, 248)
        };

        var title = new Label
        {
            Text = "RGB Studio",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI Semibold", 20, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252)
        };

        var subtitle = new Label
        {
            Text = "Panneau lateral avec apercus live des effets",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(148, 163, 184)
        };

        host.Controls.Add(orbit);
        host.Controls.Add(subtitle);
        host.Controls.Add(title);
        return host;
    }

    private TableLayoutPanel BuildControlsRow()
    {
        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 10, 14, 10),
            ColumnCount = 3
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        controls.Paint += PaintCard;
        return controls;
    }

    private FlowLayoutPanel BuildEffectsFlow() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = false,
        FlowDirection = FlowDirection.TopDown,
        Padding = new Padding(0, 2, 4, 2)
    };

    private Panel BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 8) };
        footer.Paint += PaintCard;

        var stopButton = MakeButton("Stop", Color.FromArgb(127, 29, 29), (_, _) => StopCurrentEffect());
        stopButton.Dock = DockStyle.Right;

        var patternsButton = MakeButton("Patterns", Color.FromArgb(22, 78, 99), (_, _) => OpenPatterns());
        patternsButton.Dock = DockStyle.Right;
        patternsButton.Margin = new Padding(0, 0, 8, 0);

        footer.Controls.Add(stopButton);
        footer.Controls.Add(patternsButton);
        return footer;
    }

    private void PopulateEffects()
    {
        _effectsFlow.SuspendLayout();
        _effectsFlow.Controls.Clear();
        _cards.Clear();

        foreach (var effect in _effects)
        {
            var card = new EffectCard(effect)
            {
                Width = Math.Max(360, ClientSize.Width - 54)
            };
            card.ApplyRequested += (_, selectedEffect) => ApplyEffect(selectedEffect);
            _cards.Add(card);
            _effectsFlow.Controls.Add(card);
        }

        _effectsFlow.ResumeLayout();
        HighlightCurrentEffect();
    }

    private void ApplyEffect(EffectDef effect)
    {
        StopCurrentEffect(false);
        CleanupTrackedProcesses();
        _currentEffect = effect;
        _keepEffectAlive = true;
        StartEffect(effect);
        HighlightCurrentEffect();
    }

    private void StartEffect(EffectDef effect)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = effect.FileName,
            Arguments = effect.Arguments,
            WorkingDirectory = _baseDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var intensity = (_intensityPercent / 100.0).ToString(CultureInfo.InvariantCulture);
        if (string.Equals(effect.FileName, "python", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["RGB_INTENSITY"] = intensity;
        }
        else if (!effect.Arguments.Contains("--benchmark", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Arguments = $"{effect.Arguments} --intensity-boost {intensity}";
        }

        _currentProcess = Process.Start(startInfo);
        TrackCurrentProcess();
        SetStatus(_currentProcess is null ? $"Echec: {effect.Name}" : $"En cours: {effect.Name}");
    }

    private void WatchdogTick()
    {
        if (!_keepEffectAlive || _currentEffect is null || _currentProcess is null)
        {
            return;
        }

        try
        {
            if (!_currentProcess.HasExited)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        if ((DateTime.UtcNow - _lastRestartUtc).TotalMilliseconds < 1200)
        {
            return;
        }

        try
        {
            _currentProcess.Dispose();
        }
        catch
        {
        }

        _currentProcess = null;
        ClearTrackedProcesses();
        _lastRestartUtc = DateTime.UtcNow;
        StartEffect(_currentEffect);
        HighlightCurrentEffect();
    }

    private void StopCurrentEffect(bool restoreWhite = true)
    {
        _keepEffectAlive = false;
        if (_currentProcess is not null)
        {
            try
            {
                if (!_currentProcess.HasExited)
                {
                    _currentProcess.Kill(true);
                }
            }
            catch
            {
            }
            finally
            {
                _currentProcess.Dispose();
                _currentProcess = null;
            }
        }

        ClearTrackedProcesses();
        if (restoreWhite)
        {
            RestoreWhite();
        }

        SetStatus("Aucun effet en cours");
        HighlightCurrentEffect();
    }

    private void SetIntensity(int percent)
    {
        _intensityPercent = Math.Clamp(percent, 0, 100);
        _intensityValueLabel.Text = $"{_intensityPercent}%";
        WriteIntensityState();
        foreach (var card in _cards)
        {
            card.Intensity = _intensityPercent / 100.0;
        }
        SetStatus(_currentEffect is null ? $"Intensite {_intensityPercent}%" : $"{_currentEffect.Name} | {_intensityPercent}%");
    }

    private void HighlightCurrentEffect()
    {
        foreach (var card in _cards)
        {
            card.Active = _currentEffect is not null && card.Effect == _currentEffect && _currentProcess is not null;
        }
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private void OpenPatterns()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(_baseDir, "test_patterns.html"),
            UseShellExecute = true
        });
    }

    private void RestoreWhite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "set_white.py",
                WorkingDirectory = _baseDir,
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(1500);
        }
        catch
        {
        }
    }

    private int ReadIntensityPercent()
    {
        try
        {
            if (File.Exists(_stateFile))
            {
                var raw = File.ReadAllText(_stateFile).Trim();
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return Math.Clamp((int)Math.Round(value * 100.0), 0, 100);
                }
            }
        }
        catch
        {
        }

        return 100;
    }

    private void WriteIntensityState()
    {
        try
        {
            File.WriteAllText(_stateFile, (_intensityPercent / 100.0).ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }

    private void TrackCurrentProcess()
    {
        try
        {
            if (_currentProcess is null || _currentProcess.HasExited)
            {
                return;
            }

            File.WriteAllLines(_processStateFile, [_currentProcess.Id.ToString(CultureInfo.InvariantCulture)]);
        }
        catch
        {
        }
    }

    private void ClearTrackedProcesses()
    {
        try
        {
            if (File.Exists(_processStateFile))
            {
                File.Delete(_processStateFile);
            }
        }
        catch
        {
        }
    }

    private void CleanupTrackedProcesses()
    {
        try
        {
            if (!File.Exists(_processStateFile))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(_processStateFile))
            {
                if (!int.TryParse(line, out var pid))
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        process.WaitForExit(1500);
                    }
                }
                catch
                {
                }
            }

            File.Delete(_processStateFile);
        }
        catch
        {
        }
    }

    private static void PaintCard(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(17, 24, 39));
        using var border = new Pen(Color.FromArgb(32, 44, 63), 1);
        using var path = Ui.RoundRect(new RectangleF(0, 0, control.Width - 1, control.Height - 1), 18);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (var card in _cards)
        {
            card.Width = Math.Max(360, ClientSize.Width - 54);
        }
    }

    private static Button MakeButton(string text, Color color, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = 100,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += onClick;
        return button;
    }
}

internal sealed class EffectCard : Panel
{
    private readonly Label _title;
    private readonly Label _desc;
    private readonly Label _category;
    private readonly LivePreview _preview;
    private readonly Button _applyButton;
    private bool _active;

    public EffectDef Effect { get; }
    public double Intensity { get => _preview.Intensity; set => _preview.Intensity = value; }
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Invalidate();
        }
    }

    public event EventHandler<EffectDef>? ApplyRequested;

    public EffectCard(EffectDef effect)
    {
        Effect = effect;
        Height = 132;
        Margin = new Padding(0, 0, 0, 12);
        Padding = new Padding(12);
        BackColor = Color.Transparent;

        _title = new Label
        {
            Text = effect.Name,
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252)
        };

        _category = new Label
        {
            Text = effect.Category,
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(125, 211, 252)
        };

        _desc = new Label
        {
            Text = effect.Description,
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(148, 163, 184)
        };

        _preview = new LivePreview(effect)
        {
            Dock = DockStyle.Right,
            Width = 132,
            Intensity = 1.0
        };

        _applyButton = new Button
        {
            Text = "Appliquer",
            Width = 96,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = effect.Accent,
            ForeColor = Color.White
        };
        _applyButton.FlatAppearance.BorderSize = 0;
        _applyButton.Click += (_, _) => ApplyRequested?.Invoke(this, Effect);

        var textHost = new Panel { Dock = DockStyle.Fill };
        var buttonHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            WrapContents = false
        };
        buttonHost.Controls.Add(_applyButton);
        textHost.Controls.Add(buttonHost);
        textHost.Controls.Add(_desc);
        textHost.Controls.Add(_category);
        textHost.Controls.Add(_title);

        Controls.Add(textHost);
        Controls.Add(_preview);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 20);
        using var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(14, 20, 31), Color.FromArgb(10, 14, 20), LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(_active ? Color.FromArgb(59, 130, 246) : Color.FromArgb(31, 41, 55), _active ? 2 : 1);
        using var accent = new SolidBrush(Color.FromArgb(_active ? 70 : 28, Effect.Accent));
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(border, path);
        e.Graphics.FillEllipse(accent, Width - 28, 12, 8, 8);
        base.OnPaint(e);
    }
}

internal sealed class LivePreview : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly EffectDef _effect;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public double Intensity { get; set; } = 1.0;

    public LivePreview(EffectDef effect)
    {
        _effect = effect;
        DoubleBuffered = true;
        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(10, 14, 20), Color.FromArgb(20, 24, 34), LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(bg, ClientRectangle);

        var elapsed = (DateTime.UtcNow - _startedAt).TotalSeconds;
        var zones = _effect.Frame(elapsed, Intensity);

        var keyboard = new RectangleF(10, 14, Width - 20, Height * 0.44f);
        var bar = new RectangleF(16, Height * 0.70f, Width - 32, 12);

        DrawStrip(e.Graphics, keyboard, zones, true);
        DrawStrip(e.Graphics, bar, zones, false);
        using var gloss = new Pen(Color.FromArgb(36, 255, 255, 255), 1);
        e.Graphics.DrawLine(gloss, 10, Height - 8, Width - 10, Height - 8);
    }

    private static void DrawStrip(Graphics graphics, RectangleF rect, IReadOnlyList<Color> colors, bool keyboard)
    {
        using var shell = new SolidBrush(Color.FromArgb(26, 32, 44));
        using var border = new Pen(Color.FromArgb(55, 65, 81), 1);
        using var path = Ui.RoundRect(rect, keyboard ? 14 : 8);
        graphics.FillPath(shell, path);
        graphics.DrawPath(border, path);

        var zoneWidth = rect.Width / 4f;
        for (var index = 0; index < 4; index++)
        {
            var zone = new RectangleF(rect.X + zoneWidth * index + 3, rect.Y + 3, zoneWidth - 6, rect.Height - 6);
            using var fill = new SolidBrush(colors[index]);
            using var zonePath = Ui.RoundRect(zone, keyboard ? 10 : 6);
            graphics.FillPath(fill, zonePath);

            if (!keyboard)
            {
                continue;
            }

            using var key = new SolidBrush(Color.FromArgb(24, 28, 38));
            for (var row = 0; row < 2; row++)
            for (var col = 0; col < 3; col++)
            {
                var keyRect = new RectangleF(
                    zone.X + col * (zone.Width / 3f) + 2,
                    zone.Y + row * (zone.Height / 2f) + 2,
                    zone.Width / 3f - 4,
                    zone.Height / 2f - 4);
                using var keyPath = Ui.RoundRect(keyRect, 3);
                graphics.FillPath(key, keyPath);
            }
        }
    }
}

internal sealed class OrbitVisualizer : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public Color Accent { get; set; } = Color.FromArgb(56, 189, 248);

    public OrbitVisualizer()
    {
        DoubleBuffered = true;
        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Color.Transparent);

        var elapsed = (DateTime.UtcNow - _startedAt).TotalSeconds;
        var rect = new RectangleF(10, 10, Width - 20, Height - 20);
        var center = new PointF(Width / 2f, Height / 2f);
        var angle = elapsed * 120.0;
        var radius = rect.Width / 2f - 8f;
        var rad = (float)(Math.PI * angle / 180.0);
        var point = new PointF(center.X + (float)Math.Cos(rad) * radius, center.Y + (float)Math.Sin(rad) * radius);

        using var basePen = new Pen(Color.FromArgb(35, 71, 85, 105), 8);
        using var arcPen = new Pen(Color.FromArgb(160, Accent), 8) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var core = new SolidBrush(Color.FromArgb(18, 26, 39));
        using var glow = new SolidBrush(Color.FromArgb(200, Accent));

        e.Graphics.DrawArc(basePen, rect, 0, 360);
        e.Graphics.DrawArc(arcPen, rect, (float)angle - 80, 120);
        e.Graphics.FillEllipse(core, center.X - 16, center.Y - 16, 32, 32);
        e.Graphics.FillEllipse(glow, point.X - 6, point.Y - 6, 12, 12);
    }
}

internal sealed record EffectDef(
    string Name,
    string Category,
    string Description,
    string FileName,
    string Arguments,
    Color Accent,
    Func<double, double, Color[]> Frame)
{
    public override string ToString() => Name;
}

internal static class EffectCatalog
{
    public static List<EffectDef> Build() =>
    [
        Make("Ambilight Reactif", "Temps Reel", "Suit l'ecran avec une reponse rapide.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll", ["#38bdf8","#1d4ed8","#7c3aed","#f472b6"], Ui.Pulse),
        Make("Mirror", "Temps Reel", "Reflet stylise de l'image sur clavier et bandeau.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --mirror", ["#2563eb","#06b6d4","#ec4899","#f97316"], Ui.Mirror),
        Make("Audio Pulse", "Temps Reel", "Reagit au son du PC en mode visuel.", "dotnet", @".\csharp-audio\bin\Release\net8.0-windows\AudioReactive.dll --style fire", ["#ff5a14","#ffb21e","#fff0b8","#b91c1c"], Ui.Pulse),
        Make("K2000", "Classiques", "Scanner rouge iconique.", "python", "k2000.py", ["#ff3b30"], Ui.Scanner),
        Make("Police", "Classiques", "Alternance rouge et bleu facon gyrophare.", "python", "police.py", ["#ef4444","#2563eb"], Ui.Police),
        Make("Cyberpunk", "Classiques", "Palette neon cyan magenta violet.", "python", "cyberpunk.py", ["#00eaff","#ff3bf4","#7c3aed","#ffffff"], Ui.Pulse),
        Make("VHS Neon", "Classiques", "Retro futuriste cyan et rose.", "python", "vhs_neon.py", ["#22d3ee","#ec4899","#a855f7","#f8fafc"], Ui.Pulse),
        Make("Lava Wave", "Classiques", "Vague chaude rouge ambre braise.", "python", "lava_wave.py", ["#ff461e","#ffaa1e","#fff5b4","#b91c1c"], Ui.Pulse),
        Make("Aurora Drift", "Classiques", "Derive cyan vert violet tres douce.", "python", "aurora_drift.py", ["#46dcff","#50ffaa","#966eff","#e0fbff"], Ui.Pulse),
        Make("Prism Flow", "Classiques", "Arc-en-ciel fluide plus propre.", "python", "prism_flow.py", ["#ff4646","#ffb43c","#ffe65a","#46dc78"], Ui.Pulse),
        Make("Deep Ocean", "Classiques", "Bleu profond et reflets glacés.", "python", "deep_ocean.py", ["#1446b4","#28b4e6","#d2f0ff","#0f172a"], Ui.Pulse),
        Make("Stack Fall", "Classiques", "Empilement vertical type Tetris.", "python", "stack_fall.py", ["#22c55e","#facc15","#fb7185","#38bdf8"], Ui.StackRows),
        Make("Autumn Glow", "Chaleureux", "Ambre, cuivre et bordeaux.", "python", "autumn_glow.py", ["#f59e0b","#b45309","#7f1d1d","#fca5a5"], Ui.Pulse),
        Make("Copper Pulse", "Chaleureux", "Cuivre plus metallique et nerveux.", "python", "copper_pulse.py", ["#fb923c","#c2410c","#78350f","#fde68a"], Ui.Pulse),
        Make("Ember Rain", "Chaleureux", "Braises et etincelles plus cinema.", "python", "ember_rain.py", ["#dc2626","#ea580c","#f59e0b","#450a0a"], Ui.Pulse),
        Make("Death Stranding Drift", "Jeux", "Bleu spectral et ambiance sci-fi.", "python", "death_stranding_drift.py", ["#dbeafe","#60a5fa","#1d4ed8","#1e293b"], Ui.Pulse),
        Make("Factory Core", "Jeux", "Orange industriel net et assume.", "python", "factory_core.py", ["#f97316","#facc15","#475569","#f8fafc"], Ui.Pulse),
        Make("DedSec Glitch", "Jeux", "Hacking cyan rouge avec glitch.", "python", "dedsec_glitch.py", ["#06b6d4","#ef4444","#111827","#f8fafc"], Ui.Police),
        Make("Frontier Dust", "Jeux", "Poussiere cuivre et sable western.", "python", "frontier_dust.py", ["#fbbf24","#c2410c","#a16207","#7c2d12"], Ui.Pulse),
        Make("Ice Scanner", "Froids / Speciaux", "Scanner bleu glace plus premium.", "python", "ice_scanner.py", ["#93c5fd"], Ui.Scanner),
        Make("Storm Mode", "Froids / Speciaux", "Orage bleu avec eclairs blancs.", "python", "storm_mode.py", ["#1d4ed8","#93c5fd","#f8fafc","#0f172a"], Ui.Pulse),
        Make("Acid Pulse", "Froids / Speciaux", "Vert toxique plus agressif.", "python", "acid_pulse.py", ["#84cc16","#22c55e","#14532d","#d9f99d"], Ui.Pulse),
        Make("Alter Lab", "Froids / Speciaux", "Blanc et bleu clinique futuriste.", "python", "alter_lab.py", ["#e2e8f0","#93c5fd","#6366f1","#0f172a"], Ui.Pulse)
    ];

    private static EffectDef Make(string name, string category, string description, string fileName, string arguments, string[] palette, Func<Color[], double, double, Color[]> frame)
    {
        var colors = palette.Select(ColorTranslator.FromHtml).ToArray();
        return new EffectDef(name, category, description, fileName, arguments, colors[0], (t, i) => frame(colors, t, i));
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

    public static Color[] Pulse(Color[] colors, double t, double intensity)
    {
        var pulse = 0.35 + ((Math.Sin(t * 2.6) + 1.0) / 2.0) * 0.65;
        return Enumerable.Range(0, 4)
            .Select(i => Scale(colors[i % colors.Length], Math.Min(1.0, pulse * (0.8 + i * 0.06)) * intensity))
            .ToArray();
    }

    public static Color[] Scanner(Color[] colors, double t, double intensity)
    {
        var pos = ((Math.Sin(t * 2.2) + 1.0) / 2.0) * 3.0;
        return Enumerable.Range(0, 4)
            .Select(i => Scale(colors[0], Math.Max(0.12, 1.0 - Math.Abs(i - pos) * 0.55) * intensity))
            .ToArray();
    }

    public static Color[] Police(Color[] colors, double t, double intensity)
    {
        var phase = (int)(t * 5.5) % 4;
        return Enumerable.Range(0, 4).Select(i =>
        {
            var left = i < 2;
            var active = left ? phase is 0 or 1 : phase is 2 or 3;
            return Scale(left ? colors[0] : colors[Math.Min(1, colors.Length - 1)], (active ? 1.0 : 0.18) * intensity);
        }).ToArray();
    }

    public static Color[] Mirror(Color[] colors, double t, double intensity)
    {
        var shift = (Math.Sin(t * 1.4) + 1.0) / 2.0;
        return Enumerable.Range(0, 4)
            .Select(i => Blend(colors[i % colors.Length], colors[(i + 1) % colors.Length], shift, intensity))
            .ToArray();
    }

    public static Color[] StackRows(Color[] colors, double t, double intensity)
    {
        var step = ((int)(t * 2.8)) % 8;
        var fillCount = (step % 4) + 1;
        var phase = step >= 4 ? 1 : 0;
        return Enumerable.Range(0, 4).Select(i =>
        {
            var active = phase == 0 ? i < fillCount : i >= 4 - fillCount;
            var color = colors[(i + step) % colors.Length];
            return Scale(color, (active ? 1.0 : 0.14) * intensity);
        }).ToArray();
    }

    private static Color Scale(Color color, double factor) =>
        Color.FromArgb(
            Math.Clamp((int)Math.Round(color.R * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.G * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.B * factor), 0, 255));

    private static Color Blend(Color left, Color right, double ratio, double intensity) =>
        Scale(
            Color.FromArgb(
                (int)Math.Round(left.R + (right.R - left.R) * ratio),
                (int)Math.Round(left.G + (right.G - left.G) * ratio),
                (int)Math.Round(left.B + (right.B - left.B) * ratio)),
            intensity);
}
