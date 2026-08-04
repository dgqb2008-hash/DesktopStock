using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 图形化配置窗体：让不熟悉 JSON 的用户也能编辑 settings.json 的各项配置。
    /// </summary>
    public class SettingsForm : Form
    {
        private AppSettings _settings;

        // 控件
        private NumericUpDown nudRefreshInterval;
        private TrackBar trackOpacity;
        private Label lblOpacityValue;
        private CheckBox chkTopMost;
        private CheckBox chkShowFloatingBall;
        private NumericUpDown nudWindowWidth;
        private NumericUpDown nudWindowHeight;

        /// <summary>
        /// 用户确认后的配置（点击确定时填充）
        /// </summary>
        public AppSettings ResultSettings { get; private set; }

        /// <summary>
        /// 导出配置时触发：参数为导出目标文件路径。
        /// 由 MainForm 订阅，负责保存当前设置并复制文件。
        /// </summary>
        public event Action<string> ExportRequest;

        /// <summary>
        /// 导入配置时触发：参数为用户选择的源文件路径。
        /// 由 MainForm 订阅，负责复制到 settings 路径并重启。
        /// </summary>
        public event Action<string> ImportRequest;

        public SettingsForm(AppSettings current)
        {
            _settings = current.Clone();
            SetupForm();
            LoadValues();
        }

        /// <summary>
        /// 获取 settings.json 的完整路径
        /// </summary>
        private static string GetSettingsFilePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopStock");
            return Path.Combine(dir, "settings.json");
        }

        private void SetupForm()
        {
            this.Text = "设置";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(380, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(248, 249, 250);
            this.Font = new Font("Microsoft YaHei", 9);

            // ---- 常规设置分组 ----
            var grpGeneral = new GroupBox
            {
                Text = "常规设置",
                Location = new Point(15, 12),
                Size = new Size(345, 130),
                BackColor = Color.White
            };

            var lblRefresh = new Label
            {
                Text = "刷新间隔(秒):",
                Location = new Point(20, 28),
                Size = new Size(100, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            grpGeneral.Controls.Add(lblRefresh);

            nudRefreshInterval = new NumericUpDown
            {
                Location = new Point(125, 26),
                Size = new Size(60, 22),
                Minimum = 2,
                Maximum = 60,
                Value = 5
            };
            grpGeneral.Controls.Add(nudRefreshInterval);

            var lblOpacity = new Label
            {
                Text = "窗口透明度:",
                Location = new Point(20, 60),
                Size = new Size(100, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            grpGeneral.Controls.Add(lblOpacity);

            trackOpacity = new TrackBar
            {
                Location = new Point(125, 55),
                Size = new Size(150, 28),
                Minimum = 30,
                Maximum = 100,
                TickStyle = TickStyle.None
            };
            trackOpacity.ValueChanged += (s, e) =>
            {
                lblOpacityValue.Text = trackOpacity.Value + "%";
            };
            grpGeneral.Controls.Add(trackOpacity);

            lblOpacityValue = new Label
            {
                Text = "90%",
                Location = new Point(280, 60),
                Size = new Size(40, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            grpGeneral.Controls.Add(lblOpacityValue);

            chkTopMost = new CheckBox
            {
                Text = "窗口置顶",
                Location = new Point(20, 92),
                Size = new Size(300, 22)
            };
            grpGeneral.Controls.Add(chkTopMost);

            this.Controls.Add(grpGeneral);

            // ---- 悬浮球分组 ----
            var grpBall = new GroupBox
            {
                Text = "桌面悬浮球",
                Location = new Point(15, 150),
                Size = new Size(345, 65),
                BackColor = Color.White
            };

            chkShowFloatingBall = new CheckBox
            {
                Text = "启用桌面悬浮球（关闭主窗口后显示总盈/今盈）",
                Location = new Point(20, 28),
                Size = new Size(310, 22)
            };
            grpBall.Controls.Add(chkShowFloatingBall);

            this.Controls.Add(grpBall);

            // ---- 窗口大小分组 ----
            var grpWindow = new GroupBox
            {
                Text = "窗口大小",
                Location = new Point(15, 223),
                Size = new Size(345, 70),
                BackColor = Color.White
            };

            var lblWidth = new Label
            {
                Text = "宽度:",
                Location = new Point(20, 28),
                Size = new Size(50, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            grpWindow.Controls.Add(lblWidth);

            nudWindowWidth = new NumericUpDown
            {
                Location = new Point(75, 26),
                Size = new Size(70, 22),
                Minimum = 240,
                Maximum = 4000
            };
            grpWindow.Controls.Add(nudWindowWidth);

            var lblHeight = new Label
            {
                Text = "高度:",
                Location = new Point(170, 28),
                Size = new Size(50, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            grpWindow.Controls.Add(lblHeight);

            nudWindowHeight = new NumericUpDown
            {
                Location = new Point(225, 26),
                Size = new Size(70, 22),
                Minimum = 200,
                Maximum = 4000
            };
            grpWindow.Controls.Add(nudWindowHeight);

            this.Controls.Add(grpWindow);

            // ---- 底部按钮 ----
            var btnExport = new Button
            {
                Text = "导出配置",
                Location = new Point(15, 310),
                Size = new Size(95, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            btnExport.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnExport.Click += BtnExport_Click;
            this.Controls.Add(btnExport);

            var btnImport = new Button
            {
                Text = "导入配置",
                Location = new Point(115, 310),
                Size = new Size(95, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            btnImport.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnImport.Click += BtnImport_Click;
            this.Controls.Add(btnImport);

            var btnOK = new Button
            {
                Text = "确定",
                Location = new Point(220, 310),
                Size = new Size(65, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnOK.FlatAppearance.BorderSize = 0;
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(295, 310),
                Size = new Size(65, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
        }

        private void LoadValues()
        {
            nudRefreshInterval.Value = Clamp(_settings.RefreshInterval, 2, 60);
            trackOpacity.Value = (int)(_settings.Opacity * 100);
            lblOpacityValue.Text = trackOpacity.Value + "%";
            chkTopMost.Checked = _settings.TopMost;
            chkShowFloatingBall.Checked = _settings.ShowFloatingBall;
            nudWindowWidth.Value = Clamp(_settings.WindowWidth, 240, 4000);
            nudWindowHeight.Value = Clamp(_settings.WindowHeight, 200, 4000);
        }

        private static decimal Clamp(decimal v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            _settings.RefreshInterval = (int)nudRefreshInterval.Value;
            _settings.Opacity = trackOpacity.Value / 100.0;
            _settings.TopMost = chkTopMost.Checked;
            _settings.ShowFloatingBall = chkShowFloatingBall.Checked;
            _settings.WindowWidth = (int)nudWindowWidth.Value;
            _settings.WindowHeight = (int)nudWindowHeight.Value;

            ResultSettings = _settings;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        /// <summary>
        /// 导出配置：保存当前设置后，将 settings.json 复制到用户指定位置
        /// </summary>
        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出配置";
                dlg.Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                dlg.FileName = "DesktopStock_配置_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // 触发事件让 MainForm 保存当前设置并复制文件
                    if (ExportRequest != null)
                    {
                        ExportRequest(dlg.FileName);
                        MessageBox.Show("配置已导出到:\n" + dlg.FileName, "导出成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        // 事件未订阅时的降级处理：直接尝试复制
                        string src = GetSettingsFilePath();
                        if (!File.Exists(src))
                        {
                            MessageBox.Show("配置文件不存在，无法导出。", "导出失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        File.Copy(src, dlg.FileName, true);
                        MessageBox.Show("配置已导出到:\n" + dlg.FileName, "导出成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 导入配置：让用户选择一个 settings.json 文件，确认后触发导入事件
        /// </summary>
        private void BtnImport_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "导入配置";
                dlg.Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                // 确认对话框
                var result = MessageBox.Show(
                    "导入配置将覆盖当前所有设置（包括股票列表、窗口位置、悬浮球等），\n" +
                    "是否继续？",
                    "确认导入", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result != DialogResult.Yes) return;

                try
                {
                    // 验证文件
                    if (!File.Exists(dlg.FileName))
                    {
                        MessageBox.Show("所选文件不存在。", "导入失败",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // 触发事件让 MainForm 处理导入（复制文件并重启）
                    if (ImportRequest != null)
                    {
                        ImportRequest(dlg.FileName);
                        MessageBox.Show("配置已导入，程序将重启以应用新设置。", "导入成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        this.Close(); // 关闭设置窗体，等待重启
                    }
                    else
                    {
                        // 事件未订阅时的降级处理：直接复制并提示
                        string dst = GetSettingsFilePath();
                        string dir = Path.GetDirectoryName(dst);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.Copy(dlg.FileName, dst, true);
                        MessageBox.Show("配置已导入，请重启程序以应用新设置。", "导入成功",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导入失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
