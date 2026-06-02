namespace BinkaDecoder;

public class UebaDecoder
{
    private byte[]? _pktBuf;
    private uint _pktBufSize;
    private uint _pktFilled;
    private int _pktSamples;
    private BinkaDecoderCore _core;
    private int _channels;
    private int _frameSamples;

    public UebaDecoder(int sampleRate, int channels)
    {
        _channels = channels;
        _core = new BinkaDecoderCore(sampleRate, channels, BinkaMode.Ueba);
        if (!_core.IsValid) return;
        _frameSamples = _core.FrameSamples;
        _pktBufSize = 16384;
        _pktBuf = new byte[_pktBufSize];
    }

    public bool IsValid => _core != null && _core.IsValid;

    public static int ParseHeader(byte[] data, out int version, out int channels, out int sampleRate, out int numSamples, out uint dataOffset, uint fileSize)
    {
        version = 0; channels = 0; sampleRate = 0; numSamples = 0; dataOffset = 0;
        if (data[0] != 0x41 || data[1] != 0x42 || data[2] != 0x45 || data[3] != 0x55)
            return -1;
        version = data[4];
        channels = data[5];
        sampleRate = System.BitConverter.ToInt32(data, 8);
        numSamples = System.BitConverter.ToInt32(data, 12);
        ushort flags = System.BitConverter.ToUInt16(data, 18);
        if (version != 1 || flags != 1) return -1;
        int seekEntries = System.BitConverter.ToUInt16(data, 24);
        dataOffset = (uint)(28 + seekEntries * 2);
        return 0;
    }

    private bool ReadFrame(byte[] data, ref uint offset, uint size)
    {
        while (offset + 4 <= size)
        {
            uint header = System.BitConverter.ToUInt32(data, (int)offset);
            if (header == 0x5345454B || header == 0x4B454553)
            {
                offset += 4;
                if (offset + 11 > size) return false;
                int entries = System.BitConverter.ToInt32(data, (int)(offset + 7));
                if (entries <= 0) return false;
                offset += (uint)(11 + entries * 2);
                _pktFilled = 0;
                return true;
            }
            ushort sync = (ushort)(header & 0xFFFF);
            if (sync == 0x9999)
            {
                ushort packetSize = (ushort)((header >> 16) & 0xFFFF);
                offset += 4;
                if (packetSize == 0xFFFF)
                {
                    if (offset + 4 > size) return false;
                    packetSize = System.BitConverter.ToUInt16(data, (int)offset);
                    _pktSamples = System.BitConverter.ToUInt16(data, (int)(offset + 2));
                    offset += 4;
                }
                else _pktSamples = 0;
                if (packetSize == 0 || packetSize > _pktBufSize) return false;
                if (offset + packetSize > size) return false;
                System.Buffer.BlockCopy(data, (int)offset, _pktBuf!, 0, packetSize);
                _pktFilled = packetSize;
                offset += packetSize;
                return true;
            }
            offset++;
        }
        return false;
    }

    public int DecodeFrame(byte[] input, uint inputSize, float[] output)
    {
        if (!IsValid || input == null || inputSize == 0) return 0;
        uint offset = 0;
        int totalSamples = 0;
        int maxOutputSamples = _frameSamples * 100;

        while (offset < inputSize)
        {
            if (!ReadFrame(input, ref offset, inputSize))
                break;
            if (_pktFilled == 0) continue;
            float[] tempOut = new float[_frameSamples * _channels];
            int samples = _core.Decode(_pktBuf!, (int)_pktFilled, tempOut);
            if (samples < 0) return -1;
            if (_pktSamples > 0 && samples > _pktSamples)
                samples = _pktSamples;
            if (totalSamples + samples * _channels > output.Length) break;
            System.Buffer.BlockCopy(tempOut, 0, output, totalSamples * _channels * 4, samples * _channels * 4);
            totalSamples += samples;
        }
        return totalSamples;
    }

    public void Reset() => _core?.Reset();
    public int FrameSamples => _frameSamples;
}