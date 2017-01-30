using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Acumatica.Benchmark.Common;

namespace Acumatica.Benchmark
{
    public class OrderProcessorScreenBasedApi
    {
        private IProgress<string> _progress;
        private SO301000.Screen _orderScreen;
        private static object _orderSchemaLock = new object();
        private static SO301000.Content _orderSchema;

        private AR303000.Screen _customersScreen;
        private static object _customersSchemaLock = new object();
        private static AR303000.Content _customersSchema;

        private static object _inventoryItemMapLock = new object();
        private static Dictionary<string, string> _inventoryItemMap;

        public OrderProcessorScreenBasedApi(IProgress<string> progress)
        {
            _progress = progress;
        }

        public void Login(string url, string username, string password, string company)
        {
            _progress.Report(String.Format("[{0}] Logging in to {1}...", System.Threading.Thread.CurrentThread.ManagedThreadId, url));

            _orderScreen = new SO301000.Screen();
            _orderScreen.Url = url + "/Soap/SO301000.asmx";
            _orderScreen.EnableDecompression = true;
            _orderScreen.CookieContainer = new System.Net.CookieContainer();
            _orderScreen.Login(username, password);

            _progress.Report(String.Format("[{0}] Logged in to {1}.", System.Threading.Thread.CurrentThread.ManagedThreadId, url));

            _customersScreen = new AR303000.Screen();
            _customersScreen.Url = url + "/Soap/AR303000.asmx";
            _customersScreen.EnableDecompression = true;
            _customersScreen.CookieContainer = _orderScreen.CookieContainer;

            lock (_orderSchemaLock)
            {
                // Threads can share the same schema.
                if (_orderSchema == null)
                {
                    _progress.Report(String.Format("[{0}] Retrieving SO301000 schema...", System.Threading.Thread.CurrentThread.ManagedThreadId));
                    _orderSchema = _orderScreen.GetSchema();
                    if (_orderSchema == null) throw new Exception("SO301000 GetSchema returned null. See AC-73433.");
                }
            }

            lock (_customersSchemaLock)
            {
                if (_customersSchema == null)
                {
                    _progress.Report(String.Format("[{0}] Retrieving AR303000 schema...", System.Threading.Thread.CurrentThread.ManagedThreadId));
                    _customersSchema = _customersScreen.GetSchema();
                    if (_customersSchema == null) throw new Exception("AR303000 GetSchema returned null. See AC-73433.");
                }
            }

            lock (_inventoryItemMapLock)
            {
                if (_inventoryItemMap == null)
                {
                    _progress.Report(String.Format("[{0}] Initializing inventory item map...", System.Threading.Thread.CurrentThread.ManagedThreadId));
                    InitInventoryItemMap(url);
                }
            }
        }

        private void InitInventoryItemMap(string url)
        {
            if(System.IO.File.Exists("InventoryMap.dat"))
            {
                //We cache it to disk for performance
                using (var stream = System.IO.File.OpenRead("InventoryMap.dat"))
                {
                    _inventoryItemMap = DeserializeInventoryMap(stream);
                }
            }
            else
            {
                //The Shopify orders CSV file doesn't contain any foreign key reference to items - we must match by description.
                var screen = new IN202500.Screen();
                screen.Url = url + "/Soap/IN202500.asmx";
                screen.EnableDecompression = true;
                screen.CookieContainer = _orderScreen.CookieContainer;

                var schema = screen.GetSchema();
                if (schema == null) throw new Exception("IN303000 GetSchema returned null.");

                var result = screen.Export(new IN202500.Command[]
                {
                schema.StockItemSummary.ServiceCommands.EveryInventoryID,
                schema.StockItemSummary.InventoryID,
                schema.StockItemSummary.Description,
                }, null, 0, false, false);

                _inventoryItemMap = new Dictionary<string, string>();
                for (int i = 0; i < result.Length; i++)
                {
                    _inventoryItemMap[result[i][1]] = result[i][0].TrimEnd();
                }

                using (var stream = System.IO.File.OpenWrite("InventoryMap.dat"))
                {
                    SerializeInventoryMap(_inventoryItemMap, stream);
                }
            }
        }

        public void SerializeInventoryMap(Dictionary<string, string> dictionary, Stream stream)
        {
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(dictionary.Count);
            foreach (var kvp in dictionary)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
            writer.Flush();
        }

        public Dictionary<string, string> DeserializeInventoryMap(Stream stream)
        {
            BinaryReader reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            var dictionary = new Dictionary<string, string>(count);
            for (int n = 0; n < count; n++)
            {
                var key = reader.ReadString();
                var value = reader.ReadString();
                dictionary.Add(key, value);
            }
            return dictionary;
        }

        public void Logout()
        {
            if (_orderScreen != null)
            {
                try
                {
                    _orderScreen.Logout();
                    _orderScreen = null;
                }
                catch
                {
                    // In real-life we would need to properly log and process the exceptions and only ignore those that can be attributed to network issues and temporary problems.
                }
            }
        }
        
        public bool ProcessOrders(List<Order> list)
        {
            try
            {
                _progress.Report(String.Format("[{0}] Submitting {1} customers to {2}...", System.Threading.Thread.CurrentThread.ManagedThreadId, list.Count, _orderScreen.Url));
                CreateOrUpdateCustomers(list);
                _progress.Report(String.Format("[{0}] Submitted {1} customers to {2}.", System.Threading.Thread.CurrentThread.ManagedThreadId, list.Count, _orderScreen.Url));

                _progress.Report(String.Format("[{0}] Submitting {1} orders to {2}...", System.Threading.Thread.CurrentThread.ManagedThreadId, list.Count, _orderScreen.Url));
                CreateOrders(list);
                _progress.Report(String.Format("[{0}] Submitted {1} orders to {2}.", System.Threading.Thread.CurrentThread.ManagedThreadId, list.Count, _orderScreen.Url));

                return true;
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);

                // In real-life we would need to properly log and process the exceptions and only ignore those that can be attributed to network issues and temporary problems.
                return false;
            }
        }

        private void CreateOrUpdateCustomers(List<Order> list)
        {
            var commands = new List<AR303000.Command>();
            foreach (var order in list)
            {
                commands.AddRange(GetCustomerCommands(order));
            }
            var results = _customersScreen.Submit(commands.ToArray());
        }

        private List<AR303000.Command> GetCustomerCommands(Order order)
        {
            var commands = new List<AR303000.Command>();
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.CustomerSummary.CustomerID, Value = GetCustomerIDFromEmail(order.Email) });
            if (String.IsNullOrEmpty(order.BillingCompany))
            {
                commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.CustomerSummary.CustomerName, Value = order.BillingName.Substring(0, Math.Min(60, order.BillingName.Length)) });
                commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainContact.Attention, Value = order.BillingName.Substring(0, Math.Min(250, order.BillingName.Length)) });
            }
            else
            {
                commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.CustomerSummary.CustomerName, Value = order.BillingCompany.Substring(0, Math.Min(60, order.BillingCompany.Length)) });
                commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainContact.Attention, Value = order.BillingName.Substring(0, Math.Min(250, order.BillingName.Length)) });
            }
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainContact.Email, Value = order.Email });
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainContact.Phone1, Value = order.BillingPhone });
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainAddress.AddressLine1, Value = order.BillingAddress.Substring(0, Math.Min(50, order.BillingAddress.Length)) });
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainAddress.City, Value = order.BillingCity });
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainAddress.Country, Value = order.BillingCountry });
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainAddress.State, Value = order.BillingProvince });
            commands.Add(new AR303000.Value { LinkedCommand = _customersSchema.GeneralInfoMainAddress.PostalCode, Value = order.BillingZip.Substring(0, Math.Min(10, order.BillingZip.Length)) });
            commands.Add(_customersSchema.Actions.Save);

            return commands;
        }

        private string GetCustomerIDFromEmail(string email)
        {
            //E-mail is too long to be used for CustomerID and Shopify API has no other ID field which could be used instead
            //Rather than doing a search by e-mail address we will rely on MD5-based ID for this PoC - collision risk so better solution needed
            const int CustomerIDLength = 10;
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] retVal = md5.ComputeHash(Encoding.Unicode.GetBytes(email));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < CustomerIDLength / 2; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }

                return sb.ToString().ToUpper();
            }
        }

        private void CreateOrders(List<Order> list)
        {
            var commands = new List<SO301000.Command>();
            foreach (var order in list)
            {
                commands.AddRange(GetOrderCommands(order));
            }
            _orderScreen.Submit(commands.ToArray());
        }

        private List<SO301000.Command> GetOrderCommands(Order order)
        {
            var commands = new List<SO301000.Command>();
            commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.OrderSummary.OrderType, Value = "SO" });
            commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.OrderSummary.OrderNbr, Value = order.OrderNbr });
            commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.OrderSummary.Customer, Value = GetCustomerIDFromEmail(order.Email) });
            commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.OrderSummary.Date, Value = order.OrderDate.ToString(System.Globalization.CultureInfo.InvariantCulture) });

            if (order.BillingCompany != order.ShippingCompany || order.BillingName != order.ShippingName)
            {
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfoOverrideContact.OverrideContact, Value = "True" });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfoOverrideContact.BusinessName, Value = order.ShippingCompany });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfoOverrideContact.Attention, Value = order.ShippingName });
            }

            if (order.BillingAddress != order.ShippingAddress || order.BillingCity != order.ShippingCity || order.BillingProvince != order.ShippingProvince)
            {
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfo.OverrideAddress, Value = "True" });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfo.AddressLine1, Value = order.ShippingAddress.Substring(0, Math.Min(50, order.ShippingAddress.Length)) });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfo.City, Value = order.ShippingCity });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfo.Country, Value = order.ShippingCountry });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfo.State, Value = order.ShippingProvince });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.ShippingSettingsShipToInfo.PostalCode, Value = order.ShippingZip.Substring(0, Math.Min(10, order.ShippingZip.Length)) });
            }

            foreach (var line in order.OrderLines)
            {
                commands.Add(_orderSchema.DocumentDetails.ServiceCommands.NewRow);

                if (_inventoryItemMap.ContainsKey(line.Description))
                {
                    commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.DocumentDetails.InventoryID, Value = _inventoryItemMap[line.Description] });
                }
                else
                {
                    _progress.Report(String.Format("ERROR: No product with description '{0}' was found. Default item will be used.", line.Description));
                    commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.DocumentDetails.InventoryID, Value = "DESIGN" });
                    commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.DocumentDetails.LineDescription, Value = line.Description });
                }

                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.DocumentDetails.Quantity, Value = line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.DocumentDetails.UnitPrice, Value = line.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                commands.Add(new SO301000.Value { LinkedCommand = _orderSchema.DocumentDetails.Warehouse, Value = "WHOLESALE" });

            }
            commands.Add(_orderSchema.Actions.Save);

            return commands;
        }
    }
}
