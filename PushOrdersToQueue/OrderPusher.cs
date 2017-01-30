using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Amazon.SQS.Model;
using Amazon.SQS;
using Acumatica.Benchmark.Common;

namespace Acumatica.Benchmark.Queue
{
    public class OrderPusher : IDisposable
    {
        private const int MaxNumberOfMessages = 10; //Hard SQS limit

        AmazonSQSClient _sqsClient;
        List<SendMessageBatchRequestEntry> _entries = new List<SendMessageBatchRequestEntry>();
        string _queueUrl;

        public OrderPusher(string queueUrl, string awsAccessKey, string awsSecret, Amazon.RegionEndpoint region)
        {
            _queueUrl = queueUrl;
            _sqsClient = new AmazonSQSClient(awsAccessKey, awsSecret, region);
        }

        public void PushOrderToQueue(Order order)
        {
            _entries.Add(new SendMessageBatchRequestEntry(Guid.NewGuid().ToString(), JsonConvert.SerializeObject(order)));
            if (_entries.Count == MaxNumberOfMessages)
                Flush();
        }

        public void Flush()
        {
            if (_entries.Count == 0) return;
            _sqsClient.SendMessageBatch(new SendMessageBatchRequest(_queueUrl, _entries));
            _entries.Clear();
        }

        public void Dispose()
        {
            Flush();
            _sqsClient.Dispose();
        }
    }
}
