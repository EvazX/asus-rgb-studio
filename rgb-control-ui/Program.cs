using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using var singleInstanceMutex = new Mutex(true, @"Global\AsusKeyboardFx.SingleInstance", out var isFirstInstance);
if (!isFirstInstance)
{
    return;
}

ApplicationConfiguration.Initialize();
Application.Run(new FxDeckForm());

internal sealed class FxDeckForm : Form
{
    private const int WmNclButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    private static readonly string BaseDir = ResolveBaseDir();
    private static readonly string StateFile = Path.Combine(BaseDir, "rgb_intensity.txt");
    private static readonly string ProcessStateFile = Path.Combine(BaseDir, "rgb_effect_pids.txt");
    private static readonly string[] FavoriteEffects = ["Daily Mirror", "K2000", "Police"];

    private readonly List<EffectDef> _effects = EffectCatalog.Build();
    private readonly List<GameProfile> _gameProfiles = GameProfileCatalog.Build();
    private readonly ListBox _effectList;
    private readonly NotifyIcon _trayIcon;
    private readonly Label _selectedDetail;
    private readonly Label _statusLabel;
    private readonly Label _intensityValue;
    private readonly IntensitySlider _intensityTrack;
    private readonly PillButton _startupButton;
    private readonly System.Windows.Forms.Timer _watchdog;
    private readonly System.Windows.Forms.Timer _realPreviewTimer;
    private readonly ComboBox _categorySelect;

    private Process? _currentProcess;
    private EffectDef? _currentEffect;
    private EffectDef? _previewRestoreEffect;
    private EffectDef? _effectBeforeGameProfile;
    private EffectDef? _effectBeforeScreenAction;
    private GameProfile? _activeGameProfile;
    private string? _activeScreenAction;
    private string _activeFilter = "Tous";
    private bool _keepEffectAlive;
    private bool _allowExit;
    private bool _autoGameProfiles = true;
    private bool _suppressAutoHide;
    private int _gtaWantedHits;
    private int _gtaWantedMisses;
    private DateTime _lastRestartUtc = DateTime.MinValue;
    private int _intensityPercent = 100;

    public FxDeckForm()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        const int width = 360;
        const int height = 430;

        Text = "ASUS Keyboard FX";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(workingArea.Right - width - 12, workingArea.Bottom - height - 12);
        Size = new Size(width, height);
        MinimumSize = new Size(340, 390);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0.94;
        BackColor = Theme.Void;
        ForeColor = Theme.Text;

        _intensityPercent = ReadIntensityPercent();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        Controls.Add(root);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(2, 0, 0, 0)
        };
        header.MouseDown += (_, e) => DragPanel(e);

        var title = new Label
        {
            Text = "ASUS Keyboard FX",
            Dock = DockStyle.Left,
            Width = 250,
            Font = new Font("Segoe UI Variable Display", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        title.MouseDown += (_, e) => DragPanel(e);

        var hideButton = MakeButton("×", Color.FromArgb(22, 31, 44), (_, _) => HidePanel());
        hideButton.Dock = DockStyle.Right;
        hideButton.Width = 34;
        hideButton.Height = 30;
        hideButton.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);

        header.Controls.Add(hideButton);
        header.Controls.Add(title);
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(BuildFavoritesPanel(), 0, 1);

        _categorySelect = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(14, 22, 34),
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f)
        };
        _categorySelect.SelectedIndexChanged += (_, _) =>
        {
            _activeFilter = _categorySelect.SelectedItem?.ToString() ?? "Tous";
            PopulateEffects();
        };
        _categorySelect.DropDown += (_, _) => _suppressAutoHide = true;
        _categorySelect.DropDownClosed += (_, _) =>
        {
            _suppressAutoHide = false;
            if (_effectList is not null)
            {
                _effectList.Focus();
            }
        };
        root.Controls.Add(_categorySelect, 0, 2);

        _effectList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(7, 12, 18),
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 34,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            IntegralHeight = false
        };
        _effectList.DisplayMember = nameof(EffectDef.Name);
        _effectList.SelectedIndexChanged += (_, _) => UpdateSelectedDetail();
        _effectList.DoubleClick += (_, _) => ApplySelectedEffect();
        _effectList.KeyDown += EffectListKeyDown;
        _effectList.DrawItem += DrawEffectItem;
        root.Controls.Add(_effectList, 0, 3);

        _selectedDetail = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.Muted,
            Text = "Fleches: naviguer | Entree: lancer | T: tester | Echap: masquer",
            Padding = new Padding(4, 6, 4, 0)
        };
        root.Controls.Add(_selectedDetail, 0, 4);

        _intensityTrack = new IntensitySlider
        {
            Dock = DockStyle.Top,
            Minimum = 0,
            Maximum = 100,
            Height = 32,
            Value = _intensityPercent,
        };
        _intensityTrack.ValueChanged += (_, value) => SetIntensity(value);
        _intensityValue = new Label
        {
            Text = $"{_intensityPercent}%",
            Dock = DockStyle.Right,
            Width = 52,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            ForeColor = Theme.Mint,
            TextAlign = ContentAlignment.MiddleRight
        };
        var intensityTitle = new Label
        {
            Text = "Intensite live",
            Dock = DockStyle.Left,
            Width = 112,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Theme.Muted
        };
        var intensityLine = new Panel { Dock = DockStyle.Top, Height = 24, Padding = new Padding(2, 2, 2, 0) };
        intensityLine.Controls.Add(_intensityValue);
        intensityLine.Controls.Add(intensityTitle);
        var controls = BuildControlsPanel();
        controls.Controls.Add(_intensityTrack);
        controls.Controls.Add(intensityLine);
        root.Controls.Add(controls, 0, 5);

        var footer = BuildFooter();
        _statusLabel = new Label
        {
            Text = "Pret",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.Cyan,
            TextAlign = ContentAlignment.MiddleLeft
        };
        footer.Controls.Add(_statusLabel);
        root.Controls.Add(footer, 0, 6);

        _startupButton = MakeButton("Startup", Theme.Grape, (_, _) => ToggleStartup());
        _startupButton.Dock = DockStyle.Right;
        _startupButton.Margin = new Padding(0, 0, 8, 0);
        footer.Controls.Add(_startupButton);

        BuildFilters();
        PopulateEffects();
        CleanupTrackedProcesses();
        WriteIntensityState();
        RefreshStartupButton();
        SetStatus("Nouvelle interface chargee");

        _watchdog = new System.Windows.Forms.Timer { Interval = 1400 };
        _watchdog.Tick += (_, _) => WatchdogTick();
        _watchdog.Start();
        _realPreviewTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _realPreviewTimer.Tick += (_, _) => EndRealPreview();
        _trayIcon = BuildTrayIcon();

        Shown += (_, _) => _effectList.Focus();
        Deactivate += (_, _) =>
        {
            if (!_suppressAutoHide)
            {
                HidePanel();
            }
        };
        MouseWheel += (_, e) => AdjustIntensityByWheel(e.Delta);
        _effectList.MouseWheel += (_, e) => AdjustIntensityByWheel(e.Delta, controlOnly: true);
        KeyDown += FormKeyDown;
        Resize += (_, _) => ApplyFlyoutShape();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HidePanel();
            }
        };
        FormClosing += (_, e) =>
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HidePanel();
                return;
            }

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _keepEffectAlive = false;
            StopCurrentEffect();
        };

        ApplyFlyoutShape();
    }

    private Panel BuildActivePanel()
    {
        var panel = new GlowPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 18, 12),
            Main = Color.FromArgb(16, 22, 38),
            Accent = Theme.Grape
        };
        return panel;
    }

    private Panel BuildControlsPanel()
    {
        return new GlowPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8),
            Main = Color.FromArgb(8, 17, 24),
            Accent = Theme.Mint
        };
    }

    private Panel BuildFavoritesPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 0)
        };

        foreach (var favorite in FavoriteEffects)
        {
            var effect = FindEffect(favorite);
            if (effect is null)
            {
                continue;
            }

            var button = MakeButton(ShortEffectName(effect.Name), effect.Accent, (_, _) => ApplyEffect(effect));
            button.Width = 98;
            button.Height = 28;
            button.Margin = new Padding(0, 0, 8, 0);
            panel.Controls.Add(button);
        }

        return panel;
    }

    private Panel BuildFooter()
    {
        var footer = new GlowPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 8),
            Main = Color.FromArgb(9, 13, 22),
            Accent = Theme.CyanDeep
        };

        var stopButton = MakeButton("Stop", Theme.Danger, (_, _) => StopCurrentEffect());
        stopButton.Dock = DockStyle.Right;

        var patternsButton = MakeButton("Patterns", Theme.CyanDeep, (_, _) => OpenPatterns());
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

        _categorySelect.BeginUpdate();
        _categorySelect.Items.Clear();
        foreach (var filter in filters)
        {
            _categorySelect.Items.Add(filter);
        }
        _categorySelect.SelectedItem = _activeFilter;
        _categorySelect.EndUpdate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 22);
        using var border = new Pen(Color.FromArgb(95, Theme.Cyan), 1.4f);
        using var glow = new Pen(Color.FromArgb(42, Theme.Mint), 3.5f);
        e.Graphics.DrawPath(glow, path);
        e.Graphics.DrawPath(border, path);
    }

    private void ApplyFlyoutShape()
    {
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width, Height), 22);
        Region?.Dispose();
        Region = new Region(path);
        Invalidate();
    }

    private void DragPanel(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
    }

    private void PopulateEffects()
    {
        var previous = SelectedEffect?.Name ?? _currentEffect?.Name;
        _effectList.BeginUpdate();
        _effectList.Items.Clear();

        var selected = _activeFilter == "Tous"
            ? _effects
            : _effects.Where(effect => effect.Group == _activeFilter);

        foreach (var effect in selected)
        {
            _effectList.Items.Add(effect);
        }

        _effectList.EndUpdate();

        var index = 0;
        if (!string.IsNullOrWhiteSpace(previous))
        {
            for (var i = 0; i < _effectList.Items.Count; i++)
            {
                if (_effectList.Items[i] is EffectDef effect && effect.Name == previous)
                {
                    index = i;
                    break;
                }
            }
        }

        if (_effectList.Items.Count > 0)
        {
            _effectList.SelectedIndex = Math.Clamp(index, 0, _effectList.Items.Count - 1);
        }

        UpdateSelectedDetail();
    }

    private EffectDef? SelectedEffect => _effectList.SelectedItem as EffectDef;

    private void ApplySelectedEffect()
    {
        if (SelectedEffect is { } effect)
        {
            ApplyEffect(effect);
        }
    }

    private void PreviewSelectedEffect()
    {
        if (SelectedEffect is { } effect)
        {
            PreviewEffect(effect);
        }
    }

    private void UpdateSelectedDetail()
    {
        if (SelectedEffect is not { } effect)
        {
            _selectedDetail.Text = "Aucun effet disponible.";
            return;
        }

        var active = _currentEffect?.Name == effect.Name ? "Actif - " : "";
        _selectedDetail.Text = $"{active}{effect.Group} - {effect.Description}";
    }

    private void EffectListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplySelectedEffect();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.T)
        {
            PreviewSelectedEffect();
            e.Handled = true;
        }
    }

    private void FormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            HidePanel();
            e.Handled = true;
        }
    }

    private void DrawEffectItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _effectList.Items.Count)
        {
            return;
        }

        var effect = (EffectDef)_effectList.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var active = _currentEffect?.Name == effect.Name;
        var back = selected ? Color.FromArgb(22, 42, 58) : Color.FromArgb(7, 12, 18);
        using var bg = new SolidBrush(back);
        using var text = new SolidBrush(active ? Theme.Mint : Theme.Text);
        using var accent = new SolidBrush(effect.Accent);
        e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.FillRectangle(accent, e.Bounds.Left + 3, e.Bounds.Top + 7, 4, e.Bounds.Height - 14);
        e.Graphics.DrawString(effect.Name, _effectList.Font, text, e.Bounds.Left + 14, e.Bounds.Top + 7);
        if (active)
        {
            using var small = new Font("Segoe UI", 8f);
            using var muted = new SolidBrush(Theme.Muted);
            e.Graphics.DrawString("actif", small, muted, e.Bounds.Right - 42, e.Bounds.Top + 9);
        }
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir", null, (_, _) => ShowPanel());
        menu.Items.Add(new ToolStripSeparator());
        foreach (var favorite in FavoriteEffects)
        {
            var effect = FindEffect(favorite);
            if (effect is null)
            {
                continue;
            }

            menu.Items.Add($"Lancer {ShortEffectName(effect.Name)}", null, (_, _) => ApplyEffect(effect));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Intensite +10%", null, (_, _) => SetIntensity(_intensityPercent + 10));
        menu.Items.Add("Intensite -10%", null, (_, _) => SetIntensity(_intensityPercent - 10));
        menu.Items.Add(new ToolStripSeparator());
        var gameProfilesItem = new ToolStripMenuItem("Profils jeux auto")
        {
            Checked = _autoGameProfiles,
            CheckOnClick = true
        };
        gameProfilesItem.CheckedChanged += (_, _) =>
        {
            _autoGameProfiles = gameProfilesItem.Checked;
            if (!_autoGameProfiles)
            {
                _activeGameProfile = null;
                _effectBeforeGameProfile = null;
                _activeScreenAction = null;
                _effectBeforeScreenAction = null;
                _gtaWantedHits = 0;
                _gtaWantedMisses = 0;
            }
            SetStatus(_autoGameProfiles ? "Actions jeux auto actives" : "Actions jeux auto desactives");
        };
        menu.Items.Add(gameProfilesItem);
        menu.Items.Add("Stop + blanc", null, (_, _) => StopCurrentEffect());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApplication());

        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        var icon = File.Exists(iconPath) ? new Icon(iconPath) : Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        var tray = new NotifyIcon
        {
            Icon = icon,
            Text = "ASUS Keyboard FX",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowPanel();
        return tray;
    }

    private void ShowPanel()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        Location = new Point(workingArea.Right - Width - 10, workingArea.Bottom - Height - 8);
        WindowState = FormWindowState.Normal;
        Show();
        Activate();
        _effectList.Focus();
    }

    private void HidePanel()
    {
        Hide();
        SetStatus("Masque dans la zone de notification");
    }

    private void ExitApplication()
    {
        _allowExit = true;
        Close();
    }

    private void AdjustIntensityByWheel(int delta, bool controlOnly = false)
    {
        if (controlOnly && (ModifierKeys & Keys.Control) != Keys.Control)
        {
            return;
        }

        var step = (ModifierKeys & Keys.Shift) == Keys.Shift ? 10 : 5;
        SetIntensity(_intensityPercent + (delta > 0 ? step : -step));
    }

    private static string ShortEffectName(string name) => name switch
    {
        "Daily Mirror" => "Daily",
        _ => name
    };

    private void ApplyEffect(EffectDef effect, bool automatic = false)
    {
        _realPreviewTimer.Stop();
        _previewRestoreEffect = null;
        if (!automatic)
        {
            _activeGameProfile = null;
            _effectBeforeGameProfile = null;
            _activeScreenAction = null;
            _effectBeforeScreenAction = null;
            _gtaWantedHits = 0;
            _gtaWantedMisses = 0;
        }
        StopCurrentEffect(false);
        CleanupTrackedProcesses();
        _currentEffect = effect;
        _keepEffectAlive = true;
        StartEffect(effect);
        UpdateActiveEffect(effect);
    }

    private void PreviewEffect(EffectDef effect)
    {
        _realPreviewTimer.Stop();
        _previewRestoreEffect = _currentEffect;
        StopCurrentEffect(false);
        CleanupTrackedProcesses();
        _currentEffect = effect;
        _keepEffectAlive = false;
        StartEffect(effect);
        UpdateActiveEffect(effect);
        SetStatus($"Test reel 5s: {effect.Name}");
        _realPreviewTimer.Start();
    }

    private void EndRealPreview()
    {
        _realPreviewTimer.Stop();
        var restore = _previewRestoreEffect;
        _previewRestoreEffect = null;
        StopCurrentEffect(false);

        if (restore is not null)
        {
            ApplyEffect(restore);
            SetStatus($"Retour: {restore.Name}");
            return;
        }

        RestoreWhite();
        SetStatus("Test termine - blanc applique");
    }

    private void UpdateActiveEffect(EffectDef effect)
    {
        HighlightCurrentEffect();
        UpdateSelectedDetail();
    }

    private void HighlightCurrentEffect()
    {
        _effectList.Invalidate();
    }

    private void StartEffect(EffectDef effect)
    {
        var fileName = effect.FileName;
        var arguments = effect.Arguments;
        if (string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) &&
            TryUsePublishedEngine(arguments, out var engineExe, out var engineArguments))
        {
            fileName = engineExe;
            arguments = engineArguments;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = BaseDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var intensity = (_intensityPercent / 100.0).ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["RGB_STATE_FILE"] = StateFile;
        if (string.Equals(fileName, "python", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["RGB_INTENSITY"] = intensity;
        }
        else if (!arguments.Contains("--benchmark", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Arguments = $"{arguments} --intensity-boost {intensity}";
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

    private static bool TryUsePublishedEngine(string arguments, out string engineExe, out string engineArguments)
    {
        engineExe = "";
        engineArguments = arguments;

        var trimmed = arguments.TrimStart();
        if (!trimmed.StartsWith(@".\", StringComparison.Ordinal) && !trimmed.StartsWith("./", StringComparison.Ordinal))
        {
            return false;
        }

        var split = trimmed.IndexOf(' ');
        var dllPart = split >= 0 ? trimmed[..split] : trimmed;
        if (!dllPart.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = Path.Combine(BaseDir, Path.ChangeExtension(dllPart, ".exe"));
        if (!File.Exists(candidate))
        {
            return false;
        }

        engineExe = candidate;
        engineArguments = split >= 0 ? trimmed[(split + 1)..] : "";
        return true;
    }

    private void WatchdogTick()
    {
        UpdateScreenActionProfile();
        UpdateGameProfile();

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

    private void UpdateGameProfile()
    {
        if (!_autoGameProfiles || _activeScreenAction is not null)
        {
            return;
        }

        var detected = _gameProfiles.FirstOrDefault(IsGameRunning);
        if (detected?.Name == _activeGameProfile?.Name)
        {
            return;
        }

        if (detected is not null)
        {
            if (_activeGameProfile is null)
            {
                _effectBeforeGameProfile = _currentEffect;
            }

            _activeGameProfile = detected;
            var effect = FindEffect(detected.EffectName);
            if (effect is null)
            {
                SetStatus($"Profil jeu introuvable: {detected.EffectName}");
                return;
            }

            ApplyEffect(effect, automatic: true);
            SetStatus($"Profil jeu: {detected.Name} -> {effect.Name}");
            return;
        }

        if (_activeGameProfile is null)
        {
            return;
        }

        var restore = _effectBeforeGameProfile;
        var previousGame = _activeGameProfile.Name;
        _activeGameProfile = null;
        _effectBeforeGameProfile = null;

        if (restore is not null)
        {
            ApplyEffect(restore, automatic: true);
            SetStatus($"Jeu ferme: retour {restore.Name}");
        }
        else
        {
            StopCurrentEffect();
            SetStatus($"Jeu ferme: {previousGame}");
        }
    }

    private bool IsGameRunning(GameProfile profile)
    {
        foreach (var processName in profile.ProcessNames)
        {
            try
            {
                if (Process.GetProcessesByName(processName).Length > 0)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore transient process access failures.
            }
        }

        return false;
    }

    private EffectDef? FindEffect(string name) =>
        _effects.FirstOrDefault(effect => string.Equals(effect.Name, name, StringComparison.OrdinalIgnoreCase));

    private void UpdateScreenActionProfile()
    {
        if (!_autoGameProfiles)
        {
            return;
        }

        var wanted = IsGtaRunning() && DetectGtaWantedHud();
        if (wanted)
        {
            _gtaWantedHits++;
            _gtaWantedMisses = 0;
        }
        else
        {
            _gtaWantedMisses++;
            _gtaWantedHits = Math.Max(0, _gtaWantedHits - 1);
        }

        if (_activeScreenAction is null && _gtaWantedHits >= 2)
        {
            var effect = FindEffect("Police");
            if (effect is null)
            {
                return;
            }

            _effectBeforeScreenAction = _currentEffect;
            _activeScreenAction = "GTA V - recherche police";
            ApplyEffect(effect, automatic: true);
            SetStatus("Action GTA detectee: recherche police");
            return;
        }

        if (_activeScreenAction is not null && _gtaWantedMisses >= 4)
        {
            var restore = _effectBeforeScreenAction;
            var action = _activeScreenAction;
            _activeScreenAction = null;
            _effectBeforeScreenAction = null;
            _gtaWantedHits = 0;
            _gtaWantedMisses = 0;

            if (restore is not null)
            {
                ApplyEffect(restore, automatic: true);
                SetStatus($"Action terminee: retour {restore.Name}");
            }
            else
            {
                StopCurrentEffect();
                SetStatus($"Action terminee: {action}");
            }
        }
    }

    private static bool IsGtaRunning()
    {
        var names = new[] { "GTA5", "GTA5_Enhanced", "PlayGTAV" };
        foreach (var name in names)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore transient process access failures.
            }
        }

        return false;
    }

    private static bool DetectGtaWantedHud()
    {
        try
        {
            var screen = Screen.PrimaryScreen;
            if (screen is null)
            {
                return false;
            }

            var bounds = screen.Bounds;
            var width = Math.Min(360, Math.Max(160, bounds.Width / 5));
            var height = Math.Min(110, Math.Max(70, bounds.Height / 9));
            var source = new Rectangle(bounds.Right - width - 12, bounds.Top + 12, width, height);

            using var bitmap = new Bitmap(source.Width, source.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(source.Location, Point.Empty, source.Size);
            }

            var brightNeutral = 0;
            var sampled = 0;
            for (var y = 0; y < bitmap.Height; y += 2)
            {
                for (var x = 0; x < bitmap.Width; x += 2)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                    var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                    if (max > 178 && max - min < 58)
                    {
                        brightNeutral++;
                    }
                    sampled++;
                }
            }

            var ratio = sampled == 0 ? 0.0 : brightNeutral / (double)sampled;
            return brightNeutral > 95 && ratio > 0.018;
        }
        catch
        {
            return false;
        }
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
        HighlightCurrentEffect();
        UpdateSelectedDetail();
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
            var patternPath = Path.Combine(BaseDir, "test_patterns.html");
            Process.Start(new ProcessStartInfo
            {
                FileName = patternPath,
                WorkingDirectory = BaseDir,
                UseShellExecute = true
            })?.Dispose();
            SetStatus("Page patterns ouverte");
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

    private void ToggleStartup()
    {
        try
        {
            if (StartupManager.IsEnabled())
            {
                StartupManager.Disable();
                SetStatus("Startup desactive");
            }
            else
            {
                StartupManager.Enable(BaseDir);
                SetStatus("Startup active");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Demarrage Windows indisponible: {ex.Message}");
        }

        RefreshStartupButton();
    }

    private void RefreshStartupButton()
    {
        var enabled = StartupManager.IsEnabled();
        _startupButton.Text = enabled ? "Auto On" : "Auto Off";
        _startupButton.BackColor = enabled ? Theme.MintDeep : Theme.Grape;
        _startupButton.Invalidate();
    }

    private static PillButton MakeButton(string text, Color color, EventHandler click)
    {
        var button = new PillButton
        {
            Text = text,
            Width = 84,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}

internal sealed class HeroHeader : Control
{
    public HeroHeader()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 26);
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(14, 24, 42), Color.FromArgb(48, 18, 34), LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillPath(bg, path);

        DrawLightRibbon(e.Graphics);
        DrawOrbit(e.Graphics, Width - 76, Height / 2f, 30, 25, Theme.Orange);
        DrawOrbit(e.Graphics, Width - 76, Height / 2f, 20, 205, Theme.Cyan);

        using var title = new Font("Segoe UI Variable Display", 20.5f, FontStyle.Bold);
        using var subtitle = new Font("Segoe UI", 9.5f);
        using var titleBrush = new SolidBrush(Color.White);
        using var subtitleBrush = new SolidBrush(Theme.Muted);
        e.Graphics.DrawString("ASUS Keyboard FX", title, titleBrush, 18, 14);
        e.Graphics.DrawString("Effets 4 zones + Ambilight", subtitle, subtitleBrush, 20, 51);
    }

    private static void DrawLightRibbon(Graphics graphics)
    {
        var colors = new[] { Theme.Cyan, Theme.Mint, Theme.Orange, Theme.Rose };
        for (var i = 0; i < colors.Length; i++)
        {
            var x = 18 + i * 42;
            using var pen = new Pen(Color.FromArgb(150, colors[i]), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(pen, x, 72, x + 28, 72);
        }
    }

    private static void DrawOrbit(Graphics graphics, float cx, float cy, float radius, float angleDegrees, Color color)
    {
        using var pen = new Pen(Color.FromArgb(150, color), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var dot = new SolidBrush(color);
        graphics.DrawArc(pen, cx - radius, cy - radius, radius * 2, radius * 2, angleDegrees, 95);
        var angle = angleDegrees * Math.PI / 180.0;
        graphics.FillEllipse(dot, cx + (float)Math.Cos(angle) * radius - 4, cy + (float)Math.Sin(angle) * radius - 4, 8, 8);
    }
}

internal sealed class FilterChip : Control
{
    private bool _active;

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            ForeColor = value ? Color.FromArgb(4, 12, 18) : Theme.Text;
            Invalidate();
        }
    }

    public FilterChip(string text)
    {
        Text = text;
        Height = 28;
        Width = Math.Max(72, TextRenderer.MeasureText(text, new Font("Segoe UI Semibold", 8.6f, FontStyle.Bold)).Width + 28);
        Padding = new Padding(12, 0, 12, 0);
        Margin = new Padding(0, 3, 7, 4);
        Font = new Font("Segoe UI Semibold", 8.6f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0, 0, Width - 1, Height - 1);
        using var path = Ui.RoundRect(rect, 15);
        using var fill = _active
            ? new LinearGradientBrush(ClientRectangle, Theme.Cyan, Theme.Mint, LinearGradientMode.Horizontal)
            : new LinearGradientBrush(ClientRectangle, Color.FromArgb(18, 25, 37), Color.FromArgb(10, 15, 23), LinearGradientMode.Vertical);
        using var border = new Pen(_active ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(36, 48, 66), 1);
        using var textBrush = new SolidBrush(ForeColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var textSize = e.Graphics.MeasureString(Text, Font);
        e.Graphics.DrawString(Text, Font, textBrush, (Width - textSize.Width) / 2f, (Height - textSize.Height) / 2f - 1);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }
}

internal sealed class PillButton : Button
{
    public PillButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 13);
        using var fill = new LinearGradientBrush(ClientRectangle, BackColor, Ui.Lighten(BackColor, 0.18), LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(Color.FromArgb(55, 255, 255, 255), 1);
        using var textBrush = new SolidBrush(ForeColor);
        pevent.Graphics.FillPath(fill, path);
        pevent.Graphics.DrawPath(border, path);
        var textSize = pevent.Graphics.MeasureString(Text, Font);
        pevent.Graphics.DrawString(Text, Font, textBrush, (Width - textSize.Width) / 2f, (Height - textSize.Height) / 2f - 1);
    }
}

internal sealed class IntensitySlider : Control
{
    private int _value;
    private bool _dragging;

    public event EventHandler<int>? ValueChanged;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;

    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (_value == next)
            {
                return;
            }

            _value = next;
            ValueChanged?.Invoke(this, _value);
            Invalidate();
        }
    }

    public IntensitySlider()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Height = 32;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(4, Height / 2f - 5, Width - 8, 10);
        var ratio = Maximum == Minimum ? 0 : (_value - Minimum) / (float)(Maximum - Minimum);
        var fill = new RectangleF(track.X, track.Y, Math.Max(10, track.Width * ratio), track.Height);
        var knobX = track.X + track.Width * ratio;

        using var trackPath = Ui.RoundRect(track, 5);
        using var trackBrush = new SolidBrush(Color.FromArgb(25, 35, 50));
        e.Graphics.FillPath(trackBrush, trackPath);

        using var fillPath = Ui.RoundRect(fill, 5);
        using var fillBrush = new LinearGradientBrush(fill, Theme.Cyan, Theme.Orange, LinearGradientMode.Horizontal);
        e.Graphics.FillPath(fillBrush, fillPath);

        using var glow = new SolidBrush(Color.FromArgb(55, Theme.Cyan));
        using var knob = new SolidBrush(Color.White);
        using var ring = new Pen(Color.FromArgb(180, Theme.Cyan), 2);
        e.Graphics.FillEllipse(glow, knobX - 12, Height / 2f - 12, 24, 24);
        e.Graphics.FillEllipse(knob, knobX - 7, Height / 2f - 7, 14, 14);
        e.Graphics.DrawEllipse(ring, knobX - 7, Height / 2f - 7, 14, 14);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragging = true;
        Capture = true;
        UpdateFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            UpdateFromMouse(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        UpdateFromMouse(e.X);
    }

    private void UpdateFromMouse(int x)
    {
        var ratio = Math.Clamp((x - 4) / (float)Math.Max(1, Width - 8), 0f, 1f);
        Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
    }
}

internal sealed class FxTile : Control
{
    private readonly LivePreview _preview;
    private readonly PillButton _applyButton;
    private readonly PillButton _testButton;
    private bool _active;

    public event EventHandler<EffectDef>? PreviewRequested;
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
        Margin = new Padding(0, 0, 0, 12);
        DoubleBuffered = true;
        BackColor = Theme.Deep;

        _preview = new LivePreview(effect, animated: false)
        {
            Dock = DockStyle.Right,
            Width = 96,
            Margin = new Padding(8, 12, 12, 12)
        };

        _applyButton = new PillButton
        {
            Text = "Lancer",
            Width = 78,
            Height = 30,
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
        };
        _applyButton.Click += (_, _) => ApplyRequested?.Invoke(this, Effect);

        _testButton = new PillButton
        {
            Text = "Tester 5s",
            Width = 82,
            Height = 30,
            BackColor = Color.FromArgb(18, 29, 45),
            ForeColor = Theme.Cyan,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Margin = new Padding(0, 0, 8, 0)
        };
        _testButton.Click += (_, _) => PreviewRequested?.Invoke(this, Effect);

        var textHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 10, 8, 8),
            RowCount = 4,
            ColumnCount = 1
        };
        textHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        textHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        textHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        textHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var title = new Label
        {
            Text = effect.Name,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Font = new Font("Segoe UI Semibold", 11.8f, FontStyle.Bold),
            ForeColor = Color.White
        };
        var meta = new Label
        {
            Text = effect.Group,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Font = new Font("Segoe UI", 8.8f),
            ForeColor = effect.Accent
        };
        var desc = new Label
        {
            Text = effect.Description,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 8, 0),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Theme.Muted,
            AutoEllipsis = true
        };
        var buttonHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 0)
        };
        buttonHost.Controls.Add(_testButton);
        buttonHost.Controls.Add(_applyButton);

        textHost.Controls.Add(title, 0, 0);
        textHost.Controls.Add(meta, 0, 1);
        textHost.Controls.Add(desc, 0, 2);
        textHost.Controls.Add(buttonHost, 0, 3);
        Controls.Add(textHost);
        Controls.Add(_preview);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 22);
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(10, 16, 25), Color.FromArgb(4, 8, 13), LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(_active ? Effect.Accent : Color.FromArgb(22, 32, 46), _active ? 2.0f : 1f);
        using var glow = new SolidBrush(Color.FromArgb(_active ? 68 : 18, Effect.Accent));
        e.Graphics.FillPath(bg, path);
        e.Graphics.FillEllipse(glow, Width - 160, 12, 42, 42);
        using var side = new Pen(Color.FromArgb(_active ? 220 : 120, Effect.Accent), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(side, 9, 22, 9, Height - 22);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class LivePreview : Control
{
    private readonly System.Windows.Forms.Timer? _timer;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private EffectDef _effect;
    private double _intensity = 1.0;

    public EffectDef Effect
    {
        get => _effect;
        set
        {
            _effect = value;
            Invalidate();
        }
    }

    public double Intensity
    {
        get => _intensity;
        set
        {
            _intensity = value;
            Invalidate();
        }
    }

    public LivePreview(EffectDef effect, bool animated = true)
    {
        _effect = effect;
        DoubleBuffered = true;
        if (animated)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 90 };
            _timer.Tick += (_, _) =>
            {
                if (Visible)
                {
                    Invalidate();
                }
            };
            _timer.Start();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 18);
        using var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(5, 10, 16), Color.FromArgb(15, 23, 35), LinearGradientMode.Vertical);
        e.Graphics.FillPath(bg, path);

        var elapsed = (DateTime.UtcNow - _startedAt).TotalSeconds;
        var zones = Effect.Frame(elapsed, Intensity);
        DrawKeyboard(e.Graphics, new RectangleF(10, 14, Width - 20, Height * 0.50f), zones);
        DrawBar(e.Graphics, new RectangleF(14, Height - 26, Width - 28, 9), zones);
    }

    private static void DrawKeyboard(Graphics graphics, RectangleF rect, IReadOnlyList<Color> colors)
    {
        using var shell = new SolidBrush(Color.FromArgb(18, 26, 38));
        using var path = Ui.RoundRect(rect, 14);
        graphics.FillPath(shell, path);

        var zoneWidth = rect.Width / 4f;
        for (var zone = 0; zone < 4; zone++)
        {
            var zoneRect = new RectangleF(rect.X + zone * zoneWidth + 3, rect.Y + 3, zoneWidth - 6, rect.Height - 6);
            using var zoneBrush = new SolidBrush(colors[zone]);
            using var zonePath = Ui.RoundRect(zoneRect, 9);
            graphics.FillPath(zoneBrush, zonePath);

            using var keyBrush = new SolidBrush(Color.FromArgb(42, 4, 8, 13));
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Dispose();
        }

        base.Dispose(disposing);
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

internal sealed record GameProfile(
    string Name,
    string[] ProcessNames,
    string EffectName);

internal static class GameProfileCatalog
{
    public static List<GameProfile> Build() =>
    [
        new("Cyberpunk 2077", ["Cyberpunk2077"], "Cyberpunk"),
        new("Forza Horizon", ["ForzaHorizon4", "ForzaHorizon5"], "Radar Sweep"),
        new("Watch Dogs", ["WatchDogs", "WatchDogs2", "WatchDogsLegion"], "DedSec Glitch"),
        new("Red Dead Redemption 2", ["RDR2"], "Frontier Dust"),
        new("Minecraft Bedrock", ["Minecraft.Windows"], "Deep Ocean")
    ];
}

internal static class StartupManager
{
    private const string ShortcutName = "ASUS Keyboard FX.lnk";

    public static bool IsEnabled() => File.Exists(ShortcutPath);

    public static void Enable(string baseDir)
    {
        var target = ResolveLaunchTarget(baseDir);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell unavailable");
        dynamic shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Unable to create shell shortcut");
        dynamic shortcut = shell.CreateShortcut(ShortcutPath);
        shortcut.TargetPath = target;
        shortcut.Arguments = "";
        shortcut.WorkingDirectory = baseDir;
        shortcut.IconLocation = target;
        shortcut.Description = "Launch ASUS Keyboard FX at Windows startup";
        shortcut.Save();
    }

    public static void Disable()
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    private static string ResolveLaunchTarget(string baseDir)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "AsusKeyboardFx.exe"),
            Path.Combine(baseDir, "app", "AsusKeyboardFx.exe"),
            Path.Combine(baseDir, "rgb-control-ui", "bin", "Release", "net8.0-windows", "win-x64", "publish", "AsusKeyboardFx.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("AsusKeyboardFx.exe introuvable. Publie l'application avant d'activer le demarrage Windows.");
    }
}

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
        Make("Daily Mirror", "Live", "Ambilight doux et naturel, moins sensible aux petites variations.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --profile daily", ["#38bdf8","#1d4ed8","#7c3aed","#f472b6"], Ui.SlowOrbit),
        Make("Mirror", "Live", "Reflet gauche-droite de l'ecran entier sur les 4 zones.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --mirror --screen-mode full --fps 26 --threshold 2 --samples-x 8 --samples-y 5 --saturation-boost 3.0 --value-boost 0.88 --smoothing 0.24", ["#2563eb","#06b6d4","#ec4899","#f97316"], Ui.Mirror),
        Make("Cinema Glow", "Live", "Ambilight plus doux, stable, parfait pour films et YouTube.", "dotnet", @".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll --screen-mode vibrant --fps 22 --threshold 4 --samples-x 8 --samples-y 5 --saturation-boost 2.7 --value-boost 0.78 --neutral-threshold 32 --color-bias 3.0 --smoothing 0.38", ["#f59e0b","#ef4444","#38bdf8","#1e293b"], Ui.Breath),
        Make("Audio Pulse", "Live", "Reagit au son du PC, plutot pour musique et videos.", "dotnet", @".\csharp-audio\bin\Release\net8.0-windows\AudioReactive.dll --style fire", ["#ff5a14","#ffb21e","#fff0b8","#b91c1c"], Ui.Breath),

        Make("K2000", "Classiques", "Scanner rouge plus lent et lisible.", "python", "k2000.py --speed 0.18 --tail 0.30", ["#ff3b30"], Ui.Scanner),
        Make("Police", "Classiques", "Gyrophare rouge et bleu, reserve aux alertes/actions.", "python", "police.py --speed 0.22 --pause 0.11", ["#ef4444","#2563eb"], Ui.Police),
        Make("Prism Flow", "Classiques", "Arc-en-ciel fluide et moins nerveux.", "python", "prism_flow.py --speed 0.16", ["#ff4646","#ffb43c","#ffe65a","#46dc78"], Ui.Prism),
        Make("Stack Fall", "Classiques", "Empilement gauche-droite facon Tetris.", "python", "stack_fall.py --speed 0.28 --low-glow 0.10", ["#22c55e","#facc15","#fb7185","#38bdf8"], Ui.StackRows),

        Make("Cyberpunk", "Neon", "Cyan, magenta et violet, plus stable qu'avant.", "python", "cyberpunk.py --speed 0.16", ["#00eaff","#ff3bf4","#7c3aed","#ffffff"], Ui.Neon),
        Make("Neon Comet", "Neon", "Comete blanche avec trainee cyan magenta.", "python", "neon_comet.py --speed 0.13", ["#ffffff","#00eaff","#ff2cf0","#7c3aed"], Ui.Comet),
        Make("Matrix Rain", "Neon", "Pluie digitale laterale adaptee aux 4 zones.", "python", "matrix_rain.py --speed 0.12", ["#22ff6e","#beff5a","#003010","#001208"], Ui.Matrix),

        Make("Lava Wave", "Atmosphere", "Chaud, braise et vague orange plus lente.", "python", "lava_wave.py --speed 0.18", ["#ff461e","#ffaa1e","#fff5b4","#b91c1c"], Ui.Breath),
        Make("Aurora Drift", "Atmosphere", "Cyan, vert et violet, doux pour usage long.", "python", "aurora_drift.py --speed 0.22", ["#46dcff","#50ffaa","#966eff","#e0fbff"], Ui.SlowOrbit),
        Make("Deep Ocean", "Atmosphere", "Bleu profond avec reflets froids.", "python", "deep_ocean.py --speed 0.22", ["#1446b4","#28b4e6","#d2f0ff","#0f172a"], Ui.Wave),
        Make("Storm Mode", "Atmosphere", "Orage bleu avec flashs plus espacés.", "python", "storm_mode.py --speed 0.22", ["#1d4ed8","#93c5fd","#f8fafc","#0f172a"], Ui.Storm),
        Make("Sunset Drift", "Atmosphere", "Ambiance coucher de soleil lente et chaude.", "python", "sunset_drift.py --speed 0.28", ["#fb7185","#f97316","#facc15","#451a03"], Ui.SlowOrbit),

        Make("Radar Sweep", "Jeux", "Balayage tactique vert cyan ralenti.", "python", "radar_sweep.py --speed 0.12", ["#32ffa0","#00beff","#001218","#0f766e"], Ui.Radar),
        Make("Afterburner", "Jeux", "Orange reacteur, bon pour course et action.", "python", "afterburner.py --speed 0.18", ["#ff4d00","#ffb000","#fff1b8","#3b1300"], Ui.Comet),
        Make("Frontier Dust", "Jeux", "Ambiance western poussiereuse pour RDR2.", "python", "frontier_dust.py --speed 0.24", ["#d97706","#92400e","#fde68a","#451a03"], Ui.Wave),
        Make("DedSec Glitch", "Jeux", "Hacking cyan rouge, moins brutal.", "python", "dedsec_glitch.py --speed 0.16", ["#06b6d4","#ef4444","#111827","#f8fafc"], Ui.Police)
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

    public static Color Lighten(Color color, double amount)
    {
        var factor = Math.Clamp(amount, 0.0, 1.0);
        return Color.FromArgb(
            color.A,
            Math.Clamp((int)Math.Round(color.R + (255 - color.R) * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.G + (255 - color.G) * factor), 0, 255),
            Math.Clamp((int)Math.Round(color.B + (255 - color.B) * factor), 0, 255));
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
    public static readonly Color Void = Color.FromArgb(4, 8, 13);
    public static readonly Color Deep = Color.FromArgb(6, 10, 16);
    public static readonly Color Card = Color.FromArgb(13, 18, 28);
    public static readonly Color Text = Color.FromArgb(241, 245, 249);
    public static readonly Color Muted = Color.FromArgb(148, 163, 184);
    public static readonly Color Soft = Color.FromArgb(191, 219, 254);
    public static readonly Color Cyan = Color.FromArgb(34, 211, 238);
    public static readonly Color CyanDeep = Color.FromArgb(14, 116, 144);
    public static readonly Color Mint = Color.FromArgb(52, 211, 153);
    public static readonly Color MintDeep = Color.FromArgb(16, 185, 129);
    public static readonly Color Orange = Color.FromArgb(249, 115, 22);
    public static readonly Color Rose = Color.FromArgb(244, 63, 94);
    public static readonly Color Grape = Color.FromArgb(124, 58, 237);
    public static readonly Color Danger = Color.FromArgb(153, 27, 27);
}
