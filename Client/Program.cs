using Grpc.Core;
using Grpc.Net.Client;
using GrpcService;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime;

namespace Client
{
    internal class Program
    {
        private static Streamer.StreamerClient _client;

        static async Task<int> Main(string[] args)
        {
            var rateOption = new Option<int>(new[] { "-m", "--mps" }, "The number of messages per second the server will publish messages. 0 For maximum rate") { IsRequired = true };
            var durationOption = new Option<int>(new[] { "-d", "--duration" }, () => 10, "Duration of the test in seconds");
            var warmupOption = new Option<int>(new[] { "-w", "--warmup" }, () => 5, "Duration of the warmup in seconds");
            var useUnixDomainSocketOption = new Option<bool>(new[] { "-uds" }, "Use Unix Domain Sockets");

            var rootCommand = new RootCommand();
            rootCommand.AddOption(rateOption);
            rootCommand.AddOption(durationOption);
            rootCommand.AddOption(warmupOption);
            rootCommand.AddOption(useUnixDomainSocketOption);

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

                using var grpcChannel = context.ParseResult.GetValueForOption(useUnixDomainSocketOption)
                    ? CreateUnixDomainSocketChannel()
                    : CreateChannel("http://localhost:5001");
              
                _client = new Streamer.StreamerClient(grpcChannel);

                var messageRate = context.ParseResult.GetValueForOption(rateOption);
                var warmUpTime = TimeSpan.FromSeconds(context.ParseResult.GetValueForOption(warmupOption));
                var recordingTime = TimeSpan.FromSeconds(context.ParseResult.GetValueForOption(durationOption));

                try
                {
                    await DownloadTest(messageRate, warmUpTime, recordingTime);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            });
            

            return await rootCommand.InvokeAsync(args);
        }

        public record struct MessageRecord(DateTimeOffset MessageTime, DateTimeOffset ReceivedTime, double MessageLatency);
        private static async Task DownloadTest(int messageRate, TimeSpan warmUpTime, TimeSpan recordingTime)
        {
            var cts = new CancellationTokenSource();


            var callOptions = new CallOptions().WithCancellationToken(cts.Token);
            callOptions.WithWriteOptions(new WriteOptions(WriteFlags.NoCompress));
            using var serverStreamingCall = _client.DownloadTest(new StreamRequest { Rate = messageRate }, callOptions);
            
            var recordings = new List<MessageRecord>();
            
            Console.WriteLine($"Start download test");
            var messageRateDisplay = messageRate == 0 ? "Maximum" : messageRate.ToString();
            Console.WriteLine($"Message rate: {messageRateDisplay} per second");
            Console.WriteLine($"Warm up duration: {warmUpTime}");
            Console.WriteLine($"Testing duration: {recordingTime}");
            
            cts.CancelAfter(warmUpTime + recordingTime);
            var workerTimer = Stopwatch.StartNew();
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await serverStreamingCall.ResponseStream.MoveNext();

                    if (workerTimer.Elapsed <= warmUpTime)
                        continue;

                    var message = serverStreamingCall.ResponseStream.Current;
                    var messageTime = new DateTime(message.Ticks, DateTimeKind.Utc);
                    
                    var receiveTime = DateTimeOffset.UtcNow; //GetTimestamp();
                    var latency = receiveTime - messageTime;

                    recordings.Add(new MessageRecord(messageTime, receiveTime, latency.TotalMilliseconds));
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    break;
                }
            }

            var recordingDuration = recordings[^1].ReceivedTime - recordings[0].ReceivedTime;
            var estimatedMessageRate = recordings.Count / recordingDuration.TotalSeconds;
            var averageLatency = recordings.Average(x => x.MessageLatency);

            Console.Write("\n");
            Console.WriteLine($"**************");
            Console.WriteLine($"Download test finished.");
            Console.WriteLine($"Average Latency: {averageLatency:N4}ms");
            Console.WriteLine($"Message count: {recordings.Count:N0}");
            Console.WriteLine($"Recording duration: {recordingDuration}");
            Console.WriteLine($"Estimated received message rate: {estimatedMessageRate:N2} messages per second");

            var sortedLatencies = recordings.Select(r => r.MessageLatency).Order().ToList();
           

            var highLatency = 0.1;
            var highLatencyCount = sortedLatencies.Count(l => l >= highLatency);

            Console.WriteLine($"Messages above {highLatency}ms {highLatencyCount:N0}");
            Console.WriteLine($"Max latency: {GetPercentile(100, sortedLatencies):0.###}ms");
            Console.WriteLine($"50 percentile latency: {GetPercentile(50, sortedLatencies):0.###}ms");
            Console.WriteLine($"75 percentile latency: {GetPercentile(75, sortedLatencies):0.###}ms");
            Console.WriteLine($"90 percentile latency: {GetPercentile(90, sortedLatencies):0.###}ms");
            Console.WriteLine($"99 percentile latency: {GetPercentile(99, sortedLatencies):0.###}ms");
            Console.WriteLine($"**************");
            Console.Write("\n");
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

        private static GrpcChannel CreateUnixDomainSocketChannel()
        {
            var socketPath = Path.Combine(Path.GetTempPath(), "GrpcServiceStreamTest.socket");
            Console.WriteLine($"Using Unix Domain Sockets: {socketPath}");
            return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = CreateHttpHandler(socketPath),
                Credentials = ChannelCredentials.Insecure,
            });
        }

        private static GrpcChannel CreateChannel(string address)
        {
            var httpClientHandler = new SocketsHttpHandler();
            return GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = httpClientHandler,
            });
        }

        private static SocketsHttpHandler CreateHttpHandler(string socketPath)
        {
            var udsEndPoint = new UnixDomainSocketEndPoint(socketPath);
            var connectionFactory = new UnixDomainSocketConnectionFactory(udsEndPoint);
            var socketsHttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = connectionFactory.ConnectAsync
            };

            return socketsHttpHandler;
        }
    }
}
