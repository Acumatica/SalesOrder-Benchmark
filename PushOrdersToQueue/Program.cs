using CsvHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Acumatica.Benchmark.Common;

namespace Acumatica.Benchmark.Queue
{
    class Program
    {
        static void Main(string[] args)
        {
            //This application reads the orders and pushes them to a queue. In real life, the e-commerce back-end would push orders automatically to the queue as they are received.
            //Due to the confidential nature of the test data used for the summit demo, only a single dummy order is provided in orders.csv.
            var ordersQueue = new OrderPusher(Properties.Settings.Default.QueueUrl, Properties.Settings.Default.AwsAccessKey, Properties.Settings.Default.AwsSecret, Amazon.RegionEndpoint.USWest1);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            int count = 0;

            Order currentOrder = null;
            using (StreamReader reader = File.OpenText(Environment.GetCommandLineArgs()[1]))
            {
                var csv = new CsvReader(reader);
                while (csv.Read())
                {
                    string orderNbr = csv.GetField<string>("Name");

                    if (currentOrder == null || currentOrder.OrderNbr != orderNbr)
                    {
                        count++;

                        if (currentOrder != null)
                        {
                            ordersQueue.PushOrderToQueue(currentOrder);
                        }

                        currentOrder = new Order
                        {
                            OrderNbr = orderNbr,
                            OrderDate = csv.GetField<DateTime>("Created at").Date,
                            Email = csv.GetField<string>("Email"),
                            BillingName = csv.GetField<string>("Billing Name"),
                            BillingAddress = csv.GetField<string>("Billing Street"),
                            BillingCompany = csv.GetField<string>("Billing Company"),
                            BillingCity = csv.GetField<string>("Billing City"),
                            BillingZip = csv.GetField<string>("Billing Zip").Replace("'", ""),
                            BillingProvince = csv.GetField<string>("Billing Province"),
                            BillingCountry = csv.GetField<string>("Billing Country"),
                            BillingPhone = csv.GetField<string>("Billing Phone"),
                            ShippingName = csv.GetField<string>("Shipping Name"),
                            ShippingAddress = csv.GetField<string>("Shipping Street"),
                            ShippingCompany = csv.GetField<string>("Shipping Company"),
                            ShippingCity = csv.GetField<string>("Shipping City"),
                            ShippingZip = csv.GetField<string>("Shipping Zip").Replace("'", ""),
                            ShippingProvince = csv.GetField<string>("Shipping Province"),
                            ShippingCountry = csv.GetField<string>("Shipping Country"),
                            ShippingPhone = csv.GetField<string>("Shipping Phone")
                        };
                    }

                    currentOrder.OrderLines.Add(new OrderLine
                    {
                        Description = csv.GetField<string>("Lineitem name"),
                        Quantity = csv.GetField<decimal>("Lineitem quantity"),
                        UnitPrice = csv.GetField<decimal>("Lineitem price")
                    });
                }
            }

            if (currentOrder != null)
            {
                ordersQueue.PushOrderToQueue(currentOrder);
            }

            ordersQueue.Flush();

            sw.Stop();
            Console.WriteLine("Pushed {0} orders to queue in {1} seconds", count, sw.Elapsed.TotalSeconds);
            Console.ReadKey();
        }
    }
}
