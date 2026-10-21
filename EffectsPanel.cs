namespace SdvxSmooth;

sealed class EffectsPanel : UserControl
{
    readonly TextBox path = new() { Dock = DockStyle.Fill, Text = Program.DefaultRoot };
    readonly OptionSwitch background = new("隐藏舞台背景", "移除舞台、人物背景和背景粒子绘制");
    readonly OptionSwitch deepBlack = new("深黑背景", "修正背景动画底色，并覆盖背景纹理");
    readonly OptionSwitch hit = new("减少旋钮尘粒", "保留按键命中闪光、长按反馈和按键光柱");
    readonly OptionSwitch lane = new("收敛轨道泛光", "减少反光与 HUD 泛光，保留轨道和判定线");
    readonly OptionSwitch extra = new("隐藏动态装饰", "精简顶部外框、底部机台与周边动态光效");
    readonly OptionSwitch judgment = new("隐藏判定与演出", "隐藏 EARLY / LATE 等文字；USC 预设保留此反馈");
    readonly TextBox report = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250) };
    readonly Label status = new() { Text = "读取后选择需要精简的游戏内画面。", Dock = DockStyle.Fill };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false };
    readonly FlowLayoutPanel presets = new() { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
    readonly LanePreview lanePreview = new();
    readonly Label selection = Theme.Label("当前选择", 11, true);
    readonly Panel settings = new() { Dock = DockStyle.Fill };
    EffectMode? savedMode;
    Effects? effects;
    bool busy;
    public string GameRoot => path.Text;
    public bool IsBusy => busy;
    public void UseRoot(string root)
    {
        if (path.Text == root) return;
        path.Text = root; RefreshState();
    }
    public EffectsPanel()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Background;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 18, 28, 12), ColumnCount = 1, RowCount = 6 };
        foreach (int height in new[] { 42, 40, 52 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(Theme.Label("让视线回到谱面。", 19, true), 0, 0);
        var location = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        path.BackColor = Theme.Surface; path.ForeColor = Theme.Text; path.BorderStyle = BorderStyle.FixedSingle;
        location.Controls.Add(path); location.Controls.Add(MakeButton("浏览…", Browse, 80)); location.Controls.Add(MakeButton("读取模式", (_, _) => RefreshState(), 100)); layout.Controls.Add(location, 0, 1);
        presets.Controls.Add(MakeButton("USC 深黑", (_, _) => SelectMode(EffectMode.Usc), 120));
        presets.Controls.Add(MakeButton("仅留命中", (_, _) => SelectMode(new(true, true, true, true, true, true)), 120));
        presets.Controls.Add(MakeButton("原版", (_, _) => SelectMode(EffectMode.Original), 90));
        layout.Controls.Add(presets, 0, 2);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = new Padding(0, 0, 18, 0) };
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); options.Controls.Add(Theme.Label("画面精简", 10, true, Theme.Muted), 0, 0);
        int index = 1;
        foreach (var option in new[] { background, deepBlack, hit, lane, extra, judgment })
        {
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6)); options.Controls.Add(option, 0, index++);
            option.CheckedChanged += (_, _) => UpdatePreview();
        }
        var previewColumn = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        previewColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); previewColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); previewColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        previewColumn.Controls.Add(selection, 0, 0); previewColumn.Controls.Add(lanePreview, 0, 1);
        var keep = Theme.Label("始终保留命中反馈、音符、旋钮定位、\r\n血条与分数。", 9, false, Theme.Muted); previewColumn.Controls.Add(keep, 0, 2);
        previewColumn.Controls.Add(Theme.Label("资源覆盖实验功能 · 尚未实机进歌验证\r\n镜头和 HUD 布局沿用 SDVX。", 9, false, Theme.Muted), 0, 3);
        columns.Controls.Add(options, 0, 0); columns.Controls.Add(previewColumn, 1, 0);
        settings.Controls.Add(columns); layout.Controls.Add(settings, 0, 3);
        actions.Padding = new Padding(0, 8, 0, 0);
        actions.Controls.Add(Theme.Button("应用当前选择", async (_, _) => await ApplyMode(), 145, true));
        actions.Controls.Add(MakeButton("恢复原版", async (_, _) => { SelectMode(EffectMode.Original); await ApplyMode(); }, 115));
        actions.Controls.Add(MakeButton("详细信息", (_, _) => ShowDetails(), 115));
        actions.Controls.Add(MakeButton("停用旧版覆盖", async (_, _) => await RestoreLegacy(), 135));
        status.ForeColor = Theme.Muted; status.Font = new Font("Microsoft YaHei UI", 9);
        layout.Controls.Add(actions, 0, 4); layout.Controls.Add(status, 0, 5); Controls.Add(layout);
        Load += (_, _) => RefreshState();
    }
    static Button MakeButton(string text, EventHandler action, int width)
        => Theme.Button(text, action, width);
    EffectMode Selection() => new(background.Checked, hit.Checked, lane.Checked, extra.Checked, judgment.Checked, deepBlack.Checked);
    void SelectMode(EffectMode mode)
    {
        background.Checked = mode.Background; deepBlack.Checked = mode.DeepBlack; hit.Checked = mode.Hit;
        lane.Checked = mode.Lane; extra.Checked = mode.Extra; judgment.Checked = mode.Judgment;
        UpdatePreview();
    }
    void UpdatePreview()
    {
        var mode = Selection(); lanePreview.Mode = mode; lanePreview.Invalidate();
        string name = mode == EffectMode.Usc ? "USC 深黑" : mode == EffectMode.Original ? "原版" : "自定义";
        selection.Text = name + (mode == savedMode ? " · 已保存" : " · 待应用");
        status.Text = mode == savedMode ? "当前选择与已读取的游戏模式一致。" : "当前为预览选择，点击“应用当前选择”后保存。";
    }
    void SetBusy(bool value) { busy = value; presets.Enabled = settings.Enabled = actions.Enabled = path.Parent!.Enabled = !value; }
    void ShowDetails()
    {
        using var dialog = new Form { Text = "模式详情", ClientSize = new Size(700, 460), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var content = new TextBox { Text = report.Text, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        dialog.Controls.Add(content); Theme.Apply(dialog); dialog.ShowDialog(this);
    }
    void Browse(object? sender, EventArgs args)
    {
        if (busy) return;
        using var dlg = new FolderBrowserDialog { Description = "选择 LAZY_KFC 根目录", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) { path.Text = dlg.SelectedPath; RefreshState(); }
    }
    void RefreshState()
    {
        if (busy) return;
        try
        {
            var loaded = new Game(path.Text); effects = new Effects(loaded); var mode = effects.Read(); Program.RememberRoot(loaded.Root);
            savedMode = mode; SelectMode(mode);
            report.Text = effects.Description() + "\r\n\r\n当前模式：\r\n" +
                string.Join("\r\n", Effects.Groups.Select(g => Effects.Title(g) + "：" + (mode.Enabled(g) ? "已启用精简" : "原版")));
            status.Text = effects.NeedsResourcePathUpgrade() ? "检测到旧版资源路径：点击“应用当前选择”补齐真实哈希路径。" : effects.NeedsDeepBlackUpgrade() ? "检测到旧版深黑覆盖：点击“应用当前选择”补齐背景动画底色修复。" : effects.NeedsFeedbackUpgrade() ? "检测到旧版命中覆盖；点击应用当前选择即可升级。" : "模式已读取，尚未更改游戏。";
        }
        catch (Exception e) { effects = null; savedMode = null; UpdatePreview(); report.Text = e.Message; status.Text = "无法读取，请检查游戏路径；可在“详细信息”中查看原因。"; }
    }
    async Task ApplyMode()
    {
        if (busy) return;
        try
        {
            var selected = Selection();
            var engine = new Effects(new Game(path.Text)); var before = engine.Read();
            bool upgrade = engine.NeedsFeedbackUpgrade();
            bool blackUpgrade = selected.DeepBlack && engine.NeedsDeepBlackUpgrade();
            bool pathUpgrade = engine.NeedsResourcePathUpgrade();
            if (before == selected && !upgrade && !blackUpgrade && !pathUpgrade) { status.Text = "模式没有变化。"; return; }
            Game.EnsureStopped();
            string preview = string.Join("\r\n", Effects.Groups.Where(g => before.Enabled(g) != selected.Enabled(g)).Select(g => Effects.Title(g) + "：" + (selected.Enabled(g) ? "启用精简" : "恢复原版")));
            if (upgrade) preview += "\r\n撤回旧版按键命中、长按和按键光柱覆盖，保留完整命中反馈。";
            if (blackUpgrade) preview += "\r\n升级旧版深黑覆盖：补齐 AFP 背景动画底色修复，保留其他精简选择。";
            if (pathUpgrade) preview += "\r\n补齐 IFS 内真实哈希文件名及缺少的机台装饰覆盖。";
            preview += "\r\n\r\n通过独立资源模组覆盖，下次启动游戏生效。\r\n首次生成需要一些时间，期间请勿启动游戏。\r\n资源已做静态校验，尚未实机进歌验证。";
            if (MessageBox.Show(this, preview, "特效更改预览", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            SetBusy(true);
            var progress = new Progress<string>(message => status.Text = message);
            string result = await Task.Run(() => engine.Apply(selected, progress));
            savedMode = selected; UpdatePreview();
            report.Text = result + "\r\n\r\n" + engine.Description(); status.Text = "已保存，下次启动生效；可用恢复原版画面撤销。";
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "特效模式未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); status.Text = "操作未完成；原始游戏资源未修改。"; }
        finally { SetBusy(false); }
    }
    async Task RestoreLegacy()
    {
        if (busy) return;
        try
        {
            SetBusy(true);
            var legacy = new Effects(new Game(path.Text), EffectCatalog.LoadUnsafe());
            string result = await Task.Run(() => legacy.Apply(new(false, false, false, false, false)));
            SetBusy(false); RefreshState(); MessageBox.Show(this, result, "旧版覆盖已停用");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "停用未完成"); }
        finally { SetBusy(false); }
    }
}
