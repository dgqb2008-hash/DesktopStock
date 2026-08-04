using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 悬浮球控件：
    /// - 正圆，背景纯白，无边框色
    /// - 尺寸根据内容自动调整（保证完全显示，不溢出）
    /// - 上半：总盈金额；下半：今盈金额
    /// - 数字前加 "总盈"/"今盈" 文字
    /// - 透明度跟随主窗口
    /// - 文字不加粗，固定 8pt
    /// </summary>
    public class FloatingBall : Form
    {
        // 盈亏数值
        public decimal TotalProfit { get; set; } = 0;
        public decimal DailyProfit { get; set; } = 0;

        // 拖动支持
        private bool dragging = false;
        private Point dragStart;

        /// <summary>
        /// 双击悬浮球时触发，由 MainForm 订阅以打开主窗口
        /// </summary>
        public event EventHandler OpenMainWindowRequested;

        // 内部边距（与圆边距离）
        private const int PADDING = 12;

        public FloatingBall()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;

            // 禁用自动缩放
            this.AutoScaleMode = AutoScaleMode.None;
            this.AutoSize = false;
            this.AutoSizeMode = AutoSizeMode.GrowOnly;

            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 120, 200);
            this.DoubleBuffered = true;
            this.BackColor = Color.White;

            // 设置初始最小尺寸为 72×72，避免显示默认的 200×100
            this.Size = new Size(72, 72);
            this.MinimumSize = new Size(72, 72);
            UpdateCircularRegion();

            this.MouseDown += FloatingBall_MouseDown;
            this.MouseMove += FloatingBall_MouseMove;
            this.MouseUp += FloatingBall_MouseUp;
            this.MouseDoubleClick += FloatingBall_MouseDoubleClick;
        }

        /// <summary>
        /// 计算需要的正圆直径（基于最宽的文本 + 上下两行 + 边距）
        /// </summary>
        private int CalculateDiameter()
        {
            using (var g = this.CreateGraphics())
            using (var font = new Font("Microsoft YaHei", 8f, FontStyle.Regular))
            {
                string totalText = "总盈 " + FormatMoney(TotalProfit);
                string dailyText = "今盈 " + FormatMoney(DailyProfit);
                var sizeTotal = g.MeasureString(totalText, font);
                var sizeDaily = g.MeasureString(dailyText, font);

                // 文本最大宽度
                float maxTextWidth = Math.Max(sizeTotal.Width, sizeDaily.Width);

                // 上下两行所需高度（含中间分隔）
                float textHeight = sizeTotal.Height + sizeDaily.Height + 4;

                // 圆内可用宽度 = 直径 - 2*边距（圆形可视为内接正方形）
                // 因此 直径 >= (maxTextWidth / sqrt(2)) + 2*边距
                // 直径 >= textHeight + 2*边距
                float byWidth = (float)(maxTextWidth / Math.Sqrt(2)) + 2 * PADDING;
                float byHeight = textHeight + 2 * PADDING;

                // 额外加点缓冲
                int diameter = (int)Math.Ceiling(Math.Max(byWidth, byHeight) + 4);

                // 最小尺寸
                if (diameter < 72) diameter = 72;
                return diameter;
            }
        }

        /// <summary>
        /// 重新计算并应用正圆尺寸
        /// </summary>
        public void AdjustSizeToContent()
        {
            int diameter = CalculateDiameter();
            // 必须重新设置 Size 才能让 Region 生效
            if (this.Width != diameter || this.Height != diameter)
            {
                this.Size = new Size(diameter, diameter);
                UpdateCircularRegion();
                this.Invalidate();
            }
        }

        /// <summary>
        /// 强制保持正方形（拖动时也保持）
        /// </summary>
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if ((specified & BoundsSpecified.Size) == BoundsSpecified.Size)
            {
                int size = Math.Min(width, height);
                if (size <= 0) size = CalculateDiameter();
                if (size < 72) size = 72;
                width = size;
                height = size;
            }
            base.SetBoundsCore(x, y, width, height, specified);
        }

        /// <summary>
        /// 使用 Region 把窗口裁成正圆
        /// </summary>
        private void UpdateCircularRegion()
        {
            using (var path = new GraphicsPath())
            {
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;
                if (w <= 0 || h <= 0)
                {
                    w = this.Width;
                    h = this.Height;
                }
                int d = Math.Min(w, h);
                if (d <= 0) d = 72;
                path.AddEllipse(0, 0, d - 1, d - 1);
                this.Region = new Region(path);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateCircularRegion();
            this.Invalidate();
        }

        private void FloatingBall_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragStart = e.Location;
            }
        }

        private void FloatingBall_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                this.Location = new Point(
                    this.Location.X + e.X - dragStart.X,
                    this.Location.Y + e.Y - dragStart.Y);
            }
        }

        private void FloatingBall_MouseUp(object sender, MouseEventArgs e)
        {
            dragging = false;
        }

        private void FloatingBall_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // 双击打开主窗口（由 MainForm 订阅处理），不再仅隐藏自身
            if (e.Button == MouseButtons.Left)
            {
                OpenMainWindowRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            int cx = w / 2;
            int cy = h / 2;

            // 颜色
            Color totalColor = TotalProfit > 0 ? Color.FromArgb(220, 38, 38)
                            : TotalProfit < 0 ? Color.FromArgb(22, 163, 74)
                            : Color.FromArgb(120, 120, 120);
            Color dailyColor = DailyProfit > 0 ? Color.FromArgb(220, 38, 38)
                            : DailyProfit < 0 ? Color.FromArgb(22, 163, 74)
                            : Color.FromArgb(120, 120, 120);

            string totalText = "总盈 " + FormatMoney(TotalProfit);
            string dailyText = "今盈 " + FormatMoney(DailyProfit);

            using (var font = new Font("Microsoft YaHei", 8f, FontStyle.Regular))
            {
                var sizeTotal = g.MeasureString(totalText, font);
                var sizeDaily = g.MeasureString(dailyText, font);

                // 上半：总盈
                g.DrawString(totalText, font, new SolidBrush(totalColor),
                    cx - sizeTotal.Width / 2, cy - sizeTotal.Height - 1);

                // 中间分隔线
                using (var pen = new Pen(Color.FromArgb(220, 220, 220), 1))
                {
                    float lineHalf = Math.Min(w, h) * 0.28f;
                    g.DrawLine(pen, cx - lineHalf, cy, cx + lineHalf, cy);
                }

                // 下半：今盈
                g.DrawString(dailyText, font, new SolidBrush(dailyColor),
                    cx - sizeDaily.Width / 2, cy + 1);
            }
        }

        /// <summary>
        /// 格式化金额：保留两位小数 + 千分位
        /// </summary>
        private string FormatMoney(decimal v)
        {
            string sign = v >= 0 ? "" : "-";
            return sign + Math.Abs(v).ToString("N2");
        }

        public void UpdateValues(decimal totalProfit, decimal dailyProfit)
        {
            TotalProfit = totalProfit;
            DailyProfit = dailyProfit;
            // 自适应尺寸
            AdjustSizeToContent();
        }

        /// <summary>
        /// 直接设置值并重置尺寸（不触发绘制，用于显示前）
        /// </summary>
        public void SetValuesAndResize(decimal totalProfit, decimal dailyProfit)
        {
            TotalProfit = totalProfit;
            DailyProfit = dailyProfit;

            int diameter = CalculateDiameter();
            if (this.Width != diameter || this.Height != diameter)
            {
                this.Size = new Size(diameter, diameter);
                UpdateCircularRegion();
            }
        }
    }
}
