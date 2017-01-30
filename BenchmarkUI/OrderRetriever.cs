using Amazon.SQS;
using Amazon.SQS.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Acumatica.Benchmark
{
    public class OrderRetriever : IDisposable
    {
        private const int MaxPerRequest = 10; //Hard SQS limit
        
        private AmazonSQSClient _sqsClient;
        private string _queueUrl;

        public OrderRetriever(string queueUrl, string awsAccessKey, string awsSecret, Amazon.RegionEndpoint region)
        {
            _sqsClient = new AmazonSQSClient(awsAccessKey, awsSecret, region);
            _queueUrl = queueUrl;
        }

        public int GetApproximateNumberOfMessagesInQueue()
        {
            var qar = new GetQueueAttributesRequest();
            qar.QueueUrl = _queueUrl;
            qar.AttributeNames = new List<string> { "ApproximateNumberOfMessages" };
            var attributes = _sqsClient.GetQueueAttributes(qar);

            return attributes.ApproximateNumberOfMessages;
        }

        public List<Order> RetrieveFromQueue(int count)
        {
            var list = new List<Order>();

            while (list.Count < count)
            {
                var rmr = new ReceiveMessageRequest();
                rmr.VisibilityTimeout = 120;
                rmr.QueueUrl = _queueUrl;

                if (count > MaxPerRequest)
                {
                    rmr.MaxNumberOfMessages = MaxPerRequest;
                }
                else
                {
                    rmr.MaxNumberOfMessages = count - list.Count;
                }

                ReceiveMessageResponse response = _sqsClient.ReceiveMessage(rmr);

                if (response.Messages.Count == 0) break;

                foreach (Message message in response.Messages)
                {
                    var detail = JsonConvert.DeserializeObject<Order>(message.Body);
                    detail.ReceiptHandle = message.ReceiptHandle;
                    list.Add(detail);
                }
            }

            return list;
        }

        public void DeleteFromQueue(List<Order> orders)
        {
            foreach (var orderBatches in orders.Partition(MaxPerRequest))
            {
                var entries = new List<DeleteMessageBatchRequestEntry>();
                foreach (var order in orderBatches)
                {
                    entries.Add(new DeleteMessageBatchRequestEntry { Id = Guid.NewGuid().ToString(), ReceiptHandle = order.ReceiptHandle });
                }

                var dmr = new DeleteMessageBatchRequest();
                dmr.Entries = entries;
                dmr.QueueUrl = _queueUrl;
                _sqsClient.DeleteMessageBatch(dmr);
            }
        }

        public void Dispose()
        {
            _sqsClient.Dispose();
        }
    }
}
