using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static FFTLib.FFT;

namespace ConsoleFFT {
    public static partial class Program {
        private static double[] wavDstBufL;
        private static double[][] wavHist;
        private static double[] fftWindowValues;
        private static double[][] fftHist;
        private static ComplexDouble[] fftBuffer;
        private static int fftHistSize = 4;
        private static int wavHistSize = 1;
        private static int fftSize2;
        private static int fftWavDstIndex;
        private static int ffWavSrcBufIndex;
        private static double fftWindowSum;

        private static void InitFFT() {
            fftSize2 = fftSize / 2;
            fftHist = new double[fftHistSize][];

            wavDstBufL = new double[fftSize];
            wavHist = new double[wavHistSize][];

            for(int i = 0; i < fftHistSize; i++) {
                fftHist[i] = new double[fftSize2];
            }

            for(int i = 0; i < wavHistSize; i++) {
                wavHist[i] = new double[fftSize2];
            }

            fftBuffer = new ComplexDouble[fftSize];
            for(int i = 0; i < fftSize; i++) fftBuffer[i] = new ComplexDouble();

            fftWindowValues = GetWindowValues((FFTSizeConstants)fftSize, FFTWindowConstants.Hanning);
            fftWindowSum = GetWindowSum((FFTSizeConstants)fftSize, FFTWindowConstants.Hanning);

            fftWavDstIndex = 0;
            ffWavSrcBufIndex = 0;
        }

        private static void GetSamples(AutoResetEvent resetEvent = null) {
            int availableSamples = ALC.GetAvailableSamples(audioCapture);

            if(availableSamples > 0) {
                // FIXME: Is this an OpenTK bug?
                //if(OperatingSystem.IsLinux()) availableSamples /= 4;

                if(availableSamples * SampleToByte > buffer.Length * bufferStride) {
                    buffer = new short[MathHelper.NextPowerOfTwo(
                        (int)(availableSamples * SampleToByte / bufferStride + 0.5))];
                }

                ALC.CaptureSamples(audioCapture, buffer, availableSamples);
                FillBuffer(availableSamples);
                resetEvent?.Set();
            }
        }

        private static void FillBuffer(int availableSamples) {
            do {
                while(true) {
                    if(ffWavSrcBufIndex >= availableSamples) {
                        if(fftWavDstIndex >= fftSize) fftWavDstIndex = 0;
                        ffWavSrcBufIndex = 0;
                        break;
                    } else if(fftWavDstIndex >= fftSize) {
                        fftWavDstIndex = 0;
                        switch (renderMode) {
                            case RenderModes.FFT:
                                RunFFT();
                                return;
                            case RenderModes.Waveform:
                                RunWAV();
                                return;
                        }
                        break;
                    }

                    wavDstBufL[fftWavDstIndex] = buffer[ffWavSrcBufIndex] *
                        (renderMode == RenderModes.FFT
                         ? fftWindowValues[fftWavDstIndex]
                         : 1.0);

                    fftWavDstIndex++;
                    ffWavSrcBufIndex++;
                }
            } while(fftWavDstIndex != 0 && ffWavSrcBufIndex != 0);
        }

        private static void RunFFT() {
            FourierTransform(fftSize, wavDstBufL, fftBuffer, false);

            // Shift history back one spot
            Array.Copy(fftHist, 1, fftHist, 0, fftHist.Length - 1);

            // Update the last spot with the new data from the FFT using the Power() function
            fftHist[fftHistSize - 1] = [.. fftBuffer.Select(c => c.Power()) ];
        }

        private static void RunWAV() {
            // Shift history back one spot
            Array.Copy(wavHist, 1, wavHist, 0, wavHist.Length - 1);

            // Update the last spot with the new data from the WAV buffer
            wavHist[wavHistSize - 1] = wavDstBufL;
        }

        private static (int Width, int Height) FFT2Pts(int x, int w, int h, int fftSize, double scale = 1.0) {
            double v = (FFTAvg(x) / fftWindowSum) / 10.0 * scale;
            v = Math.Min(Math.Log10(v + 1) / 10.0 * w, h);
            x = (int)Math.Min(Math.Log10(x + 1) / Math.Log10(fftSize2 - 1) * w, w);

            return (x, (int)v);
        }

        private static double FFTAvg(int x) {
            double v = 0;
            for(int i = 0; i < fftHistSize; i++) {
                v += fftHist[i][x] * (i + 1) / (double)fftHistSize;
            }
            return v / fftHistSize;
        }

        private static double WAVAvg(int x) {
            double v = 0;
            for(int i = 0; i < wavHistSize; i++) {
                v += wavHist[i][x] * (i + 1) / (double)wavHistSize;
            }
            return v / wavHistSize;
        }
    }
}
