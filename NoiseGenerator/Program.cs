using Microsoft.AspNetCore.SignalR.Client;
using SIPSorcery.Net;
using System.Collections.Concurrent;

class Program
{
    static ConcurrentDictionary<string, RTCPeerConnection> peers = [];
    static readonly string ClientURL = "https://localhost:7214/connectionHub";
    static readonly string GoogleStunServer = "stun:stun.l.google.com:19302";
    static readonly string TargetChannel = "CHANNEL1";
    
    static bool BallEffect = false;
    static int _blockX = 0;
    static int _blockY = 0;
    static int _blockVX = 6;
    static int _blockVY = 4;
    const int _blockD = 10;

    // Create BMP header
    static byte[] GetBMPHeader(int width, int height)
    {
        byte[] header = new byte[54];
        using var ms = new MemoryStream(header);
        using var bw = new BinaryWriter(ms);

        int dataSize = width * height * 3;
        int fileSize = 54 + dataSize;

        bw.Write(new char[] { 'B', 'M' }); 
        bw.Write(fileSize);                
        bw.Write(0);                       
        bw.Write(54);                      
        bw.Write(40);                      
        bw.Write(width);                   
        bw.Write(height);                  
        bw.Write((short)1);                
        bw.Write((short)24);               
        bw.Write(0);                     
        bw.Write(dataSize);                
        bw.Write(0); bw.Write(0);          
        bw.Write(0); bw.Write(0);  

        return header;
    }

    // Create BMP noise
    static byte[] GenerateBMPNoise(byte[] header, int width, int height, bool bouncingBall = false)
    {
        int dataSize = width * height * 3;
        byte[] frame = new byte[header.Length + dataSize];

        // Copy pre-built header and load noise
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Span<byte> pixelSpan = new(frame, header.Length, dataSize);
        Random.Shared.NextBytes(pixelSpan);

        if (!bouncingBall)
        {
            return frame;
        }

        _blockX += _blockVX;
        _blockY += _blockVY;

        // Collision Detection AABB
        if (_blockX - _blockD < 0)
        {
            _blockX = _blockD;
            _blockVX *= -1;
        }
        else if (_blockX + _blockD >= width)
        {
            _blockX = width - _blockD - 1;
            _blockVX *= -1;
        }

        if (_blockY - _blockD < 0)
        {
            _blockY = _blockD;
            _blockVY *= -1;
        }
        else if (_blockY + _blockD >= height)
        {
            _blockY = height - _blockD - 1;
            _blockVY *= -1;
        }

        // Bounding Box
        int startY = Math.Max(0, _blockY - _blockD);
        int endY = Math.Min(height - 1, _blockY + _blockD);
        int startX = Math.Max(0, _blockX - _blockD);
        int endX = Math.Min(width - 1, _blockX + _blockD);

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                int dx = x - _blockX;
                int dy = y - _blockY;

                // Fill Circle
                if (dx * dx + dy * dy <= _blockD * _blockD)
                {
                    int offset = (y * width + x) * 3;
                    pixelSpan[offset] = 255;     
                    pixelSpan[offset + 1] = 255; 
                    pixelSpan[offset + 2] = 255; 
                }
            }
        }

        return frame;
    }

    public static async Task Main(string[] args)
    {
        if (args.Length > 0)
        {
            BallEffect = String.Equals(args[0], "-b");
        }

        var connection = new HubConnectionBuilder()
            .WithUrl(ClientURL)
            .WithAutomaticReconnect()
            .Build();

        try
        {
            await connection.StartAsync();
            Console.WriteLine("Connected to SignalR Hub.");
            Console.WriteLine("Tuning Channel 1....");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to connect with server hub !\n {e.Message}");
            return;
        }
        
        // Setup RTC Configuration with stun server
        var config = new RTCConfiguration
        {
            iceServers = [ 
                new RTCIceServer() 
                { 
                    urls = GoogleStunServer 
                }
            ]
        };

        // Receive feed request
        connection.On<string>("ReceiveFeedRequest", async (requestId) =>
        {
            var pc = new RTCPeerConnection(config);
            Console.WriteLine($"Feed request from {requestId}");
            peers.TryAdd(requestId, pc);

            // Handle outgoing ICE candidates
            pc.onicecandidate += (candidate) =>
            {
                if (candidate != null)
                {
                    Console.WriteLine($"Sending ICE Candidate...");
                    connection.InvokeAsync("SendIceCandidate", requestId, candidate.ToString());
                }
            };

            // Setup data channel / feed
            var dc = await pc.createDataChannel("noise-channel");
            dc.onopen += () =>
            {
                Console.WriteLine("Data channel open for sending frames !");
                Console.WriteLine("----------------------------------------------");

                byte[] header = GetBMPHeader(256, 256);

                Task.Run(async () =>
                {
                    while (dc.readyState == RTCDataChannelState.open)
                    {
                        byte[] bmpData = GenerateBMPNoise(header, 256, 256, BallEffect);
                        dc.send(bmpData);
                        await Task.Delay(50); // ~5 Frames Per Second
                    }
                });
            };

            // Initiate handshake
            var offer = pc.createOffer(null);
            await pc.setLocalDescription(offer);
            await connection.InvokeAsync("SendSignal", requestId, offer.sdp.ToString());
            Console.WriteLine("Sending offer to browser");
        });

        // Handle incoming ICE Candidates
        connection.On<string, string>("ReceiveIceCandidate", (senderId, candidateStr) =>
        {
            if (peers.TryGetValue(senderId, out var pc))
            {
                var cObj = RTCIceCandidate.Parse(candidateStr);
                var candidateInit = new RTCIceCandidateInit
                {
                    candidate = cObj.candidate
                };
                pc.addIceCandidate(candidateInit);
                Console.WriteLine("Added remote ICE candidate.");
            }             
        });

        // Handle handshaking for P2P connection
        connection.On<string, string>("ReceiveAcknowledgment", async (senderId, signal) =>
        {
            Console.WriteLine("Received acknowledgement from browser");
            if (peers.TryGetValue(senderId, out var pc))
            {
                var descriptionInit = new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.answer,
                    sdp = signal
                };

                var result = pc.setRemoteDescription(descriptionInit);
                if (result == SetDescriptionResultEnum.OK)
                {
                    Console.WriteLine("P2P Handshake Complete!");

                    // Send count
                    await connection.InvokeAsync("SendCount", peers.Count);
                }
            }
        });

        // Disconnect callback
        connection.On<string>("RemoveConnection", async (viewerId) =>
        {
            if (peers.TryRemove(viewerId, out var pc))
            {
                Console.WriteLine($"Viewer {viewerId} disconnected.");
                pc.Close("Viewer left");
                pc.Dispose();
                
                // Send count
                await connection.InvokeAsync("SendCount", peers.Count);
            }
        });

        // Join and wait for others
        await connection.InvokeAsync("JoinStream", TargetChannel);

        Console.WriteLine("Waiting for connection... Press Enter to exit.");
        Console.ReadLine();
    }
}