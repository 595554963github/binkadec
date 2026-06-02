namespace BinkaDecoder;

public class Bcf1Decoder
{
    private byte[] _pktBuf;
    private uint _pktBufSize;
    private uint _pktFilled;
    private int _pktSamples;
    private BinkaDecoderCore _core;
    private int _channels;
    private int _frameSamples;

    public Bcf1Decoder(int sampleRate, int channels)
    {
        _channels = channels;
        _core = new BinkaDecoderCore(sampleRate, channels, BinkaMode.Bfc);
        if (!_core.IsValid) return;
        _frameSamples = _core.FrameSamples;
        _pktBufSize = 16384;
        _pktBuf = new byte[_pktBufSize];
    }

    public bool IsValid => _core != null && _core.IsValid;

    public static int ParseHeader(byte[] data, out int version, out int channels, out int sampleRate, out int numSamples, out uint dataOffset)
    {
        version = 0; channels = 0; sampleRate = 0; numSamples = 0; dataOffset = 0;
        if (data[0] != 0x31 || data[1] != 0x46 || data[2] != 0x43 || data[3] != 0x42)
            return -1;
        version = data[4];
        channels = data[5];
        sampleRate = System.BitConverter.ToUInt16(data, 6);
        numSamples = System.BitConverter.ToInt32(data, 8);
        int seekEntries;
        if (version == 1)
        {
            seekEntries = System.BitConverter.ToInt32(data, 20);
            dataOffset = 24;
        }
        else if (version == 2)
        {
            seekEntries = System.BitConverter.ToUInt16(data, 20);
            dataOffset = (uint)(24 + seekEntries * 2);
        }
        else return -1;
        return 0;
    }

    private bool ReadFrame(byte[] data, ref uint offset, uint size)
    {
        if (offset + 4 > size) return false;
        uint header = System.BitConverter.ToUInt32(data, (int)offset);
        ushort sync = (ushort)(header & 0xFFFF);
        ushort packetSize = (ushort)((header >> 16) & 0xFFFF);
        offset += 4;
        if (sync != 0x9999) return false;
        if (packetSize == 0xFFFF)
        {
            if (offset + 4 > size) return false;
            packetSize = System.BitConverter.ToUInt16(data, (int)offset);
            _pktSamples = System.BitConverter.ToUInt16(data, (int)(offset + 2));
            offset += 4;
        }
        else _pktSamples = 0;
        if (packetSize > _pktBufSize) return false;
        if (offset + packetSize > size) return false;
        System.Buffer.BlockCopy(data, (int)offset, _pktBuf, 0, packetSize);
        _pktFilled = packetSize;
        offset += packetSize;
        return true;
    }

    public int DecodeFrame(byte[] input, uint inputSize, float[] output)
    {
        if (!IsValid || input == null || inputSize == 0) return 0;
        uint offset = 0;
        if (!ReadFrame(input, ref offset, inputSize)) return -1;
        if (_pktFilled == 0) return 0;
        float[] tempOut = new float[_frameSamples * _channels];
        int samples = _core.Decode(_pktBuf, (int)_pktFilled, tempOut);
        if (samples < 0) return -1;
        if (_pktSamples > 0 && samples > _pktSamples)
            samples = _pktSamples;
        System.Buffer.BlockCopy(tempOut, 0, output, 0, samples * _channels * 4);
        return samples;
    }

    public void Reset() => _core?.Reset();
    public int FrameSamples => _frameSamples;
}
