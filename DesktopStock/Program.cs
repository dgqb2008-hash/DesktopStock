using System;
using System.Windows.Forms;

namespace DesktopStock
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 全局启用 TLS 1.2（必须在任何 HTTP 请求前设置，.NET 4.5 默认不开启）
            System.Net.ServicePointManager.SecurityProtocol = 
                (System.Net.SecurityProtocolType)3072 |  // Tls12
                (System.Net.SecurityProtocolType)768  |  // Tls11
                (System.Net.SecurityProtocolType)192;    // Tls
            System.Net.ServicePointManager.Expect100Continue = false;
            System.Net.ServicePointManager.DefaultConnectionLimit = 10;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
