using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 单只股票显示面板（涨红跌绿）
    /// </summary>
    public class StockItemPanel : UserControl
    {
        // 颜色定义（A股：红涨绿跌）
        private static readonly Color UpColor = Color.FromArgb(220, 38, 38);
        private static readonly Color DownColor = Color.FromArgb(22, 163, 74);
        private static readonly Color FlatColor = Color.FromArgb(120, 120, 120);
        private static readonly Color UpBgColor = Color.FromArgb(255, 245, 245);
        private static readonly Color DownBgColor = Color.FromArgb(240, 253, 244);
        private static readonly Color FlatBgColor = Color.FromArgb(248, 248, 248);
        private static readonly Color NormalBgColor = Color.White;

        private Label lblName;
        private Label lblCode;
        private Label lblPrice;
        private Label lblChange;
        private Label lblTotalProfit;
        private Label lblDailyProfit;
        private Label lblClose;
        private Timer flashTimer;
        private int flashCount;
        private ContextMenuStrip contextMenu;
        private ToolStripMenuItem miEditCostQuantity;

        // 当前数据
        private string _stockName = "----";
        private string _stockCode = "";
        private decimal _price;
        private decimal _changeAmount;
        private decimal _changePercent;
        private decimal _costPrice;
        private int _quantity;
        private bool _hasData;

        public event EventHandler DeleteRequested;
        public event EventHandler DoubleClickRequested;
        public event EventHandler EditCostQuantityRequested;

        public string StockCode
        {
            get { return _stockCode; }
        }

        public string StockName
        {
            get { return _stockName; }
        }

        public decimal CostPrice
        {
            get { return _costPrice; }
        }

        public int Quantity
        {
            get { return _quantity; }
        }

        public StockItemPanel()
        {
            InitializeComponent();
        }

        public StockItemPanel(string code, decimal costPrice, int quantity)
        {
            _stockCode = (code ?? "").Trim();
            _costPrice = costPrice;
            _quantity = quantity;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Margin = new Padding(0, 0, 0, 0);
            this.Padding = new Padding(6, 2, 6, 2);
            this.BackColor = NormalBgColor;
            this.Cursor = Cursors.Hand;
            this.DoubleClick += (s, e) =>
            {
                if (DoubleClickRequested != null)
                    DoubleClickRequested(this, EventArgs.Empty);
            };

            // 右键菜单
            contextMenu = new ContextMenuStrip();
            miEditCostQuantity = new ToolStripMenuItem("修改成本与数量");
            miEditCostQuantity.Click += (s, e) =>
            {
                if (EditCostQuantityRequested != null)
                    EditCostQuantityRequested(this, EventArgs.Empty);
            };
            contextMenu.Items.Add(miEditCostQuantity);
            this.ContextMenuStrip = contextMenu;

            // 删除按钮 ×
            lblClose = new Label
            {
                Text = "\u00D7",
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 180, 180),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(20, 20),
                Cursor = Cursors.Hand
            };
            lblClose.Click += (s, e) =>
            {
                if (DeleteRequested != null)
                    DeleteRequested(this, EventArgs.Empty);
            };
            lblClose.MouseEnter += (s, e) => { lblClose.ForeColor = Color.FromArgb(220, 38, 38); };
            lblClose.MouseLeave += (s, e) => { lblClose.ForeColor = Color.FromArgb(180, 180, 180); };

            // 股票名称
            lblName = new Label
            {
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                BackColor = Color.Transparent,
                AutoSize = true,
                Text = "----"
            };

            // 股票代码
            lblCode = new Label
            {
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(150, 150, 150),
                BackColor = Color.Transparent,
                AutoSize = true,
                Text = ""
            };

            // 当前价格
            lblPrice = new Label
            {
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(50, 50, 50),
                BackColor = Color.Transparent,
                AutoSize = true,
                Text = "--"
            };

            // 涨跌幅+涨跌额
            lblChange = new Label
            {
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = FlatColor,
                BackColor = Color.Transparent,
                AutoSize = true,
                Text = "0.00  0.00%"
            };

            // 总盈亏
            lblTotalProfit = new Label
            {
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = FlatColor,
                BackColor = Color.Transparent,
                AutoSize = true,
                Text = "--"
            };

            // 今盈亏
            lblDailyProfit = new Label
            {
                Font = new Font("Microsoft YaHei", 8, FontStyle.Regular),
                ForeColor = FlatColor,
                BackColor = Color.Transparent,
                AutoSize = true,
                Text = "--"
            };

            this.Controls.Add(lblClose);
            this.Controls.Add(lblName);
            this.Controls.Add(lblCode);
            this.Controls.Add(lblPrice);
            this.Controls.Add(lblChange);
            this.Controls.Add(lblTotalProfit);
            this.Controls.Add(lblDailyProfit);

            // 点击面板也触发闪烁动画
            this.Click += (s, e) => StartFlash();
            lblName.Click += (s, e) => StartFlash();
            lblCode.Click += (s, e) => StartFlash();
            lblPrice.Click += (s, e) => StartFlash();
            lblChange.Click += (s, e) => StartFlash();
            lblTotalProfit.Click += (s, e) => StartFlash();
            lblDailyProfit.Click += (s, e) => StartFlash();

            // 子控件双击也要冒泡到面板
            EventHandler dblBubble = (s, e) =>
            {
                if (DoubleClickRequested != null)
                    DoubleClickRequested(this, EventArgs.Empty);
            };
            lblName.DoubleClick += dblBubble;
            lblCode.DoubleClick += dblBubble;
            lblPrice.DoubleClick += dblBubble;
            lblChange.DoubleClick += dblBubble;
            lblTotalProfit.DoubleClick += dblBubble;
            lblDailyProfit.DoubleClick += dblBubble;

            // 闪动定时器
            flashTimer = new Timer { Interval = 80 };
            flashTimer.Tick += FlashTimer_Tick;

            this.Size = new Size(400, 32);
        }

        public void SetCostQuantity(decimal costPrice, int quantity)
        {
            _costPrice = costPrice;
            _quantity = quantity;
            if (_hasData) UpdateDisplay();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            if (lblClose == null || lblName == null || lblPrice == null || lblChange == null)
                return;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            // 关闭按钮：最右边
            lblClose.Location = new Point(w - 26, (h - 20) / 2);

            // 名称在左边
            lblName.Location = new Point(8, (h - lblName.Height) / 2);

            // 代码在名称右边
            if (!string.IsNullOrEmpty(_stockCode) && lblCode != null)
            {
                lblCode.Location = new Point(lblName.Right + 5, lblName.Top + 1);
                lblCode.Text = _stockCode;
            }

            // 现价（等比例 48%）
            lblPrice.Location = new Point((int)(w * 0.48), (h - lblPrice.Height) / 2);

            // 涨跌（等比例 62%）
            lblChange.Location = new Point((int)(w * 0.62), (h - lblChange.Height) / 2);

            // 总盈亏（等比例 76%）
            lblTotalProfit.Location = new Point((int)(w * 0.76), (h - lblTotalProfit.Height) / 2);

            // 今盈亏（等比例 88%）
            lblDailyProfit.Location = new Point((int)(w * 0.88), (h - lblDailyProfit.Height) / 2);
        }

        /// <summary>
        /// 更新股票数据并刷新显示
        /// </summary>
        public void UpdateStock(StockInfo info)
        {
            // 保存旧状态
            bool wasUp = _hasData && _changeAmount > 0;
            bool wasDown = _hasData && _changeAmount < 0;

            _hasData = info.IsValid;

            if (!info.IsValid)
            {
                _stockCode = info.Code;
                _stockName = "";
                lblName.Text = string.IsNullOrEmpty(info.ErrorMessage) ? info.Code : info.Code;
                lblPrice.Text = "----";
                lblChange.Text = string.IsNullOrEmpty(info.ErrorMessage) ? "获取中..." : info.ErrorMessage;
                lblTotalProfit.Text = "";
                lblDailyProfit.Text = "";
                this.BackColor = FlatBgColor;
                lblPrice.ForeColor = FlatColor;
                lblChange.ForeColor = FlatColor;
                lblName.ForeColor = Color.FromArgb(160, 160, 160);
                return;
            }

            _stockName = info.Name;
            _stockCode = info.Code;
            _price = info.Price;
            _changeAmount = info.ChangeAmount;
            _changePercent = info.ChangePercent;

            bool isUp = _changeAmount > 0;
            bool isDown = _changeAmount < 0;

            if ((wasUp && isDown) || (wasDown && isUp))
            {
                StartFlash();
            }

            UpdateDisplay();
            PerformLayout();
        }

        private void UpdateDisplay()
        {
            if (!_hasData) return;

            bool isUp = _changeAmount > 0;
            bool isDown = _changeAmount < 0;

            // 名称
            lblName.Text = _stockName;
            lblName.ForeColor = Color.FromArgb(50, 50, 50);

            // 价格
            lblPrice.Text = _price.ToString("F2");
            if (isUp) lblPrice.ForeColor = UpColor;
            else if (isDown) lblPrice.ForeColor = DownColor;
            else lblPrice.ForeColor = FlatColor;

            // 涨跌信息
            string sign = isUp ? "+" : "";
            lblChange.Text = string.Format("{0}{1:F2}/{2:F2}%",
                sign, _changeAmount, _changePercent);
            if (isUp) lblChange.ForeColor = UpColor;
            else if (isDown) lblChange.ForeColor = DownColor;
            else lblChange.ForeColor = FlatColor;

            // 总盈亏
            if (_costPrice > 0 && _quantity > 0)
            {
                decimal totalProfit = (_price - _costPrice) * _quantity;
                decimal totalProfitPct = _costPrice > 0 ? (_price - _costPrice) / _costPrice * 100 : 0;
                string totalSign = totalProfit > 0 ? "+" : "";
                lblTotalProfit.Text = string.Format("{0}{1:F2}%/{2:N0}",
                    totalSign, totalProfitPct, totalProfit);
                if (totalProfit > 0) lblTotalProfit.ForeColor = UpColor;
                else if (totalProfit < 0) lblTotalProfit.ForeColor = DownColor;
                else lblTotalProfit.ForeColor = FlatColor;
            }
            else
            {
                lblTotalProfit.Text = "--";
                lblTotalProfit.ForeColor = FlatColor;
            }

            // 今盈亏
            if (_quantity > 0)
            {
                decimal dailyProfit = _changeAmount * _quantity;
                string dailySign = dailyProfit > 0 ? "+" : "";
                lblDailyProfit.Text = string.Format("{0}{1:F2}%/{2:N0}",
                    dailySign, _changePercent, dailyProfit);
                if (dailyProfit > 0) lblDailyProfit.ForeColor = UpColor;
                else if (dailyProfit < 0) lblDailyProfit.ForeColor = DownColor;
                else lblDailyProfit.ForeColor = FlatColor;
            }
            else
            {
                lblDailyProfit.Text = _changePercent.ToString("F2") + "%";
                if (isUp) lblDailyProfit.ForeColor = UpColor;
                else if (isDown) lblDailyProfit.ForeColor = DownColor;
                else lblDailyProfit.ForeColor = FlatColor;
            }

            // 背景色
            if (isUp) this.BackColor = UpBgColor;
            else if (isDown) this.BackColor = DownBgColor;
            else this.BackColor = FlatBgColor;

            // 代码
            lblCode.Text = _stockCode;
        }

        private void StartFlash()
        {
            flashCount = 0;
            flashTimer.Start();
        }

        private void FlashTimer_Tick(object sender, EventArgs e)
        {
            flashCount++;
            if (flashCount >= 4)
            {
                flashTimer.Stop();
                UpdateDisplay();
                return;
            }

            if (flashCount % 2 == 1)
            {
                if (_changeAmount > 0)
                    this.BackColor = Color.FromArgb(255, 230, 230);
                else if (_changeAmount < 0)
                    this.BackColor = Color.FromArgb(220, 252, 230);
                else
                    this.BackColor = Color.FromArgb(240, 240, 240);
            }
            else
            {
                UpdateDisplay();
            }
        }

        /// <summary>
        /// 绘制圆角边框
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // 绘制底部细线
            using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
                e.Graphics.DrawLine(pen, 12, this.ClientSize.Height - 1,
                    this.ClientSize.Width - 12, this.ClientSize.Height - 1);
            }

            // 圆角边框
            using (var pen = new Pen(Color.FromArgb(225, 225, 225), 1))
            {
                var rect = new Rectangle(1, 1, this.ClientSize.Width - 3, this.ClientSize.Height - 3);
                e.Graphics.DrawRectangle(pen, rect);
            }
        }
    }
}
