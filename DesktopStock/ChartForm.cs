using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 分时走势图窗体（GDI+自绘）
    /// </summary>
    public class ChartForm : Form
    {
        private TrendData _data;
        private List<TrendPoint> _points;

        // 分时图区域（上午/下午）
        private List<TrendPoint> _morningPoints = new List<TrendPoint>();
        private List<TrendPoint> _afternoonPoints = new List<TrendPoint>();

        // 图表边距
        private int _marginLeft = 8;
        private int _marginRight = 52;
        private int _marginTop = 48;
        private int _marginBottom = 30;

        // 字体
        private Font _titleFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);
        private Font _subFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);
        private Font _priceFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);
        private Font _axisFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);

        // 价格范围
        private decimal _priceMax, _priceMin, _prevClose;

        // 绘图区域
        private Rectangle _chartRect;

        // 十字光标
        private Point? _mousePos;
        private int _crossIdx = -1;
        private bool _showCross;

        public ChartForm(TrendData data)
        {
            _data = data;
            _points = data.Points ?? new List<TrendPoint>();
            _prevClose = data.PrevClose;

            // 分离上午/下午
            foreach (var p in _points)
            {
                if (p.Time.Hour < 12)
                    _morningPoints.Add(p);
                else
                    _afternoonPoints.Add(p);
            }

            SetupForm();
            CalculateRanges();
        }

        private void SetupForm()
        {
            this.Text = (_data.Name ?? "") + " " + _data.Code + " - 分时走势";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(260, 260);
            this.Size = new Size(340, 300);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Font = new Font("Microsoft YaHei", 8);

            this.Paint += ChartForm_Paint;
            this.MouseMove += ChartForm_MouseMove;
            this.MouseLeave += ChartForm_MouseLeave;
            this.Resize += (s, e) => { CalculateRanges(); this.Invalidate(); };
        }

        private void ChartForm_MouseMove(object sender, MouseEventArgs e)
        {
            _mousePos = e.Location;
            _showCross = true;

            // 判断在哪个区域
            if (_chartRect.Contains(e.Location) && _points.Count > 0)
            {
                CalcCrossIndex(e.Location);
            }
            else
            {
                _crossIdx = -1;
            }
            this.Invalidate();
        }

        private void ChartForm_MouseLeave(object sender, EventArgs e)
        {
            _showCross = false;
            _crossIdx = -1;
            this.Invalidate();
        }

        private void CalcCrossIndex(Point mousePt)
        {
            int totalCount = _points.Count;
            if (totalCount == 0) return;

            int chartW = _chartRect.Width;
            int x = mousePt.X - _chartRect.X;

            // 上午/下午各占一半宽度
            int halfW = chartW / 2;
            int idx;
            if (x <= halfW && _morningPoints.Count > 0)
            {
                idx = (int)((float)x / halfW * _morningPoints.Count);
                if (idx >= _morningPoints.Count) idx = _morningPoints.Count - 1;
                if (idx < 0) idx = 0;
                var pt = _morningPoints[idx];
                _crossIdx = _points.IndexOf(pt);
            }
            else if (_afternoonPoints.Count > 0)
            {
                int x2 = x - halfW;
                // 下午实际占 halfW (扣除中午休息视觉)
                idx = (int)((float)x2 / halfW * _afternoonPoints.Count);
                if (idx >= _afternoonPoints.Count) idx = _afternoonPoints.Count - 1;
                if (idx < 0) idx = 0;
                var pt = _afternoonPoints[idx];
                _crossIdx = _points.IndexOf(pt);
            }
        }

        private void CalculateRanges()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            _chartRect = new Rectangle(
                _marginLeft,
                _marginTop,
                w - _marginLeft - _marginRight,
                h - _marginTop - _marginBottom);

            if (_points.Count == 0) return;

            // 价格范围：包含昨收
            decimal maxP = _prevClose;
            decimal minP = _prevClose;
            foreach (var p in _points)
            {
                if (p.Price > maxP) maxP = p.Price;
                if (p.Price < minP) minP = p.Price;
            }
            // 上下留一点 margin
            decimal pad = (maxP - minP) * 0.1m;
            if (pad == 0) pad = _prevClose * 0.01m;
            _priceMax = maxP + pad;
            _priceMin = minP - pad;

            // 昨收参考线不能超出范围
            if (_prevClose < _priceMin) _priceMin = _prevClose - pad;
            if (_prevClose > _priceMax) _priceMax = _prevClose + pad;
        }

        #region 绘制

        private void ChartForm_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_points.Count == 0)
            {
                DrawEmptyState(g);
                return;
            }

            DrawHeader(g);
            DrawChartGrid(g);
            DrawPrevCloseLine(g);
            DrawAvgPriceLine(g);
            DrawPriceLine(g);
            DrawAxisLabels(g);
            if (_showCross && _crossIdx >= 0)
                DrawCrossHair(g);
        }

        private void DrawEmptyState(Graphics g)
        {
            string msg = _data.ErrorMessage ?? "暂无走势数据";
            var sz = g.MeasureString(msg, _titleFont);
            g.DrawString(msg, _titleFont, Brushes.Gray,
                (this.ClientSize.Width - sz.Width) / 2,
                (this.ClientSize.Height - sz.Height) / 2);
        }

        private void DrawHeader(Graphics g)
        {
            string title = (_data.Name ?? "") + "  " + _data.Code + "  分时走势";
            g.DrawString(title, _titleFont, new SolidBrush(Color.FromArgb(40, 40, 40)), 12, 10);

            // 价格和涨跌
            if (_points.Count > 0)
            {
                var last = _points[_points.Count - 1];
                decimal change = last.Price - _prevClose;
                decimal pct = _prevClose > 0 ? (change / _prevClose) * 100 : 0;
                Color c = change >= 0 ? Color.FromArgb(220, 38, 38) : Color.FromArgb(26, 155, 82);

                string priceStr = last.Price.ToString("F3");
                string changeStr = string.Format("{0}{1:F3}  {2}{3:F2}%",
                    change >= 0 ? "+" : "", change, change >= 0 ? "+" : "", pct);

                var ps = g.MeasureString(priceStr, _titleFont);
                g.DrawString(priceStr, _titleFont, new SolidBrush(c), 12, 28);

                g.DrawString(changeStr, _subFont, new SolidBrush(c),
                    14 + ps.Width, 34);

                string prevStr = "昨收 " + _prevClose.ToString("F3");
                g.DrawString(prevStr, _subFont, new SolidBrush(Color.FromArgb(140, 140, 140)),
                    this.ClientSize.Width - _marginRight - 2, 12);
            }
        }

        private void DrawChartGrid(Graphics g)
        {
            var rect = _chartRect;
            using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
                // 水平线（4条）
                int horzCount = 4;
                for (int i = 0; i <= horzCount; i++)
                {
                    int y = rect.Top + rect.Height * i / horzCount;
                    if (i == 0 || i == horzCount)
                    {
                        using (var pGrid = new Pen(Color.FromArgb(210, 210, 210)))
                            g.DrawLine(pGrid, rect.Left, y, rect.Right, y);
                    }
                    else
                    {
                        pen.DashStyle = DashStyle.Dot;
                        g.DrawLine(pen, rect.Left, y, rect.Right, y);
                        pen.DashStyle = DashStyle.Solid;
                    }
                }

                // 竖线：上午/下午分割
                int midX = rect.Left + rect.Width / 2;
                g.DrawLine(pen, midX, rect.Top, midX, rect.Bottom);
            }

            // 背景色：上午白色，下午微蓝
            int halfW = rect.Width / 2;
            using (var brush = new SolidBrush(Color.FromArgb(248, 250, 255)))
            {
                g.FillRectangle(brush, rect.Left + halfW, rect.Top, halfW, rect.Height);
            }
        }

        private void DrawPrevCloseLine(Graphics g)
        {
            var rect = _chartRect;
            int y = (int)PriceToY(_prevClose);
            if (y < rect.Top) y = rect.Top;
            if (y > rect.Bottom) y = rect.Bottom;

            // 虚线
            using (var pen = new Pen(Color.FromArgb(150, 150, 150), 1))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, rect.Left, y, rect.Right, y);
            }

            // 昨收标签
            string label = _prevClose.ToString("F2");
            var sz = g.MeasureString(label, _axisFont);
            g.DrawString(label, _axisFont, new SolidBrush(Color.FromArgb(150, 150, 150)),
                rect.Right + 2, y - sz.Height / 2);
        }

        private void DrawPriceLine(Graphics g)
        {
            var rect = _chartRect;
            int halfW = rect.Width / 2;

            // 上午线
            if (_morningPoints.Count > 1)
            {
                DrawSmoothLine(g, _morningPoints, rect.Left, halfW, GetPriceColor());
            }
            // 下午线
            if (_afternoonPoints.Count > 1)
            {
                DrawSmoothLine(g, _afternoonPoints, rect.Left + halfW, halfW, GetPriceColor());
            }
        }

        private void DrawAvgPriceLine(Graphics g)
        {
            var rect = _chartRect;
            int halfW = rect.Width / 2;
            var pen = new Pen(Color.FromArgb(255, 170, 50), 1);

            // 上午均价
            if (_morningPoints.Count > 1)
            {
                DrawAvgLine(g, _morningPoints, rect.Left, halfW, pen);
            }
            // 下午均价
            if (_afternoonPoints.Count > 1)
            {
                DrawAvgLine(g, _afternoonPoints, rect.Left + halfW, halfW, pen);
            }
            pen.Dispose();
        }

        private void DrawSmoothLine(Graphics g, List<TrendPoint> pts, int startX, int width, Color color)
        {
            if (pts.Count < 2) return;
            using (var pen = new Pen(color, 1.2f))
            {
                var path = new GraphicsPath();
                float stepX = (float)width / pts.Count;
                int stepCount = Math.Max(pts.Count - 1, 1);

                for (int i = 0; i < pts.Count; i++)
                {
                    float x = startX + stepX * i;
                    float y = PriceToY(pts[i].Price);
                    if (i == 0) path.StartFigure();
                    else path.AddLine(startX + stepX * (i - 1), PriceToY(pts[i - 1].Price), x, y);
                    // 终点小圆点
                    if (i == pts.Count - 1)
                    {
                        g.FillEllipse(new SolidBrush(color), x - 2, y - 2, 4, 4);
                        g.FillEllipse(Brushes.White, x - 1, y - 1, 2, 2);
                    }
                }
                g.DrawPath(pen, path);
                path.Dispose();
            }
        }

        private void DrawAvgLine(Graphics g, List<TrendPoint> pts, int startX, int width, Pen pen)
        {
            if (pts.Count < 2) return;
            float stepX = (float)width / pts.Count;
            for (int i = 1; i < pts.Count; i++)
            {
                float x1 = startX + stepX * (i - 1);
                float y1 = PriceToY(pts[i - 1].AvgPrice);
                float x2 = startX + stepX * i;
                float y2 = PriceToY(pts[i].AvgPrice);
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        private void DrawAxisLabels(Graphics g)
        {
            var rect = _chartRect;

            // 价格轴（右侧）
            int horzCount = 4;
            for (int i = 0; i <= horzCount; i++)
            {
                int y = rect.Top + rect.Height * i / horzCount;
                decimal price = _priceMax - (_priceMax - _priceMin) * i / horzCount;
                string label = price.ToString("F2");
                var sz = g.MeasureString(label, _axisFont);
                g.DrawString(label, _axisFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                    rect.Right + 2, y - sz.Height / 2);
            }

            // 时间轴
            DrawTimeLabel(g, "09:30", rect.Left);
            DrawTimeLabelCenter(g, "11:30/13:00", rect.Left + rect.Width / 2);
            DrawTimeLabel(g, "15:00", rect.Right);
        }

        private void DrawTimeLabel(Graphics g, string text, int x)
        {
            var sz = g.MeasureString(text, _axisFont);
            int y = _chartRect.Bottom + 4;
            g.DrawString(text, _axisFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                x - (text == "15:00" ? (int)sz.Width : 0), y);
        }

        private void DrawTimeLabelCenter(Graphics g, string text, int x)
        {
            var sz = g.MeasureString(text, _axisFont);
            int y = _chartRect.Bottom + 4;
            g.DrawString(text, _axisFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                x - sz.Width / 2, y);
        }

        private void DrawCrossHair(Graphics g)
        {
            var rect = _chartRect;
            if (_crossIdx < 0 || _crossIdx >= _points.Count) return;

            var pt = _points[_crossIdx];

            // 找到该点的X位置
            int halfW = rect.Width / 2;
            int x;
            if (pt.Time.Hour < 12 && _morningPoints.Count > 0)
            {
                int idx = _morningPoints.IndexOf(pt);
                if (idx < 0) return;
                x = rect.Left + (int)((float)idx / _morningPoints.Count * halfW);
            }
            else if (_afternoonPoints.Count > 0)
            {
                int idx = _afternoonPoints.IndexOf(pt);
                if (idx < 0) return;
                x = rect.Left + halfW + (int)((float)idx / _afternoonPoints.Count * halfW);
            }
            else return;

            int y = (int)PriceToY(pt.Price);

            // 十字线
            using (var pen = new Pen(Color.FromArgb(100, 100, 100), 1))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawLine(pen, rect.Left, y, rect.Right, y);
                g.DrawLine(pen, x, rect.Top, x, rect.Bottom);
            }

            // 提示框
            string info = string.Format("{0:HH:mm}  {1:F3}  {2}{3:F3}%",
                pt.Time, pt.Price,
                pt.Price >= _prevClose ? "+" : "",
                _prevClose > 0 ? (pt.Price - _prevClose) / _prevClose * 100 : 0);

            var sz = g.MeasureString(info, _axisFont);
            int boxX = x + 8;
            if (boxX + sz.Width + 8 > rect.Right) boxX = x - (int)sz.Width - 16;

            using (var brush = new SolidBrush(Color.FromArgb(220, 40, 40, 40)))
            using (var fb = new SolidBrush(Color.White))
            {
                var r = new Rectangle(boxX, y - 14, (int)sz.Width + 8, (int)sz.Height + 4);
                g.FillRectangle(brush, r);
                g.DrawString(info, _axisFont, fb, boxX + 4, y - 12);
            }
        }

        #endregion

        private float PriceToY(decimal price)
        {
            var rect = _chartRect;
            decimal ratio = (_priceMax - price) / (_priceMax - _priceMin);
            return rect.Top + (float)ratio * rect.Height;
        }

        private Color GetPriceColor()
        {
            if (_points.Count == 0) return Color.Black;
            var last = _points[_points.Count - 1];
            if (last.Price > _prevClose) return Color.FromArgb(220, 38, 38);
            if (last.Price < _prevClose) return Color.FromArgb(26, 155, 82);
            return Color.FromArgb(100, 100, 100);
        }
    }
}
