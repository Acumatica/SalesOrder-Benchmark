using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Acumatica.Benchmark
{
    public class Order : Acumatica.Benchmark.Common.Order
    {
        public string ReceiptHandle { get; set; }
    }
}
