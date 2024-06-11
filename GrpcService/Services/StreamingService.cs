using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Diagnostics;

namespace GrpcService.Services
{
    public class StreamingService : Streamer.StreamerBase
    {
        private readonly ILogger<StreamingService> _logger;
        public StreamingService(ILogger<StreamingService> logger)
        {
            _logger = logger;
        }

        public override async Task DownloadTest(StreamRequest request, IServerStreamWriter<Message> responseStream, ServerCallContext context)
        {
            var messageRate = request.Rate;
            var millisecondsBetweenMessages = messageRate == 0 ? 0 : 1000 / (double)messageRate;
            var messageRateDisplay = messageRate == 0 ? "max" : messageRate.ToString();

            Console.Write("\n");
            Console.WriteLine($"Start message pump. {messageRateDisplay} messages per second.");
            
            var sendDurations = new List<double>();

            var messageTimer = Stopwatch.StartNew();
            var testDuration = Stopwatch.StartNew();

            try
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (messageTimer.Elapsed.TotalMilliseconds >= millisecondsBetweenMessages)
                    {
                        messageTimer.Restart();

                        var startTime = Stopwatch.GetTimestamp();
                        var message = new Message
                        {
                            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow), //Timestamp.FromDateTimeOffset(GetTimestamp())
                            Ticks = DateTime.UtcNow.Ticks

                        };

                        await responseStream.WriteAsync(message);
                        sendDurations.Add(Stopwatch.GetElapsedTime(startTime).TotalMilliseconds);
                    }
                }
            }
            catch (Exception)
            {
                //ignore
            }

            testDuration.Stop();

            Console.WriteLine($"Finished message pump.");

            if (sendDurations.Count > 0)
            {
                var estimatedMessageRate = sendDurations.Count / testDuration.Elapsed.TotalSeconds;
                sendDurations.Sort();

                Console.WriteLine($"Test duration: {testDuration.Elapsed}");
                Console.WriteLine($"Messages sent: {sendDurations.Count:N0}");
                Console.WriteLine($"Estimated average send message rate: {estimatedMessageRate:N2} messages per second");

                Console.WriteLine($"Max latency: {GetPercentile(100, sendDurations):0.###}ms");
                Console.WriteLine($"50 percentile latency: {GetPercentile(50, sendDurations):0.###}ms");
                Console.WriteLine($"75 percentile latency: {GetPercentile(75, sendDurations):0.###}ms");
                Console.WriteLine($"90 percentile latency: {GetPercentile(90, sendDurations):0.###}ms");
                Console.WriteLine($"99 percentile latency: {GetPercentile(99, sendDurations):0.###}ms");

                var highLatency = 0.1;
                var highLatencyCount = sendDurations.Count(l => l >= highLatency);

                Console.WriteLine($"Messages above {highLatency}ms {highLatencyCount:N0}");
                Console.Write("\n");
            }
        }

        private static double GetPercentile(int percent, List<double> sortedData)
        {
            if (percent == 100)
            {
                return sortedData[sortedData.Count - 1];
            }

            var i = ((long)percent * sortedData.Count) / 100.0 + 0.5;
            var fractionPart = i - Math.Truncate(i);

            return (1.0 - fractionPart) * sortedData[(int)Math.Truncate(i) - 1] + fractionPart * sortedData[(int)Math.Ceiling(i) - 1];
        }
    }

}
