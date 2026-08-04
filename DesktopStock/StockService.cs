using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopStock
{
    /// <summary>
    /// 股票实时行情数据
    /// </summary>
    public class StockInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public decimal PrevClose { get; set; }
        public decimal ChangeAmount { get; set; }
        public decimal ChangePercent { get; set; }
        public DateTime UpdateTime { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 分时走势数据点
    /// </summary>
    public class TrendPoint
    {
        public DateTime Time { get; set; }
        public decimal Price { get; set; }
        public decimal AvgPrice { get; set; }
        public decimal Volume { get; set; }
    }

    /// <summary>
    /// 走势图数据
    /// </summary>
    public class TrendData
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public decimal PrevClose { get; set; }
        public List<TrendPoint> Points { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }

        public TrendData()
        {
            Points = new List<TrendPoint>();
            IsValid = true;
        }
    }

    /// <summary>
    /// 股票行情服务（使用新浪财经API）
    /// </summary>
    public static class StockService
    {
        /// <summary>
        /// 将纯数字代码转换为新浪财经接口代码
        /// </summary>
        public static string ToSinaCode(string code)
        {
            code = code.Trim();
            if (string.IsNullOrEmpty(code)) return code;

            // 上交所：6开头(股票)、5开头(ETF/基金)
            if (code.StartsWith("6") || code.StartsWith("5"))
                return "sh" + code;
            // 深交所：0开头(主板)、2开头(中小板)、3开头(创业板)、1开头(ETF/基金如159xxx)
            else if (code.StartsWith("0") || code.StartsWith("2") || code.StartsWith("3") || code.StartsWith("1"))
                return "sz" + code;
            // 北交所：8、4开头
            else if (code.StartsWith("8") || code.StartsWith("4"))
                return "bj" + code;
            else
                return "sh" + code;
        }

        /// <summary>
        /// 同步获取股票实时行情（在后台线程调用）
        /// </summary>
        private static void DebugLog(string msg)
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ds_debug.txt");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " T" +
                    System.Threading.Thread.CurrentThread.ManagedThreadId + " " + msg + "\r\n");
            }
            catch { }
        }

        public static List<StockInfo> FetchStocksSync(List<string> codes)
        {
            var result = new List<StockInfo>();
            if (codes == null || codes.Count == 0) return result;

            // 过滤空代码
            var validCodes = new List<string>();
            foreach (var c in codes)
            {
                if (!string.IsNullOrWhiteSpace(c))
                    validCodes.Add(c.Trim());
            }
            if (validCodes.Count == 0) return result;

            try
            {
                var sinaCodes = new List<string>();
                foreach (var c in validCodes)
                {
                    sinaCodes.Add(ToSinaCode(c));
                }

                string url = "https://hq.sinajs.cn/list=" + string.Join(",", sinaCodes);
                DebugLog("URL=" + url);

                // 方法1: HttpWebRequest 不启用自动解压，手动读取原始字节
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                request.Referer = "https://finance.sina.com.cn/";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                request.Accept = "*/*";
                request.KeepAlive = false;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 5;
                // 不启用 AutomaticDecompression —— 有些 .NET 4.5 环境 bug 导致流变空

                DebugLog("Sending request...");
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    DebugLog(string.Format("Response: Status={0}, ContentLength={1}, ContentEncoding={2}",
                        (int)response.StatusCode,
                        response.ContentLength,
                        response.ContentEncoding ?? "(null)"));

                    using (var stream = response.GetResponseStream())
                    {
                        if (stream == null)
                        {
                            DebugLog("ResponseStream is null!");
                            foreach (var c in validCodes)
                                result.Add(new StockInfo { Code = c, IsValid = false, ErrorMessage = "流为空" });
                            return result;
                        }

                        // 读取全部字节
                        var ms = new System.IO.MemoryStream();
                        byte[] buffer = new byte[4096];
                        int bytesRead;
                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ms.Write(buffer, 0, bytesRead);
                        }
                        byte[] rawData = ms.ToArray();

                        DebugLog(string.Format("Read {0} bytes, first 30: {1}",
                            rawData.Length,
                            rawData.Length > 0 ? BitConverter.ToString(rawData, 0, System.Math.Min(30, rawData.Length)) : "(empty)"));

                        if (rawData.Length == 0)
                        {
                            foreach (var c in validCodes)
                                result.Add(new StockInfo { Code = c, IsValid = false, ErrorMessage = "空白" });
                            return result;
                        }

                        // 检查是否为 GZIP 压缩（前两字节 0x1F 0x8B）
                        if (rawData.Length >= 2 && rawData[0] == 0x1F && rawData[1] == 0x8B)
                        {
                            DebugLog("Data is GZIP compressed, decompressing...");
                            using (var compressed = new System.IO.MemoryStream(rawData))
                            using (var gzip = new System.IO.Compression.GZipStream(compressed,
                                System.IO.Compression.CompressionMode.Decompress))
                            using (var decompressed = new System.IO.MemoryStream())
                            {
                                gzip.CopyTo(decompressed);
                                rawData = decompressed.ToArray();
                                DebugLog(string.Format("Decompressed to {0} bytes", rawData.Length));
                            }
                        }

                        // GBK 解码
                        string responseText = Encoding.GetEncoding(936).GetString(rawData);

                        DebugLog("Decoded text (first 200): " +
                            (responseText.Length > 200 ? responseText.Substring(0, 200) : responseText));

                        if (string.IsNullOrWhiteSpace(responseText) || !responseText.Contains("var hq_str_"))
                        {
                            string hint = rawData.Length < 100
                                ? "短" + rawData.Length
                                : "无匹配(" + rawData.Length + ")";

                            DebugLog("Parse failed: " + hint);
                            foreach (var c in validCodes)
                                result.Add(new StockInfo { Code = c, IsValid = false, ErrorMessage = hint });
                            return result;
                        }

                        var matches = Regex.Matches(responseText,
                            @"var hq_str_(?<sina>[^=]+)=""(?<data>[^""]*)""");

                        DebugLog(string.Format("Found {0} matches", matches.Count));

                        var parsedCodes = new HashSet<string>();

                        foreach (Match match in matches)
                        {
                            string sinaCode = match.Groups["sina"].Value;
                            string dataStr = match.Groups["data"].Value;
                            string originalCode = sinaCode.Substring(2);
                            parsedCodes.Add(originalCode);
                            result.Add(ParseStockData(originalCode, dataStr));
                        }

                        foreach (var c in validCodes)
                        {
                            if (!parsedCodes.Contains(c))
                            {
                                result.Add(new StockInfo { Code = c, IsValid = false, ErrorMessage = "无代码" });
                            }
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                DebugLog("WebException: Status=" + ex.Status + " Msg=" + ex.Message);
                string errMsg;
                if (ex.Status == WebExceptionStatus.Timeout)
                    errMsg = "超时";
                else if (ex.Status == WebExceptionStatus.NameResolutionFailure)
                    errMsg = "DNS失败";
                else if (ex.Status == WebExceptionStatus.ConnectFailure)
                    errMsg = "连接失败";
                else if (ex.Status == WebExceptionStatus.SecureChannelFailure)
                    errMsg = "SSL失败";
                else if (ex.Response != null)
                    errMsg = "HTTP" + (int)((HttpWebResponse)ex.Response).StatusCode;
                else
                    errMsg = ex.Status.ToString();

                foreach (var c in validCodes)
                    result.Add(new StockInfo { Code = c, IsValid = false, ErrorMessage = errMsg });
            }
            catch (Exception ex)
            {
                DebugLog("Exception: " + ex.GetType().FullName + " Msg=" + ex.Message
                    + (ex.InnerException != null ? " Inner=" + ex.InnerException.GetType().Name + ":" + ex.InnerException.Message : ""));
                string errMsg = ex.GetType().Name;
                if (ex.InnerException != null)
                    errMsg = ex.InnerException.GetType().Name + ex.InnerException.Message.Length.ToString();

                foreach (var c in validCodes)
                    result.Add(new StockInfo { Code = c, IsValid = false, ErrorMessage = errMsg });
            }

            return result;
        }

        /// <summary>
        /// 解析单只股票数据
        /// </summary>
        private static StockInfo ParseStockData(string code, string dataStr)
        {
            var info = new StockInfo { Code = code, UpdateTime = DateTime.Now };

            try
            {
                var fields = dataStr.Split(',');

                if (fields.Length < 4)
                {
                    info.IsValid = false;
                    info.ErrorMessage = "数据不全";
                    return info;
                }

                info.Name = fields[0].Trim();

                if (string.IsNullOrEmpty(info.Name))
                {
                    info.IsValid = false;
                    info.ErrorMessage = "无名称";
                    return info;
                }

                decimal price;
                if (!decimal.TryParse(fields[3], out price))
                {
                    info.IsValid = false;
                    info.ErrorMessage = "价格?";
                    return info;
                }
                info.Price = price;

                decimal prevClose;
                if (!decimal.TryParse(fields[2], out prevClose))
                {
                    info.IsValid = false;
                    info.ErrorMessage = "昨收?";
                    return info;
                }
                info.PrevClose = prevClose;

                if (prevClose > 0)
                {
                    info.ChangeAmount = price - prevClose;
                    info.ChangePercent = Math.Round((price - prevClose) / prevClose * 100, 3);
                }

                info.IsValid = true;
            }
            catch
            {
                info.IsValid = false;
                info.ErrorMessage = "解析错";
            }

            return info;
        }

        /// <summary>
        /// 获取分时走势图数据（同步，新浪昨收价 + 腾讯分时数据）
        /// </summary>
        public static TrendData FetchTrendSync(string code, string name)
        {
            var data = new TrendData { Code = code, Name = name };
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                string sinaCode = ToSinaCode(code);

                // 1. 从新浪获取昨收价和股票名称
                decimal prevClose = 0;
                try
                {
                    var sinaReq = (HttpWebRequest)WebRequest.Create("https://hq.sinajs.cn/list=" + sinaCode);
                    sinaReq.Method = "GET";
                    sinaReq.Timeout = 8000;
                    sinaReq.Referer = "https://finance.sina.com.cn/";
                    sinaReq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

                    using (var sinaResp = (HttpWebResponse)sinaReq.GetResponse())
                    using (var sinaStream = sinaResp.GetResponseStream())
                    {
                        if (sinaStream != null)
                        {
                            using (var reader = new StreamReader(sinaStream, Encoding.GetEncoding(936)))
                            {
                                string text = reader.ReadToEnd();
                                var match = Regex.Match(text, @"""([^""]*)""");
                                if (match.Success)
                                {
                                    var fields = match.Groups[1].Value.Split(',');
                                    if (fields.Length > 2)
                                    {
                                        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(fields[0]))
                                            data.Name = fields[0];
                                        decimal.TryParse(fields[2],
                                            System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out prevClose);
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 新浪获取失败不影响后续流程
                }
                data.PrevClose = prevClose;

                // 2. 从腾讯获取分时数据
                string tencentUrl = "https://web.ifzq.gtimg.cn/appstock/app/minute/query?_var=&code=" + sinaCode;

                var request = (HttpWebRequest)WebRequest.Create(tencentUrl);
                request.Method = "GET";
                request.Timeout = 10000;
                request.Referer = "https://gu.qq.com/";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        data.IsValid = false;
                        data.ErrorMessage = "响应流为空";
                        return data;
                    }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    data.IsValid = false;
                    data.ErrorMessage = "响应内容为空";
                    return data;
                }

                // 解析腾讯分时数据: 每行为 "HHmm price cumVolume cumAmount"
                  // 注：第 3 段是累计成交量（手），需要做差分转换为单笔成交量
                  var points = new List<TrendPoint>();
                  decimal runningSum = 0;
                  int count = 0;
                  decimal lastCumVolume = 0;

                  // 找到最内层 data 数组: "data":["0930 ...","0931 ..."]
                  int arrStart = json.IndexOf("\"data\":[\"", StringComparison.Ordinal);
                  if (arrStart < 0)
                  {
                      data.IsValid = false;
                      data.ErrorMessage = "未找到走势数据";
                      return data;
                  }
                  arrStart += "\"data\":[\"".Length;

                  // 找这个数组的结束（arrStart在第一个字符串内部，所以inString初始为true）
                  int bracketDepth = 1;
                  int arrEnd = arrStart;
                  bool inString = true;
                  for (int i = arrStart; i < json.Length; i++)
                  {
                      char ch = json[i];
                      if (ch == '\\' && inString) { i++; continue; }
                      if (ch == '"') { inString = !inString; continue; }
                      if (inString) continue;
                      if (ch == '[') bracketDepth++;
                      else if (ch == ']') { bracketDepth--; if (bracketDepth == 0) { arrEnd = i; break; } }
                  }

                  string section = json.Substring(arrStart, arrEnd - arrStart);
                  if (string.IsNullOrWhiteSpace(section))
                  {
                      data.IsValid = false;
                      data.ErrorMessage = "走势数据为空";
                      return data;
                  }

                  // 按 " 分割每个条目
                  var entries = section.Split(new[] { "\",\"" }, StringSplitOptions.RemoveEmptyEntries);
                  foreach (var entry in entries)
                  {
                      string e = entry.Trim(' ', '"', '\n', '\r');
                      if (string.IsNullOrWhiteSpace(e) || e.Length < 12) continue;

                      var parts = e.Split(' ');
                      if (parts.Length < 2) continue;

                      // 解析时间: "0930" → 09:30
                      string timeStr = parts[0].Trim();
                      if (timeStr.Length != 4) continue;
                      int hour, minute;
                      if (!int.TryParse(timeStr.Substring(0, 2), out hour)) continue;
                      if (!int.TryParse(timeStr.Substring(2, 2), out minute)) continue;

                      DateTime time = DateTime.Today.AddHours(hour).AddMinutes(minute);

                      // 解析价格
                      decimal price;
                      if (!decimal.TryParse(parts[1],
                          System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out price))
                          continue;

                      // 解析累计成交量（第 3 段，"手"为单位），再做差分转为单笔
                      decimal cumVolume = 0;
                      if (parts.Length >= 3)
                      {
                          decimal.TryParse(parts[2],
                              System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out cumVolume);
                      }

                      // 差分：当前累计 - 上一次累计
                      // 跨时段（11:30→13:00）累计会重置，若当前累计 < 上一次累计则按当前累计算（即整段第一笔）
                      decimal volume;
                      if (cumVolume >= lastCumVolume)
                          volume = cumVolume - lastCumVolume;
                      else
                          volume = cumVolume;
                      lastCumVolume = cumVolume;
                      if (volume < 0) volume = 0;

                      runningSum += price;
                      count++;
                      decimal avgPrice = count > 0 ? runningSum / count : price;

                      points.Add(new TrendPoint
                      {
                          Time = time,
                          Price = price,
                          AvgPrice = avgPrice,
                          Volume = volume
                      });
                }

                data.Points = points;
                data.IsValid = points.Count > 0;
                if (!data.IsValid)
                    data.ErrorMessage = "无走势数据";
            }
            catch (WebException ex)
            {
                data.IsValid = false;
                if (ex.Status == WebExceptionStatus.Timeout)
                    data.ErrorMessage = "请求超时";
                else if (ex.Status == WebExceptionStatus.NameResolutionFailure)
                    data.ErrorMessage = "DNS解析失败";
                else if (ex.Status == WebExceptionStatus.ConnectFailure)
                    data.ErrorMessage = "无法连接服务器";
                else if (ex.Response != null)
                {
                    using (ex.Response)
                    {
                        data.ErrorMessage = "HTTP " + (int)((HttpWebResponse)ex.Response).StatusCode;
                    }
                }
                else
                    data.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                data.IsValid = false;
                data.ErrorMessage = ex.Message;
            }
            return data;
        }

        #region K线数据

        // K线请求节流：两次请求之间最小间隔（毫秒）
        private static int _klineRequestIntervalMs = 200;
        private static DateTime _lastKlineRequestTime = DateTime.MinValue;
        private static readonly object _klineThrottleLock = new object();

        // K线结果缓存：同 code+period+count 短时间内复用，避免重复请求触发风控
        private static readonly Dictionary<string, CacheItem> _klineCache = new Dictionary<string, CacheItem>();
        private static readonly object _klineCacheLock = new object();
        private static readonly TimeSpan _klineCacheTtl = TimeSpan.FromMinutes(2);

        private class CacheItem
        {
            public KLineData Data;
            public DateTime Time;
        }

        /// <summary>
        /// 获取K线数据（东方财富API，带重试 + 节流 + 缓存 + 备用源）
        /// </summary>
        public static KLineData FetchKLineSync(string code, string name, KLinePeriod period = KLinePeriod.Daily, int count = 120)
        {
            // 1. 命中缓存直接返回
            string cacheKey = string.Format("{0}|{1}|{2}", code, (int)period, count);
            KLineData cached = TryGetCache(cacheKey);
            if (cached != null)
            {
                // 名称用最新的（如果原缓存没有）
                if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(cached.Name))
                {
                    cached.Name = name;
                }
                return cached;
            }

            // 2. 节流：确保两次请求间有足够间隔，避免被风控
            ThrottleKLineRequest();

            // 3. 多源轮询（任一成功就立即返回）：
            //    新浪 K线（最稳）→ 东财（数据全）→ 腾讯 → 网易 → 雪球
            KLineData data = TryFetchFromSinaKLine(code, name, period, count);
            if (!data.IsValid)
            {
                data = TryFetchFromEastMoney(code, name, period, count);
            }
            if (!data.IsValid)
            {
                System.Threading.Thread.Sleep(300);
                data = TryFetchFromTencent(code, name, period, count);
            }
            if (!data.IsValid)
            {
                System.Threading.Thread.Sleep(300);
                data = TryFetchFromNetease(code, name, period, count);
            }
            if (!data.IsValid)
            {
                System.Threading.Thread.Sleep(300);
                data = TryFetchFromXueqiu(code, name, period, count);
            }

            // 4. 有效结果写入缓存
            if (data != null && data.IsValid)
            {
                SaveCache(cacheKey, data);
            }
            return data;
        }

        /// <summary>
        /// 从东方财富获取K线（带重试）
        /// </summary>
        private static KLineData TryFetchFromEastMoney(string code, string name, KLinePeriod period, int count)
        {
            int maxRetry = 3;
            int retryDelay = 1000;

            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                var data = FetchEastMoneyOnce(code, name, period, count);
                if (data.IsValid)
                    return data;

                // 最后一次不再等待
                if (attempt < maxRetry)
                {
                    // 连接被关/超时/空内容 都需要重试
                    bool shouldRetry =
                        string.IsNullOrEmpty(data.ErrorMessage) ||
                        data.ErrorMessage.Contains("关闭") ||
                        data.ErrorMessage.Contains("超时") ||
                        data.ErrorMessage.Contains("空") ||
                        data.ErrorMessage.Contains("未找到") ||
                        data.ErrorMessage.Contains("HTTP 5") ||
                        data.ErrorMessage.Contains("HTTP 4");
                    if (!shouldRetry) return data;

                    System.Threading.Thread.Sleep(retryDelay);
                    retryDelay *= 2; // 指数退避
                }
                else
                {
                    return data;
                }
            }
            return new KLineData { Code = code, Name = name, IsValid = false, ErrorMessage = "重试失败" };
        }

        /// <summary>
        /// 单次从东方财富拉取K线
        /// </summary>
        private static KLineData FetchEastMoneyOnce(string code, string name, KLinePeriod period, int count)
        {
            var data = new KLineData { Code = code, Name = name };
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.DefaultConnectionLimit = 64;
                ServicePointManager.Expect100Continue = false;

                string sinaCode = ToSinaCode(code);
                string market = sinaCode.StartsWith("sh") ? "1" : "0";
                string pureCode = sinaCode.Substring(2);

                string url = string.Format(
                    "https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={0}.{1}&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61&klt={2}&fqt=1&end=20500101&lmt={3}",
                    market, pureCode, (int)period, count);

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.Referer = "https://quote.eastmoney.com/";
                request.Host = "push2his.eastmoney.com";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                request.Accept = "*/*";
                request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                request.Headers.Add("Accept-Encoding", "gzip, deflate");
                request.KeepAlive = true;
                request.AllowAutoRedirect = true;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        data.IsValid = false;
                        data.ErrorMessage = "响应流为空";
                        return data;
                    }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    data.IsValid = false;
                    data.ErrorMessage = "响应内容为空";
                    return data;
                }

                // 解析东方财富K线数据
                var points = new List<KLinePoint>();

                int klinesStart = json.IndexOf("\"klines\":[\"", StringComparison.Ordinal);
                if (klinesStart < 0)
                {
                    data.IsValid = false;
                    data.ErrorMessage = "未找到K线数据";
                    return data;
                }
                klinesStart += "\"klines\":[\"".Length;

                int bracketDepth = 1;
                int klinesEnd = klinesStart;
                bool inString = true;
                for (int i = klinesStart; i < json.Length; i++)
                {
                    char ch = json[i];
                    if (ch == '\\' && inString) { i++; continue; }
                    if (ch == '"') { inString = !inString; continue; }
                    if (inString) continue;
                    if (ch == '[') bracketDepth++;
                    else if (ch == ']') { bracketDepth--; if (bracketDepth == 0) { klinesEnd = i; break; } }
                }

                string section = json.Substring(klinesStart, klinesEnd - klinesStart);
                if (string.IsNullOrWhiteSpace(section))
                {
                    data.IsValid = false;
                    data.ErrorMessage = "K线数据为空";
                    return data;
                }

                var entries = section.Split(new[] { "\",\"" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var entry in entries)
                {
                    string e = entry.Trim(' ', '"', '\n', '\r');
                    if (string.IsNullOrWhiteSpace(e)) continue;

                    var parts = e.Split(',');
                    if (parts.Length < 7) continue;

                    DateTime date;
                    if (!DateTime.TryParse(parts[0], out date)) continue;

                    decimal open, close, high, low, volume, amount;
                    if (!decimal.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out open)) continue;
                    if (!decimal.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out close)) continue;
                    if (!decimal.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out high)) continue;
                    if (!decimal.TryParse(parts[4], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out low)) continue;
                    if (!decimal.TryParse(parts[5], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out volume)) volume = 0;
                    if (!decimal.TryParse(parts[6], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out amount)) amount = 0;

                    points.Add(new KLinePoint
                    {
                        Date = date,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,
                        Amount = amount
                    });
                }

                points.Sort((a, b) => a.Date.CompareTo(b.Date));

                data.Points = points;
                data.IsValid = points.Count > 0;
                if (!data.IsValid)
                    data.ErrorMessage = "无K线数据";

                if (string.IsNullOrWhiteSpace(name))
                {
                    int nameIdx = json.IndexOf("\"name\":\"", StringComparison.Ordinal);
                    if (nameIdx >= 0)
                    {
                        nameIdx += "\"name\":\"".Length;
                        int nameEnd = json.IndexOf('"', nameIdx);
                        if (nameEnd > nameIdx)
                        {
                            data.Name = json.Substring(nameIdx, nameEnd - nameIdx);
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                data.IsValid = false;
                if (ex.Status == WebExceptionStatus.Timeout)
                    data.ErrorMessage = "请求超时";
                else if (ex.Status == WebExceptionStatus.NameResolutionFailure)
                    data.ErrorMessage = "DNS解析失败";
                else if (ex.Status == WebExceptionStatus.ConnectFailure)
                    data.ErrorMessage = "无法连接服务器";
                else if (ex.Response != null)
                {
                    using (ex.Response)
                    {
                        data.ErrorMessage = "HTTP " + (int)((HttpWebResponse)ex.Response).StatusCode;
                    }
                }
                else
                    data.ErrorMessage = "连接被关闭";
            }
            catch (Exception ex)
            {
                data.IsValid = false;
                data.ErrorMessage = ex.Message;
            }
            return data;
        }

        /// <summary>
        /// 备用源：从腾讯获取K线
        /// 腾讯日K接口: https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param=sh600000,day,,,120,qfq
        /// </summary>
        private static KLineData TryFetchFromTencent(string code, string name, KLinePeriod period, int count)
        {
            var data = new KLineData { Code = code, Name = name };
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.Expect100Continue = false;

                string sinaCode = ToSinaCode(code); // sh600000 / sz000001
                string tencentPeriod = period == KLinePeriod.Daily ? "day" :
                                       period == KLinePeriod.Weekly ? "week" :
                                       period == KLinePeriod.Monthly ? "month" : "year";

                string url = string.Format(
                    "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param={0},{1},,,{2},qfq",
                    sinaCode, tencentPeriod, count);

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.Referer = "https://gu.qq.com/";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                request.Accept = "*/*";
                request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                request.Headers.Add("Accept-Encoding", "gzip, deflate");
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.KeepAlive = true;
                request.AllowAutoRedirect = true;

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    if (stream == null)
                    {
                        data.IsValid = false;
                        data.ErrorMessage = "响应流为空";
                        return data;
                    }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    data.IsValid = false;
                    data.ErrorMessage = "响应内容为空";
                    return data;
                }

                // 腾讯格式: "qfqday":[["2024-01-02","12.50","12.60","12.40","12.55","1234567","155432100.00"],...]
                // 或 "day":同上
                string key = "\"qfq" + tencentPeriod + "\":[";
                int arrayStart = json.IndexOf(key, StringComparison.Ordinal);
                if (arrayStart < 0)
                {
                    key = "\"" + tencentPeriod + "\":[";
                    arrayStart = json.IndexOf(key, StringComparison.Ordinal);
                }
                if (arrayStart < 0)
                {
                    data.IsValid = false;
                    data.ErrorMessage = "腾讯K线数据格式未找到";
                    return data;
                }
                arrayStart += key.Length;

                // 找数组结束
                int bracketDepth = 1;
                int arrayEnd = arrayStart;
                bool inString = false;
                for (int i = arrayStart; i < json.Length; i++)
                {
                    char ch = json[i];
                    if (ch == '\\' && inString) { i++; continue; }
                    if (ch == '"') { inString = !inString; continue; }
                    if (inString) continue;
                    if (ch == '[') bracketDepth++;
                    else if (ch == ']') { bracketDepth--; if (bracketDepth == 0) { arrayEnd = i; break; } }
                }

                string section = json.Substring(arrayStart, arrayEnd - arrayStart);
                if (string.IsNullOrWhiteSpace(section))
                {
                    data.IsValid = false;
                    data.ErrorMessage = "腾讯K线数据为空";
                    return data;
                }

                var points = new List<KLinePoint>();
                // 解析内层数组
                int pos = 0;
                while (pos < section.Length)
                {
                    int lb = section.IndexOf('[', pos);
                    if (lb < 0) break;
                    int rb = section.IndexOf(']', lb);
                    if (rb < 0) break;

                    string item = section.Substring(lb + 1, rb - lb - 1);
                    // 拆分各字段（以引号包围的字符串为主）
                    var fields = SplitJsonArray(item);
                    if (fields.Count >= 6)
                    {
                        DateTime date;
                        decimal open, close, high, low, volume;
                        if (DateTime.TryParse(StripQuotes(fields[0]), out date) &&
                            decimal.TryParse(StripQuotes(fields[1]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out open) &&
                            decimal.TryParse(StripQuotes(fields[2]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out close) &&
                            decimal.TryParse(StripQuotes(fields[3]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out high) &&
                            decimal.TryParse(StripQuotes(fields[4]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out low) &&
                            decimal.TryParse(StripQuotes(fields[5]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out volume))
                        {
                            points.Add(new KLinePoint
                            {
                                Date = date,
                                Open = open,
                                High = high,
                                Low = low,
                                Close = close,
                                Volume = volume,
                                Amount = 0
                            });
                        }
                    }
                    pos = rb + 1;
                }

                points.Sort((a, b) => a.Date.CompareTo(b.Date));

                data.Points = points;
                data.IsValid = points.Count > 0;
                if (!data.IsValid)
                    data.ErrorMessage = "腾讯K线数据为空";

                // 解析名称
                if (string.IsNullOrWhiteSpace(name))
                {
                    int nameIdx = json.IndexOf("\"name\":\"", StringComparison.Ordinal);
                    if (nameIdx >= 0)
                    {
                        nameIdx += "\"name\":\"".Length;
                        int nameEnd = json.IndexOf('"', nameIdx);
                        if (nameEnd > nameIdx)
                        {
                            data.Name = json.Substring(nameIdx, nameEnd - nameIdx);
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "腾讯源: " + (ex.Status == WebExceptionStatus.Timeout ? "请求超时" :
                    ex.Status == WebExceptionStatus.ConnectFailure ? "无法连接" : "连接异常");
            }
            catch (Exception ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "腾讯源: " + ex.Message;
            }
            return data;
        }

        /// <summary>
        /// 备用源2：新浪K线（最稳定，反爬最松，推荐主用）
        /// https://quotes.sina.cn/cn/api/json_v2.php/CN_MarketDataService.getKLineData?symbol=sh600000&scale=240&datalen=120
        /// scale: 240=日K, 1680=周K, 7200=月K
        /// 返回纯 JSON 数组: [{day, open, high, low, close, volume, ...}, ...]
        /// </summary>
        private static KLineData TryFetchFromSinaKLine(string code, string name, KLinePeriod period, int count)
        {
            KLineData data = new KLineData { Code = code, Name = name, IsValid = false };
            try
            {
                string sinaCode = ToSinaCode(code); // sh600000 / sz000001

                // 周期映射
                int scale;
                if (period == KLinePeriod.Weekly) scale = 1680;
                else if (period == KLinePeriod.Monthly) scale = 7200;
                else scale = 240; // 日K

                string url = string.Format(
                    "https://quotes.sina.cn/cn/api/json_v2.php/CN_MarketDataService.getKLineData?symbol={0}&scale={1}&datalen={2}&ma=no",
                    sinaCode, scale, count);

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 10000;
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                request.Referer = "https://finance.sina.com.cn/";
                request.Accept = "*/*";
                request.KeepAlive = true;

                using (var resp = (HttpWebResponse)request.GetResponse())
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null) { data.ErrorMessage = "新浪K线: 返回空流"; return data; }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        if (string.IsNullOrWhiteSpace(json) || json == "[]" || json.Length < 5)
                        {
                            data.ErrorMessage = "新浪K线: 内容为空";
                            return data;
                        }

                        // 解析 JSON 数组：[{"day":"...","open":"...","high":"...","low":"...","close":"...","volume":"..."}, ...]
                        var points = new List<KLinePoint>();
                        int pos = 0;
                        // 跳过 '['
                        while (pos < json.Length && json[pos] != '[') pos++;
                        pos++;

                        while (pos < json.Length)
                        {
                            // 跳过空白
                            while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\r' || json[pos] == '\n' || json[pos] == '\t' || json[pos] == ',')) pos++;
                            if (pos >= json.Length || json[pos] == ']') break;
                            if (json[pos] != '{') { pos++; continue; }

                            int braceStart = pos;
                            int braceEnd = pos;
                            int depth = 1;
                            bool inString = false;
                            for (int i = pos + 1; i < json.Length; i++)
                            {
                                char ch = json[i];
                                if (ch == '\\' && inString) { i++; continue; }
                                if (ch == '"') { inString = !inString; continue; }
                                if (inString) continue;
                                if (ch == '{') depth++;
                                else if (ch == '}') { depth--; if (depth == 0) { braceEnd = i; break; } }
                            }
                            if (braceEnd <= braceStart) break;

                            string obj = json.Substring(braceStart, braceEnd - braceStart + 1);
                            // 提取各字段值（简化：直接字符串查找）
                            string day = ExtractJsonValue(obj, "day");
                            string open = ExtractJsonValue(obj, "open");
                            string high = ExtractJsonValue(obj, "high");
                            string low = ExtractJsonValue(obj, "low");
                            string close = ExtractJsonValue(obj, "close");
                            string volume = ExtractJsonValue(obj, "volume");

                            if (!string.IsNullOrEmpty(day) && !string.IsNullOrEmpty(open))
                            {
                                DateTime dt;
                                if (DateTime.TryParse(day, out dt))
                                {
                                    decimal o, h, l, c;
                                    long v;
                                    if (decimal.TryParse(open, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out o) &&
                                        decimal.TryParse(high, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h) &&
                                        decimal.TryParse(low, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out l) &&
                                        decimal.TryParse(close, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c) &&
                                        long.TryParse(volume, out v))
                                    {
                                        points.Add(new KLinePoint
                                        {
                                            Date = dt,
                                            Open = o,
                                            High = h,
                                            Low = l,
                                            Close = c,
                                            Volume = v / 100 // 股 → 手
                                        });
                                    }
                                }
                            }
                            pos = braceEnd + 1;
                        }

                        points.Sort((a, b) => a.Date.CompareTo(b.Date));
                        data.Points = points;
                        data.IsValid = points.Count > 0;
                        if (!data.IsValid) data.ErrorMessage = "新浪K线: 解析后为空";
                    }
                }
            }
            catch (WebException ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "新浪K线: " + (ex.Status == WebExceptionStatus.Timeout ? "请求超时" :
                    ex.Status == WebExceptionStatus.ConnectFailure ? "无法连接" : "连接异常");
            }
            catch (Exception ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "新浪K线: " + ex.Message;
            }
            return data;
        }

        /// <summary>
        /// 从 JSON 对象片段中提取 "key":"value" 形式的 value（不带引号处理转义）
        /// </summary>
        private static string ExtractJsonValue(string objJson, string key)
        {
            string pattern = "\"" + key + "\":\"";
            int idx = objJson.IndexOf(pattern, StringComparison.Ordinal);
            if (idx >= 0)
            {
                idx += pattern.Length;
                int end = objJson.IndexOf('"', idx);
                if (end > idx) return objJson.Substring(idx, end - idx);
            }
            // 数值类型不带引号
            pattern = "\"" + key + "\":";
            idx = objJson.IndexOf(pattern, StringComparison.Ordinal);
            if (idx >= 0)
            {
                idx += pattern.Length;
                int end = idx;
                while (end < objJson.Length && objJson[end] != ',' && objJson[end] != '}') end++;
                return objJson.Substring(idx, end - idx).Trim();
            }
            return null;
        }

        /// <summary>
        /// 备用源3：网易财经K线（CSV 格式，不容易被风控）
        /// http://quotes.money.163.com/service/chddata.html?code=0600000&start=...&end=...&fields=...
        /// code 前缀：上证 0 + 6位，深证 1 + 6位（与新浪相反）
        /// </summary>
        private static KLineData TryFetchFromNetease(string code, string name, KLinePeriod period, int count)
        {
            KLineData data = new KLineData { Code = code, Name = name, IsValid = false };
            try
            {
                // 网易 code 转换：sh600000 → 0600000, sz000001 → 1000001
                string sinaCode = ToSinaCode(code);
                string neteaseCode = sinaCode.StartsWith("sh") ? "0" + sinaCode.Substring(2) : "1" + sinaCode.Substring(2);

                // 日期范围：日K 取 6 个月以上，周K/月K 更长
                int months = 6;
                if (period == KLinePeriod.Weekly) months = 36;
                else if (period == KLinePeriod.Monthly) months = 60;
                DateTime startDate = DateTime.Now.AddMonths(-months);
                string startStr = startDate.ToString("yyyyMMdd");
                string endStr = DateTime.Now.ToString("yyyyMMdd");

                // fields: TCLOSE 收盘, HIGH 最高, LOW 最低, TOPEN 开盘, LCLOSE 前收, VOTURNOVER 成交量(股), VATURNOVER 成交金额
                string url = string.Format(
                    "http://quotes.money.163.com/service/chddata.html?code={0}&start={1}&end={2}&fields=TCLOSE;HIGH;LOW;TOPEN;LCLOSE;CHG;PCHG;TURNOVER;VOTURNOVER;VATURNOVER",
                    neteaseCode, startStr, endStr);

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 10000;
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                request.Referer = "https://quotes.money.163.com/";
                request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                request.KeepAlive = true;

                using (var resp = (HttpWebResponse)request.GetResponse())
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null) { data.ErrorMessage = "网易源: 返回空流"; return data; }
                    using (var reader = new StreamReader(stream, Encoding.GetEncoding("GBK")))
                    {
                        string csv = reader.ReadToEnd();
                        if (string.IsNullOrWhiteSpace(csv) || !csv.Contains(","))
                        {
                            data.ErrorMessage = "网易源: 内容无效";
                            return data;
                        }

                        var points = new List<KLinePoint>();
                        string[] lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 1; i < lines.Length; i++) // 跳过表头
                        {
                            try
                            {
                                string[] cells = ParseCsvLine(lines[i]);
                                // CSV字段：日期,股票代码,名称,收盘价,最高价,最低价,开盘价,前收盘,涨跌额,涨跌幅,换手率,成交量,成交金额,总市值,流通市值
                                if (cells.Length < 12) continue;

                                DateTime dt;
                                if (!DateTime.TryParse(cells[0].Trim(), out dt)) continue;

                                decimal open, high, low, close;
                                if (!decimal.TryParse(cells[6].Trim(), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out open)) continue;
                                if (!decimal.TryParse(cells[4].Trim(), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out high)) continue;
                                if (!decimal.TryParse(cells[5].Trim(), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out low)) continue;
                                if (!decimal.TryParse(cells[3].Trim(), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out close)) continue;

                                long volume = 0;
                                long v;
                                if (cells.Length >= 12 && long.TryParse(cells[11].Trim(), out v)) volume = v / 100; // 股 → 手

                                points.Add(new KLinePoint
                                {
                                    Date = dt,
                                    Open = open,
                                    High = high,
                                    Low = low,
                                    Close = close,
                                    Volume = volume
                                });
                            }
                            catch { /* 跳过单行解析失败 */ }
                        }

                        // 网易只返回日K，按需聚合
                        points.Sort((a, b) => a.Date.CompareTo(b.Date));
                        if (period == KLinePeriod.Weekly)
                            points = AggregateKLine(points, KLinePeriod.Weekly);
                        else if (period == KLinePeriod.Monthly)
                            points = AggregateKLine(points, KLinePeriod.Monthly);

                        // 取最后 N 条
                        if (points.Count > count)
                            points = points.GetRange(points.Count - count, count);

                        data.Points = points;
                        data.IsValid = points.Count > 0;
                        if (!data.IsValid) data.ErrorMessage = "网易源: 数据为空";
                    }
                }
            }
            catch (WebException ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "网易源: " + (ex.Status == WebExceptionStatus.Timeout ? "请求超时" :
                    ex.Status == WebExceptionStatus.ConnectFailure ? "无法连接" : "连接异常");
            }
            catch (Exception ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "网易源: " + ex.Message;
            }
            return data;
        }

        /// <summary>
        /// 备用源4：雪球行情（K线，jsonp 格式，宽松反爬）
        /// https://stock.xueqiu.com/v5/stock/chart/kline.json?symbol=SH600000&begin=...&period=day&type=before&count=-120
        /// </summary>
        private static KLineData TryFetchFromXueqiu(string code, string name, KLinePeriod period, int count)
        {
            KLineData data = new KLineData { Code = code, Name = name, IsValid = false };
            try
            {
                // 雪球 code 转换：sh600000 → SH600000, sz000001 → SZ000001
                string sinaCode = ToSinaCode(code); // sh600000 / sz000001
                string xqSymbol = sinaCode.ToUpper();

                string xqPeriod = "day"; // day/week/month
                if (period == KLinePeriod.Weekly) xqPeriod = "week";
                else if (period == KLinePeriod.Monthly) xqPeriod = "month";

                long beginTs = (DateTime.Now.AddMonths(-12).Ticks - 621355968000000000L) / 10000L;

                string url = string.Format(
                    "https://stock.xueqiu.com/v5/stock/chart/kline.json?symbol={0}&begin={1}&period={2}&type=before&count=-{3}&indicator=kline",
                    xqSymbol, beginTs, xqPeriod, count);

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 10000;
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                request.Referer = "https://xueqiu.com/";
                request.Accept = "application/json, text/plain, */*";
                request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
                request.KeepAlive = true;

                using (var resp = (HttpWebResponse)request.GetResponse())
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null) { data.ErrorMessage = "雪球源: 返回空流"; return data; }
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        // 雪球返回结构：{"data":{"column":["timestamp","volume","open","high","low","close","chg","percent","turnoverrate","amount","..."],"item":[[ts,vol,open,high,low,close,chg,pct,tr,amt,...], ...]}}
                        int itemIdx = json.IndexOf("\"item\":", StringComparison.Ordinal);
                        if (itemIdx < 0) { data.ErrorMessage = "雪球源: 格式错误"; return data; }
                        int arrStart = json.IndexOf('[', itemIdx);
                        if (arrStart < 0) { data.ErrorMessage = "雪球源: 无数据"; return data; }

                        int bracketDepth = 1, arrEnd = arrStart;
                        bool inString = false;
                        for (int i = arrStart + 1; i < json.Length; i++)
                        {
                            char ch = json[i];
                            if (ch == '\\' && inString) { i++; continue; }
                            if (ch == '"') { inString = !inString; continue; }
                            if (inString) continue;
                            if (ch == '[') bracketDepth++;
                            else if (ch == ']') { bracketDepth--; if (bracketDepth == 0) { arrEnd = i; break; } }
                        }
                        string section = json.Substring(arrStart, arrEnd - arrStart + 1);
                        if (string.IsNullOrWhiteSpace(section) || section == "[]")
                        { data.ErrorMessage = "雪球源: 数据为空"; return data; }

                        var points = new List<KLinePoint>();
                        int pos = 1; // 跳过 '['
                        while (pos < section.Length)
                        {
                            int lb = section.IndexOf('[', pos);
                            if (lb < 0) break;
                            int rb = lb + 1;
                            int depth = 1;
                            bool inS = false;
                            for (int i = lb + 1; i < section.Length; i++)
                            {
                                char ch = section[i];
                                if (ch == '\\' && inS) { i++; continue; }
                                if (ch == '"') { inS = !inS; continue; }
                                if (inS) continue;
                                if (ch == '[') depth++;
                                else if (ch == ']') { depth--; if (depth == 0) { rb = i; break; } }
                            }
                            string item = section.Substring(lb + 1, rb - lb - 1);
                            var cells = SplitJsonArray(item);
                            // 雪球字段: [ts, vol, open, high, low, close, chg, pct, tr, amt, ...]
                            if (cells.Count >= 6)
                            {
                                long ts;
                                if (long.TryParse(StripQuotes(cells[0]), out ts))
                                {
                                    DateTime dt = (new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddMilliseconds(ts).ToLocalTime();
                                    decimal open, high, low, close;
                                    long volume;
                                    if (decimal.TryParse(StripQuotes(cells[2]), System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out open) &&
                                        decimal.TryParse(StripQuotes(cells[3]), System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out high) &&
                                        decimal.TryParse(StripQuotes(cells[4]), System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out low) &&
                                        decimal.TryParse(StripQuotes(cells[5]), System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out close))
                                    {
                                        long.TryParse(StripQuotes(cells[1]), out volume);
                                        points.Add(new KLinePoint
                                        {
                                            Date = dt,
                                            Open = open,
                                            High = high,
                                            Low = low,
                                            Close = close,
                                            Volume = volume / 100 // 股 → 手
                                        });
                                    }
                                }
                            }
                            pos = rb + 1;
                        }

                        points.Sort((a, b) => a.Date.CompareTo(b.Date));
                        data.Points = points;
                        data.IsValid = points.Count > 0;
                        if (!data.IsValid) data.ErrorMessage = "雪球源: 数据为空";
                    }
                }
            }
            catch (WebException ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "雪球源: " + (ex.Status == WebExceptionStatus.Timeout ? "请求超时" :
                    ex.Status == WebExceptionStatus.ConnectFailure ? "无法连接" : "连接异常");
            }
            catch (Exception ex)
            {
                data.IsValid = false;
                data.ErrorMessage = "雪球源: " + ex.Message;
            }
            return data;
        }

        /// <summary>
        /// 将日K聚合成周K/月K
        /// </summary>
        private static List<KLinePoint> AggregateKLine(List<KLinePoint> dailyPoints, KLinePeriod targetPeriod)
        {
            if (dailyPoints == null || dailyPoints.Count == 0) return new List<KLinePoint>();
            var result = new List<KLinePoint>();
            KLinePoint current = null;
            foreach (var p in dailyPoints)
            {
                if (current == null || !IsSamePeriod(current.Date, p.Date, targetPeriod))
                {
                    if (current != null) result.Add(current);
                    current = new KLinePoint
                    {
                        Date = p.Date,
                        Open = p.Open,
                        High = p.High,
                        Low = p.Low,
                        Close = p.Close,
                        Volume = p.Volume
                    };
                }
                else
                {
                    if (p.High > current.High) current.High = p.High;
                    if (p.Low < current.Low) current.Low = p.Low;
                    current.Close = p.Close;
                    current.Volume += p.Volume;
                }
            }
            if (current != null) result.Add(current);
            return result;
        }

        private static bool IsSamePeriod(DateTime a, DateTime b, KLinePeriod period)
        {
            if (period == KLinePeriod.Weekly)
            {
                var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
                int wa = cal.GetWeekOfYear(a, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                int wb = cal.GetWeekOfYear(b, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                return a.Year == b.Year && wa == wb;
            }
            else if (period == KLinePeriod.Monthly)
            {
                return a.Year == b.Year && a.Month == b.Month;
            }
            return a == b;
        }

        /// <summary>
        /// 简单 CSV 行解析（处理引号包裹的字段）
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            var cells = new List<string>();
            bool inQuote = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"') { inQuote = !inQuote; continue; }
                if (ch == ',' && !inQuote)
                {
                    cells.Add(line.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start <= line.Length) cells.Add(line.Substring(start));
            return cells.ToArray();
        }

        /// <summary>
        /// 简易 JSON 数组元素拆分（处理引号内的逗号）
        /// </summary>
        private static List<string> SplitJsonArray(string s)
        {
            var list = new List<string>();
            bool inStr = false;
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && inStr) { i++; continue; }
                if (ch == '"') { inStr = !inStr; continue; }
                if (ch == ',' && !inStr)
                {
                    list.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < s.Length) list.Add(s.Substring(start));
            return list;
        }

        private static string StripQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        /// <summary>
        /// K线请求节流
        /// </summary>
        private static void ThrottleKLineRequest()
        {
            lock (_klineThrottleLock)
            {
                DateTime now = DateTime.Now;
                TimeSpan elapsed = now - _lastKlineRequestTime;
                int wait = _klineRequestIntervalMs - (int)elapsed.TotalMilliseconds;
                if (wait > 0)
                {
                    System.Threading.Thread.Sleep(wait);
                }
                _lastKlineRequestTime = DateTime.Now;
            }
        }

        private static KLineData TryGetCache(string key)
        {
            lock (_klineCacheLock)
            {
                CacheItem item;
                if (_klineCache.TryGetValue(key, out item))
                {
                    if (DateTime.Now - item.Time < _klineCacheTtl)
                    {
                        return item.Data;
                    }
                    _klineCache.Remove(key);
                }
            }
            return null;
        }

        private static void SaveCache(string key, KLineData data)
        {
            lock (_klineCacheLock)
            {
                _klineCache[key] = new CacheItem { Data = data, Time = DateTime.Now };
                // 防止缓存无限增长，超过 50 项清理最老的
                if (_klineCache.Count > 50)
                {
                    var oldest = _klineCache.OrderBy(kv => kv.Value.Time).First();
                    _klineCache.Remove(oldest.Key);
                }
            }
        }

        /// <summary>
        /// 计算简单移动平均线 (MA)
        /// </summary>
        public static decimal[] CalculateMA(List<KLinePoint> points, int period)
        {
            var result = new decimal[points.Count];
            decimal sum = 0;

            for (int i = 0; i < points.Count; i++)
            {
                sum += points[i].Close;
                if (i >= period)
                    sum -= points[i - period].Close;

                if (i >= period - 1)
                    result[i] = sum / period;
                else
                    result[i] = 0;
            }
            return result;
        }

        #endregion
    }

    /// <summary>
    /// K线数据点
    /// </summary>
    public class KLinePoint
    {
        public DateTime Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// K线数据
    /// </summary>
    public class KLineData
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public List<KLinePoint> Points { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }

        public KLineData()
        {
            Points = new List<KLinePoint>();
            IsValid = true;
        }
    }

    /// <summary>
    /// K线周期
    /// </summary>
    public enum KLinePeriod
    {
        Daily = 101,
        Weekly = 102,
        Monthly = 103,
        Yearly = 104
    }
}
