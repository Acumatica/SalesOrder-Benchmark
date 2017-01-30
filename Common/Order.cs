using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Acumatica.Benchmark.Common
{
    public class Order
    {
        public string OrderNbr;
        public DateTime OrderDate;
        public string Email;
        public string BillingName;
        public string BillingAddress;
        public string BillingCompany;
        public string BillingCity;
        public string BillingZip;
        public string BillingProvince;
        public string BillingCountry;
        public string BillingPhone;
        public string ShippingName;
        public string ShippingAddress;
        public string ShippingCompany;
        public string ShippingCity;
        public string ShippingZip;
        public string ShippingProvince;
        public string ShippingCountry;
        public string ShippingPhone;
        public List<OrderLine> OrderLines = new List<OrderLine>();
    }

    public class OrderLine
    {
        public string Description;
        public decimal Quantity;
        public decimal UnitPrice;
    }
}
