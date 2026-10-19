namespace SdvxSmooth;

sealed class EffectsPanel : UserControl
{
    readonly TextBox path = new() { Dock = DockStyle.Fill, Text = Program.DefaultRoot };
    readonly CheckBox background = new() { Text = "隐藏曲目背景绘制（舞台、人物背景、背景粒子）", AutoSize = true };
    readonly CheckBox hit = new() { Text = "减少旋钮尘粒（保留按键命中闪光与长按反馈）", AutoSize = true };
    readonly CheckBox lane = new() { Text = "减少轨道反光和 HUD 泛光（保留按键光柱、轨道线和判定线）", AutoSize = true };
    readonly CheckBox extra = new() { Text = "隐藏旋钮周边光效、连击光束与 HUD 装饰（保留旋钮定位、血条和分数）", AutoSize = true };
    readonly CheckBox judgment = new() { Text = "隐藏判定文字与演出（CRITICAL/ERROR、EARLY/LATE、通关/失败大字）", AutoSize = true };
    readonly TextBox report = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250) };
    readonly Label status = new() { Text = "读取后选择需要精简的游戏内画面。", Dock = DockStyle.Fill };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = false };
    Effects? effects;
    bool busy;
    public EffectsPanel()
    {
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 11 };
        foreach (int height in new[] { 58, 48, 36, 36, 36, 36, 36, 55 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Text = "游戏内特效精简", Font = new Font("Microsoft YaHei UI", 22, FontStyle.Bold), AutoSize = true }, 0, 0);
        var location = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        location.Controls.Add(path); location.Controls.Add(MakeButton("浏览…", Browse, 80)); location.Controls.Add(MakeButton("读取模式", (_, _) => RefreshState(), 100)); layout.Controls.Add(location, 0, 1);
        layout.Controls.Add(background, 0, 2); layout.Controls.Add(hit, 0, 3); layout.Controls.Add(lane, 0, 4);
        layout.Controls.Add(extra, 0, 5); layout.Controls.Add(judgment, 0, 6);
        layout.Controls.Add(new Label { AutoSize = true, ForeColor = Color.DarkSlateGray, Text = "已修正旧版绘制计数清零问题；保留命中反馈与读谱信息。\r\n新方法尚未进歌验证，当前不会自动启用；建议先用原版确认游戏稳定。" }, 0, 7);
        layout.Controls.Add(report, 0, 8);
        actions.Controls.Add(MakeButton("仅留命中", (_, _) => { background.Checked = hit.Checked = lane.Checked = extra.Checked = judgment.Checked = true; status.Text = "已选中全部精简项（包含隐藏判定显示），点击应用后生效。"; }, 145));
        actions.Controls.Add(MakeButton("应用特效模式", async (_, _) => await ApplyMode(), 145));
        actions.Controls.Add(MakeButton("停用旧版覆盖", async (_, _) => await RestoreLegacy(), 145));
        actions.Controls.Add(MakeButton("恢复原版画面", async (_, _) => { background.Checked = hit.Checked = lane.Checked = extra.Checked = judgment.Checked = false; await ApplyMode(); }, 145));
        layout.Controls.Add(actions, 0, 9); layout.Controls.Add(status, 0, 10); Controls.Add(layout);
        Load += (_, _) => RefreshState();
    }
    static Button MakeButton(string text, EventHandler action, int width)
    { var b = new Button { Text = text, Width = width, Height = 34 }; b.Click += action; return b; }
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
            background.Checked = mode.Background; hit.Checked = mode.Hit; lane.Checked = mode.Lane; extra.Checked = mode.Extra; judgment.Checked = mode.Judgment;
            report.Text = effects.Description() + "\r\n\r\n当前模式：\r\n" +
                string.Join("\r\n", Effects.Groups.Select(g => Effects.Title(g) + "：" + (mode.Enabled(g) ? "已启用精简" : "原版")));
            status.Text = effects.NeedsFeedbackUpgrade() ? "检测到旧版命中覆盖；点击应用特效模式即可恢复命中反馈并保留背景精简。" : "模式已读取，尚未更改游戏。";
        }
        catch (Exception e) { effects = null; report.Text = e.Message; status.Text = "无法读取，请检查游戏路径。"; }
    }
    async Task ApplyMode()
    {
        if (busy) return;
        try
        {
            var selected = new EffectMode(background.Checked, hit.Checked, lane.Checked, extra.Checked, judgment.Checked);
            var engine = new Effects(new Game(path.Text)); var before = engine.Read();
            bool upgrade = engine.NeedsFeedbackUpgrade();
            if (before == selected && !upgrade) { status.Text = "模式没有变化。"; return; }
            Game.EnsureStopped();
            string preview = string.Join("\r\n", Effects.Groups.Where(g => before.Enabled(g) != selected.Enabled(g)).Select(g => Effects.Title(g) + "：" + (selected.Enabled(g) ? "启用精简" : "恢复原版")));
            if (upgrade) preview += "\r\n撤回旧版按键命中、长按和按键光柱覆盖，保留完整命中反馈。";
            preview += "\r\n\r\n通过独立资源模组覆盖，下次启动游戏生效。\r\n首次生成需要一些时间，期间请勿启动游戏。\r\n资源已做静态校验，尚未实机进歌验证。";
            if (MessageBox.Show(this, preview, "特效更改预览", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            busy = true; actions.Enabled = path.Enabled = background.Enabled = hit.Enabled = lane.Enabled = extra.Enabled = judgment.Enabled = false;
            var progress = new Progress<string>(message => status.Text = message);
            string result = await Task.Run(() => engine.Apply(selected, progress));
            report.Text = result + "\r\n\r\n" + engine.Description(); status.Text = "已保存，下次启动生效；可用恢复原版画面撤销。";
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "特效模式未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); status.Text = "操作未完成；原始游戏资源未修改。"; }
        finally { busy = false; actions.Enabled = path.Enabled = background.Enabled = hit.Enabled = lane.Enabled = extra.Enabled = judgment.Enabled = true; }
    }
    async Task RestoreLegacy()
    {
        if (busy) return;
        try
        {
            busy = true; actions.Enabled = false;
            var legacy = new Effects(new Game(path.Text), EffectCatalog.LoadUnsafe());
            string result = await Task.Run(() => legacy.Apply(new(false, false, false, false, false)));
            busy = false; RefreshState(); MessageBox.Show(this, result, "旧版覆盖已停用");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "停用未完成"); }
        finally { busy = false; actions.Enabled = true; }
    }
}
