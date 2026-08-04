using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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
        private TextBox txtCostPrice;
        private TextBox txtQuantity;
        private Button btnAdd;
        private Button btnPin;
        private TrackBar trackOpacity;
        private Label lblOpacityVal;
        private Label lblStatus;
        private Label lblTotalCost;
        private Label lblCurrentTotal;
        private Label lblTotalProfitAmt;
        private Label lblTotalDailyProfitAmt;
        private DataGridView stockGridView;
        private ContextMenuStrip rowContextMenu;
        private ToolStripMenuItem miEditCostQuantity;
        private ToolStripMenuItem miDeleteStock;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem miShow;
        private ToolStripMenuItem miExit;
        private ToolStripMenuItem miToggleFloatingBall;
        private FloatingBall floatingBall;

        // ---- 数据 ----
        private Timer refreshTimer;
        private Timer saveDebounceTimer;
        private AppSettings settings;
        private Dictionary<string, StockConfig> stockConfigs = new Dictionary<string, StockConfig>();
        private Dictionary<string, StockInfo> stockData = new Dictionary<string, StockInfo>();
        // 排序状态
        private int sortColumn = 0;
        private SortOrder sortOrder = SortOrder.None;
        private bool isRefreshing = false;
        private bool moveResizePending = false;
        // 标记是否真正退出程序（由托盘“退出”菜单触发），否则关闭按钮只是隐藏到托盘
        private bool exitApp = false;
        // 导入配置时跳过 FormClosing 中的 SaveSettings，避免用旧配置覆盖刚导入的文件
        private bool skipSaveOnClosing = false;
        // 隐藏到托盘前的窗口位置和大小（仅 Normal 状态记录），用于恢复时还原
        private Point hiddenLocation;
        private Size hiddenSize;
        private bool hasHiddenState = false;

        // ---- 常量 ----
        private const int TOOLBAR_HEIGHT = 26;
        // 取消过大的最小宽度限制，允许窗口自由缩窄；
        // 工具栏统计标签会按优先级自动隐藏，避免控件重叠。
        private const int MIN_FORM_WIDTH = 240;
        private const int MIN_FORM_HEIGHT = 200;

        public MainForm()
        {
            // 窗体基本设置
            this.Text = "发财致富 - 历尽沧桑还得装";
            this.StartPosition = FormStartPosition.Manual;
            this.MinimumSize = new Size(MIN_FORM_WIDTH, MIN_FORM_HEIGHT);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            // 隐藏最小化和最大化按钮
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            this.DoubleBuffered = true;
            // 不在任务栏显示图标，驻留系统托盘
            this.ShowInTaskbar = false;

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
                if (this.WindowState == FormWindowState.Normal)
                {
                    hiddenLocation = this.Location;
                    hiddenSize = this.Size;
                    hasHiddenState = true;
                }
                moveResizePending = true;
                saveDebounceTimer.Stop();
                saveDebounceTimer.Start();
            };
            this.Move += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Normal)
                {
                    hiddenLocation = this.Location;
                    hiddenSize = this.Size;
                    hasHiddenState = true;
                    moveResizePending = true;
                    saveDebounceTimer.Stop();
                    saveDebounceTimer.Start();
                }
            };

            InitializeCustomComponents();
            InitializeTrayIcon();
            LoadAndApplySettings();
            StartRefreshTimer();

            this.FormClosing += MainForm_FormClosing;
            this.FormClosed += MainForm_FormClosed;
            this.Resize += MainForm_Resize;
            this.Move += MainForm_Move;
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
                lblStatus.Location = new Point(toolPanel.ClientSize.Width - 48, 5);
                AdjustToolbarVisibility();
            };

            // + 按钮（自绘图标：蓝色圆角方块+白色加号）
            btnAdd = new Button
            {
                Text = "",
                Size = new Size(18, 18),
                Location = new Point(182, 4),
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
                Color bg = btn.FlatAppearance.MouseOverBackColor.IsEmpty
                    ? btn.BackColor
                    : (btn.RectangleToScreen(btn.ClientRectangle).Contains(Cursor.Position)
                        ? btn.FlatAppearance.MouseOverBackColor
                        : btn.BackColor);
                using (var brush = new SolidBrush(bg))
                {
                    var rect = new Rectangle(1, 1, btn.Width - 3, btn.Height - 3);
                    var path = RoundedRect(rect, 4);
                    g.FillPath(brush, path);
                    path.Dispose();
                }
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

            // 股票代码输入框
            txtStockCode = new TextBox
            {
                Font = new Font("Microsoft YaHei", 8),
                Location = new Point(5, 4),
                Size = new Size(60, 18),
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 6,
                Text = "股票代码",
                ForeColor = Color.FromArgb(180, 180, 180)
            };
            txtStockCode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    AddStockFromInput();
            };
            txtStockCode.GotFocus += (s, e) =>
            {
                if (txtStockCode.Text == "股票代码")
                {
                    txtStockCode.Text = "";
                    txtStockCode.ForeColor = Color.FromArgb(50, 50, 50);
                }
            };
            txtStockCode.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtStockCode.Text))
                {
                    txtStockCode.Text = "股票代码";
                    txtStockCode.ForeColor = Color.FromArgb(180, 180, 180);
                }
            };

            // 成本价输入框
            txtCostPrice = new TextBox
            {
                Font = new Font("Microsoft YaHei", 8),
                Location = new Point(69, 4),
                Size = new Size(55, 18),
                BorderStyle = BorderStyle.FixedSingle,
                Text = "成本价",
                ForeColor = Color.FromArgb(180, 180, 180)
            };
            txtCostPrice.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    AddStockFromInput();
            };
            txtCostPrice.GotFocus += (s, e) =>
            {
                if (txtCostPrice.Text == "成本价")
                {
                    txtCostPrice.Text = "";
                    txtCostPrice.ForeColor = Color.FromArgb(50, 50, 50);
                }
            };
            txtCostPrice.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtCostPrice.Text))
                {
                    txtCostPrice.Text = "成本价";
                    txtCostPrice.ForeColor = Color.FromArgb(180, 180, 180);
                }
            };

            // 数量输入框
            txtQuantity = new TextBox
            {
                Font = new Font("Microsoft YaHei", 8),
                Location = new Point(128, 4),
                Size = new Size(50, 18),
                BorderStyle = BorderStyle.FixedSingle,
                Text = "数量",
                ForeColor = Color.FromArgb(180, 180, 180)
            };
            txtQuantity.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    AddStockFromInput();
            };
            txtQuantity.GotFocus += (s, e) =>
            {
                if (txtQuantity.Text == "数量")
                {
                    txtQuantity.Text = "";
                    txtQuantity.ForeColor = Color.FromArgb(50, 50, 50);
                }
            };
            txtQuantity.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtQuantity.Text))
                {
                    txtQuantity.Text = "数量";
                    txtQuantity.ForeColor = Color.FromArgb(180, 180, 180);
                }
            };

            // 置顶按钮（自绘图钉图标）
            btnPin = new Button
            {
                Text = "",
                Size = new Size(18, 18),
                Location = new Point(205, 4),
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
                Color lineColor = isActive ? Color.White : Color.FromArgb(130, 130, 130);
                using (var pen = new Pen(lineColor, 1.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    int cx = btn.Width / 2;
                    g.DrawLine(pen, cx, 11, cx, 14);
                    g.DrawLine(pen, cx, 13, cx - 1, 15);
                    g.DrawLine(pen, cx, 13, cx + 1, 15);
                }
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
                Location = new Point(228, 2),
                Size = new Size(50, 22),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            trackOpacity.ValueChanged += TrackOpacity_ValueChanged;
            trackOpacity.MouseUp += (s, ev) => SaveSettings();

            // 透明度数值
            lblOpacityVal = new Label
            {
                Text = "90%",
                Location = new Point(282, 5),
                Size = new Size(20, 16),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Microsoft YaHei", 8)
            };

            // 总成本标签
            lblTotalCost = new Label
            {
                Text = "成本:0",
                Location = new Point(305, 5),
                Size = new Size(80, 16),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Microsoft YaHei", 8, FontStyle.Bold)
            };

            // 当前总额标签
            lblCurrentTotal = new Label
            {
                Text = "现额:0",
                Location = new Point(388, 5),
                Size = new Size(80, 16),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Microsoft YaHei", 8, FontStyle.Bold)
            };

            // 总盈利标签
            lblTotalProfitAmt = new Label
            {
                Text = "盈利:0",
                Location = new Point(471, 5),
                Size = new Size(80, 16),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Microsoft YaHei", 8, FontStyle.Bold)
            };

            // 今盈利标签
            lblTotalDailyProfitAmt = new Label
            {
                Text = "今盈:0",
                Location = new Point(554, 5),
                Size = new Size(80, 16),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Microsoft YaHei", 8, FontStyle.Bold)
            };

            // 状态标签（右侧）
            lblStatus = new Label
            {
                Text = "",
                Location = new Point(0, 5),
                Size = new Size(48, 16),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(160, 160, 160),
                Font = new Font("Microsoft YaHei", 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            toolPanel.Controls.AddRange(new Control[] {
                btnAdd, txtStockCode, txtCostPrice, txtQuantity,
                btnPin, trackOpacity, lblOpacityVal,
                lblTotalCost, lblCurrentTotal, lblTotalProfitAmt, lblTotalDailyProfitAmt,
                lblStatus
            });

            // ---- 股票列表 DataGridView ----
            stockGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                ColumnHeadersVisible = true,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 26,
                GridColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Microsoft YaHei", 8),
                AllowUserToResizeColumns = true,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ScrollBars = ScrollBars.Vertical
            };
            stockGridView.RowTemplate.Height = 26;

            // 定义列（FillWeight 控制初始比例）
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCode", HeaderText = "代码", FillWeight = 8, ReadOnly = true });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "名称", FillWeight = 10, ReadOnly = true });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colPrice", HeaderText = "现价", FillWeight = 7, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colChangeAmount", HeaderText = "涨跌额", FillWeight = 8, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colChangePercent", HeaderText = "涨跌幅", FillWeight = 8, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colTotalProfitPct", HeaderText = "总盈亏%", FillWeight = 9, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colTotalProfitAmt", HeaderText = "总盈亏额", FillWeight = 11, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDailyProfitPct", HeaderText = "今盈亏%", FillWeight = 9, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDailyProfitAmt", HeaderText = "今盈亏额", FillWeight = 11, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCostPrice", HeaderText = "成本价", FillWeight = 8, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colQuantity", HeaderText = "数量", FillWeight = 7, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCostTotal", HeaderText = "成本总额", FillWeight = 11, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
            stockGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCurrentTotal", HeaderText = "当前总额", FillWeight = 11, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });

            // 设置列头样式
            stockGridView.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
            stockGridView.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(80, 80, 80);
            stockGridView.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei", 8, FontStyle.Regular);
            stockGridView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            stockGridView.EnableHeadersVisualStyles = false;

            // 列头点击排序
            stockGridView.ColumnHeaderMouseClick += StockGridView_ColumnHeaderMouseClick;
            // 列头右击菜单
            stockGridView.ColumnHeaderMouseClick += StockGridView_ColumnHeaderMouseRightClick;
            // 列宽变化保存
            stockGridView.ColumnWidthChanged += StockGridView_ColumnWidthChanged;
            // 右键菜单
            stockGridView.CellMouseUp += StockGridView_CellMouseUp;
            stockGridView.DoubleClick += (s, e) =>
            {
                if (stockGridView.CurrentRow != null)
                {
                    string code = stockGridView.CurrentRow.Cells["colCode"].Value?.ToString();
                    if (!string.IsNullOrEmpty(code))
                        OpenChartWindow(code);
                }
            };

            // 右键菜单
            rowContextMenu = new ContextMenuStrip();
            miEditCostQuantity = new ToolStripMenuItem("修改成本与数量");
            miEditCostQuantity.Click += MiEditCostQuantity_Click;
            miDeleteStock = new ToolStripMenuItem("删除股票");
            miDeleteStock.Click += MiDeleteStock_Click;
            rowContextMenu.Items.Add(miEditCostQuantity);
            rowContextMenu.Items.Add(miDeleteStock);

            // ---- 添加到主窗体 ----
            this.Controls.Add(stockGridView);
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

        /// <summary>
        /// 窗口变窄时按优先级隐藏工具栏统计标签（从右到左），
        /// 避免控件重叠。隐藏顺序：今盈 → 盈利 → 现额 → 成本。
        /// 左侧输入区（代码/成本价/数量/+ /置顶/透明度）始终保留。
        /// </summary>
        private void AdjustToolbarVisibility()
        {
            if (toolPanel == null) return;
            int w = toolPanel.ClientSize.Width;

            // 状态标签固定占右侧 48px
            const int rightReserved = 52;
            int available = w - rightReserved;

            // 各统计标签的右边界（绝对坐标）
            // 今盈: 554+80=634, 盈利: 471+80=551, 现额: 388+80=468, 成本: 305+80=385
            // 从右到左依次判断：右边界超出可用空间则隐藏
            lblTotalDailyProfitAmt.Visible = available > 634;
            lblTotalProfitAmt.Visible = available > 551;
            lblCurrentTotal.Visible = available > 468;
            lblTotalCost.Visible = available > 385;
        }

        /// <summary>
        /// 初始化系统托盘图标及右键菜单
        /// </summary>
        private void InitializeTrayIcon()
        {
            // 右键菜单
            trayMenu = new ContextMenuStrip();
            trayMenu.RenderMode = ToolStripRenderMode.System;

            miShow = new ToolStripMenuItem("显示主窗口");
            miShow.Click += (s, e) => ShowMainWindow();
            trayMenu.Items.Add(miShow);

            trayMenu.Items.Add(new ToolStripSeparator());

            miToggleFloatingBall = new ToolStripMenuItem("显示悬浮球");
            miToggleFloatingBall.CheckOnClick = true;
            miToggleFloatingBall.Checked = settings != null && settings.ShowFloatingBall;
            miToggleFloatingBall.Click += MiToggleFloatingBall_Click;
            trayMenu.Items.Add(miToggleFloatingBall);

            trayMenu.Items.Add(new ToolStripSeparator());

            var miOpenConfig = new ToolStripMenuItem("打开配置");
            miOpenConfig.Click += (s, e) => OpenSettingsDialog();
            trayMenu.Items.Add(miOpenConfig);

            var miResetConfig = new ToolStripMenuItem("重置所有设置");
            miResetConfig.Click += (s, e) => ResetAllSettings();
            trayMenu.Items.Add(miResetConfig);

            trayMenu.Items.Add(new ToolStripSeparator());

            miExit = new ToolStripMenuItem("退出");
            miExit.Click += (s, e) =>
            {
                exitApp = true;
                this.Close();
            };
            trayMenu.Items.Add(miExit);

            // 托盘图标
            trayIcon = new NotifyIcon
            {
                Icon = this.Icon,
                Text = "桌面股市",
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            trayIcon.DoubleClick += (s, e) => ShowMainWindow();
            trayIcon.MouseClick += (s, e) =>
            {
                // 左键单击也显示主窗口
                if (e.Button == MouseButtons.Left)
                {
                    ShowMainWindow();
                }
            };
        }

        /// <summary>
/// 显示主窗口并置于前台
/// </summary>
private void ShowMainWindow()
{
    // 显式还原隐藏前的位置和大小（ShowInTaskbar=false 时句柄可能被重建导致位置丢失）
    if (hasHiddenState)
    {
        // 先把窗口状态设为 Normal，再设置位置和大小
        this.WindowState = FormWindowState.Normal;
        this.Location = hiddenLocation;
        this.Size = hiddenSize;
    }
    else
    {
        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }
    }
    this.Show();
    this.Activate();
    this.BringToFront();
    // 重新设为 TopMost 可保持之前的置顶状态
    if (settings != null && settings.TopMost)
    {
        this.TopMost = false;
        this.TopMost = true;
    }

    // 恢复主窗口时自动隐藏悬浮球（避免重复显示）
    if (floatingBall != null && floatingBall.Visible)
    {
        floatingBall.Hide();
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
            foreach (var stockConfig in settings.Stocks)
            {
                AddStockToGrid(stockConfig.Code, stockConfig.CostPrice, stockConfig.Quantity);
            }

            // 恢复列宽（使用 FillWeight）
            if (settings.ColumnWidths != null && settings.ColumnWidths.Count > 0)
            {
                for (int i = 0; i < settings.ColumnWidths.Count && i < stockGridView.Columns.Count; i++)
                {
                    stockGridView.Columns[i].FillWeight = settings.ColumnWidths[i];
                }
            }

            // 恢复列可见性
            if (settings.ColumnVisible != null && settings.ColumnVisible.Count > 0)
            {
                for (int i = 0; i < settings.ColumnVisible.Count && i < stockGridView.Columns.Count; i++)
                {
                    stockGridView.Columns[i].Visible = settings.ColumnVisible[i];
                }
            }

            PerformLayout();

            // 初始化托盘恢复用的位置/大小记录，避免启动后未移动即隐藏时丢失位置
            hiddenLocation = this.Location;
            hiddenSize = this.Size;
            hasHiddenState = true;

            // 初始化悬浮球，并根据上次保存的状态恢复显示
            InitializeFloatingBall();

            // 恢复"显示悬浮球"状态：避免重启后丢失该设置
            // （InitializeTrayIcon 在 settings 加载前执行，其 Checked 不可信，这里以 settings 为准重新设定）
            if (miToggleFloatingBall != null)
                miToggleFloatingBall.Checked = settings.ShowFloatingBall;
            if (settings.ShowFloatingBall)
            {
                ShowFloatingBall();
            }
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
            settings.Stocks = new List<StockConfig>();
            foreach (var config in stockConfigs.Values)
            {
                settings.Stocks.Add(new StockConfig(config.Code, config.CostPrice, config.Quantity));
            }
            // 保存列宽（使用 FillWeight）
            settings.ColumnWidths = new List<int>();
            foreach (DataGridViewColumn col in stockGridView.Columns)
            {
                settings.ColumnWidths.Add((int)col.FillWeight);
            }
            // 保存列可见性
            settings.ColumnVisible = new List<bool>();
            foreach (DataGridViewColumn col in stockGridView.Columns)
            {
                settings.ColumnVisible.Add(col.Visible);
            }
            // 保存悬浮球状态
            settings.ShowFloatingBall = (miToggleFloatingBall != null && miToggleFloatingBall.Checked);
            if (floatingBall != null)
            {
                settings.FloatingBallX = floatingBall.Location.X;
                settings.FloatingBallY = floatingBall.Location.Y;
            }
            // 保存置顶按钮状态
            if (btnPin != null && btnPin.Tag != null)
            {
                settings.TopMost = (btnPin.Tag.ToString() == "1");
            }
            StockDataStore.Save(settings);
        }

        #endregion

        #region 股票表格管理

        private void AddStockToGrid(string code, decimal costPrice, int quantity)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            code = code.Trim();

            // 检查是否已存在
            if (stockConfigs.ContainsKey(code)) return;

            // 添加到字典
            stockConfigs[code] = new StockConfig(code, costPrice, quantity);

            // 添加到 DataGridView
            int rowIndex = stockGridView.Rows.Add();
            var row = stockGridView.Rows[rowIndex];
            row.Cells["colCode"].Value = code;
            row.Cells["colName"].Value = "获取中...";
            row.Cells["colCostPrice"].Value = costPrice > 0 ? costPrice.ToString("F2") : "";
            row.Cells["colQuantity"].Value = quantity > 0 ? quantity.ToString() : "";

            ApplyRowStyle(row, 0, false);
            UpdateSummaryStats();
        }

        private void RemoveStockFromGrid(string code)
        {
            if (!stockConfigs.ContainsKey(code)) return;

            stockConfigs.Remove(code);
            stockData.Remove(code);

            // 从 DataGridView 中移除
            foreach (DataGridViewRow row in stockGridView.Rows)
            {
                if (row.Cells["colCode"].Value?.ToString() == code)
                {
                    stockGridView.Rows.Remove(row);
                    break;
                }
            }

            UpdateSummaryStats();
            SaveSettings();
        }

        private void UpdateStockRow(string code, StockInfo info)
        {
            stockData[code] = info;

            // 查找对应行
            foreach (DataGridViewRow row in stockGridView.Rows)
            {
                if (row.Cells["colCode"].Value?.ToString() == code)
                {
                    var config = stockConfigs.ContainsKey(code) ? stockConfigs[code] : null;
                    decimal costPrice = config != null ? config.CostPrice : 0;
                    int quantity = config != null ? config.Quantity : 0;

                    row.Cells["colName"].Value = info.Name ?? code;
                    row.Cells["colPrice"].Value = info.Price.ToString("F2");
                    row.Cells["colChangeAmount"].Value = (info.ChangeAmount >= 0 ? "+" : "") + info.ChangeAmount.ToString("F2");
                    row.Cells["colChangePercent"].Value = (info.ChangePercent >= 0 ? "+" : "") + info.ChangePercent.ToString("F2") + "%";

                    // 计算盈亏
                    if (costPrice > 0 && quantity > 0)
                    {
                        decimal totalProfit = (info.Price - costPrice) * quantity;
                        decimal totalProfitPct = (info.Price - costPrice) / costPrice * 100;
                        row.Cells["colTotalProfitPct"].Value = (totalProfit >= 0 ? "+" : "") + totalProfitPct.ToString("F2") + "%";
                        row.Cells["colTotalProfitAmt"].Value = (totalProfit >= 0 ? "+" : "") + totalProfit.ToString("N0");
                        // 成本总额 = 成本价 × 数量
                        row.Cells["colCostTotal"].Value = (costPrice * quantity).ToString("N0");
                        // 当前总额 = 现价 × 数量
                        row.Cells["colCurrentTotal"].Value = (info.Price * quantity).ToString("N0");
                    }
                    else
                    {
                        row.Cells["colTotalProfitPct"].Value = "--";
                        row.Cells["colTotalProfitAmt"].Value = "--";
                        row.Cells["colCostTotal"].Value = "--";
                        row.Cells["colCurrentTotal"].Value = "--";
                    }

                    if (quantity > 0)
                    {
                        decimal dailyProfit = info.ChangeAmount * quantity;
                        row.Cells["colDailyProfitPct"].Value = (info.ChangePercent >= 0 ? "+" : "") + info.ChangePercent.ToString("F2") + "%";
                        row.Cells["colDailyProfitAmt"].Value = (dailyProfit >= 0 ? "+" : "") + dailyProfit.ToString("N0");
                    }
                    else
                    {
                        row.Cells["colDailyProfitPct"].Value = (info.ChangePercent >= 0 ? "+" : "") + info.ChangePercent.ToString("F2") + "%";
                        row.Cells["colDailyProfitAmt"].Value = "--";
                    }

                    // 应用颜色样式
                    ApplyRowStyle(row, info.ChangeAmount, info.IsValid);

                    // 刷新总统计
                    UpdateSummaryStats();
                    break;
                }
            }
        }

        /// <summary>
        /// 计算并更新工具栏上的总统计：成本总额、当前总额、盈利总额、今盈利总额
        /// </summary>
        private void UpdateSummaryStats()
        {
            decimal totalCost = 0;
            decimal totalCurrent = 0;
            decimal totalProfit = 0;
            decimal totalDailyProfit = 0;

            foreach (DataGridViewRow row in stockGridView.Rows)
            {
                string code = row.Cells["colCode"].Value?.ToString();
                if (string.IsNullOrEmpty(code)) continue;
                if (!stockConfigs.ContainsKey(code)) continue;
                var cfg = stockConfigs[code];
                if (cfg.CostPrice <= 0 || cfg.Quantity <= 0) continue;

                totalCost += cfg.CostPrice * cfg.Quantity;
                if (stockData.ContainsKey(code))
                {
                    var info = stockData[code];
                    totalCurrent += info.Price * cfg.Quantity;
                    totalProfit += (info.Price - cfg.CostPrice) * cfg.Quantity;
                    // 今盈亏 = 涨跌额 × 数量
                    totalDailyProfit += info.ChangeAmount * cfg.Quantity;
                }
                else
                {
                    // 没有实时行情时按成本价计入
                    totalCurrent += cfg.CostPrice * cfg.Quantity;
                }
            }

            // 更新工具栏显示
            if (lblTotalCost != null)
            {
                lblTotalCost.Text = "成本:" + totalCost.ToString("N0");
                lblCurrentTotal.Text = "现额:" + totalCurrent.ToString("N0");
                lblTotalProfitAmt.Text = "盈利:" + (totalProfit >= 0 ? "+" : "") + totalProfit.ToString("N0");
                lblTotalDailyProfitAmt.Text = "今盈:" + (totalDailyProfit >= 0 ? "+" : "") + totalDailyProfit.ToString("N0");

                // 总盈利颜色
                if (totalProfit > 0) lblTotalProfitAmt.ForeColor = Color.FromArgb(220, 38, 38);
                else if (totalProfit < 0) lblTotalProfitAmt.ForeColor = Color.FromArgb(22, 163, 74);
                else lblTotalProfitAmt.ForeColor = Color.FromArgb(120, 120, 120);

                // 今盈利颜色
                if (totalDailyProfit > 0) lblTotalDailyProfitAmt.ForeColor = Color.FromArgb(220, 38, 38);
                else if (totalDailyProfit < 0) lblTotalDailyProfitAmt.ForeColor = Color.FromArgb(22, 163, 74);
                else lblTotalDailyProfitAmt.ForeColor = Color.FromArgb(120, 120, 120);
            }

            // 更新悬浮球（如果已创建）
            if (floatingBall != null && floatingBall.Visible)
            {
                floatingBall.UpdateValues(totalProfit, totalDailyProfit);
            }
        }

        private void ApplyRowStyle(DataGridViewRow row, decimal changeAmount, bool hasData)
        {
            Color upColor = Color.FromArgb(220, 38, 38);
            Color downColor = Color.FromArgb(22, 163, 74);
            Color flatColor = Color.FromArgb(120, 120, 120);

            Color foreColor = flatColor;
            Color backColor = Color.White;

            if (hasData)
            {
                if (changeAmount > 0)
                {
                    foreColor = upColor;
                    backColor = Color.FromArgb(255, 245, 245);
                }
                else if (changeAmount < 0)
                {
                    foreColor = downColor;
                    backColor = Color.FromArgb(240, 253, 244);
                }
            }

            row.DefaultCellStyle.ForeColor = foreColor;
            row.DefaultCellStyle.BackColor = backColor;

            // 特定列着色
            if (row.Cells["colChangeAmount"] != null)
                row.Cells["colChangeAmount"].Style.ForeColor = foreColor;
            if (row.Cells["colChangePercent"] != null)
                row.Cells["colChangePercent"].Style.ForeColor = foreColor;
        }

        private void StockGridView_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            if (e.Button != MouseButtons.Left) return;

            // 切换排序方向
            if (sortColumn == e.ColumnIndex)
            {
                sortOrder = sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                sortColumn = e.ColumnIndex;
                sortOrder = SortOrder.Ascending;
            }

            // 排序
            stockGridView.Sort(stockGridView.Columns[e.ColumnIndex],
                sortOrder == SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
        }

        private void StockGridView_ColumnHeaderMouseRightClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            var menu = new ContextMenuStrip();
            foreach (DataGridViewColumn col in stockGridView.Columns)
            {
                var item = new ToolStripMenuItem(col.HeaderText);
                item.CheckOnClick = true;
                item.Checked = col.Visible;
                int colIndex = col.Index;
                item.CheckedChanged += (s, args) =>
                {
                    stockGridView.Columns[colIndex].Visible = item.Checked;
                    if (saveDebounceTimer != null)
                    {
                        saveDebounceTimer.Stop();
                        saveDebounceTimer.Start();
                    }
                };
                menu.Items.Add(item);
            }
            menu.Show(stockGridView, stockGridView.PointToClient(Cursor.Position));
        }

        private void StockGridView_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e)
        {
            // 延迟保存，避免频繁写入
            if (saveDebounceTimer != null)
            {
                saveDebounceTimer.Stop();
                saveDebounceTimer.Start();
            }
        }

        private void StockGridView_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                // 找到第一个可见列的单元格作为 CurrentCell，
                // 避免设置到隐藏列抛出 "Current cell cannot be set to an invisible cell" 异常
                DataGridViewCell firstVisibleCell = null;
                foreach (DataGridViewColumn col in stockGridView.Columns)
                {
                    if (col.Visible)
                    {
                        firstVisibleCell = stockGridView.Rows[e.RowIndex].Cells[col.Index];
                        break;
                    }
                }
                if (firstVisibleCell != null)
                {
                    stockGridView.CurrentCell = firstVisibleCell;
                }
                else
                {
                    // 极端情况：所有列都隐藏，仅选中行
                    stockGridView.Rows[e.RowIndex].Selected = true;
                }
                rowContextMenu.Show(stockGridView, stockGridView.PointToClient(Cursor.Position));
            }
        }

        private void MiEditCostQuantity_Click(object sender, EventArgs e)
        {
            if (stockGridView.CurrentRow == null) return;

            string code = stockGridView.CurrentRow.Cells["colCode"].Value?.ToString();
            if (string.IsNullOrEmpty(code)) return;

            string name = stockGridView.CurrentRow.Cells["colName"].Value?.ToString();
            if (name == "获取中..." || string.IsNullOrEmpty(name)) name = "";

            decimal currentCost = stockConfigs.ContainsKey(code) ? stockConfigs[code].CostPrice : 0;
            int currentQty = stockConfigs.ContainsKey(code) ? stockConfigs[code].Quantity : 0;

            using (var form = new EditCostQuantityForm(code, name, currentCost, currentQty))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    stockConfigs[code] = new StockConfig(code, form.CostPrice, form.Quantity);
                    stockGridView.CurrentRow.Cells["colCostPrice"].Value = form.CostPrice > 0 ? form.CostPrice.ToString("F2") : "";
                    stockGridView.CurrentRow.Cells["colQuantity"].Value = form.Quantity > 0 ? form.Quantity.ToString() : "";

                    // 更新盈亏显示
                    if (stockData.ContainsKey(code))
                    {
                        UpdateStockRow(code, stockData[code]);
                    }
                    else
                    {
                        // 没有实时行情也要更新总额显示
                        if (form.CostPrice > 0 && form.Quantity > 0)
                        {
                            stockGridView.CurrentRow.Cells["colCostTotal"].Value = (form.CostPrice * form.Quantity).ToString("N0");
                            stockGridView.CurrentRow.Cells["colCurrentTotal"].Value = (form.CostPrice * form.Quantity).ToString("N0");
                        }
                        else
                        {
                            stockGridView.CurrentRow.Cells["colCostTotal"].Value = "--";
                            stockGridView.CurrentRow.Cells["colCurrentTotal"].Value = "--";
                        }
                        UpdateSummaryStats();
                    }

                    SaveSettings();
                }
            }
        }

        private void MiDeleteStock_Click(object sender, EventArgs e)
        {
            if (stockGridView.CurrentRow == null) return;

            string code = stockGridView.CurrentRow.Cells["colCode"].Value?.ToString();
            if (string.IsNullOrEmpty(code)) return;

            string name = stockGridView.CurrentRow.Cells["colName"].Value?.ToString();
            string display = string.IsNullOrEmpty(name) || name == "获取中..."
                ? code
                : name + "（" + code + "）";

            var result = MessageBox.Show(
                "确定删除 " + display + " 吗？",
                "删除股票",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                RemoveStockFromGrid(code);
            }
        }

        #region 悬浮球

        private void InitializeFloatingBall()
        {
            if (floatingBall != null) return;

            floatingBall = new FloatingBall();

            // 恢复悬浮球位置
            if (settings.FloatingBallX >= 0 && settings.FloatingBallY >= 0)
            {
                floatingBall.Location = new Point(settings.FloatingBallX, settings.FloatingBallY);
            }

            // 悬浮球右键菜单：显示主窗口 / 隐藏悬浮球
            var ballMenu = new ContextMenuStrip();
            var miBallShow = new ToolStripMenuItem("显示主窗口");
            miBallShow.Click += (s, e) => ShowMainWindow();
            var miBallHide = new ToolStripMenuItem("隐藏悬浮球");
            miBallHide.Click += (s, e) => HideFloatingBall();
            ballMenu.Items.Add(miBallShow);
            ballMenu.Items.Add(miBallHide);
            floatingBall.ContextMenuStrip = ballMenu;

            // 鼠标释放时记录位置
            floatingBall.MouseUp += (s, e) =>
            {
                if (settings != null && floatingBall.Visible)
                {
                    settings.FloatingBallX = floatingBall.Location.X;
                    settings.FloatingBallY = floatingBall.Location.Y;
                    SaveSettings();
                }
            };

            // 双击悬浮球打开主窗口（与右键菜单"显示主窗口"走同一路径）
            floatingBall.OpenMainWindowRequested += (s, e) => ShowMainWindow();
        }

        /// <summary>
        /// 显示悬浮球
        /// </summary>
        private void ShowFloatingBall()
        {
            if (floatingBall == null) InitializeFloatingBall();

            if (settings != null) settings.ShowFloatingBall = true;

            // 同步主窗口透明度
            if (this.Opacity > 0.1) floatingBall.Opacity = this.Opacity;

            // 在 Show 前预先用当前真实数据计算尺寸，避免出现"先大后小"的问题
            PreAdjustBallSize();

            // 立即更新一次值（保持自适应逻辑）
            UpdateSummaryStats();
            floatingBall.Show();
            floatingBall.BringToFront();

            if (miToggleFloatingBall != null) miToggleFloatingBall.Checked = true;

            // 立即持久化 ShowFloatingBall=true 状态（避免下次启动丢失）
            SaveSettings();
        }

        /// <summary>
        /// 在悬浮球显示前，根据当前汇总数据先调整一次尺寸
        /// </summary>
        private void PreAdjustBallSize()
        {
            if (floatingBall == null) return;

            decimal totalProfit = 0;
            decimal totalDailyProfit = 0;

            foreach (var pair in stockConfigs)
            {
                var cfg = pair.Value;
                if (cfg.CostPrice <= 0 || cfg.Quantity <= 0) continue;

                if (stockData.ContainsKey(pair.Key))
                {
                    var info = stockData[pair.Key];
                    totalProfit += (info.Price - cfg.CostPrice) * cfg.Quantity;
                    totalDailyProfit += info.ChangeAmount * cfg.Quantity;
                }
            }

            // 设置属性后立即调整尺寸（Show 之前完成）
            floatingBall.SetValuesAndResize(totalProfit, totalDailyProfit);
        }

        /// <summary>
        /// 隐藏悬浮球
        /// </summary>
        private void HideFloatingBall()
        {
            if (floatingBall != null) floatingBall.Hide();
            if (settings != null) settings.ShowFloatingBall = false;
            if (miToggleFloatingBall != null) miToggleFloatingBall.Checked = false;
            SaveSettings();
        }

        /// <summary>
        /// 托盘菜单点击：显示/隐藏悬浮球
        /// </summary>
        private void MiToggleFloatingBall_Click(object sender, EventArgs e)
        {
            if (miToggleFloatingBall.Checked)
            {
                ShowFloatingBall();
            }
            else
            {
                HideFloatingBall();
            }
        }

        #endregion

        #region 配置管理（导入/导出/重置/打开目录）

        /// <summary>
        /// 获取配置文件目录
        /// </summary>
        private string GetConfigDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopStock");
        }

        /// <summary>
        /// 获取配置文件完整路径
        /// </summary>
        private string GetConfigFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "settings.json");
        }

        /// <summary>
        /// 打开图形化配置窗体
        /// </summary>
        private void OpenSettingsDialog()
        {
            // 先保存最新状态，确保用户看到的是当前配置
            SaveSettings();

            using (var form = new SettingsForm(settings))
            {
                // 订阅导出/导入事件
                form.ExportRequest += ExportConfigViaForm;
                form.ImportRequest += ImportConfigViaForm;

                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    ApplySettingsChanges(form.ResultSettings);
                }
            }
        }

        /// <summary>
        /// 处理配置窗体的导出请求：保存当前设置后复制文件
        /// </summary>
        private void ExportConfigViaForm(string targetPath)
        {
            SaveSettings();
            string src = GetConfigFilePath();
            if (!File.Exists(src))
                throw new InvalidOperationException("配置文件不存在，无法导出。");
            File.Copy(src, targetPath, true);
        }

        /// <summary>
        /// 处理配置窗体的导入请求：复制文件并重启应用
        /// </summary>
        private void ImportConfigViaForm(string sourcePath)
        {
            string dst = GetConfigFilePath();
            string dir = Path.GetDirectoryName(dst);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // 标记跳过 FormClosing 中的 SaveSettings，避免用内存中的旧配置覆盖刚导入的文件
            skipSaveOnClosing = true;

            File.Copy(sourcePath, dst, true);

            // 重启应用以加载新配置
            exitApp = true;
            Application.Restart();
        }

        /// <summary>
        /// 应用用户在配置窗体中修改的设置
        /// </summary>
        private void ApplySettingsChanges(AppSettings newSettings)
        {
            // 透明度
            settings.Opacity = newSettings.Opacity;
            this.Opacity = settings.Opacity;
            trackOpacity.Value = (int)(settings.Opacity * 100);
            lblOpacityVal.Text = trackOpacity.Value + "%";

            // 置顶
            settings.TopMost = newSettings.TopMost;
            this.TopMost = settings.TopMost;
            btnPin.Tag = settings.TopMost ? "1" : "0";
            btnPin.BackColor = settings.TopMost ? Color.FromArgb(59, 130, 246) : Color.FromArgb(200, 200, 200);
            btnPin.Invalidate();

            // 刷新间隔
            settings.RefreshInterval = newSettings.RefreshInterval;
            if (refreshTimer != null)
            {
                refreshTimer.Interval = settings.RefreshInterval * 1000;
            }

            // 悬浮球
            bool ballChanged = settings.ShowFloatingBall != newSettings.ShowFloatingBall;
            settings.ShowFloatingBall = newSettings.ShowFloatingBall;
            if (miToggleFloatingBall != null)
                miToggleFloatingBall.Checked = settings.ShowFloatingBall;
            if (ballChanged)
            {
                if (settings.ShowFloatingBall)
                    ShowFloatingBall();
                else
                    HideFloatingBall();
            }

            // 窗口大小
            settings.WindowWidth = newSettings.WindowWidth;
            settings.WindowHeight = newSettings.WindowHeight;
            if (this.WindowState == FormWindowState.Normal)
            {
                this.Width = settings.WindowWidth;
                this.Height = settings.WindowHeight;
                hiddenLocation = this.Location;
                hiddenSize = this.Size;
                hasHiddenState = true;
            }

            // 保存到文件
            SaveSettings();

            MessageBox.Show("设置已应用并保存。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 重置所有设置
        /// </summary>
        private void ResetAllSettings()
        {
            var result = MessageBox.Show(
                "确定要重置所有设置吗？\n\n" +
                "将会清除：\n" +
                "• 所有股票列表\n" +
                "• 窗口位置和大小\n" +
                "• 悬浮球位置\n" +
                "• 列宽和可见性\n" +
                "• 透明度和置顶状态\n\n" +
                "此操作不可撤销！",
                "重置所有设置", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                string configFile = GetConfigFilePath();
                if (File.Exists(configFile)) File.Delete(configFile);

                MessageBox.Show("所有设置已重置，程序将重启。", "重置成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                exitApp = true;
                Application.Restart();
            }
            catch (Exception ex)
            {
                MessageBox.Show("重置失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        private void OpenChartWindow(string code)
        {
            string name = stockData.ContainsKey(code) && stockData[code].IsValid ? stockData[code].Name : code;
            System.Threading.Tasks.Task.Run(() =>
            {
                this.Invoke(new Action(() => lblStatus.Text = "加载走势..."));
                try
                {
                    var trendData = StockService.FetchTrendSync(code, name);
                    this.Invoke(new Action(() =>
                    {
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
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() =>
                        MessageBox.Show("走势图加载失败: " + ex.Message, "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
                finally
                {
                    this.Invoke(new Action(() =>
                    {
                        var now = DateTime.Now;
                        lblStatus.Text = string.Format("{0}:{1:D2}", now.Hour, now.Minute);
                    }));
                }
            });
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
            if (string.IsNullOrWhiteSpace(input) || input == "股票代码") return;

            decimal costPrice = 0;
            int quantity = 0;

            // 解析成本价
            string costText = txtCostPrice.Text.Trim();
            if (!string.IsNullOrWhiteSpace(costText) && costText != "成本价")
            {
                decimal.TryParse(costText, out costPrice);
            }

            // 解析数量
            string qtyText = txtQuantity.Text.Trim();
            if (!string.IsNullOrWhiteSpace(qtyText) && qtyText != "数量")
            {
                int.TryParse(qtyText, out quantity);
            }

            // 清空输入框（提示文本由LostFocus事件自动恢复）
            txtStockCode.Text = "";
            txtCostPrice.Text = "";
            txtQuantity.Text = "";
            txtStockCode.Focus();
            ProcessAddCode(input, costPrice, quantity);
        }

        private void ProcessAddCode(string code, decimal costPrice = 0, int quantity = 0)
        {

            // 简单校验：A股代码6位数字
            if (code.Length < 5 || code.Length > 6)
            {
                MessageBox.Show("请输入正确的A股代码（6位数字）", "格式错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 检查是否重复
            if (stockConfigs.ContainsKey(code))
            {
                MessageBox.Show("该股票已在列表中", "重复添加",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 添加到表格
            AddStockToGrid(code, costPrice, quantity);
            SaveSettings();

            // 立即刷新这只股票
            RefreshSingleStock(code);
        }


        private void TrackOpacity_ValueChanged(object sender, EventArgs e)
        {
            double opacity = trackOpacity.Value / 100.0;
            this.Opacity = opacity;
            // 同步悬浮球透明度
            if (floatingBall != null) floatingBall.Opacity = opacity;
            lblOpacityVal.Text = trackOpacity.Value + "%";
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 关闭前立即保存（停止防抖定时器，直接写盘）
            moveResizePending = false;
            if (saveDebounceTimer != null) saveDebounceTimer.Stop();
            // 导入配置时跳过保存，避免用内存中的旧配置覆盖刚导入的文件
            if (!skipSaveOnClosing)
            {
                SaveSettings();
            }

            // 非用户主动"退出"时，仅隐藏到托盘而不结束程序
            if (!exitApp && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                // 在隐藏前记录当前窗口位置和大小（只有 Normal 状态才记录）
                if (this.WindowState == FormWindowState.Normal)
                {
                    hiddenLocation = this.Location;
                    hiddenSize = this.Size;
                    hasHiddenState = true;
                }
                this.Hide();

                // 显示悬浮球（如果用户没有禁用）
                if (settings != null && settings.ShowFloatingBall)
                {
                    ShowFloatingBall();
                }

                if (trayIcon != null)
                {
                    trayIcon.ShowBalloonTip(2000, "桌面股市", "程序已最小化到托盘，双击图标可恢复窗口。", ToolTipIcon.Info);
                }
                return;
            }

            // 真正退出：停止定时器
            if (refreshTimer != null) refreshTimer.Stop();
            // 退出时释放悬浮球
            if (floatingBall != null)
            {
                floatingBall.Close();
                floatingBall.Dispose();
                floatingBall = null;
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 释放托盘图标，避免残留
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            if (saveDebounceTimer != null) saveDebounceTimer.Dispose();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            // 最小化时隐藏窗口，驻留托盘
            if (this.WindowState == FormWindowState.Minimized)
            {
                // 在隐藏前先记录窗口的"前一次 Normal 状态"的位置和大小
                // WinForms 的 RestoreBounds 在 Minimized 状态下仍保留正常状态的尺寸
                if (this.RestoreBounds.Width > 0 && this.RestoreBounds.Height > 0)
                {
                    hiddenLocation = this.RestoreBounds.Location;
                    hiddenSize = this.RestoreBounds.Size;
                }
                else
                {
                    // 兜底：使用当前 Size
                    hiddenLocation = this.Location;
                    hiddenSize = this.Size;
                }
                hasHiddenState = true;

                this.Hide();
                if (trayIcon != null)
                {
                    trayIcon.ShowBalloonTip(2000, "桌面股市", "程序已最小化到托盘，双击图标可恢复窗口。", ToolTipIcon.Info);
                }
            }
            else if (this.WindowState == FormWindowState.Normal)
            {
                // 在 Normal 状态下持续记录当前位置和大小
                hiddenLocation = this.Location;
                hiddenSize = this.Size;
                hasHiddenState = true;
            }
        }

        private void MainForm_Move(object sender, EventArgs e)
        {
            // 窗口移动时记录位置（仅在 Normal 状态）
            if (this.WindowState == FormWindowState.Normal)
            {
                hiddenLocation = this.Location;
                hasHiddenState = true;
            }
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
                    UpdateStockRow(info.Code, info);
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
            if (stockConfigs.Count == 0) return;

            isRefreshing = true;
            try
            {
                lblStatus.Text = "刷新中...";
                var codes = stockConfigs.Keys.ToList();

                var results = await System.Threading.Tasks.Task.Run(() =>
                    StockService.FetchStocksSync(codes)
                );

                foreach (var info in results)
                {
                    UpdateStockRow(info.Code, info);
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
            lblStatus.Location = new Point(toolPanel.ClientSize.Width - 64, 5);
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
