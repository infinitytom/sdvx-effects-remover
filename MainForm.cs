using System.Diagnostics;

namespace SdvxSmooth;

sealed class MainForm : Form
{
    readonly TextBox path = new() { Text = Program.DefaultRoot, Dock = DockStyle.Fill };
    readonly CheckBox redraw = new() { Text = "副屏强制刷新（副屏在全屏下不更新时才需要）", AutoSize = true };
    readonly CheckBox logs = new() { Text = "减少游戏日志（只保留 AVS 警告和错误，Spice 日志仍保留）", AutoSize = true };
    readonly CheckBox overlay = new() { Text = "保留 Spice 叠加界面（含 F9/FPS 显示和副屏触控入口）", AutoSize = true };
    readonly RadioButton fpsKeep = new() { Text = "保持当前", AutoSize = true, Checked = true };
    readonly RadioButton fps60 = new() { Text = "60 FPS（较低负载）", AutoSize = true };
    readonly RadioButton fps120 = new() { Text = "120 FPS", AutoSize = true };
    readonly TextBox report = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250) };
    readonly Label status = new() { Text = "选择 LAZY_KFC 根目录后读取设置。", AutoSize = true, ForeColor = Color.DarkSlateGray };
    Game? game;
    public MainForm()
    {
        Text = "SDVX Effects Remover · USC 深黑"; Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(1060, 840); MinimumSize = new Size(1000, 820); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.Controls.Add(new Label { Text = "SDVX 流畅设置", Font = new Font(Font.FontFamily, 22, FontStyle.Bold), AutoSize = true }, 0, 0);
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        row.Controls.Add(path); row.Controls.Add(Button("浏览…", Browse)); row.Controls.Add(Button("读取设置", (_, _) => LoadGame())); layout.Controls.Add(row, 0, 1);
        layout.Controls.Add(redraw, 0, 2); layout.Controls.Add(logs, 0, 3); layout.Controls.Add(overlay, 0, 4);
        var fpsRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        fpsRow.Controls.Add(new Label { Text = "游戏与音符帧率：", AutoSize = true, Padding = new Padding(0, 3, 0, 0) });
        fpsRow.Controls.Add(fpsKeep); fpsRow.Controls.Add(fps60); fpsRow.Controls.Add(fps120);
        layout.Controls.Add(fpsRow, 0, 5);
        layout.Controls.Add(new Label { AutoSize = true, Text = "逐项预览后应用 · 自动备份 · 退出游戏及 LAZY 启动器后操作", ForeColor = Color.DarkSlateGray }, 0, 6);
        layout.Controls.Add(report, 0, 7);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(Button("轻量预设", (_, _) => { redraw.Checked = false; logs.Checked = true; overlay.Checked = true; fpsKeep.Checked = true; status.Text = "已选择轻量预设；点击预览并应用后才保存。"; }, 125));
        actions.Controls.Add(Button("预览并应用", Apply, 135)); actions.Controls.Add(Button("恢复备份…", Restore, 135));
        actions.Controls.Add(Button("打开备份文件夹", (_, _) => Run(() => { var g = GetGame(); Directory.CreateDirectory(g.Backups); Process.Start(new ProcessStartInfo("explorer.exe", g.Backups) { UseShellExecute = true }); }), 160));
        layout.Controls.Add(actions, 0, 8); layout.Controls.Add(status, 0, 9);
        Theme.Apply(layout);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(30, 18, 20, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        header.Controls.Add(Theme.Label("SDVX  /  EFFECTS REMOVER", 17, true), 0, 0);
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var effects = new EffectsPanel(); body.Controls.Add(effects); body.Controls.Add(layout); layout.Visible = false;
        var effectTab = Theme.Button("画面精简", (_, _) => {
            if (effects.IsBusy) return;
            if (layout.Visible) effects.UseRoot(path.Text);
            layout.Visible = false; effects.Visible = true;
        }, 125);
        var startupTab = Theme.Button("启动设置", (_, _) => {
            if (effects.IsBusy) return;
            effects.Visible = false; layout.Visible = true; path.Text = effects.GameRoot; LoadGame();
        }, 125);
        navigation.Controls.Add(effectTab); navigation.Controls.Add(startupTab); header.Controls.Add(navigation, 1, 0);
        shell.Controls.Add(header, 0, 0); shell.Controls.Add(body, 0, 1); Controls.Add(shell);
    }
    static Button Button(string text, EventHandler action, int width = 85)
    {
        return Theme.Button(text, action, width);
    }
    void Run(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); status.Text = "操作未完成，请查看提示。"; } }
    Game GetGame()
    {
        if (game == null || !string.Equals(Path.GetFullPath(path.Text.Trim()), game.Root, StringComparison.OrdinalIgnoreCase)) throw new IOException("路径已变化，请先点击读取设置。");
        return game;
    }
    void Browse(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择含 contents、launcher 的 LAZY_KFC 根目录", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) { path.Text = dlg.SelectedPath; LoadGame(); }
    }
    void LoadGame() => Run(() => {
        game = null; var loaded = new Game(path.Text); var c = loaded.Read();
        redraw.Checked = c.Redraw; logs.Checked = c.ReducedLogs; overlay.Checked = c.Overlay; fpsKeep.Checked = true;
        report.Text = loaded.Describe(); game = loaded; Program.RememberRoot(loaded.Root); status.Text = "设置已读取，尚未修改游戏。";
    });
    public void RenderPreview()
    {
        Opacity = 0; Show(); Application.DoEvents(); PerformLayout();
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "preview.png"));
        Close();
    }
    Choices Selection() => new(redraw.Checked, logs.Checked, overlay.Checked, fps60.Checked ? 60 : fps120.Checked ? 120 : 0);
    void Apply(object? sender, EventArgs e) => Run(() => {
        var g = GetGame(); Game.EnsureStopped(); var c = Selection(); var before = g.Read(); var plan = g.Plan(c);
        if (plan.Count == 0) { status.Text = "当前设置与所选设置一致。"; return; }
        var preview = new List<string>();
        if (before.Redraw != c.Redraw) preview.Add("副屏强制刷新：" + (c.Redraw ? "开启" : "关闭；若副屏画面不更新请恢复"));
        if (before.ReducedLogs != c.ReducedLogs) preview.Add("游戏日志：" + (c.ReducedLogs ? "只保留警告和错误" : "使用默认详细级别"));
        if (before.Overlay != c.Overlay) preview.Add("Spice 叠加界面：" + (c.Overlay ? "开启" : "关闭；F9、FPS 显示及副屏叠加触控可能不可用"));
        if (c.Fps != 0) preview.Add("游戏和音符帧率一起设为 " + c.Fps + " FPS；会改变画面流畅度，游玩后检查音画同步。");
        preview.Add("\r\n自动备份后修改 " + plan.Count + " 个配置文件，下次启动生效。");
        if (MessageBox.Show(this, string.Join("\r\n", preview), "应用更改预览", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        string message = g.Apply(plan); LoadGame(); status.Text = "已保存，下次启动生效。"; MessageBox.Show(this, message, "完成");
    });
    void Restore(object? sender, EventArgs e) => Run(() => {
        var g = GetGame();
        if (!Directory.Exists(g.Backups)) throw new IOException("还没有备份。");
        using var dlg = new OpenFileDialog { InitialDirectory = g.Backups, Filter = "备份记录 (journal.json)|journal.json", Title = "进入备份时间文件夹，选择 journal.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string message = g.Restore(Path.GetDirectoryName(dlg.FileName)!); LoadGame(); MessageBox.Show(this, message, "恢复完成");
    });
}
