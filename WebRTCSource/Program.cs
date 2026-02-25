using Microsoft.AspNetCore.SignalR.Client;
using SIPSorcery.Net;
using System.Collections.Concurrent;
using WebRTCSource;

class Program
{
    static ConcurrentDictionary<string, RTCPeerConnection> peers = [];
    static readonly string ClientURL = "https://localhost:7214/connectionHub";
    static readonly string GoogleStunServer = "stun:stun.l.google.com:19302";
    static readonly string TargetChannel = "CHANNEL1";
    
    public static async Task Main(string[] args)
    {
        if (args.Length > 0)
        {
            BMPNoiseGenerator.BallEffect = String.Equals(args[0], "-b");
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

                Task.Run(async () =>
                {
                    while (dc.readyState == RTCDataChannelState.open)
                    {
                        byte[] bmpData = BMPNoiseGenerator.CurrentFrame;
                        if(bmpData != null) dc.send(bmpData);
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
                    await connection.InvokeAsync("AddConnection", senderId);
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
        BMPNoiseGenerator.TickNoiseFeed();

        Console.WriteLine("Waiting for connection... Press Enter to exit.");
        Console.ReadLine();
    }
}