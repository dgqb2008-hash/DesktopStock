using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 桌面股市主窗口
    /// </summary>
    public class MainForm : Form
    {
        // ---- 控件 ----
        private Panel toolPanel;
        private TextBox txtStockCode;
        private Button btnAdd;
        private Button btnPin;
        private TrackBar trackOpacity;
        private Label lblOpacityVal;
        private Label lblStatus;
        private Panel stockListPanel;
        private Label lblEmptyHint;

        // ---- 数据 ----
        private Timer refreshTimer;
        private Timer saveDebounceTimer;
        private AppSettings settings;
        private List<StockItemPanel> stockPanels = new List<StockItemPanel>();
        private bool isRefreshing = false;
        private bool moveResizePending = false;

        // ---- 常量 ----
        private const int TOOLBAR_HEIGHT = 26;
        private const int MIN_FORM_WIDTH = 260;
        private const int MIN_FORM_HEIGHT = 220;

        public MainForm()
        {
            // 窗体基本设置
            this.Text = "桌面股市";
            this.StartPosition = FormStartPosition.Manual;
            this.MinimumSize = new Size(MIN_FORM_WIDTH, MIN_FORM_HEIGHT);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            this.DoubleBuffered = true;

            // 窗口拖拽/缩放时延迟保存（300ms防抖）
            saveDebounceTimer = new Timer { Interval = 300 };
            saveDebounceTimer.Tick += (s, e) =>
            {
                saveDebounceTimer.Stop();
                if (moveResizePending)
                {
                    moveResizePending = false;
                    SaveSettings();
                }
            };

            this.ResizeBegin += (s, e) => { this.SuspendLayout(); };
            this.ResizeEnd += (s, e) =>
            {
                this.ResumeLayout(true);
                moveResizePending = true;
                saveDebounceTimer.Stop();
                saveDebounceTimer.Start();
            };
            this.Move += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Normal)
                {
                    moveResizePending = true;
                    saveDebounceTimer.Stop();
                    saveDebounceTimer.Start();
                }
            };

            InitializeCustomComponents();
            LoadAndApplySettings();
            StartRefreshTimer();

            this.FormClosing += MainForm_FormClosing;
            this.FormClosed += MainForm_FormClosed;
        }

        #region 初始化界面

        private void InitializeCustomComponents()
        {
            // ---- 顶部工具栏 ----
            toolPanel = new Panel
            {
                Height = TOOLBAR_HEIGHT,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(245, 245, 245),
                Padding = new Padding(4, 1, 4, 1)
            };
            toolPanel.Paint += ToolPanel_Paint;
            toolPanel.Resize += (s, e) =>
            {
                lblStatus.Location = new Point(toolPanel.ClientSize.Width - 52, 4);
            };

            // + 按钮（自绘图标：蓝色圆角方块+白色加号）
            btnAdd = new Button
            {
                Text = "",
                Size = new Size(18, 18),
                Location = new Point(69, 4),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var btn = s as Button;
                // 绘制背景色
                Color bg = btn.FlatAppearance.MouseOverBackColor.IsEmpty
                    ? btn.BackColor
                    : (btn.RectangleToScreen(btn.ClientRectangle).Contains(Cursor.Position)
                        ? btn.FlatAppearance.MouseOverBackColor
                        : btn.BackColor);
                // 圆角填充
                using (var brush = new SolidBrush(bg))
                {
                    var rect = new Rectangle(1, 1, btn.Width - 3, btn.Height - 3);
                    var path = RoundedRect(rect, 4);
                    g.FillPath(brush, path);
                    path.Dispose();
                }
                // 绘制白色加号
                using (var pen = new Pen(Color.White, 2))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    int cx = btn.Width / 2;
                    int cy = btn.Height / 2;
                    int half = 5;
                    g.DrawLine(pen, cx - half, cy, cx + half, cy);
                    g.DrawLine(pen, cx, cy - half, cx, cy + half);
                }
            };
            btnAdd.MouseEnter += (s, e) =>
            {
                btnAdd.BackColor = Color.FromArgb(37, 99, 235);
                btnAdd.Invalidate();
            };
            btnAdd.MouseLeave += (s, e) =>
            {
                btnAdd.BackColor = Color.FromArgb(59, 130, 246);
                btnAdd.Invalidate();
            };
            btnAdd.Click += BtnAdd_Click;

            // 股票代码输入框（工具栏最前面）
            txtStockCode = new TextBox
            {
                Font = new Font("Microsoft YaHei", 8),
                Location = new Point(5, 4),
                Size = new Size(60, 18),
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 6
            };
            txtStockCode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    AddStockFromInput();
            };

            // 置顶按钮（自绘图钉图标）
            btnPin = new Button
            {
                Text = "",
                Size = new Size(18, 18),
                Location = new Point(91, 4),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 200, 200),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            btnPin.FlatAppearance.BorderSize = 0;
            btnPin.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var btn = s as Button;
                bool isActive = (btn.Tag != null && btn.Tag.ToString() == "1");
                Color bg = isActive ? Color.FromArgb(59, 130, 246) : Color.FromArgb(200, 200, 200);

                using (var brush = new SolidBrush(bg))
                {
                    var rect = new Rectangle(1, 1, btn.Width - 3, btn.Height - 3);
                    var path = RoundedRect(rect, 4);
                    g.FillPath(brush, path);
                    path.Dispose();
                }
                // 绘制图钉图标
                Color lineColor = isActive ? Color.White : Color.FromArgb(130, 130, 130);
                using (var pen = new Pen(lineColor, 1.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    int cx = btn.Width / 2;
                    // 针体（竖线）
                    g.DrawLine(pen, cx, 11, cx, 14);
                    // 针尖
                    g.DrawLine(pen, cx, 13, cx - 1, 15);
                    g.DrawLine(pen, cx, 13, cx + 1, 15);
                }
                // 顶部圆形
                using (var brush = new SolidBrush(lineColor))
                {
                    g.FillEllipse(brush, btn.Width / 2 - 2, 2, 5, 5);
                }
            };
            btnPin.Click += (s, e) =>
            {
                if (btnPin.Tag == null || btnPin.Tag.ToString() != "1")
                {
                    btnPin.Tag = "1";
                    btnPin.BackColor = Color.FromArgb(59, 130, 246);
                }
                else
                {
                    btnPin.Tag = "0";
                    btnPin.BackColor = Color.FromArgb(200, 200, 200);
                }
                btnPin.Invalidate();
                this.TopMost = (btnPin.Tag.ToString() == "1");
                SaveSettings();
            };

            // 透明度滑块
            trackOpacity = new TrackBar
            {
                Minimum = 30,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Location = new Point(113, 2),
                Size = new Size(60, 22),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            trackOpacity.ValueChanged += TrackOpacity_ValueChanged;
            trackOpacity.MouseUp += (s, ev) => SaveSettings();

            // 透明度数值
            lblOpacityVal = new Label
            {
                Text = "90%",
                Location = new Point(177, 5),
                Size = new Size(20, 16),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Microsoft YaHei", 8)
            };

            // 状态标签（右侧）
            lblStatus = new Label
            {
                Text = "",
                Location = new Point(0, 4),
                Size = new Size(48, 16),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Microsoft YaHei", 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            toolPanel.Controls.AddRange(new Control[] {
                btnAdd, txtStockCode, btnPin, trackOpacity, lblOpacityVal, lblStatus
            });

            // ---- 股票列表面板 ----
            stockListPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(4, 4, 4, 4),
                BackColor = Color.FromArgb(252, 252, 252)
            };
            stockListPanel.SizeChanged += StockListPanel_SizeChanged;

            // 空列表提示
            lblEmptyHint = new Label
            {
                Text = "输入股票代码，点击 + 添加\r\n\r\n支持沪深北股票代码\r\n如：600519  000001  300750",
                Font = new Font("Microsoft YaHei", 8),
                ForeColor = Color.FromArgb(180, 180, 180),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(260, 120),
                Visible = false
            };

            stockListPanel.Controls.Add(lblEmptyHint);

            // ---- 添加到主窗体 ----
            this.Controls.Add(stockListPanel);
            this.Controls.Add(toolPanel);
        }

        private void ToolPanel_Paint(object sender, PaintEventArgs e)
        {
            // 工具栏底部分隔线
            using (var pen = new Pen(Color.FromArgb(220, 220, 220), 1))
            {
                e.Graphics.DrawLine(pen, 0, TOOLBAR_HEIGHT - 1,
                    toolPanel.Width, TOOLBAR_HEIGHT - 1);
            }
        }

        #endregion

        #region 设置加载与保存

        private void LoadAndApplySettings()
        {
            settings = StockDataStore.Load();

            // 恢复窗口状态
            this.Width = settings.WindowWidth;
            this.Height = settings.WindowHeight;

            // 确保窗口在屏幕内
            var screen = Screen.FromPoint(new Point(settings.WindowLeft, settings.WindowTop))
                ?? Screen.PrimaryScreen;
            if (settings.WindowLeft >= screen.WorkingArea.Left &&
                settings.WindowLeft < screen.WorkingArea.Right - 50 &&
                settings.WindowTop >= screen.WorkingArea.Top &&
                settings.WindowTop < screen.WorkingArea.Bottom - 50)
            {
                this.Left = settings.WindowLeft;
                this.Top = settings.WindowTop;
            }
            else
            {
                this.StartPosition = FormStartPosition.CenterScreen;
            }

            // 透明度
            this.Opacity = settings.Opacity;
            trackOpacity.Value = (int)(settings.Opacity * 100);
            lblOpacityVal.Text = trackOpacity.Value + "%";

            // 置顶
            this.TopMost = settings.TopMost;
            btnPin.Tag = settings.TopMost ? "1" : "0";
            btnPin.BackColor = settings.TopMost ? Color.FromArgb(59, 130, 246) : Color.FromArgb(200, 200, 200);
            btnPin.Invalidate();

            // 刷新间隔
            refreshTimer = new Timer
            {
                Interval = settings.RefreshInterval * 1000
            };
            refreshTimer.Tick += RefreshTimer_Tick;

            // 恢复股票列表
            foreach (var code in settings.StockCodes)
            {
                AddStockPanel(code.Trim());
            }

            UpdateEmptyHint();
            PerformLayout();
        }

        private void SaveSettings()
        {
            // 只在窗口正常状态时保存位置和大小（最小化时坐标异常）
            if (this.WindowState == FormWindowState.Normal)
            {
                settings.WindowWidth = this.Width;
                settings.WindowHeight = this.Height;
                settings.WindowLeft = this.Left;
                settings.WindowTop = this.Top;
            }
            settings.Opacity = this.Opacity;
            settings.TopMost = this.TopMost;
            settings.RefreshInterval = (refreshTimer != null) ? refreshTimer.Interval / 1000 : 5;
            settings.StockCodes = new List<string>();
            foreach (var panel in stockPanels)
            {
                if (!string.IsNullOrWhiteSpace(panel.StockCode))
                    settings.StockCodes.Add(panel.StockCode.Trim());
            }
            StockDataStore.Save(settings);
        }

        #endregion

        #region 股票面板管理

        private StockItemPanel CreateStockPanel(string code)
        {
            var panel = new StockItemPanel(code);
            panel.Dock = DockStyle.Top;
            panel.DeleteRequested += StockPanel_DeleteRequested;
            panel.DoubleClickRequested += StockPanel_DoubleClickRequested;
            return panel;
        }

        private void AddStockPanel(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            code = code.Trim();

            // 检查是否已存在
            if (stockPanels.Any(p => p.StockCode == code)) return;

            var panel = CreateStockPanel(code);
            stockPanels.Add(panel);
            stockListPanel.Controls.Add(panel);
            stockListPanel.Controls.SetChildIndex(panel, stockListPanel.Controls.Count - 2); // 在空提示前面

            UpdateEmptyHint();
        }

        private void RemoveStockPanel(StockItemPanel panel)
        {
            stockPanels.Remove(panel);
            stockListPanel.Controls.Remove(panel);
            panel.Dispose();

            UpdateEmptyHint();
            SaveSettings();
        }

        private void StockPanel_DeleteRequested(object sender, EventArgs e)
        {
            var panel = sender as StockItemPanel;
            if (panel == null) return;

            var result = MessageBox.Show(
                string.Format("确定删除 {0} 吗？", panel.StockCode),
                "删除股票",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                RemoveStockPanel(panel);
            }
        }

        private async void StockPanel_DoubleClickRequested(object sender, EventArgs e)
        {
            var panel = sender as StockItemPanel;
            if (panel == null) return;

            string code = panel.StockCode;
            string name = panel.StockName ?? code;

            lblStatus.Text = "加载走势...";
            try
            {
                var trendData = await System.Threading.Tasks.Task.Run(() =>
                    StockService.FetchTrendSync(code, name));

                if (trendData.IsValid)
                {
                    var chartForm = new ChartForm(trendData);
                    chartForm.Show(this);
                }
                else
                {
                    MessageBox.Show(
                        code + " 走势数据获取失败\r\n" + (trendData.ErrorMessage ?? ""),
                        "走势图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("走势图加载失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                var now = DateTime.Now;
                lblStatus.Text = string.Format("{0}:{1:D2}", now.Hour, now.Minute);
            }
        }

        private void UpdateEmptyHint()
        {
            lblEmptyHint.Visible = stockPanels.Count == 0;

            if (lblEmptyHint.Visible)
            {
                lblEmptyHint.Size = new Size(
                    stockListPanel.ClientSize.Width - 20,
                    120);
                int x = (stockListPanel.ClientSize.Width - lblEmptyHint.Width) / 2;
                int y = (stockListPanel.ClientSize.Height - lblEmptyHint.Height) / 3;
                lblEmptyHint.Location = new Point(Math.Max(0, x), Math.Max(0, y));
            }
        }

        #endregion

        #region 事件处理

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            AddStockFromInput();
        }

        private void AddStockFromInput()
        {
            string input = txtStockCode.Text.Trim();
            if (string.IsNullOrWhiteSpace(input)) return;
            txtStockCode.Text = "";
            txtStockCode.Focus();
            ProcessAddCode(input);
        }

        private void ProcessAddCode(string code)
        {

            // 简单校验：A股代码6位数字
            if (code.Length < 5 || code.Length > 6)
            {
                MessageBox.Show("请输入正确的A股代码（6位数字）", "格式错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查是否重复
            if (stockPanels.Any(p => p.StockCode == code))
            {
                MessageBox.Show("该股票已在列表中", "重复添加",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 添加面板
            AddStockPanel(code);
            SaveSettings();

            // 立即刷新这只股票
            RefreshSingleStock(code);
        }


        private void TrackOpacity_ValueChanged(object sender, EventArgs e)
        {
            double opacity = trackOpacity.Value / 100.0;
            this.Opacity = opacity;
            lblOpacityVal.Text = trackOpacity.Value + "%";
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 关闭前立即保存（停止防抖定时器，直接写盘）
            moveResizePending = false;
            saveDebounceTimer?.Stop();
            SaveSettings();
            refreshTimer?.Stop();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            saveDebounceTimer?.Dispose();
        }

        private void StockListPanel_SizeChanged(object sender, EventArgs e)
        {
            UpdateEmptyHint();
        }

        #endregion

        #region 行情刷新

        private void StartRefreshTimer()
        {
            refreshTimer.Start();
            // 启动时立即刷新一次
#pragma warning disable CS4014
            RefreshAllStocksAsync();
#pragma warning restore CS4014
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            await RefreshAllStocksAsync();
        }

        private async void RefreshSingleStock(string code)
        {
            try
            {
                lblStatus.Text = "正在获取 " + code + "...";
                var codes = new List<string> { code };

                var results = await System.Threading.Tasks.Task.Run(() =>
                    StockService.FetchStocksSync(codes)
                );

                lblStatus.Text = "OK";

                foreach (var info in results)
                {
                    var panel = stockPanels.FirstOrDefault(p => p.StockCode == info.Code);
                    if (panel != null)
                    {
                        panel.UpdateStock(info);
                    }
                }
                UpdateStatus(results);
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                lblStatus.Text = "X " + (msg.Length > 40 ? msg.Substring(0, 40) : msg);
            }
        }

        private async System.Threading.Tasks.Task RefreshAllStocksAsync()
        {
            if (isRefreshing) return;
            if (stockPanels.Count == 0) return;

            isRefreshing = true;
            try
            {
                lblStatus.Text = "刷新中...";
                var codes = stockPanels.Select(p => p.StockCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                var results = await System.Threading.Tasks.Task.Run(() =>
                    StockService.FetchStocksSync(codes)
                );

                foreach (var info in results)
                {
                    var panel = stockPanels.FirstOrDefault(p => p.StockCode == info.Code);
                    if (panel != null)
                    {
                        panel.UpdateStock(info);
                    }
                }
                UpdateStatus(results);
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                lblStatus.Text = "! " + (msg.Length > 40 ? msg.Substring(0, 40) : msg);
            }
            finally
            {
                isRefreshing = false;
            }
        }

        private void UpdateStatus(List<StockInfo> results)
        {
            var now = DateTime.Now;
            lblStatus.Text = string.Format("{0}:{1:D2}", now.Hour, now.Minute);
            lblStatus.Location = new Point(toolPanel.ClientSize.Width - 64, 6);
        }

        #endregion

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

    }
}
