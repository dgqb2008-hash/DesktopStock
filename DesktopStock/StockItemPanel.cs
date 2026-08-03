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
        private static readonly Color UpColor = Color.FromArgb(220, 38, 38);       // 涨-红色
        private static readonly Color DownColor = Color.FromArgb(22, 163, 74);      // 跌-绿色
        private static readonly Color FlatColor = Color.FromArgb(120, 120, 120);    // 平-灰色
        private static readonly Color UpBgColor = Color.FromArgb(255, 245, 245);    // 涨-浅红底
        private static readonly Color DownBgColor = Color.FromArgb(240, 253, 244);  // 跌-浅绿底
        private static readonly Color FlatBgColor = Color.FromArgb(248, 248, 248);  // 平-浅灰底
        private static readonly Color NormalBgColor = Color.White;

        private Label lblName;
        private Label lblCode;
        private Label lblPrice;
        private Label lblChange;
        private Label lblClose;
        private Timer flashTimer;
        private int flashCount;

        // 当前数据
        private string _stockName = "----";
        private string _stockCode = "";
        private decimal _price;
        private decimal _changeAmount;
        private decimal _changePercent;
        private bool _hasData;

        public event EventHandler DeleteRequested;
        public event EventHandler DoubleClickRequested;

        public string StockCode
        {
            get { return _stockCode; }
        }

        public string StockName
        {
            get { return _stockName; }
        }

        public StockItemPanel()
        {
            InitializeComponent();
        }

        public StockItemPanel(string code)
        {
            _stockCode = (code ?? "").Trim();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Margin = new Padding(0, 0, 0, 0);
            this.Padding = new Padding(6, 1, 6, 1);
            this.BackColor = NormalBgColor;
            this.Cursor = Cursors.Hand;
            this.DoubleClick += (s, e) =>
            {
                if (DoubleClickRequested != null)
                    DoubleClickRequested(this, EventArgs.Empty);
            };

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

            this.Controls.Add(lblClose);
            this.Controls.Add(lblName);
            this.Controls.Add(lblCode);
            this.Controls.Add(lblPrice);
            this.Controls.Add(lblChange);

            // 点击面板也触发闪烁动画
            this.Click += (s, e) => StartFlash();
            lblName.Click += (s, e) => StartFlash();
            lblCode.Click += (s, e) => StartFlash();
            lblPrice.Click += (s, e) => StartFlash();
            lblChange.Click += (s, e) => StartFlash();

            // 子控件双击也要冒泡到面板（WinForms默认Label不冒泡DoubleClick）
            EventHandler dblBubble = (s, e) =>
            {
                if (DoubleClickRequested != null)
                    DoubleClickRequested(this, EventArgs.Empty);
            };
            lblName.DoubleClick += dblBubble;
            lblCode.DoubleClick += dblBubble;
            lblPrice.DoubleClick += dblBubble;
            lblChange.DoubleClick += dblBubble;

            // 闪动定时器
            flashTimer = new Timer { Interval = 80 };
            flashTimer.Tick += FlashTimer_Tick;

            this.Size = new Size(300, 26);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            if (lblClose == null || lblName == null || lblPrice == null || lblChange == null)
                return;

            int w = this.ClientSize.Width;
            int yMid = (this.ClientSize.Height - lblPrice.Height) / 2;

            // 关闭按钮：最右边
            lblClose.Location = new Point(w - 26, (this.ClientSize.Height - 20) / 2);

            // 名称在左边
            lblName.Location = new Point(8, yMid);

            // 代码在名称右边
            if (!string.IsNullOrEmpty(_stockCode) && lblCode != null)
            {
                lblCode.Location = new Point(lblName.Right + 5, lblName.Top + 1);
                lblCode.Text = _stockCode;
            }

            // 价格靠右（关闭按钮左边）
            lblPrice.Location = new Point(lblClose.Left - lblPrice.Width - 8, yMid);

            // 涨跌在价格左边
            lblChange.Location = new Point(lblPrice.Left - lblChange.Width - 8, yMid + 1);
        }

        /// <summary>
        /// 更新股票数据并刷新显示
        /// </summary>
        public void UpdateStock(StockInfo info)
        {
            // 保存旧状态（必须在覆盖前读取）
            bool wasUp = _hasData && _changeAmount > 0;
            bool wasDown = _hasData && _changeAmount < 0;

            // 写入新数据
            _hasData = info.IsValid;

            if (!info.IsValid)
            {
                _stockCode = info.Code;
                _stockName = "";
                lblName.Text = string.IsNullOrEmpty(info.ErrorMessage) ? info.Code : info.Code;
                lblPrice.Text = "----";
                // 显示具体错误信息，否则显示"获取中..."
                lblChange.Text = string.IsNullOrEmpty(info.ErrorMessage) ? "获取中..." : info.ErrorMessage;
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

            // 价格由跌转涨或由涨转跌时闪动
            if ((wasUp && isDown) || (wasDown && isUp))
            {
                StartFlash();
            }

            UpdateDisplay();

            // 布局更新
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
            lblPrice.Text = _price.ToString("F3");
            if (isUp) lblPrice.ForeColor = UpColor;
            else if (isDown) lblPrice.ForeColor = DownColor;
            else lblPrice.ForeColor = FlatColor;

            // 涨跌信息
            string sign = isUp ? "+" : "";
            lblChange.Text = string.Format("{0}{1:F3}  {2}{3:F3}%",
                sign, _changeAmount, sign, _changePercent);
            if (isUp) lblChange.ForeColor = UpColor;
            else if (isDown) lblChange.ForeColor = DownColor;
            else lblChange.ForeColor = FlatColor;

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
                // 恢复原来背景
                UpdateDisplay();
                return;
            }

            // 交替闪烁
            if (flashCount % 2 == 1)
            {
                // 闪亮色
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
