using DevExpress.XtraGauges.Core.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Acumatica.Benchmark
{
    public partial class Main : Form
    {
        private DateTime _startTime = DateTime.MinValue;
        private int _totalProcessed = 0;
        private object _syncLock = new object();

        private Queue<string> _acumaticaServers = new Queue<string>();
        private FixedStack<string> _consoleOutput = new FixedStack<string>(5);
        private List<Thread> _threads = new List<Thread>();

        private DateTime _hideStatusTime = DateTime.MinValue;

        public Main()
        {
            InitializeComponent();
            
            arcScaleComponent1.EasingFunction = new BounceEase();
            arcScaleComponent1.EnableAnimation = true;
            labelActivity.Text = String.Empty;

            System.Net.ServicePointManager.DefaultConnectionLimit = 100;

            foreach(var server in Properties.Settings.Default.TestHosts.Split(';'))
            {
                _acumaticaServers.Enqueue(server);
            }

            var t = new Thread(() => WarmupServers());
            t.Start();
        }
        
        private void WarmupServers()
        {
            WriteToConsole("Warming up servers...");

            for(int i = 0; i < _acumaticaServers.Count; i++)
            {
                string url = _acumaticaServers.Dequeue();
                Warmup(url, new Progress<string>(p => WriteToConsole(p)));
                _acumaticaServers.Enqueue(url);
                Application.DoEvents();
            }

            WriteToConsole("Ready!");
        }

        private void StartWorkers(int count)
        {
            WriteToConsole("Starting {0} worker threads", count);

            for (int i = 0; i < count; i++)
            {
                string url = _acumaticaServers.Dequeue();
                var t = new Thread(() =>
                {
                    System.Threading.Thread.Sleep(i * 100); //To ensure load increases gradually
                    RetrieveAndProcessOrdersFromQueue(url, new Progress<string>(p => WriteToConsole(p)));
                });

                t.Start();
                _acumaticaServers.Enqueue(url);
                _threads.Add(t);
            }
        }

        private void Warmup(string url, IProgress<string> progress)
        {
            var processor = new OrderProcessorScreenBasedApi(progress);
            processor.Login(url, Properties.Settings.Default.Username, Properties.Settings.Default.Password, null);
        }

        private void RetrieveAndProcessOrdersFromQueue(string url, IProgress<string> progress)
        {
            var retriever = new OrderRetriever(Properties.Settings.Default.InventoryQueueUrl, Properties.Settings.Default.AwsAccessKey, Properties.Settings.Default.AwsSecret, Amazon.RegionEndpoint.USWest1);
            var processor = new OrderProcessorScreenBasedApi(progress);
            processor.Login(url, Properties.Settings.Default.Username, Properties.Settings.Default.Password, null);

            while (true)
            {
                List<Order> list = retriever.RetrieveFromQueue(Properties.Settings.Default.OrderSize);
                if (list.Count == 0)
                {
                    progress.Report(String.Format("[{0}] Queue is now empty - we can stop.", System.Threading.Thread.CurrentThread.ManagedThreadId));
                    break;
                }
                else
                {
                    progress.Report(String.Format("[{0}] Retrieved {1} orders from queue.", System.Threading.Thread.CurrentThread.ManagedThreadId, list.Count));
                }

                if (processor.ProcessOrders(list))
                {
                    //If we crash at any point, the items will become visible in the queue again for processing by another thread after the visibility timeout has been exceeded
                    retriever.DeleteFromQueue(list);

                    lock (_syncLock)
                    {
                        _totalProcessed += list.Count;
                    }
                }
                else
                {
                    progress.Report(String.Format("[{0}] An error occured processing the orders; they will be left in the queue and we will try again.", System.Threading.Thread.CurrentThread.ManagedThreadId));
                }
            }

            processor.Logout();
        }

        private void WriteToConsole(string format, params object[] args)
        {
            System.Diagnostics.Debug.WriteLine(format, args);

            lock (_syncLock)
            { 
                _consoleOutput.Push(String.Format(format, args));
            }
        }

        private void Main_Activated(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Bounds = Screen.PrimaryScreen.Bounds;
        }

        private void timerGauge_Tick(object sender, EventArgs e)
        {
            if (DateTime.Now >= _hideStatusTime) lblStatus.Visible = false;

            //Update orders/hr. gauge
            if (_startTime == DateTime.MinValue ) return;
            var totalSecondsElapsed = (DateTime.Now - _startTime).TotalSeconds;
            if (totalSecondsElapsed > 0)
            {
                var ordersPerSeconds =_totalProcessed / totalSecondsElapsed;
                var ordersPerHour = ordersPerSeconds * 3600 * 24;

                if ((float)ordersPerHour > arcScaleComponent1.Value)
                {
                    arcScaleComponent1.Value = (float)ordersPerHour;
                }
            }
        }


        private void timerText_Tick(object sender, EventArgs e)
        {
            //Display top 3 lines of activity in a console-like field
            var activity = new StringBuilder();
            lock (_syncLock)
            {
                for (int i = 0; i < 5; i++)
                {
                    activity.AppendLine(_consoleOutput.PeekAt(i));
                }
            }
            labelActivity.Text = activity.ToString();
        }

        private void Main_FormClosed(object sender, FormClosedEventArgs e)
        {
            //Force stop all threads
            foreach(Thread thread in _threads)
            {
                thread.Abort();
            }
        }

        private void Main_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Enter)
            { 
                if(_threads.Count == 0)
                {
                    DisplayStatus("Starting benchmark...", 10);
                    _startTime = DateTime.Now;
                    _totalProcessed = 0;
                    StartWorkers(Properties.Settings.Default.MaximumWorkers);
                }
            }
        }

        private void DisplayStatus(string text, int seconds)
        {
            lblStatus.Text = text;
            lblStatus.Visible = true;
            _hideStatusTime = DateTime.Now.AddSeconds(seconds);
        }

        private void Main_Resize(object sender, EventArgs e)
        {
            gaugeControl1.Width = this.Width;
            gaugeControl1.Height = this.Height - labelActivity.Height - 50;
        }
    }
}
