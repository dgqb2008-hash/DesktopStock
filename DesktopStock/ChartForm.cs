using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DesktopStock
{
    /// <summary>
    /// 图表窗体（分时走势 + K线图，GDI+自绘）
    /// </summary>
    public class ChartForm : Form
    {
        // 分时数据
        private TrendData _trendData;
        private List<TrendPoint> _points = new List<TrendPoint>();
        private List<TrendPoint> _morningPoints = new List<TrendPoint>();
        private List<TrendPoint> _afternoonPoints = new List<TrendPoint>();

        // K线数据
        private KLineData _klineData;
        private List<KLinePoint> _klinePoints = new List<KLinePoint>();
        private KLinePeriod _klinePeriod = KLinePeriod.Daily;

        // MA均线
        private decimal[] _ma5, _ma10, _ma20, _ma60;

        // 图表模式
        private enum ChartMode { Trend, KLine }
        private ChartMode _mode = ChartMode.Trend;

        // 图表区域
        private Rectangle _chartRect;
        private Rectangle _volumeRect;
        private Rectangle _priceAxisRect;

        // 分时图边距
        private int _marginLeft = 8;
        private int _marginRight = 52;
        private int _marginTop = 48;
        private int _marginBottom = 30;

        // K线图边距
        private int _klineMarginLeft = 50;
        private int _klineMarginRight = 55;
        private int _klineMarginTop = 55;
        private int _klineMarginBottom = 25;
        private int _volumeHeightRatio = 4;

        // 字体
        private Font _titleFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);
        private Font _subFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);
        private Font _priceFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);
        private Font _axisFont = new Font("Microsoft YaHei", 8, FontStyle.Regular);
        private Font _smallFont = new Font("Microsoft YaHei", 7, FontStyle.Regular);

        // 价格范围
        private decimal _priceMax, _priceMin, _prevClose;
        private decimal _klinePriceMax, _klinePriceMin;
        private decimal _volumeMax;

        // 十字光标
        private Point? _mousePos;
        private int _crossIdx = -1;
        private bool _showCross;

        // 控件
        private RadioButton _rbTrend;
        private RadioButton _rbKLine;
        private ComboBox _cmbPeriod;
        // 周期选择器右侧的状态提示（如"加载中…"/"加载失败"）
        private Label _lblPeriodStatus;

        // 当前加载任务的"代次"标记，用于丢弃过期回调，避免重入时崩溃
        private int _klineLoadGeneration = 0;

        public ChartForm(TrendData data)
        {
            _trendData = data;
            _points = data.Points ?? new List<TrendPoint>();
            _prevClose = data.PrevClose;

            foreach (var p in _points)
            {
                if (p.Time.Hour < 12)
                    _morningPoints.Add(p);
                else
                    _afternoonPoints.Add(p);
            }

            _klineData = new KLineData { Code = data.Code, Name = data.Name, IsValid = false };
            _mode = ChartMode.Trend;

            SetupForm();
            CalculateRanges();

            // 后台自动加载K线，加载成功后自动切换到K线视图
            this.Shown += (s, e) => LoadKLineData();
        }

        /// <summary>
        /// 仅传代码与名称的"懒加载"构造：窗口立即以分时模式显示（分时数据秒回，体验流畅），
        /// 后台并行拉取K线；K线到达后自动切到K线视图。
        /// </summary>
        public ChartForm(string code, string name)
        {
            _klineData = new KLineData { Code = code, Name = name, IsValid = false };
            _trendData = new TrendData { Code = code, Name = name, IsValid = false };
            _mode = ChartMode.Trend;

            SetupForm();
            CalculateRanges();
            // 初始窗口打开时：分时模式，K线选择器后面显示"加载中…"
            SetPeriodStatus("加载中…");

            // 窗口显示后立即后台加载
            this.Shown += (s, e) => LoadAllInBackground();
        }

        private void LoadAllInBackground()
        {
            string code = _klineData?.Code ?? _trendData?.Code;
            string name = _klineData?.Name ?? _trendData?.Name ?? code;
            if (string.IsNullOrEmpty(code)) return;

            int currentGen = System.Threading.Interlocked.Increment(ref _klineLoadGeneration);

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                // 先拉分时（让分时图尽快显示），分时返回后再异步拉K线
                TrendData trendData = null;
                try { trendData = StockService.FetchTrendSync(code, name); } catch { }

                if (currentGen != _klineLoadGeneration) return;
                if (IsDisposed || !IsHandleCreated) return;

                // 分时数据先填进去，立即刷新分时图
                try
                {
                    this.Invoke((Action)(() =>
                    {
                        if (currentGen != _klineLoadGeneration) return;

                        if (trendData != null && trendData.IsValid)
                        {
                            _trendData = trendData;
                            _points = trendData.Points ?? new List<TrendPoint>();
                            _prevClose = trendData.PrevClose;
                            _morningPoints.Clear();
                            _afternoonPoints.Clear();
                            foreach (var p in _points)
                            {
                                if (p.Time.Hour < 12) _morningPoints.Add(p);
                                else _afternoonPoints.Add(p);
                            }
                            // 保持分时模式（用户双击后第一眼就是分时）
                            if (_mode != ChartMode.Trend) SwitchMode(ChartMode.Trend);
                            else { CalculateRanges(); this.Invalidate(); }
                            this.Text = (trendData.Name ?? name) + " " + code + " - 分时走势";
                            SetPeriodStatus(""); // 分时显示时清空K线状态
                        }
                        else
                        {
                            this.Text = (name) + " " + code + " - 分时数据加载失败";
                        }
                    }));
                }
                catch { }

                // 后台再拉K线（不切视图，只更新数据；用户手动切K线单选按钮时直接使用）
                KLineData klineData = null;
                string klineErr = null;
                try { klineData = StockService.FetchKLineSync(code, name, KLinePeriod.Daily, 120); }
                catch (Exception ex) { klineErr = ex.Message; }

                if (currentGen != _klineLoadGeneration) return;
                if (IsDisposed || !IsHandleCreated) return;

                try
                {
                    this.Invoke((Action)(() =>
                    {
                        if (currentGen != _klineLoadGeneration) return;

                        if (klineData != null && klineData.IsValid && klineData.Points.Count > 0)
                        {
                            // 仅更新数据，不切视图
                            _klineData = klineData;
                            _klinePoints = klineData.Points;
                            CalculateMA();
                            // 当前在K线模式时刷新；分时模式不打扰用户
                            if (_mode == ChartMode.KLine) this.Invalidate();
                        }
                        else if (!string.IsNullOrEmpty(klineErr) ||
                                 (klineData != null && !klineData.IsValid && !string.IsNullOrEmpty(klineData.ErrorMessage)))
                        {
                            string msg = klineErr ?? klineData.ErrorMessage;
                            this.BeginInvoke((Action)(() =>
                                MessageBox.Show(this, "K线后台加载失败: " + msg,
                                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                        }
                    }));
                }
                catch { }
            });
        }

        public ChartForm(KLineData klineData, TrendData trendData = null)
        {
            _klineData = klineData;
            _klinePoints = klineData?.Points ?? new List<KLinePoint>();
            _prevClose = _klinePoints.Count > 0 ? _klinePoints[_klinePoints.Count - 1].Close : 0;

            _trendData = trendData;
            if (trendData != null)
            {
                _points = trendData.Points ?? new List<TrendPoint>();
                _prevClose = trendData.PrevClose;

                foreach (var p in _points)
                {
                    if (p.Time.Hour < 12)
                        _morningPoints.Add(p);
                    else
                        _afternoonPoints.Add(p);
                }
            }

            // 模式选择：K线数据有效 → K线模式；否则分时模式（后台再尝试加载K线）
            _mode = (_klineData != null && _klineData.IsValid && _klinePoints.Count > 0)
                ? ChartMode.KLine
                : ChartMode.Trend;

            SetupForm();
            CalculateRanges();
            CalculateMA();

            // 如果K线数据无效，窗口显示出来后自动后台加载K线，加载成功自动切到K线
            if (_mode == ChartMode.Trend && _klineData != null && !_klineData.IsValid)
            {
                this.Shown += (s, e) => LoadKLineData();
            }
            // 如果根本没传K线数据，也尝试加载
            else if (_mode == ChartMode.Trend && _klineData == null && _trendData != null)
            {
                _klineData = new KLineData { Code = _trendData.Code, Name = _trendData.Name, IsValid = false };
                this.Shown += (s, e) => LoadKLineData();
            }
        }

        private void SetupForm()
        {
            string title;
            if (_klineData != null)
                title = (_klineData.Name ?? "") + " " + _klineData.Code + " - K线图";
            else
                title = (_trendData?.Name ?? "") + " " + (_trendData?.Code ?? "") + " - 分时走势";

            this.Text = title;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(480, 360);
            this.Size = new Size(900, 560);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.DoubleBuffered = true;
            this.BackColor = Color.White;
            this.Font = new Font("Microsoft YaHei", 8);

            // 切换控件
            _rbTrend = new RadioButton
            {
                Text = "分时",
                Location = new Point(12, 5),
                AutoSize = true,
                Checked = _mode == ChartMode.Trend,
                Font = new Font("Microsoft YaHei", 8)
            };
            _rbTrend.CheckedChanged += (s, e) =>
            {
                if (_rbTrend.Checked) SwitchMode(ChartMode.Trend);
            };

            _rbKLine = new RadioButton
            {
                Text = "K线",
                Location = new Point(60, 5),
                AutoSize = true,
                Checked = _mode == ChartMode.KLine,
                Font = new Font("Microsoft YaHei", 8),
                Enabled = _klineData != null || _trendData != null
            };
            _rbKLine.CheckedChanged += (s, e) =>
            {
                if (_rbKLine.Checked) SwitchMode(ChartMode.KLine);
            };

            // 周期选择
            _cmbPeriod = new ComboBox
            {
                Location = new Point(108, 2),
                Width = 75,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft YaHei", 8),
                Enabled = _klineData != null || _trendData != null
            };
            _cmbPeriod.Items.AddRange(new object[] { "日K", "周K", "月K" });
            _cmbPeriod.SelectedIndex = 0;
            _cmbPeriod.SelectedIndexChanged += CmbPeriod_SelectedIndexChanged;

            // 状态标签（紧挨周期选择器右侧），显示 "加载中…" / "加载失败" 等
            _lblPeriodStatus = new Label
            {
                Location = new Point(189, 5),
                AutoSize = true,
                ForeColor = Color.FromArgb(180, 0, 0),
                Font = new Font("Microsoft YaHei", 8),
                Text = ""
            };

            this.Controls.AddRange(new Control[] { _rbTrend, _rbKLine, _cmbPeriod, _lblPeriodStatus });

            this.Paint += ChartForm_Paint;
            this.MouseMove += ChartForm_MouseMove;
            this.MouseLeave += ChartForm_MouseLeave;
            this.Resize += (s, e) => { CalculateRanges(); this.Invalidate(); };
        }

        private void CmbPeriod_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_cmbPeriod.SelectedIndex == 0) _klinePeriod = KLinePeriod.Daily;
            else if (_cmbPeriod.SelectedIndex == 1) _klinePeriod = KLinePeriod.Weekly;
            else _klinePeriod = KLinePeriod.Monthly;

            // 切周期时立即切到K线模式
            string code = _klineData?.Code ?? _trendData?.Code;
            if (string.IsNullOrEmpty(code)) return;

            // 强制切到K线：直接设置单选按钮（会触发 CheckedChanged → SwitchMode）
            // 不论 _mode 当前是什么都能保证视图切到K线
            if (_rbKLine != null && !_rbKLine.Checked)
            {
                _rbKLine.Checked = true;
            }
            else if (_mode != ChartMode.KLine)
            {
                // 单选按钮已经是 KLine 选中但 _mode 没同步的兜底
                SwitchMode(ChartMode.KLine);
            }

            // 状态提示放在K线选择器后面，标题不再带"加载中"
            SetPeriodStatus("加载中…");

            // 后台加载对应周期的K线
            LoadKLineData();
        }

        /// <summary>
        /// 设置K线选择器后面的状态提示文字（如"加载中…"/"加载失败"/空）。
        /// 自动从UI线程或工作线程调用。
        /// </summary>
        private void SetPeriodStatus(string text)
        {
            if (_lblPeriodStatus == null || IsDisposed) return;
            if (this.IsHandleCreated && InvokeRequired)
            {
                try { this.BeginInvoke((Action)(() => _lblPeriodStatus.Text = text ?? "")); }
                catch { }
                return;
            }
            _lblPeriodStatus.Text = text ?? "";
        }

        private void LoadKLineData()
        {
            string code = _klineData?.Code ?? _trendData?.Code;
            string name = _klineData?.Name ?? _trendData?.Name ?? code;
            KLinePeriod period = _klinePeriod;

            // 代次+1，之前的回调全部失效
            int currentGen = System.Threading.Interlocked.Increment(ref _klineLoadGeneration);

            // 状态提示放在K线选择器后面，不在标题栏
            SetPeriodStatus("加载中…");
            SetTitleSafe(name + " " + code + " - K线图");

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                KLineData data = null;
                string errMsg = null;
                try
                {
                    data = StockService.FetchKLineSync(code, name, period, 120);
                }
                catch (Exception ex)
                {
                    errMsg = ex.Message;
                }

                // 回到 UI 线程前先做守护检查：代次失效 或 窗口已销毁则直接丢弃
                if (currentGen != _klineLoadGeneration) return;
                if (IsDisposed || !IsHandleCreated) return;

                try
                {
                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    this.Invoke((Action)(() =>
                    {
                        // 再次校验代次（UI 线程上可能又被新请求覆盖）
                        if (currentGen != _klineLoadGeneration) return;

                        if (data != null && data.IsValid && data.Points.Count > 0)
                        {
                            _klineData = data;
                            _klinePoints = data.Points;
                            // 自动切到K线模式（无论之前是分时还是K线）
                            if (_mode != ChartMode.KLine)
                            {
                                SwitchMode(ChartMode.KLine);
                            }
                            else
                            {
                                CalculateRanges();
                                CalculateMA();
                                this.Invalidate();
                            }
                            this.Text = (data.Name ?? name) + " " + code + " - K线图";
                            SetPeriodStatus(""); // 成功：清空提示
                        }
                        else
                        {
                            string msg = errMsg ?? (data != null ? data.ErrorMessage : null) ?? "未知错误";
                            this.Text = (data?.Name ?? name) + " " + code + " - K线图";
                            SetPeriodStatus("加载失败");
                            this.BeginInvoke((Action)(() =>
                                MessageBox.Show(this, "K线数据加载失败: " + msg, "提示",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information)));
                        }
                    }));
                }
                catch (Exception)
                {
                    // 窗口已关闭/重建导致的异常一律吞掉
                }
            });
        }

        private void SetTitleSafe(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                if (this.InvokeRequired)
                    this.BeginInvoke((Action)(() => { if (!IsDisposed) this.Text = text; }));
                else if (!IsDisposed)
                    this.Text = text;
            }
            catch { }
        }

        private void SwitchMode(ChartMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            _crossIdx = -1;
            _showCross = false;

            // 同步单选按钮状态（程序触发，CheckedChanged 不会再回调形成死循环）
            if (_rbTrend != null) _rbTrend.Checked = (mode == ChartMode.Trend);
            if (_rbKLine != null) _rbKLine.Checked = (mode == ChartMode.KLine);

            CalculateRanges();
            CalculateMA();
            this.Invalidate();
        }

        #region 鼠标事件

        private void ChartForm_MouseMove(object sender, MouseEventArgs e)
        {
            _mousePos = e.Location;
            _showCross = true;

            if (_mode == ChartMode.Trend)
            {
                if (_chartRect.Contains(e.Location) && _points.Count > 0)
                    CalcCrossIndex(e.Location);
                else
                    _crossIdx = -1;
            }
            else
            {
                if (_chartRect.Contains(e.Location) && _klinePoints != null && _klinePoints.Count > 0)
                    CalcKLineCrossIndex(e.Location);
                else
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

        private void CalcKLineCrossIndex(Point mousePt)
        {
            if (_klinePoints == null || _klinePoints.Count == 0) return;

            int x = mousePt.X - _chartRect.Left;
            int barWidth = _chartRect.Width / _klinePoints.Count;
            int idx = x / barWidth;

            if (idx >= 0 && idx < _klinePoints.Count)
                _crossIdx = idx;
            else
                _crossIdx = -1;
        }

        #endregion

        #region 时间/坐标转换

        private DateTime XToMorningTime(float x, int startX, int width)
        {
            const int morningStartMinutes = 9 * 60 + 30;
            const int morningEndMinutes = 11 * 60 + 30;

            float ratio = (x - startX) / width;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            int minutes = morningStartMinutes + (int)(ratio * (morningEndMinutes - morningStartMinutes));
            return DateTime.Today.AddMinutes(minutes);
        }

        private DateTime XToAfternoonTime(float x, int startX, int width)
        {
            const int afternoonStartMinutes = 13 * 60;
            const int afternoonEndMinutes = 15 * 60;

            float ratio = (x - startX) / width;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            int minutes = afternoonStartMinutes + (int)(ratio * (afternoonEndMinutes - afternoonStartMinutes));
            return DateTime.Today.AddMinutes(minutes);
        }

        private int FindNearestPointIndex(List<TrendPoint> pts, DateTime targetTime)
        {
            int nearestIdx = 0;
            long minDiff = long.MaxValue;

            for (int i = 0; i < pts.Count; i++)
            {
                long diff = Math.Abs((pts[i].Time - targetTime).Ticks);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    nearestIdx = i;
                }
            }
            return nearestIdx;
        }

        private void CalcCrossIndex(Point mousePt)
        {
            int totalCount = _points.Count;
            if (totalCount == 0) return;

            int chartW = _chartRect.Width;
            int halfW = chartW / 2;
            int x = mousePt.X - _chartRect.Left;

            if (x <= halfW && _morningPoints.Count > 0)
            {
                DateTime targetTime = XToMorningTime(x, _chartRect.Left, halfW);
                int idx = FindNearestPointIndex(_morningPoints, targetTime);
                var pt = _morningPoints[idx];
                _crossIdx = _points.IndexOf(pt);
            }
            else if (x > halfW && _afternoonPoints.Count > 0)
            {
                int afternoonStartX = _chartRect.Left + halfW;
                int afternoonX = mousePt.X - afternoonStartX;
                DateTime targetTime = XToAfternoonTime(afternoonX, 0, halfW);
                int idx = FindNearestPointIndex(_afternoonPoints, targetTime);
                var pt = _afternoonPoints[idx];
                _crossIdx = _points.IndexOf(pt);
            }
            else
            {
                _crossIdx = -1;
            }
        }

        private float MorningTimeToX(DateTime time, int startX, int width)
        {
            const int morningStartMinutes = 9 * 60 + 30;
            const int morningEndMinutes = 11 * 60 + 30;
            int timeMinutes = time.Hour * 60 + time.Minute;

            if (timeMinutes <= morningStartMinutes) return startX;
            if (timeMinutes >= morningEndMinutes) return startX + width;

            float ratio = (float)(timeMinutes - morningStartMinutes) / (morningEndMinutes - morningStartMinutes);
            return startX + ratio * width;
        }

        private float AfternoonTimeToX(DateTime time, int startX, int width)
        {
            const int afternoonStartMinutes = 13 * 60;
            const int afternoonEndMinutes = 15 * 60;
            int timeMinutes = time.Hour * 60 + time.Minute;

            if (timeMinutes <= afternoonStartMinutes) return startX;
            if (timeMinutes >= afternoonEndMinutes) return startX + width;

            float ratio = (float)(timeMinutes - afternoonStartMinutes) / (afternoonEndMinutes - afternoonStartMinutes);
            return startX + ratio * width;
        }

        #endregion

        #region 计算范围

        private void CalculateRanges()
        {
            if (_mode == ChartMode.Trend)
            {
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;

                // 上下分区：价格区 3/4，成交量区 1/4
                int priceAreaHeight = (h - _marginTop - _marginBottom) * 3 / 4;
                int volumeAreaHeight = (h - _marginTop - _marginBottom) / 4;

                _chartRect = new Rectangle(
                    _marginLeft,
                    _marginTop,
                    w - _marginLeft - _marginRight,
                    priceAreaHeight);

                _volumeRect = new Rectangle(
                    _marginLeft,
                    _chartRect.Bottom + 20, // 留出 20px 给时间标签 + 边距
                    w - _marginLeft - _marginRight,
                    volumeAreaHeight - 20);

                if (_points.Count == 0) return;

                decimal maxP = _prevClose;
                decimal minP = _prevClose;
                foreach (var p in _points)
                {
                    if (p.Price > maxP) maxP = p.Price;
                    if (p.Price < minP) minP = p.Price;
                }
                decimal pad = (maxP - minP) * 0.1m;
                if (pad == 0) pad = _prevClose * 0.01m;
                _priceMax = maxP + pad;
                _priceMin = minP - pad;

                if (_prevClose < _priceMin) _priceMin = _prevClose - pad;
                if (_prevClose > _priceMax) _priceMax = _prevClose + pad;

                // 成交量最大值
                _volumeMax = 0;
                foreach (var p in _points)
                {
                    if (p.Volume > _volumeMax) _volumeMax = p.Volume;
                }
                if (_volumeMax == 0) _volumeMax = 1;
            }
            else
            {
                int w = this.ClientSize.Width;
                int h = this.ClientSize.Height;

                int priceAreaHeight = (h - _klineMarginTop - _klineMarginBottom) * 3 / 4;
                int volumeAreaHeight = (h - _klineMarginTop - _klineMarginBottom) / 4;

                _chartRect = new Rectangle(
                    _klineMarginLeft,
                    _klineMarginTop,
                    w - _klineMarginLeft - _klineMarginRight,
                    priceAreaHeight);

                _volumeRect = new Rectangle(
                    _klineMarginLeft,
                    _chartRect.Bottom + 5,
                    w - _klineMarginLeft - _klineMarginRight,
                    volumeAreaHeight - 5);

                if (_klinePoints == null || _klinePoints.Count == 0) return;

                // 计算K线价格范围
                decimal maxP = decimal.MinValue;
                decimal minP = decimal.MaxValue;
                foreach (var p in _klinePoints)
                {
                    if (p.High > maxP) maxP = p.High;
                    if (p.Low < minP) minP = p.Low;
                }

                // 考虑MA均线范围
                for (int i = 0; i < _klinePoints.Count; i++)
                {
                    if (_ma5 != null && _ma5[i] > 0)
                    {
                        if (_ma5[i] > maxP) maxP = _ma5[i];
                        if (_ma5[i] < minP) minP = _ma5[i];
                    }
                    if (_ma10 != null && _ma10[i] > 0)
                    {
                        if (_ma10[i] > maxP) maxP = _ma10[i];
                        if (_ma10[i] < minP) minP = _ma10[i];
                    }
                    if (_ma20 != null && _ma20[i] > 0)
                    {
                        if (_ma20[i] > maxP) maxP = _ma20[i];
                        if (_ma20[i] < minP) minP = _ma20[i];
                    }
                    if (_ma60 != null && _ma60[i] > 0)
                    {
                        if (_ma60[i] > maxP) maxP = _ma60[i];
                        if (_ma60[i] < minP) minP = _ma60[i];
                    }
                }

                if (maxP == decimal.MinValue) maxP = _klinePoints[_klinePoints.Count - 1].Close;
                if (minP == decimal.MaxValue) minP = _klinePoints[_klinePoints.Count - 1].Close;

                decimal pad = (maxP - minP) * 0.05m;
                if (pad == 0) pad = maxP * 0.01m;
                _klinePriceMax = maxP + pad;
                _klinePriceMin = minP - pad;

                // 计算成交量最大值
                _volumeMax = 0;
                foreach (var p in _klinePoints)
                {
                    if (p.Volume > _volumeMax) _volumeMax = p.Volume;
                }
                if (_volumeMax == 0) _volumeMax = 1;
            }
        }

        private void CalculateMA()
        {
            if (_klinePoints == null || _klinePoints.Count == 0) return;
            _ma5 = StockService.CalculateMA(_klinePoints, 5);
            _ma10 = StockService.CalculateMA(_klinePoints, 10);
            _ma20 = StockService.CalculateMA(_klinePoints, 20);
            _ma60 = StockService.CalculateMA(_klinePoints, 60);
        }

        #endregion

        #region 绘制主入口

        private void ChartForm_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_mode == ChartMode.Trend)
            {
                if (_points.Count == 0)
                {
                    DrawEmptyState(g);
                    return;
                }
                DrawTrendHeader(g);
                DrawChartGrid(g);
                DrawPrevCloseLine(g);
                DrawAvgPriceLine(g);
                DrawPriceLine(g);
                DrawAxisLabels(g);
                DrawTrendVolumeBars(g);
                if (_showCross && _crossIdx >= 0)
                    DrawCrossHair(g);
            }
            else
            {
                if (_klinePoints == null || _klinePoints.Count == 0)
                {
                    DrawEmptyState(g);
                    return;
                }
                DrawKLineHeader(g);
                DrawKLineGrid(g);
                DrawKLines(g);
                DrawMALines(g);
                DrawVolumeBars(g);
                DrawKLineAxisLabels(g);
                if (_showCross && _crossIdx >= 0)
                    DrawKLineCrossHair(g);
            }
        }

        #endregion

        #region 共用绘制

        private void DrawEmptyState(Graphics g)
        {
            string msg = "暂无数据";
            var sz = g.MeasureString(msg, _titleFont);
            g.DrawString(msg, _titleFont, Brushes.Gray,
                (this.ClientSize.Width - sz.Width) / 2,
                (this.ClientSize.Height - sz.Height) / 2);
        }

        #endregion

        #region 分时图绘制

        private void DrawTrendHeader(Graphics g)
        {
            string title = (_trendData?.Name ?? "") + "  " + (_trendData?.Code ?? "") + "  分时走势";
            g.DrawString(title, _titleFont, new SolidBrush(Color.FromArgb(40, 40, 40)), 12, 28);

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
                g.DrawString(priceStr, _titleFont, new SolidBrush(c), 12, 44);
                g.DrawString(changeStr, _subFont, new SolidBrush(c),
                    14 + ps.Width, 50);

                string prevStr = "昨收 " + _prevClose.ToString("F3");
                g.DrawString(prevStr, _subFont, new SolidBrush(Color.FromArgb(140, 140, 140)),
                    this.ClientSize.Width - _marginRight - 2, 28);
            }
        }

        private void DrawChartGrid(Graphics g)
        {
            var rect = _chartRect;
            using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
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

                int midX = rect.Left + rect.Width / 2;
                g.DrawLine(pen, midX, rect.Top, midX, rect.Bottom);
            }

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

            using (var pen = new Pen(Color.FromArgb(150, 150, 150), 1))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, rect.Left, y, rect.Right, y);
            }

            string label = _prevClose.ToString("F2");
            var sz = g.MeasureString(label, _axisFont);
            g.DrawString(label, _axisFont, new SolidBrush(Color.FromArgb(150, 150, 150)),
                rect.Right + 2, y - sz.Height / 2);
        }

        private void DrawPriceLine(Graphics g)
        {
            var rect = _chartRect;
            int halfW = rect.Width / 2;

            if (_morningPoints.Count > 1)
                DrawSmoothLine(g, _morningPoints, rect.Left, halfW, GetPriceColor(), true);
            if (_afternoonPoints.Count > 1)
                DrawSmoothLine(g, _afternoonPoints, rect.Left + halfW, halfW, GetPriceColor(), false);
        }

        private void DrawAvgPriceLine(Graphics g)
        {
            var rect = _chartRect;
            int halfW = rect.Width / 2;
            var pen = new Pen(Color.FromArgb(255, 170, 50), 1);

            if (_morningPoints.Count > 1)
                DrawAvgLine(g, _morningPoints, rect.Left, halfW, pen, true);
            if (_afternoonPoints.Count > 1)
                DrawAvgLine(g, _afternoonPoints, rect.Left + halfW, halfW, pen, false);
            pen.Dispose();
        }

        private void DrawSmoothLine(Graphics g, List<TrendPoint> pts, int startX, int width, Color color, bool isMorning)
        {
            if (pts.Count < 2) return;
            using (var pen = new Pen(color, 1.2f))
            {
                var path = new GraphicsPath();

                for (int i = 0; i < pts.Count; i++)
                {
                    float x = isMorning
                        ? MorningTimeToX(pts[i].Time, startX, width)
                        : AfternoonTimeToX(pts[i].Time, startX, width);
                    float y = PriceToY(pts[i].Price);

                    if (i == 0) path.StartFigure();
                    else
                    {
                        float prevX = isMorning
                            ? MorningTimeToX(pts[i - 1].Time, startX, width)
                            : AfternoonTimeToX(pts[i - 1].Time, startX, width);
                        float prevY = PriceToY(pts[i - 1].Price);
                        path.AddLine(prevX, prevY, x, y);
                    }

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

        private void DrawAvgLine(Graphics g, List<TrendPoint> pts, int startX, int width, Pen pen, bool isMorning)
        {
            if (pts.Count < 2) return;
            for (int i = 1; i < pts.Count; i++)
            {
                float x1 = isMorning
                    ? MorningTimeToX(pts[i - 1].Time, startX, width)
                    : AfternoonTimeToX(pts[i - 1].Time, startX, width);
                float y1 = PriceToY(pts[i - 1].AvgPrice);
                float x2 = isMorning
                    ? MorningTimeToX(pts[i].Time, startX, width)
                    : AfternoonTimeToX(pts[i].Time, startX, width);
                float y2 = PriceToY(pts[i].AvgPrice);
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        private void DrawAxisLabels(Graphics g)
        {
            var rect = _chartRect;

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

            DrawTimeLabel(g, "09:30", rect.Left);
            DrawTimeLabelCenter(g, "11:30/13:00", rect.Left + rect.Width / 2);
            DrawTimeLabel(g, "15:00", rect.Right);
        }

        private void DrawTimeLabel(Graphics g, string text, int x)
        {
            var sz = g.MeasureString(text, _axisFont);
            int y = _chartRect.Bottom + 2;
            g.DrawString(text, _axisFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                x - (text == "15:00" ? (int)sz.Width : 0), y);
        }

        private void DrawTimeLabelCenter(Graphics g, string text, int x)
        {
            var sz = g.MeasureString(text, _axisFont);
            int y = _chartRect.Bottom + 2;
            g.DrawString(text, _axisFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                x - sz.Width / 2, y);
        }

        private void DrawCrossHair(Graphics g)
        {
            var rect = _chartRect;
            if (_crossIdx < 0 || _crossIdx >= _points.Count) return;

            var pt = _points[_crossIdx];

            int halfW = rect.Width / 2;
            float x;
            bool isMorning = pt.Time.Hour < 12;

            if (isMorning && _morningPoints.Count > 0)
                x = MorningTimeToX(pt.Time, rect.Left, halfW);
            else if (!isMorning && _afternoonPoints.Count > 0)
                x = AfternoonTimeToX(pt.Time, rect.Left + halfW, halfW);
            else return;

            int y = (int)PriceToY(pt.Price);

            using (var pen = new Pen(Color.FromArgb(100, 100, 100), 1))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawLine(pen, rect.Left, y, rect.Right, y);
                g.DrawLine(pen, x, rect.Top, x, rect.Bottom);
            }

            string info = string.Format("{0:HH:mm}  {1:F3}  {2}{3:F3}%",
                pt.Time, pt.Price,
                pt.Price >= _prevClose ? "+" : "",
                _prevClose > 0 ? (pt.Price - _prevClose) / _prevClose * 100 : 0);

            var sz = g.MeasureString(info, _axisFont);
            int boxX = (int)x + 8;
            if (boxX + sz.Width + 8 > rect.Right) boxX = (int)x - (int)sz.Width - 16;

            using (var brush = new SolidBrush(Color.FromArgb(220, 40, 40, 40)))
            using (var fb = new SolidBrush(Color.White))
            {
                var r = new Rectangle(boxX, y - 14, (int)sz.Width + 8, (int)sz.Height + 4);
                g.FillRectangle(brush, r);
                g.DrawString(info, _axisFont, fb, boxX + 4, y - 12);
            }
        }

        private void DrawTrendVolumeBars(Graphics g)
        {
            if (_points == null || _points.Count == 0) return;
            var rect = _volumeRect;
            if (rect.Height <= 0) return;

            int halfW = rect.Width / 2;
            // 一分钟 1 根柱子，按密度算宽度
            int morningCount = _morningPoints.Count;
            int afternoonCount = _afternoonPoints.Count;
            int morningBarArea = morningCount > 0 ? halfW / morningCount : halfW;
            int afternoonBarArea = afternoonCount > 0 ? halfW / afternoonCount : halfW;
            int morningBarWidth = Math.Max(2, morningBarArea * 80 / 100);
            int afternoonBarWidth = Math.Max(2, afternoonBarArea * 80 / 100);

            // 用单笔成交量最大值为基准
            decimal maxVol = 0;
            foreach (var p in _points) if (p.Volume > maxVol) maxVol = p.Volume;
            if (maxVol <= 0) return;

            // 上涨红、下跌绿
            Color upColor = Color.FromArgb(220, 38, 38);
            Color downColor = Color.FromArgb(26, 155, 82);
            Color flatColor = Color.FromArgb(180, 180, 180);

            // 上午
            for (int i = 0; i < _morningPoints.Count; i++)
            {
                var p = _morningPoints[i];
                if (p.Volume <= 0) continue;
                float x = MorningTimeToX(p.Time, rect.Left, halfW);
                float h = (float)(p.Volume / maxVol) * rect.Height;
                if (h < 1) h = 1;
                float top = rect.Bottom - h;
                Color c;
                if (p.Price > _prevClose) c = upColor;
                else if (p.Price < _prevClose) c = downColor;
                else c = flatColor;
                using (var brush = new SolidBrush(c))
                    g.FillRectangle(brush, x - morningBarWidth / 2f, top, morningBarWidth, h);
            }

            // 下午
            for (int i = 0; i < _afternoonPoints.Count; i++)
            {
                var p = _afternoonPoints[i];
                if (p.Volume <= 0) continue;
                float x = AfternoonTimeToX(p.Time, rect.Left + halfW, halfW);
                float h = (float)(p.Volume / maxVol) * rect.Height;
                if (h < 1) h = 1;
                float top = rect.Bottom - h;
                Color c;
                if (p.Price > _prevClose) c = upColor;
                else if (p.Price < _prevClose) c = downColor;
                else c = flatColor;
                using (var brush = new SolidBrush(c))
                    g.FillRectangle(brush, x - afternoonBarWidth / 2f, top, afternoonBarWidth, h);
            }

            // 区域顶部分隔虚线
            using (var pen = new Pen(Color.FromArgb(200, 200, 200), 1))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Top);
            }

            // 标签
            string volLabel = "成交量(手)";
            var sz = g.MeasureString(volLabel, _smallFont);
            g.DrawString(volLabel, _smallFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                rect.Left, rect.Top - sz.Height - 2);

            // 最大值（用"万"换算）
            string maxLabel;
            if (maxVol >= 10000)
                maxLabel = (maxVol / 10000m).ToString("F1") + "万";
            else
                maxLabel = maxVol.ToString("F0");
            g.DrawString(maxLabel, _smallFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                rect.Right + 2, rect.Top);
        }

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

        #endregion

        #region K线图绘制

        private void DrawKLineHeader(Graphics g)
        {
            string name = _klineData?.Name ?? _trendData?.Name ?? "";
            string code = _klineData?.Code ?? _trendData?.Code ?? "";
            string periodText = _klinePeriod == KLinePeriod.Daily ? "日K" :
                _klinePeriod == KLinePeriod.Weekly ? "周K" : "月K";
            string title = string.Format("{0}  {1}  {2}", name, code, periodText);
            g.DrawString(title, _titleFont, new SolidBrush(Color.FromArgb(40, 40, 40)), 12, 28);

            if (_klinePoints != null && _klinePoints.Count > 0)
            {
                var last = _klinePoints[_klinePoints.Count - 1];
                decimal change = last.Close - last.Open;
                decimal pct = last.Open > 0 ? (last.Close - last.Open) / last.Open * 100 : 0;
                Color c = last.Close >= last.Open ? Color.FromArgb(220, 38, 38) : Color.FromArgb(26, 155, 82);

                string priceStr = last.Close.ToString("F2");
                string changeStr = string.Format("{0}{1:F2}  {2}{3:F2}%",
                    last.Close >= last.Open ? "+" : "", change, last.Close >= last.Open ? "+" : "", pct);

                var ps = g.MeasureString(priceStr, _titleFont);
                g.DrawString(priceStr, _titleFont, new SolidBrush(c), 12, 44);
                g.DrawString(changeStr, _subFont, new SolidBrush(c),
                    14 + ps.Width, 50);

                // MA均线图例
                int legendX = this.ClientSize.Width - _klineMarginRight - 10;
                float legendY = 30;

                if (_ma5 != null && _ma5[_ma5.Length - 1] > 0)
                {
                    string ma5Str = string.Format("MA5:{0:F2}", _ma5[_ma5.Length - 1]);
                    g.DrawString(ma5Str, _smallFont, new SolidBrush(Color.FromArgb(255, 100, 100)),
                        legendX - g.MeasureString(ma5Str, _smallFont).Width, legendY);
                    legendY += 14;
                }
                if (_ma10 != null && _ma10[_ma10.Length - 1] > 0)
                {
                    string ma10Str = string.Format("MA10:{0:F2}", _ma10[_ma10.Length - 1]);
                    g.DrawString(ma10Str, _smallFont, new SolidBrush(Color.FromArgb(255, 165, 0)),
                        legendX - g.MeasureString(ma10Str, _smallFont).Width, legendY);
                    legendY += 14;
                }
                if (_ma20 != null && _ma20[_ma20.Length - 1] > 0)
                {
                    string ma20Str = string.Format("MA20:{0:F2}", _ma20[_ma20.Length - 1]);
                    g.DrawString(ma20Str, _smallFont, new SolidBrush(Color.FromArgb(100, 150, 255)),
                        legendX - g.MeasureString(ma20Str, _smallFont).Width, legendY);
                    legendY += 14;
                }
                if (_ma60 != null && _ma60[_ma60.Length - 1] > 0)
                {
                    string ma60Str = string.Format("MA60:{0:F2}", _ma60[_ma60.Length - 1]);
                    g.DrawString(ma60Str, _smallFont, new SolidBrush(Color.FromArgb(150, 50, 200)),
                        legendX - g.MeasureString(ma60Str, _smallFont).Width, legendY);
                }
            }
        }

        private void DrawKLineGrid(Graphics g)
        {
            var rect = _chartRect;
            using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
                int horzCount = 5;
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
            }

            // 成交量区域分隔线
            using (var pen = new Pen(Color.FromArgb(200, 200, 200), 1))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, _volumeRect.Left, _volumeRect.Top, _volumeRect.Right, _volumeRect.Top);
            }
        }

        private void DrawKLines(Graphics g)
        {
            if (_klinePoints == null || _klinePoints.Count == 0) return;

            var rect = _chartRect;
            int count = _klinePoints.Count;
            int barWidth = rect.Width / count;
            int candleWidth = Math.Max(2, barWidth * 70 / 100);

            for (int i = 0; i < count; i++)
            {
                var point = _klinePoints[i];
                float x = rect.Left + i * barWidth + barWidth / 2;

                float openY = KLinePriceToY(point.Open);
                float closeY = KLinePriceToY(point.Close);
                float highY = KLinePriceToY(point.High);
                float lowY = KLinePriceToY(point.Low);

                bool isUp = point.Close >= point.Open;
                Color candleColor = isUp ? Color.FromArgb(220, 38, 38) : Color.FromArgb(26, 155, 82);

                // 影线（最高价到最低价）
                using (var wickPen = new Pen(candleColor, 1))
                {
                    g.DrawLine(wickPen, x, highY, x, lowY);
                }

                // 实体
                float bodyTop, bodyHeight;
                if (isUp)
                {
                    bodyTop = closeY;
                    bodyHeight = Math.Max(1, openY - closeY);
                }
                else
                {
                    bodyTop = openY;
                    bodyHeight = Math.Max(1, closeY - openY);
                }

                using (var brush = new SolidBrush(isUp ? Color.FromArgb(220, 38, 38) : Color.FromArgb(26, 155, 82)))
                {
                    g.FillRectangle(brush, x - candleWidth / 2, bodyTop, candleWidth, bodyHeight);
                }
            }
        }

        private void DrawMALines(Graphics g)
        {
            if (_klinePoints == null || _klinePoints.Count == 0) return;

            DrawMALine(g, _ma5, Color.FromArgb(255, 100, 100));
            DrawMALine(g, _ma10, Color.FromArgb(255, 165, 0));
            DrawMALine(g, _ma20, Color.FromArgb(100, 150, 255));
            DrawMALine(g, _ma60, Color.FromArgb(150, 50, 200));
        }

        private void DrawMALine(Graphics g, decimal[] ma, Color color)
        {
            if (ma == null || _klinePoints == null) return;
            if (ma.Length != _klinePoints.Count) return;

            var rect = _chartRect;
            int count = _klinePoints.Count;
            int barWidth = rect.Width / count;

            using (var pen = new Pen(color, 1))
            {
                bool started = false;
                float prevX = 0, prevY = 0;

                for (int i = 0; i < count; i++)
                {
                    if (ma[i] <= 0) continue;

                    float x = rect.Left + i * barWidth + barWidth / 2;
                    float y = KLinePriceToY(ma[i]);

                    if (!started)
                    {
                        started = true;
                        prevX = x;
                        prevY = y;
                    }
                    else
                    {
                        g.DrawLine(pen, prevX, prevY, x, y);
                        prevX = x;
                        prevY = y;
                    }
                }
            }
        }

        private void DrawVolumeBars(Graphics g)
        {
            if (_klinePoints == null || _klinePoints.Count == 0) return;

            var rect = _volumeRect;
            int count = _klinePoints.Count;
            int barWidth = rect.Width / count;
            int volBarWidth = Math.Max(2, barWidth * 70 / 100);

            for (int i = 0; i < count; i++)
            {
                var point = _klinePoints[i];
                float x = rect.Left + i * barWidth + barWidth / 2;

                float volHeight = (float)point.Volume / (float)_volumeMax * rect.Height;
                float top = rect.Bottom - volHeight;

                bool isUp = point.Close >= point.Open;
                Color barColor = isUp ? Color.FromArgb(220, 38, 38, 180) : Color.FromArgb(26, 155, 82, 180);

                using (var brush = new SolidBrush(barColor))
                {
                    g.FillRectangle(brush, x - volBarWidth / 2, top, volBarWidth, volHeight);
                }
            }

            // 成交量标签
            string volLabel = "成交量(手)";
            var sz = g.MeasureString(volLabel, _smallFont);
            g.DrawString(volLabel, _smallFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                rect.Left, rect.Top - sz.Height - 2);

            // 最大值
            if (_volumeMax >= 10000)
            {
                string maxLabel = (_volumeMax / 10000).ToString("F0") + "万";
                g.DrawString(maxLabel, _smallFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                    rect.Right + 2, rect.Top);
            }
        }

        private void DrawKLineAxisLabels(Graphics g)
        {
            var rect = _chartRect;
            if (_klinePoints == null || _klinePoints.Count == 0) return;

            // 价格轴
            int priceCount = 5;
            for (int i = 0; i <= priceCount; i++)
            {
                int y = rect.Top + rect.Height * i / priceCount;
                decimal price = _klinePriceMax - (_klinePriceMax - _klinePriceMin) * i / priceCount;
                string label = price.ToString("F2");
                var sz = g.MeasureString(label, _axisFont);
                g.DrawString(label, _axisFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                    rect.Right + 2, y - sz.Height / 2);
            }

            // 日期轴
            int count = _klinePoints.Count;
            int labelCount = 6;
            for (int i = 0; i < labelCount; i++)
            {
                int idx = (int)((count - 1) * i / (labelCount - 1));
                if (idx < 0 || idx >= count) continue;

                var point = _klinePoints[idx];
                int barWidth = rect.Width / count;
                float x = rect.Left + idx * barWidth + barWidth / 2;

                string dateLabel = point.Date.ToString("MM-dd");
                var sz = g.MeasureString(dateLabel, _axisFont);
                g.DrawString(dateLabel, _axisFont, new SolidBrush(Color.FromArgb(120, 120, 120)),
                    x - sz.Width / 2, rect.Bottom + 2);
            }
        }

        private void DrawKLineCrossHair(Graphics g)
        {
            if (_crossIdx < 0 || _klinePoints == null || _crossIdx >= _klinePoints.Count) return;

            var point = _klinePoints[_crossIdx];
            var rect = _chartRect;
            int count = _klinePoints.Count;
            int barWidth = rect.Width / count;

            float x = rect.Left + _crossIdx * barWidth + barWidth / 2;
            float y = KLinePriceToY(point.Close);

            // 十字线
            using (var pen = new Pen(Color.FromArgb(100, 100, 100), 1))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawLine(pen, rect.Left, y, rect.Right, y);
                g.DrawLine(pen, x, rect.Top, x, _volumeRect.Bottom);
            }

            // 提示框
            bool isUp = point.Close >= point.Open;
            Color upColor = Color.FromArgb(220, 38, 38);
            Color downColor = Color.FromArgb(26, 155, 82);

            string info = string.Format("{0:yyyy-MM-dd}  开:{1:F2}  高:{2:F2}  低:{3:F2}  收:{4:F2}  量:{5}",
                point.Date, point.Open, point.High, point.Low, point.Close,
                point.Volume >= 10000 ? (point.Volume / 10000m).ToString("F2") + "万" : point.Volume.ToString("F0"));

            Color infoColor = isUp ? upColor : downColor;
            var sz = g.MeasureString(info, _axisFont);
            int boxX = (int)x + 10;
            if (boxX + sz.Width + 10 > rect.Right) boxX = (int)x - (int)sz.Width - 20;
            int boxY = Math.Max(rect.Top, (int)y - 60);

            using (var brush = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
            using (var borderPen = new Pen(Color.FromArgb(200, 200, 200), 1))
            using (var fb = new SolidBrush(infoColor))
            {
                var r = new Rectangle(boxX, boxY, (int)sz.Width + 10, (int)sz.Height + 6);
                g.FillRectangle(brush, r);
                g.DrawRectangle(borderPen, r);
                g.DrawString(info, _axisFont, fb, boxX + 5, boxY + 3);
            }
        }

        private float KLinePriceToY(decimal price)
        {
            var rect = _chartRect;
            if (_klinePriceMax == _klinePriceMin) return rect.Top + rect.Height / 2;
            decimal ratio = (_klinePriceMax - price) / (_klinePriceMax - _klinePriceMin);
            return rect.Top + (float)ratio * rect.Height;
        }

        #endregion

        #region 辅助方法

        private Rectangle RoundedRect(Rectangle rect, int radius)
        {
            int r = radius;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return Rectangle.Round(rect);
        }

        #endregion
    }
}
