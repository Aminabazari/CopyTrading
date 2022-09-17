using CefSharp;
using CefSharp.WinForms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CopyTradingBybit
{
    public partial class Form1 : Form
    {
        ChromiumWebBrowser browser;
        HttpClient client = new HttpClient();
        HttpClient clientTelegram = new HttpClient();
        DateTime baseDate = new DateTime(1970, 1, 1);
        bool signal, finished = true;
        string userName, accessId, secretKey, alarmChatId, masterTrader, lastDate;
        string telStr = "";
        Dictionary<string, string> sideDict = new Dictionary<string, string>() { { "Long", "Buy" }, { "Short", "Sell" } };
        //delegate
        delegate void RefreshBrowserCallback();
        public void RefreshBrowser()
        {
            if (this.browser.InvokeRequired)
            {
                RefreshBrowserCallback d = new RefreshBrowserCallback(RefreshBrowser);
                this.Invoke(d, new object[] { });
            }
            else
            {
                browser.Load(masterTrader);
            }
        }
        private async void browser_LoadCompleted(object sender, LoadingStateChangedEventArgs e)
        {
            if (!e.Browser.IsLoading && !signal)
            {
                var script = @"document.querySelectorAll('.ant-tabs-tab-btn')[1].click();";//click statistics of copied trades
                await browser.GetMainFrame().EvaluateScriptAsync(script);
                signal = true;
            }
        }
        public Form1()
        {
            InitializeComponent();
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }
        private void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;
            List<string> res = new List<string>();
            string readText = File.ReadAllText("Codes.txt");
            var resTemp = readText.Split('\n');
            for (int i = 0; i < resTemp.Length; i++)
                if (resTemp[i].Length > 0)
                    res.Add(resTemp[i].Substring(0, resTemp[i].Length - 1));
            this.Text += " (" + res[0] + ")";
            userName = res[0];
            accessId = res[1];
            secretKey = res[2];
            alarmChatId = res[3];
            masterTrader = res[4];
            lastDate = res[5];
            InitializeBrowsers();
            client.BaseAddress = new Uri("https://api.bybit.com");
            timer1.Enabled = true;
        }
        private void InitializeBrowsers()
        {
            signal = false;
            browser = new ChromiumWebBrowser(masterTrader);
            panel1.Controls.Clear();
            panel1.Controls.Add(browser);
            browser = new ChromiumWebBrowser(masterTrader);
            browser.LoadingStateChanged += new EventHandler<LoadingStateChangedEventArgs>(browser_LoadCompleted);
            panel1.Controls.Clear();
            panel1.Controls.Add(browser);
        }
        private async void timer1_Tick(object sender, EventArgs e)//TODO set Timer 60000ms
        {
            if (!signal || !finished)
                return;
            finished = false;
            await CoreFunction();
            signal = false;
            RefreshBrowser();
            finished = true;
        }
        private async Task CoreFunction()
        {
            try
            {
                var script = @"document.querySelectorAll('.ant-table-row').length";//Row Count
                var response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                var count = int.Parse(response.Result.ToString());
                if (count < 9)//No open Trades
                    return;
                script = @"document.querySelectorAll('.ant-table-row')[0].children[4].textContent";//First Opening Date & Time
                response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                var firstDate = response.Result.ToString();
                var theDate = firstDate;
                int cnt = 0, lastCnt = 8;
                while (theDate != lastDate)//we can use while(1) here
                {
                    script = @"document.querySelectorAll('.ant-table-row')[" + cnt.ToString() + "].children[4].textContent";//Opening Date & Time
                    response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                    theDate = response.Result.ToString();
                    if (theDate == lastDate)
                        break;
                    await CopyTrades(cnt);
                    cnt++;
                    if (cnt < 2)
                    {
                        script = @"document.querySelectorAll('.ant-table-row').length";//Row Count
                        response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                        count = int.Parse(response.Result.ToString());
                        lastCnt = count - 8;
                    }
                    if (cnt >= lastCnt)
                    {
                        Logger(alarmChatId, telStr);
                        telStr = "";
                        script = @"document.querySelectorAll('.ant-pagination-item-link')[1].disabled";//Next Page Disabled?
                        response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                        var res = bool.Parse(response.Result.ToString());
                        if (res)
                            break;
                        script = @"document.querySelectorAll('.ant-pagination-item-link')[1].click();";//Next Page
                        await browser.GetMainFrame().EvaluateScriptAsync(script);
                        Thread.Sleep(3000);
                        cnt = 0;
                    }
                }
                if (firstDate != lastDate)
                {
                    lastDate = firstDate;
                    string str = userName + Environment.NewLine + accessId + Environment.NewLine + secretKey + Environment.NewLine + alarmChatId + Environment.NewLine + masterTrader + Environment.NewLine + lastDate + Environment.NewLine;
                    File.WriteAllText("Codes.txt", str);
                }
            }
            catch(Exception ex)
            {
                Logger(alarmChatId, "CoreFunction Error --> " + ex.ToString());
            }
}
        private async Task CopyTrades(int cnt)
        {
            try
            {
                var script = @"document.querySelectorAll('.ant-table-row')[" + cnt.ToString() + "].children[0].children[0].children[0].textContent";//Market Name
                var response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                var market = response.Result.ToString();
                script = @"document.querySelectorAll('.ant-table-row')[" + cnt.ToString() + "].children[0].children[0].children[1].textContent";//Side of Position
                response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                var side = response.Result.ToString().Split(' ')[0];
                script = @"document.querySelectorAll('.ant-table-row')[" + cnt.ToString() + "].children[1].children[0].textContent";//Entry Price
                response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                var entryPrice = response.Result.ToString();
                var entry = entryPrice.Split(' ')[0].Replace(",", "");
                var price = double.Parse(entry);
                script = @"document.querySelectorAll('.ant-table-row')[" + cnt.ToString() + "].children[5].children[0].children[0].textContent";//TP
                response = await browser.GetMainFrame().EvaluateScriptAsync(script);
                var tppPrice = response.Result.ToString();
                var tpp = tppPrice.Replace(",", "");
                double tp = 0.0;
                double.TryParse(tpp, out tp);
                await ByBit_Limit_Order(market, side, price, tp);
            }
            catch(Exception ex)
            {
                Logger(alarmChatId, "CopyTrades Error --> " + ex.ToString());
            }
        }
        private async Task ByBit_Limit_Order(string market, string side, double price, double tp)
        {
            try
            {
                telStr += market + " " + side + " price=" + price + " tp=" + tp + Environment.NewLine;
                if (tp==0.0)
                {
                    if (side == "Long") tp = price * 1.02;
                    else tp = price * 0.98;
                }
                double qty = 0.001;
                if (market == "ETHUSDT") qty = 0.01;
                else if (market == "BTCUSDT") ;
                else return;
                var diff = DateTime.UtcNow - baseDate;
                var timems = ((long)diff.TotalMilliseconds + 5000).ToString();
                var param_str = "api_key=" + accessId + "&close_on_trigger=false" + "&order_type=Limit" + "&price=" + price.ToString() + "&qty=" + qty.ToString() +
                    "&reduce_only=false" + "&side=" + sideDict[side] +
                    "&symbol=" + market + "&take_profit=" + tp.ToString() + "&time_in_force=GoodTillCancel" + "&timestamp=" + timems;
                var sign = CreateSignature(param_str, secretKey);
                Dictionary<string, object> tradeParam =
                  new Dictionary<string, object>()
                  {
                    {"api_key" , accessId },
                    {"close_on_trigger" , false},
                    {"order_type" , "Limit"},
                    {"price" , price.ToString()},
                    {"qty" , qty.ToString()},
                    {"reduce_only" , false},
                    {"side", sideDict[side] },
                    {"symbol", market },
                    {"take_profit" , tp.ToString()},
                    {"time_in_force" , "GoodTillCancel"},
                    {"timestamp" , timems},
                    {"sign" , sign}
                  };
                var json = JsonConvert.SerializeObject(tradeParam);

                var data = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("/private/linear/order/create", data);
                var result = await response.Content.ReadAsStringAsync();
                Logger(alarmChatId, "Position Opened " + market + " " + side + " price=" + price + " tp=" + tp + Environment.NewLine + result);
            }
            catch(Exception ex)
            {
                Logger(alarmChatId, "ByBit_Limit_Order Error --> " + ex.ToString());
            }
        }
        private string CreateSignature(string message, string secret)
        {
            secret = secret ?? "";
            var encoding = new System.Text.ASCIIEncoding();
            byte[] keyByte = encoding.GetBytes(secret);
            byte[] messageBytes = encoding.GetBytes(message);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
                var hex = new StringBuilder(hashmessage.Length * 2);

                foreach (var b in hashmessage)
                {
                    hex.AppendFormat("{0:x2}", b);
                }
                return hex.ToString();
            }
        }
        private void timer2_Tick(object sender, EventArgs e)
        {
            Logger(alarmChatId, "I am Alive...");
        }
        private async Task Logger(string chatId, string s1)
        {
            try
            {
                await clientTelegram.GetStringAsync("https://api.telegram.org/bot" + "2135050880:AAGNiAJdwagxVP4Y3LBsHYqgBFUxk0Hnq8A"
                            + "/sendMessage?chat_id=" + chatId
                            + "&text=ByBit" + Environment.NewLine
                            + userName + Environment.NewLine + s1);
            }
            catch { }
        }
    }
}
