namespace WebRTCSource
{
    public static class BMPNoiseGenerator
    {
        private static byte[] _currentFrame = [];
        private static readonly byte[] _header = GetBMPHeader(256, 256);
        private static readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(50));

        private static int _blockX = 0;
        private static int _blockY = 0;
        private static int _blockVX = 6;
        private static int _blockVY = 4;
        private const int _blockD = 10;
        public static bool BallEffect = false;
        public static byte[] CurrentFrame => _currentFrame;


        // Create BMP header
        public static byte[] GetBMPHeader(int width, int height)
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
        public static byte[] GenerateBMPNoise(byte[] header, int width, int height, bool bouncingBall = false)
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

        public static void TickNoiseFeed()
        {
            Task.Run(async () =>
            {
                while (await  _timer.WaitForNextTickAsync())
                {
                    _currentFrame = GenerateBMPNoise(_header, 256, 256, BallEffect);
                }
            });
        }
    }
}
