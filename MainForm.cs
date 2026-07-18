using System.Drawing;

namespace Edf3Koreanizer.Patcher;

internal sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(30, 52, 94);
    private static readonly Color MutedBlue = Color.FromArgb(93, 112, 151);
    private static readonly Color SuccessGreen = Color.FromArgb(27, 126, 72);
    private static readonly Color ErrorRed = Color.FromArgb(210, 48, 35);

    private readonly PatchEngine _engine = new();
    private readonly Dictionary<PatchStage, Label> _stageValues = new();
    private readonly Panel _dropPanel = new();
    private readonly Label _dropTitle = new();
    private readonly Label _isoRequirement = new();
    private readonly Label _xexRequirement = new();
    private readonly Button _startButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly TextBox _logBox = new();

    private CancellationTokenSource? _patchCancellation;
    private string? _selectedIsoPath;
    private string? _selectedXexToolPath;
    private bool _internalEnvironmentReady;
    private bool _xexToolReady;
    private bool _isPatching;
    private PatchStage? _activeStage;

    public MainForm()
    {
        Text = "지구방위군 3 한글 패치";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 500);
        MinimumSize = new Size(870, 480);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(249, 250, 252);
        AllowDrop = true;

        BuildLayout();
        ResetProgressDisplay();

        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
        Shown += HandleShown;
        FormClosing += HandleFormClosing;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 3,
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 330F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        BuildDropPanel();
        _dropPanel.Margin = new Padding(0, 0, 12, 10);
        root.Controls.Add(_dropPanel, 0, 0);

        var statusPanel = BuildStatusPanel();
        statusPanel.Margin = new Padding(12, 0, 0, 10);
        root.Controls.Add(statusPanel, 1, 0);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Margin = new Padding(0, 0, 0, 4);
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        root.Controls.Add(_progressBar, 0, 1);
        root.SetColumnSpan(_progressBar, 2);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Margin = new Padding(2, 6, 2, 0);
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.White;
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _logBox.Text = "대기 중";
        root.Controls.Add(_logBox, 0, 2);
        root.SetColumnSpan(_logBox, 2);
    }

    private void BuildDropPanel()
    {
        _dropPanel.Dock = DockStyle.Fill;
        _dropPanel.BorderStyle = BorderStyle.FixedSingle;
        _dropPanel.BackColor = Color.White;
        _dropPanel.AllowDrop = true;
        _dropPanel.Cursor = Cursors.Hand;
        _dropPanel.Click += (_, _) => SelectInputFiles();
        _dropPanel.DragEnter += HandleDragEnter;
        _dropPanel.DragDrop += HandleDragDrop;
        _dropPanel.Resize += (_, _) => LayoutDropPanelContents();

        _dropTitle.AutoSize = false;
        _dropTitle.Location = new Point(24, 52);
        _dropTitle.Size = new Size(318, 58);
        _dropTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _dropTitle.Text = "ISO 및 관련 파일을 여기에 드래그 & 드롭\r\n또는 클릭해서 파일 선택";
        _dropTitle.TextAlign = ContentAlignment.MiddleCenter;
        _dropTitle.Font = new Font(Font, FontStyle.Bold);
        _dropTitle.ForeColor = Navy;
        _dropTitle.Cursor = Cursors.Hand;
        _dropTitle.Click += (_, _) => SelectInputFiles();
        _dropPanel.Controls.Add(_dropTitle);

        ConfigureRequirementLabel(
            _isoRequirement,
            30,
            148,
            "❌ (필수) 지구방위군 3 일본판 원본 ISO",
            ErrorRed);
        _isoRequirement.TextAlign = ContentAlignment.MiddleLeft;
        ConfigureRequirementLabel(
            _xexRequirement,
            30,
            195,
            "… (필수) xextool.exe 6.3 검사 중",
            MutedBlue);

        LayoutDropPanelContents();
    }

    private Panel BuildStatusPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor
        };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(0, 12),
            Text = "진행 상태",
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(20, 26, 38)
        };
        panel.Controls.Add(title);

        var stageNames = new Dictionary<PatchStage, string>
        {
            [PatchStage.IsoValidation] = "ISO 확인",
            [PatchStage.WorkFolder] = "작업 폴더 준비",
            [PatchStage.Extraction] = "XISO 추출",
            [PatchStage.XexValidation] = "XEX 확인",
            [PatchStage.DeltaPatch] = "전체 한글 패치 적용",
            [PatchStage.Repackaging] = "ISO 재패킹"
        };

        var y = 63;
        foreach (var stageName in stageNames)
        {
            var stage = stageName.Key;
            var name = stageName.Value;
            var nameLabel = new Label
            {
                AutoSize = true,
                Location = new Point(0, y),
                Text = name,
                ForeColor = MutedBlue
            };
            panel.Controls.Add(nameLabel);

            var valueLabel = new Label
            {
                AutoSize = false,
                Location = new Point(285, y - 2),
                Size = new Size(82, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Text = "대기",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = MutedBlue
            };
            panel.Controls.Add(valueLabel);
            _stageValues.Add(stage, valueLabel);
            y += 38;
        }

        _startButton.Size = new Size(140, 58);
        _startButton.Location = new Point(382, 128);
        _startButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _startButton.Text = "패치 시작";
        _startButton.Font = new Font(Font.FontFamily, 11F, FontStyle.Regular);
        _startButton.ForeColor = Navy;
        _startButton.Enabled = false;
        _startButton.Click += HandleStartClick;
        panel.Controls.Add(_startButton);

        panel.Resize += (_, _) =>
        {
            _startButton.Left = Math.Max(375, panel.ClientSize.Width - _startButton.Width - 2);
            foreach (var value in _stageValues.Values)
            {
                value.Left = Math.Max(278, _startButton.Left - 97);
            }
        };

        return panel;
    }

    private void ConfigureRequirementLabel(
        Label label,
        int x,
        int y,
        string text,
        Color color)
    {
        label.AutoSize = false;
        label.Location = new Point(x, y);
        label.Size = new Size(310, 24);
        label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        label.Text = text;
        label.ForeColor = color;
        _dropPanel.Controls.Add(label);
    }

    private void LayoutDropPanelContents()
    {
        var contentWidth = Math.Max(180, _dropPanel.ClientSize.Width - 32);
        _dropTitle.SetBounds(16, 52, contentWidth, 58);
        _isoRequirement.SetBounds(30, 148, Math.Max(160, _dropPanel.ClientSize.Width - 60), 24);
        _xexRequirement.SetBounds(30, 195, Math.Max(160, _dropPanel.ClientSize.Width - 60), 24);
    }

    private async void HandleShown(object? sender, EventArgs e)
    {
        await CheckEnvironmentAsync();
    }

    private async Task CheckEnvironmentAsync()
    {
        try
        {
            var result = await _engine.ValidateEnvironmentAsync(_selectedXexToolPath);
            _xexRequirement.Text = result.XexToolValid
                ? "✓ (필수) xextool.exe 6.3"
                : "❌ (필수) xextool.exe 6.3";
            _xexRequirement.ForeColor = result.XexToolValid ? SuccessGreen : ErrorRed;
            _xexToolReady = result.XexToolValid;
            _internalEnvironmentReady = result.ExisoValid && result.PackageValid;

            if (_selectedXexToolPath is not null && !result.XexToolValid)
            {
                AppendLog("xextool.exe 6.3 확인 실패");
            }

            if (!result.ExisoValid)
            {
                AppendLog("내부 ISO 도구 검사 실패");
            }

            if (!result.PackageValid)
            {
                AppendLog("내부 패치 데이터 검사 실패");
            }
        }
        catch (Exception exception)
        {
            _xexToolReady = false;
            _internalEnvironmentReady = false;
            AppendLog("환경 검사 실패: " + exception.Message);
        }

        UpdateStartButton();
    }

    private async void SelectInputFiles()
    {
        if (_isPatching)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "원본 ISO 및 xextool.exe 6.3 선택",
            Filter = "패치 입력 파일 (*.iso;*.exe)|*.iso;*.exe|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await SetSelectedFilesAsync(dialog.FileNames);
        }
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        if (!_isPatching &&
            e.Data?.GetDataPresent(DataFormats.FileDrop) == true &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length is >= 1 and <= 2 &&
            files.All(IsSupportedInput))
        {
            e.Effect = DragDropEffects.Copy;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private async void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await SetSelectedFilesAsync(files);
        }
    }

    private async Task SetSelectedFilesAsync(IEnumerable<string> paths)
    {
        var accepted = 0;
        var xexToolChanged = false;

        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                _selectedIsoPath = Path.GetFullPath(path);
                _isoRequirement.Text = "✓ (필수) 원본 ISO 선택됨";
                _isoRequirement.ForeColor = SuccessGreen;
                AppendLog("ISO 선택: " + Path.GetFileName(_selectedIsoPath));
                accepted++;
            }
            else if (IsXexTool(path))
            {
                _selectedXexToolPath = Path.GetFullPath(path);
                _xexRequirement.Text = "… (필수) xextool.exe 6.3 검사 중";
                _xexRequirement.ForeColor = MutedBlue;
                AppendLog("xextool.exe 선택됨");
                accepted++;
                xexToolChanged = true;
            }
        }

        if (accepted == 0)
        {
            MessageBox.Show(
                this,
                "원본 ISO 또는 xextool.exe를 선택해 주세요.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (xexToolChanged)
        {
            await CheckEnvironmentAsync();
        }
        else
        {
            UpdateStartButton();
        }
    }

    private async void HandleStartClick(object? sender, EventArgs e)
    {
        if (_selectedIsoPath is null ||
            _selectedXexToolPath is null ||
            !_internalEnvironmentReady ||
            !_xexToolReady ||
            _isPatching)
        {
            return;
        }

        _isPatching = true;
        _activeStage = null;
        _patchCancellation = new CancellationTokenSource();
        _startButton.Enabled = false;
        _dropPanel.Enabled = false;
        ResetProgressDisplay();
        _logBox.Clear();
        AppendLog("전체 한글 패치를 시작합니다.");

        var progress = new Progress<PatchEvent>(HandlePatchEvent);
        var log = new Progress<string>(AppendLog);

        try
        {
            var result = await _engine.RunAsync(
                _selectedIsoPath,
                _selectedXexToolPath,
                progress,
                log,
                _patchCancellation.Token);
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 100;
            MessageBox.Show(
                this,
                $"한글 패치가 완료되었습니다.\r\n\r\n{result.OutputIsoPath}",
                "패치 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            MarkActiveStageFailed();
            AppendLog("사용자가 작업을 취소했습니다.");
        }
        catch (Exception exception)
        {
            MarkActiveStageFailed();
            AppendLog("오류: " + exception.Message);
            MessageBox.Show(
                this,
                exception.Message,
                "패치 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _patchCancellation.Dispose();
            _patchCancellation = null;
            _isPatching = false;
            _dropPanel.Enabled = true;
            UpdateStartButton();
        }
    }

    private void HandlePatchEvent(PatchEvent patchEvent)
    {
        _activeStage = patchEvent.Stage;
        var label = _stageValues[patchEvent.Stage];
        label.Text = patchEvent.State switch
        {
            PatchStageState.Waiting => "대기",
            PatchStageState.Running => "진행 중",
            PatchStageState.Completed => "완료",
            PatchStageState.Failed => "실패",
            _ => "대기"
        };
        label.ForeColor = patchEvent.State switch
        {
            PatchStageState.Completed => SuccessGreen,
            PatchStageState.Failed => ErrorRed,
            PatchStageState.Running => Navy,
            _ => MutedBlue
        };

        if (patchEvent.Current.HasValue && patchEvent.Total is > 0)
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            var percentage = patchEvent.Current.Value * 100 / patchEvent.Total.Value;
            _progressBar.Value = Math.Max(0, Math.Min(100, percentage));
        }
        else if (patchEvent.State == PatchStageState.Running)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 28;
        }

        if (patchEvent.State == PatchStageState.Running &&
            (!patchEvent.Current.HasValue || patchEvent.Current is 0))
        {
            AppendLog(patchEvent.Message);
        }
    }

    private void ResetProgressDisplay()
    {
        foreach (var label in _stageValues.Values)
        {
            label.Text = "대기";
            label.ForeColor = MutedBlue;
        }

        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = 0;
    }

    private void MarkActiveStageFailed()
    {
        if (_activeStage.HasValue)
        {
            var label = _stageValues[_activeStage.Value];
            label.Text = "실패";
            label.ForeColor = ErrorRed;
        }

        _progressBar.Style = ProgressBarStyle.Continuous;
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_logBox.TextLength > 0)
        {
            _logBox.AppendText(Environment.NewLine);
        }

        _logBox.AppendText(line);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void UpdateStartButton()
    {
        _startButton.Enabled = !_isPatching &&
                               _internalEnvironmentReady &&
                               _xexToolReady &&
                               _selectedIsoPath is not null &&
                               _selectedXexToolPath is not null;
    }

    private static bool IsSupportedInput(string path) =>
        File.Exists(path) &&
        (Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase) ||
         IsXexTool(path));

    private static bool IsXexTool(string path) =>
        Path.GetFileName(path).Equals("xextool.exe", StringComparison.OrdinalIgnoreCase);

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_isPatching)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "현재 패치 작업을 취소하시겠습니까?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes)
        {
            _patchCancellation?.Cancel();
            AppendLog("취소 요청을 전송했습니다.");
        }

        e.Cancel = true;
    }
}
