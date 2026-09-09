using NAudio.Wave;
using System;

namespace AudioVisualizerWidget
{
    class SampleReader
    {
        private readonly int _bytesPerSample;
        private readonly int _bytesPerFrame;
        private readonly int _channels;

        public SampleReader(WaveFormat format)
        {
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            if (format.Encoding != WaveFormatEncoding.IeeeFloat)
                throw new ArgumentException("Only IEEE float format is supported", nameof(format));

            if (format.BitsPerSample != 32)
                throw new ArgumentException("Only 32-bit float format is supported", nameof(format));

            _channels = format.Channels;
            _bytesPerSample = format.BitsPerSample / 8;
            _bytesPerFrame = format.Channels * _bytesPerSample;
        }

        public int NumSamples(int numBytes)
        {
            if (numBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(numBytes), "Number of bytes cannot be negative");

            if (_bytesPerFrame <= 0)
                return 0;

            return numBytes / _bytesPerFrame;
        }

        public void ReadSamples(byte[] data, int dataCount, double[] dest)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            
            if (dest == null)
                throw new ArgumentNullException(nameof(dest));

            if (dataCount < 0 || dataCount > data.Length)
                throw new ArgumentOutOfRangeException(nameof(dataCount), "Data count is out of range");

            if (dataCount == 0 || dest.Length == 0)
                return;

            var size = dest.Length;
            var sampleCount = NumSamples(dataCount);
            sampleCount = Math.Min(size, sampleCount);

            if (sampleCount <= 0)
                return;

            var offset = size - sampleCount;
            
            if (offset > 0)
                Array.Copy(dest, sampleCount, dest, 0, offset);

            for (int i = 0; i < sampleCount; i++)
            {
                try
                {
                    dest[offset + i] = ReadSample(data, i);
                }
                catch (Exception)
                {
                    dest[offset + i] = 0.0;
                }
            }
        }

        private double ReadSample(byte[] data, int idx)
        {
            if (idx < 0 || (idx * _bytesPerFrame + (_channels * _bytesPerSample)) > data.Length)
                return 0.0;

            double result = 0;
            var pos = idx * _bytesPerFrame;
            
            for (int i = 0; i < _channels; i++)
            {
                try
                {
                    if (pos + (_bytesPerSample * i) + 3 < data.Length)
                    {
                        var val = BitConverter.ToSingle(data, pos + _bytesPerSample * i);
                        if (!float.IsNaN(val) && !float.IsInfinity(val))
                        {
                            result += val;
                        }
                    }
                }
                catch
                {
                    // Skip this channel if there's an error
                }
            }
            
            return _channels > 0 ? result / _channels : 0.0;
        }
    }
}
