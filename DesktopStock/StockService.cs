using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

                // 解析腾讯分时数据: 每行为 "HHmm price volume turnover"
                var points = new List<TrendPoint>();
                decimal runningSum = 0;
                int count = 0;

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

                    runningSum += price;
                    count++;
                    decimal avgPrice = count > 0 ? runningSum / count : price;

                    points.Add(new TrendPoint
                    {
                        Time = time,
                        Price = price,
                        AvgPrice = avgPrice,
                        Volume = 0  // 不需要成交量
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
    }
}
