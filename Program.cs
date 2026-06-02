using System;
using System.IO;
using BinkaDecoder;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "-d")
        {
            if (args.Length >= 1 && args[0] == "-h")
            {
                Console.WriteLine("BCF1/UEBA Binka Decoder");
                Console.WriteLine("Usage: BinkaDecoder -d <input.binka>");
                return 0;
            }
            Console.WriteLine("Usage: BinkaDecoder -d <input.binka>");
            return 1;
        }

        string inputFile = args[1];
        string outputFile = Path.ChangeExtension(inputFile, ".wav");

        byte[] buffer;
        try
        {
            buffer = File.ReadAllBytes(inputFile);
        }
        catch
        {
            Console.WriteLine("Cannot open input file: " + inputFile);
            return 1;
        }

        int version, channels, sampleRate, numSamples;
        uint dataOffset;
        bool isUeba = false;

        if (buffer[0] == 0x31 && buffer[1] == 0x46 && buffer[2] == 0x43 && buffer[3] == 0x42)
        {
            if (Bcf1Decoder.ParseHeader(buffer, out version, out channels, out sampleRate, out numSamples, out dataOffset) < 0)
            {
                Console.WriteLine("Invalid BCF1 header");
                return 1;
            }
            Console.WriteLine("Format: BCF1");
        }
        else if (buffer[0] == 0x41 && buffer[1] == 0x42 && buffer[2] == 0x45 && buffer[3] == 0x55)
        {
            isUeba = true;
            if (UebaDecoder.ParseHeader(buffer, out version, out channels, out sampleRate, out numSamples, out dataOffset, (uint)buffer.Length) < 0)
            {
                Console.WriteLine("Invalid UEBA header");
                return 1;
            }
            Console.WriteLine("Format: UEBA");
        }
        else
        {
            Console.WriteLine("Unknown format (not BCF1 or UEBA)");
            return 1;
        }

        Console.WriteLine("  Version: " + version);
        Console.WriteLine("  Channels: " + channels);
        Console.WriteLine("  Sample Rate: " + sampleRate + " Hz");
        Console.WriteLine("  Total Samples: " + numSamples + " (" + ((float)numSamples / sampleRate).ToString("F2") + " seconds)");
        Console.WriteLine("  Data Offset: 0x" + dataOffset.ToString("X"));

        float[] allPcm = new float[(long)numSamples * channels];
        int totalSamplesDecoded = 0;
        int frameCount = 0;

        if (isUeba)
        {
            var dec = new UebaDecoder(sampleRate, channels);
            if (!dec.IsValid)
            {
                Console.WriteLine("Failed to create decoder");
                return 1;
            }
            Console.WriteLine("Frame samples: " + dec.FrameSamples);

            totalSamplesDecoded = dec.DecodeFrame(buffer, (uint)buffer.Length, allPcm);
            if (totalSamplesDecoded < 0)
            {
                Console.WriteLine("Decode error");
            }
            else
            {
                frameCount = (totalSamplesDecoded + dec.FrameSamples - 1) / dec.FrameSamples;
                Console.WriteLine("Decoded: " + frameCount + " frames, " + totalSamplesDecoded + "/" + numSamples + " samples");
            }
        }
        else
        {
            var dec = new Bcf1Decoder(sampleRate, channels);
            if (!dec.IsValid)
            {
                Console.WriteLine("Failed to create decoder");
                return 1;
            }
            Console.WriteLine("Frame samples: " + dec.FrameSamples);

            uint pos = dataOffset;
            float[] pcmBuffer = new float[dec.FrameSamples * channels];

            while (pos < buffer.Length)
            {
                int remainingLen = buffer.Length - (int)pos;
                byte[] slice = new byte[remainingLen];
                System.Buffer.BlockCopy(buffer, (int)pos, slice, 0, remainingLen);
                int samples = dec.DecodeFrame(slice, (uint)remainingLen, pcmBuffer);
                if (samples < 0)
                {
                    Console.WriteLine("Decode error at frame " + frameCount + " pos=0x" + pos.ToString("X"));
                    break;
                }
                if (samples == 0)
                {
                    Console.WriteLine("Decode returned 0 at frame " + frameCount + " pos=0x" + pos.ToString("X"));
                    break;
                }

                int remaining = numSamples - totalSamplesDecoded;
                int copySamples = samples < remaining ? samples : remaining;

                System.Buffer.BlockCopy(pcmBuffer, 0, allPcm, totalSamplesDecoded * channels * 4, copySamples * channels * 4);
                totalSamplesDecoded += copySamples;
                frameCount++;

                if (frameCount % 100 == 0)
                {
                    Console.Write("\rDecoding: " + frameCount + " frames, " + totalSamplesDecoded + "/" + numSamples + " samples");
                }

                uint header = System.BitConverter.ToUInt32(buffer, (int)pos);
                ushort sync = (ushort)(header & 0xFFFF);
                ushort packetSize = (ushort)((header >> 16) & 0xFFFF);

                uint bytesConsumed = 4;
                if (packetSize == 0xFFFF)
                {
                    if (pos + 8 > buffer.Length) break;
                    packetSize = System.BitConverter.ToUInt16(buffer, (int)(pos + 4));
                    bytesConsumed += 4;
                }
                bytesConsumed += packetSize;
                pos += bytesConsumed;

                if (totalSamplesDecoded >= numSamples) break;
            }

            Console.WriteLine("\nDecoded " + frameCount + " frames, " + totalSamplesDecoded + " samples");
        }

        if (totalSamplesDecoded > 0)
        {
            if (WriteWav(outputFile, allPcm, totalSamplesDecoded, sampleRate, channels) == 0)
            {
                Console.WriteLine("WAV saved to: " + outputFile);
            }
            else
            {
                Console.WriteLine("Failed to write WAV file");
            }
        }

        return 0;
    }

    static int WriteWav(string filename, float[] samples, int numSamples, int sampleRate, int channels)
    {
        using var f = File.Create(filename);

        float minVal = 0, maxVal = 0;
        for (int i = 0; i < numSamples * channels; i++)
        {
            if (samples[i] < minVal) minVal = samples[i];
            if (samples[i] > maxVal) maxVal = samples[i];
        }
        Console.WriteLine("Sample range: " + minVal.ToString("F6") + " to " + maxVal.ToString("F6"));

        var header = new byte[44];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        System.BitConverter.GetBytes((uint)(36 + numSamples * channels * 2)).CopyTo(header, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);
        System.BitConverter.GetBytes((uint)16).CopyTo(header, 16);
        System.BitConverter.GetBytes((ushort)1).CopyTo(header, 20);
        System.BitConverter.GetBytes((ushort)channels).CopyTo(header, 22);
        System.BitConverter.GetBytes((uint)sampleRate).CopyTo(header, 24);
        System.BitConverter.GetBytes((uint)(sampleRate * channels * 2)).CopyTo(header, 28);
        System.BitConverter.GetBytes((ushort)(channels * 2)).CopyTo(header, 32);
        System.BitConverter.GetBytes((ushort)16).CopyTo(header, 34);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        System.BitConverter.GetBytes((uint)(numSamples * channels * 2)).CopyTo(header, 40);

        f.Write(header, 0, 44);

        byte[] pcmBuf = new byte[numSamples * channels * 2];
        for (int i = 0; i < numSamples * channels; i++)
        {
            float sample = samples[i];
            if (sample > 1f) sample = 1f;
            if (sample < -1f) sample = -1f;
            short pcm = (short)(sample * 32767f);
            pcmBuf[i * 2] = (byte)(pcm & 0xFF);
            pcmBuf[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }
        f.Write(pcmBuf, 0, pcmBuf.Length);

        return 0;
    }
}
