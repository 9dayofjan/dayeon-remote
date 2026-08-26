using System;
using System.IO;
using System.Threading;
using NAudio.Wave;

class AudioCap {
    [STAThread]
    static void Main(string[] args) {
        try {
            WasapiLoopbackCapture capture = null;
            try {
                capture = new WasapiLoopbackCapture();
            } catch (Exception ex) {
                Console.Error.WriteLine("Failed to create WasapiLoopbackCapture: " + ex.Message);
                return;
            }

            WaveFormat format = capture.WaveFormat;
            Stream stdout = Console.OpenStandardOutput();
            BinaryWriter bw = new BinaryWriter(stdout);

            uint sampleRate = (uint)format.SampleRate;
            ushort channels = (ushort)format.Channels;
            ushort bitsPerSample = 16;

            bw.Write(sampleRate);
            bw.Write(channels);
            bw.Write(bitsPerSample);
            bw.Flush();

            DateTime lastAudioTime = DateTime.UtcNow;
            byte[] silenceBuffer = new byte[sampleRate * channels * (bitsPerSample / 8) / 20]; // 50ms silence block
            bool isFloat = (format.Encoding == WaveFormatEncoding.IeeeFloat || format.BitsPerSample == 32);

            capture.DataAvailable += (s, e) => {
                try {
                    if (e.BytesRecorded == 0) return;
                    lastAudioTime = DateTime.UtcNow;

                    if (isFloat) {
                        int floatCount = e.BytesRecorded / 4;
                        byte[] pcm16 = new byte[floatCount * 2];
                        for (int i = 0; i < floatCount; i++) {
                            float f = BitConverter.ToSingle(e.Buffer, i * 4);
                            if (f > 1.0f) f = 1.0f;
                            else if (f < -1.0f) f = -1.0f;
                            short sVal = (short)(f * 32767.0f);
                            pcm16[i * 2] = (byte)(sVal & 0xFF);
                            pcm16[i * 2 + 1] = (byte)((sVal >> 8) & 0xFF);
                        }
                        lock (bw) {
                            bw.Write((uint)pcm16.Length);
                            bw.Write(pcm16, 0, pcm16.Length);
                            bw.Flush();
                        }
                    } else {
                        lock (bw) {
                            bw.Write((uint)e.BytesRecorded);
                            bw.Write(e.Buffer, 0, e.BytesRecorded);
                            bw.Flush();
                        }
                    }
                } catch {
                    Environment.Exit(0);
                }
            };

            capture.RecordingStopped += (s, e) => {
                Environment.Exit(0);
            };

            capture.StartRecording();
            Console.Error.WriteLine("WASAPI Loopback Capture RUNNING with NAudio! SampleRate=" + sampleRate + ", Channels=" + channels);

            while (true) {
                Thread.Sleep(50);
                if ((DateTime.UtcNow - lastAudioTime).TotalMilliseconds >= 50) {
                    lock (bw) {
                        try {
                            bw.Write((uint)silenceBuffer.Length);
                            bw.Write(silenceBuffer, 0, silenceBuffer.Length);
                            bw.Flush();
                        } catch {
                            Environment.Exit(0);
                        }
                    }
                }
            }
        } catch (Exception ex) {
            Console.Error.WriteLine("AudioCap Fatal Error: " + ex.Message);
        }
    }
}
