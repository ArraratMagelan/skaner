using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;


namespace курсовая
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        static String HTTP_TEST_HOST;
        static int HTTP_TEST_PORT;
        static int HTTP_TIMEOUT;
        static int PING_COUNT;
        static int PING_DELAY;
        static int PING_TIMEOUT;
        static List<String> PING_HOSTS;
        static int MEASURE_DELAY;
        static String ROUTER_IP;
        static double MAX_PKT_LOSS;
        static String OUT_FILE;
        static bool WRITE_CSV;
        static String CSV_PATTERN;
        static bool prev_inet_ok = true;
        static DateTime first_fail_time;
        static long total_time = 0;
        static int pkt_sent = 0;
        static int success_pkts = 0;
        static int exited_threads = 0;
        static Dictionary<string, int> measure_results = new Dictionary<string, int>();
        static int flag = 0;

      
        private static List<PortInfo> GetOpenPort()
        {
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] tcpEndPoints = properties.GetActiveTcpListeners();
            TcpConnectionInformation[] tcpConnections = properties.GetActiveTcpConnections();

            return tcpConnections.Select(p =>
            {
                return new PortInfo(
                    i: p.LocalEndPoint.Port,
                    local: String.Format("{0}:{1}", p.LocalEndPoint.Address, p.LocalEndPoint.Port),
                    remote: String.Format("{0}:{1}", p.RemoteEndPoint.Address, p.RemoteEndPoint.Port),
                    state: p.State.ToString());
            }).ToList();
        }
        private void button1_Click(object sender, EventArgs e)
        {
            List<PortInfo> pi = GetOpenPort();
            for(int i=0; i<pi.Count;i++)
            {
                listBox1.Items.Add("Адрес локальный: "+ pi[i].Local+"   Порт:  " + pi[i].PortNumber+"    Удаленный адресс:   "+pi[i].Remote+"   Состояние порта:   " + pi[i].State);
            }
        }
        static void MyMethod()
        {
            var config = JsonConvert.DeserializeObject<Dictionary<String, Object>>(File.ReadAllText("setting.json"));

            HTTP_TEST_HOST = (String)config["http_test_host"];
            PING_HOSTS = ((JArray)config["ping_hosts"]).ToObject<List<String>>();
            ROUTER_IP = (String)config["router_ip"];
            HTTP_TEST_PORT = int.Parse((String)config["http_test_port"]);
            HTTP_TIMEOUT = int.Parse((String)config["http_timeout"]);
            PING_COUNT = int.Parse((String)config["ping_count"]);
            PING_TIMEOUT = int.Parse((String)config["ping_timeout"]);
            PING_DELAY = int.Parse((String)config["ping_packet_delay"]);
            MEASURE_DELAY = int.Parse((String)config["measure_delay"]);
            OUT_FILE = (String)config["out_file"];
            WRITE_CSV = bool.Parse((String)config["w_csv"]);
            CSV_PATTERN = (String)config["out_format"];
            MAX_PKT_LOSS = double.Parse((String)config["nq_max_loss"]);

            String CSV_HEADER = CSV_PATTERN
                .Replace("FTIME", "Data")
                .Replace("IUP", "Internet up")
                .Replace("AVGRTT", "Average ping (ms)")
                .Replace("ROUTERRTT", "Ping to router (ms)")
                .Replace("LOSS", "Packet loss, %")
                .Replace("HTTP", "HTTP OK")
                .Replace("STIME", "Time");

            foreach (var host in PING_HOSTS)
            {
                CSV_HEADER = CSV_HEADER.Replace("RN", $"Ping to {host};RN");
            }
            CSV_HEADER = CSV_HEADER.Replace("RN", "\r\n");
            if (WRITE_CSV)
            {
                if (!File.Exists(OUT_FILE)) File.WriteAllText(OUT_FILE, CSV_HEADER);
            }
            flag = 0;
            while (flag == 0)
            {
                monitor_inet();
                Thread.Sleep(MEASURE_DELAY);
            }
         
        }
       
        private void button2_Click(object sender, EventArgs e)
        {

            var config = JsonConvert.DeserializeObject<Dictionary<String, Object>>(File.ReadAllText("setting.json"));
            PING_HOSTS = ((JArray)config["ping_hosts"]).ToObject<List<String>>();
            label4.Text = "Началась проверка ip: " + PING_HOSTS[0]+" " +PING_HOSTS[1];
            new Thread(() => MyMethod()).Start();
            
        }
        private void button4_Click(object sender, EventArgs e)
        {
            label4.Text = "Проверка ip закончена";
            flag = 1;
        }
        static void Save_log(net_state snapshot)
        {
            if (WRITE_CSV)
            {

                String rtts = "";
                int avg_rtt = 0;
                foreach (var ci in PING_HOSTS)
                {
                    rtts += $"{snapshot.avg_rtts[ci]};";
                    avg_rtt += snapshot.avg_rtts[ci];
                }
                avg_rtt = avg_rtt / PING_HOSTS.Count;
                File.AppendAllText(OUT_FILE, CSV_PATTERN
                    .Replace("FTIME", snapshot.measure_time.ToShortDateString())
                    .Replace("IUP", snapshot.inet_ok.ToString())
                    .Replace("AVGRTT", avg_rtt.ToString())
                    .Replace("ROUTERRTT", snapshot.router_rtt.ToString())
                    .Replace("LOSS", snapshot.packet_loss.ToString())
                    .Replace("HTTP", snapshot.http_ok.ToString())
                    .Replace("STIME", snapshot.measure_time.ToShortTimeString())
                    .Replace("RN", $"{rtts}\r\n"));
            }
        }
        static void monitor_inet()
        {
            net_state snapshot = new net_state();
            snapshot.inet_ok = true;
            snapshot.measure_time = DateTime.Now;
            Ping ping = new Ping();
            var prr = ping.Send(ROUTER_IP, PING_TIMEOUT);
            snapshot.router_rtt = prr.Status == IPStatus.Success ? (int)prr.RoundtripTime : PING_TIMEOUT;
            if (prr.Status != IPStatus.Success)
            {
                snapshot.avg_rtts = new Dictionary<string, int>();
                snapshot.http_ok = false;
                snapshot.inet_ok = false;
                snapshot.packet_loss = 1;
                foreach (var ci in PING_HOSTS)
                {
                    snapshot.avg_rtts.Add(ci, PING_TIMEOUT);
                }
                Save_log(snapshot);
                return;
            }
            snapshot.inet_ok = true;
            try
            {
                snapshot.http_ok = true;
                TcpClient tc = new TcpClient();
                tc.BeginConnect(HTTP_TEST_HOST, HTTP_TEST_PORT, null, null);
                Thread.Sleep(HTTP_TIMEOUT);
                if (!tc.Connected)
                {
                    snapshot.http_ok = false;
                }
                tc.Dispose();
            }
            catch { snapshot.http_ok = false; snapshot.inet_ok = false; }
            exited_threads = 0;
            pkt_sent = 0;
            success_pkts = 0;
            total_time = 0;
            measure_results = new Dictionary<string, int>();
            foreach (var ci in PING_HOSTS)
            {
                Thread thread = new Thread(new ParameterizedThreadStart(PingTest));
                thread.Start(ci);
            }
            while (exited_threads < PING_HOSTS.Count) continue;
            snapshot.avg_rtts = measure_results;
            snapshot.packet_loss = (double)(pkt_sent - success_pkts) / pkt_sent;
            snapshot.inet_ok = !(
                snapshot.http_ok == false ||
                ((double)total_time / success_pkts >= 0.75 * PING_TIMEOUT) ||
                snapshot.packet_loss >= MAX_PKT_LOSS ||
                snapshot.router_rtt == PING_TIMEOUT);
            Save_log(snapshot);
            if (prev_inet_ok && !snapshot.inet_ok)
            {
                prev_inet_ok = false;
                first_fail_time = DateTime.Now;
            }
            else if (!prev_inet_ok && snapshot.inet_ok)
            {
                String t_s = new TimeSpan(DateTime.Now.Ticks - first_fail_time.Ticks).ToString(@"hh\:mm\:ss");

                prev_inet_ok = true;
            }
        }
        static void PingTest(Object arg)
        {
            String host = (String)arg;
            int pkts_lost_row = 0;
            int local_success = 0;
            long local_time = 0;
            Ping ping = new Ping();
            for (int i = 0; i < PING_COUNT; i++)
            {
                if (pkts_lost_row == 3)
                {
                    measure_results.Add(host, (int)(local_time / (local_success == 0 ? 1 : local_success)));
                    exited_threads++;
                    return;
                }
                try
                {
                    var result = ping.Send(host, PING_TIMEOUT);
                    if (result.Status == IPStatus.Success)
                    {
                        pkts_lost_row = 0;
                        local_success++;
                        local_time += result.RoundtripTime;
                        total_time += result.RoundtripTime;
                        pkt_sent++;
                        success_pkts++;
                    }
                    switch (result.Status)
                    {
                        case IPStatus.Success: break;
                        case IPStatus.BadDestination:

                            measure_results.Add(host, -1);
                            exited_threads++;
                            return;
                        case IPStatus.DestinationHostUnreachable:
                        case IPStatus.DestinationNetworkUnreachable:
                        case IPStatus.DestinationUnreachable:

                            measure_results.Add(host, -1);
                            exited_threads++;
                            return;
                        case IPStatus.TimedOut:
                            pkts_lost_row++;
                            pkt_sent++;
                            break;
                        default:

                            measure_results.Add(host, -1);
                            exited_threads++;
                            return;
                    }
                }
                catch (Exception xc)
                {

                    exited_threads++;
                    measure_results.Add(host, -1);
                    return;
                }
            }
            measure_results.Add(host, (int)(local_time / (local_success == 0 ? 1 : local_success)));
            exited_threads++;
            return;
        }
        public static ManualResetEvent connectDone = new ManualResetEvent(false);

        private static void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                Socket client = (Socket)ar.AsyncState;
                client.EndConnect(ar);
                connectDone.Set();
            }
            catch (Exception e)
            {

            }
        }

        static void scan()
        { 
        }
        private void button3_Click(object sender, EventArgs e)
        {
            int startport = Convert.ToInt32(textBox2.Text);
            int endport = Convert.ToInt32(textBox3.Text);
            int i;
            listView1.Items.Clear();

            IPAddress addr = IPAddress.Parse(textBox1.Text);

            for (i = startport; i <= endport; i++)
            {
                IPEndPoint ep = new IPEndPoint(addr, i);
                Socket soc = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IAsyncResult asyncResult = soc.BeginConnect(ep, new AsyncCallback(ConnectCallback), soc);

                if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                {
                    soc.Close();
                    listView1.Items.Add("Порт " + i.ToString());
                    listView1.Items[i - startport].SubItems.Add("");
                    listView1.Items[i - startport].SubItems.Add("закрыт");
                    listView1.Items[i - startport].BackColor = Color.Bisque;
                    listView1.Refresh();
                }
                else
                {
                    soc.Close();
                    listView1.Items.Add("Порт " + i.ToString());
                    listView1.Items[i - startport].SubItems.Add("открыт");
                    listView1.Items[i - startport].BackColor = Color.LightGreen;
                }
            }

        }

        private void button5_Click(object sender, EventArgs e)
        {
            int startport =1;
            int endport = 1000;
            int i;
            listView1.Items.Clear();

            IPAddress addr = IPAddress.Parse(textBox1.Text);

            for (i = startport; i <= endport; i++)
            {
                IPEndPoint ep = new IPEndPoint(addr, i);
                Socket soc = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IAsyncResult asyncResult = soc.BeginConnect(ep, new AsyncCallback(ConnectCallback), soc);

                if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                {
                    soc.Close();
                    listView1.Items.Add("Порт " + i.ToString());
                    listView1.Items[i - startport].SubItems.Add("");
                    listView1.Items[i - startport].SubItems.Add("закрыт");
                    listView1.Items[i - startport].BackColor = Color.Bisque;
                    listView1.Refresh();
                }
                else
                {
                    soc.Close();
                    listView1.Items.Add("Порт " + i.ToString());
                    listView1.Items[i - startport].SubItems.Add("открыт");
                    listView1.Items[i - startport].BackColor = Color.LightGreen;
                }
            }

        }

        private void button6_Click(object sender, EventArgs e)
        {
            listView1.Items.Clear();
            IPAddress addr = IPAddress.Parse(textBox1.Text);
            for(int i=1; i<=994;i++)
            {
                if (i == 21 || i == 22 || i == 23 || i == 25 || i == 43 || i == 53 || i == 68 || i == 80 || i == 110
                    || i == 115 || i == 119 || i == 123 || i == 139 || i == 143 || i == 161 || i == 179 || i == 220 || i == 389 || i == 443 || i == 993)
                {
                    IPEndPoint ep = new IPEndPoint(addr, i);
                    Socket soc = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IAsyncResult asyncResult = soc.BeginConnect(ep, new AsyncCallback(ConnectCallback), soc);
                    if (i == 21)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 21.ToString() + " (ftp)");
                            listView1.Items[0].SubItems.Add("");
                            listView1.Items[0].SubItems.Add("закрыт");
                            listView1.Items[0].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 21.ToString() + " (ftp)");
                            listView1.Items[0].SubItems.Add("открыт");
                            listView1.Items[0].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 22)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 22.ToString() + " (ssh)");
                            listView1.Items[1].SubItems.Add("");
                            listView1.Items[1].SubItems.Add("закрыт");
                            listView1.Items[1].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 22.ToString() + " (ssh)");
                            listView1.Items[1].SubItems.Add("открыт");
                            listView1.Items[1].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 23)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 23.ToString() + " (telnet)");
                            listView1.Items[2].SubItems.Add("");
                            listView1.Items[2].SubItems.Add("закрыт");
                            listView1.Items[2].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 23.ToString() + " (telnet)");
                            listView1.Items[2].SubItems.Add("открыт");
                            listView1.Items[2].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 25)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 25.ToString() + " (smtp)");
                            listView1.Items[3].SubItems.Add("");
                            listView1.Items[3].SubItems.Add("закрыт");
                            listView1.Items[3].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 25.ToString() + " (smtp)");
                            listView1.Items[3].SubItems.Add("открыт");
                            listView1.Items[3].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 43)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 43.ToString() + " (whois)");
                            listView1.Items[4].SubItems.Add("");
                            listView1.Items[4].SubItems.Add("закрыт");
                            listView1.Items[4].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 43.ToString() + " (whois)");
                            listView1.Items[4].SubItems.Add("открыт");
                            listView1.Items[4].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 53)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 53.ToString() + " (dns)");
                            listView1.Items[5].SubItems.Add("");
                            listView1.Items[5].SubItems.Add("закрыт");
                            listView1.Items[5].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 53.ToString() + " (dns)");
                            listView1.Items[5].SubItems.Add("открыт");
                            listView1.Items[5].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 68)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 68.ToString() + " (dhcp)");
                            listView1.Items[6].SubItems.Add("");
                            listView1.Items[6].SubItems.Add("закрыт");
                            listView1.Items[6].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 68.ToString() + " (dhcp)");
                            listView1.Items[6].SubItems.Add("открыт");
                            listView1.Items[6].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 80)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 80.ToString() + " (http)");
                            listView1.Items[7].SubItems.Add("");
                            listView1.Items[7].SubItems.Add("закрыт");
                            listView1.Items[7].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 80.ToString() + " (http)");
                            listView1.Items[7].SubItems.Add("открыт");
                            listView1.Items[7].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 110)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 110.ToString() + " (pop3)");
                            listView1.Items[8].SubItems.Add("");
                            listView1.Items[8].SubItems.Add("закрыт");
                            listView1.Items[8].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 110.ToString() + " (pop3)");
                            listView1.Items[8].SubItems.Add("открыт");
                            listView1.Items[8].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 115)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 115.ToString() + " (sftp)");
                            listView1.Items[9].SubItems.Add("");
                            listView1.Items[9].SubItems.Add("закрыт");
                            listView1.Items[9].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 115.ToString() + " (sftp)");
                            listView1.Items[9].SubItems.Add("открыт");
                            listView1.Items[9].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 119)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 119.ToString() + " (nntp)");
                            listView1.Items[10].SubItems.Add("");
                            listView1.Items[10].SubItems.Add("закрыт");
                            listView1.Items[10].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 119.ToString() + " (nntp)");
                            listView1.Items[10].SubItems.Add("открыт");
                            listView1.Items[10].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 123)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 123.ToString() + " (ntp)");
                            listView1.Items[11].SubItems.Add("");
                            listView1.Items[11].SubItems.Add("закрыт");
                            listView1.Items[11].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 123.ToString() + " (ntp)");
                            listView1.Items[11].SubItems.Add("открыт");
                            listView1.Items[11].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 139)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 139.ToString() + " (netbios)");
                            listView1.Items[12].SubItems.Add("");
                            listView1.Items[12].SubItems.Add("закрыт");
                            listView1.Items[12].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 139.ToString() + " (netbios)");
                            listView1.Items[12].SubItems.Add("открыт");
                            listView1.Items[12].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 143)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 143.ToString() + " (imap)");
                            listView1.Items[13].SubItems.Add("");
                            listView1.Items[13].SubItems.Add("закрыт");
                            listView1.Items[13].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 143.ToString() + " (imap)");
                            listView1.Items[13].SubItems.Add("открыт");
                            listView1.Items[13].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 161)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 161.ToString() + " (snmp)");
                            listView1.Items[14].SubItems.Add("");
                            listView1.Items[14].SubItems.Add("закрыт");
                            listView1.Items[14].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 161.ToString() + " (snmp)");
                            listView1.Items[14].SubItems.Add("открыт");
                            listView1.Items[14].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 179)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 179.ToString() + " (bgp)");
                            listView1.Items[15].SubItems.Add("");
                            listView1.Items[15].SubItems.Add("закрыт");
                            listView1.Items[15].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 179.ToString() + " (bgp)");
                            listView1.Items[15].SubItems.Add("открыт");
                            listView1.Items[15].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 220)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 220.ToString() + " (imap3)");
                            listView1.Items[16].SubItems.Add("");
                            listView1.Items[16].SubItems.Add("закрыт");
                            listView1.Items[16].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 220.ToString() + " (imap3)");
                            listView1.Items[16].SubItems.Add("открыт");
                            listView1.Items[16].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 389)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 389.ToString() + " (ldap)");
                            listView1.Items[17].SubItems.Add("");
                            listView1.Items[17].SubItems.Add("закрыт");
                            listView1.Items[17].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 389.ToString() + " (ldap)");
                            listView1.Items[17].SubItems.Add("открыт");
                            listView1.Items[17].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 443)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 443.ToString() + " (https)");
                            listView1.Items[18].SubItems.Add("");
                            listView1.Items[18].SubItems.Add("закрыт");
                            listView1.Items[18].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 443.ToString() + " (https)");
                            listView1.Items[18].SubItems.Add("открыт");
                            listView1.Items[18].BackColor = Color.LightGreen;
                        }
                    }
                    if (i == 993)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(30, false))
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 993.ToString() + " (imaps)");
                            listView1.Items[19].SubItems.Add("");
                            listView1.Items[19].SubItems.Add("закрыт");
                            listView1.Items[19].BackColor = Color.Bisque;
                            listView1.Refresh();
                        }
                        else
                        {
                            soc.Close();
                            listView1.Items.Add("Порт " + 993.ToString() + " (imaps)");
                            listView1.Items[19].SubItems.Add("открыт");
                            listView1.Items[19].BackColor = Color.LightGreen;
                        }
                    }
                }
                else
                {
                    continue;
                }

            }
        }
    }
    struct net_state
    {
        public bool inet_ok;
        public bool http_ok;
        public Dictionary<String, int> avg_rtts;
        public double packet_loss;
        public DateTime measure_time;
        public int router_rtt;
    }
    class PortInfo
    {
        public int PortNumber { get; set; }
        public string Local { get; set; }
        public string Remote { get; set; }
        public string State { get; set; }

        public PortInfo(int i, string local, string remote, string state)
        {
            PortNumber = i;
            Local = local;
            Remote = remote;
            State = state;
        }
    }

}
