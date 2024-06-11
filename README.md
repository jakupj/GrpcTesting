# GrpcTesting

Basic client/server apps to test grpc end-to-end latency

### Instructions

1. Open a terminal and go to the folder '*GrpcService*' and run `dotnet run -c release`
2. Open a new terminal, go to the folder '*Client*' and run `dotnet run -c release -m 200 -d 60 -uds`
    
    The client options are:
    - `-m <mps>` messages per second the service will stream to the client. 0 for maximum rate
    - `-d <duration>` the duration of the test in seconds
    - `-w <warmup>` optional warmup in seconds. Defaults to 5 seconds
    - `-uds` use unix domain sockets
  


