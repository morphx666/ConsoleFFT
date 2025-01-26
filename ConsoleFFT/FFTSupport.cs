using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using System;
using static FFTLib.FFT;

namespace ConsoleFFT {
    public static partial class Program {
        private static double[] wavDstBufL;
        private static double[][] wavHist;
        private static double[] fftWindowValues;
        private static double[][] fftHist;
        private static ComplexDouble[] fftBuffer;
        private static int histSize = 8;
        private static int fftSize2;
        private static int fftWavDstIndex;
        private static int ffWavSrcBufIndex;
        private static double fftWindowSum;

        private static void InitFFT() {
            fftSize2 = fftSize / 2;
            fftHist = new double[histSize][];

            wavDstBufL = new double[fftSize];
            wavHist = new double[histSize][];

            for(int i = 0; i < histSize; i++) {
                fftHist[i] = new double[fftSize2];
                wavHist[i] = new double[fftSize];
            }

            fftBuffer = new ComplexDouble[fftSize];
            for(int i = 0; i < fftSize; i++) fftBuffer[i] = new ComplexDouble();

            fftWindowValues = GetWindowValues((FFTSizeConstants)fftSize, FFTWindowConstants.Hanning);
            fftWindowSum = GetWindowSum((FFTSizeConstants)fftSize, FFTWindowConstants.Hanning);

            fftWavDstIndex = 0;
            ffWavSrcBufIndex = 0;
        }

        private static void GetSamples() {
            int availableSamples = ALC.GetAvailableSamples(audioCapture);

            if(availableSamples > 0) {
                // FIXME: Is this an OpenTK bug?
                if(OperatingSystem.IsLinux()) availableSamples /= 4;

                if(availableSamples * SampleToByte > buffer.Length * bufferStride) {
                    buffer = new short[MathHelper.NextPowerOfTwo(
                        (int)(availableSamples * SampleToByte / bufferStride + 0.5))];
                }

                ALC.CaptureSamples(audioCapture, buffer, availableSamples);
                FillBuffer(availableSamples);
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
                        switch(renderMode) {
                            case RenderModes.FFT:
                                RunFFT();
                                goto ExitLoop;
                            case RenderModes.Waveform:
                                RunWAV();
                                goto ExitLoop;
                        }
                        break;
                    }

                    wavDstBufL[fftWavDstIndex] = buffer[ffWavSrcBufIndex] *
                        (renderMode == RenderModes.FFT ? fftWindowValues[fftWavDstIndex] : 1.0);

                    fftWavDstIndex++;
                    ffWavSrcBufIndex++;
                }
            } while(fftWavDstIndex != 0 && ffWavSrcBufIndex != 0);

        ExitLoop:;
        }

        private static void RunFFT() {
            FourierTransform(fftSize, wavDstBufL, fftBuffer, false);

            // Shift history back one spot
            for(int i = 0; i < histSize - 1; i++) Array.Copy(fftHist[i + 1], 0, fftHist[i], 0, fftSize2);

            // Update the last spot with the new data from the FFT using the Power() function
            for(int i = 0; i < fftSize2; i++) fftHist[histSize - 1][i] = fftBuffer[i].Power();
        }

        private static void RunWAV() {
            // Shift history back one spot
            for(int i = 0; i < histSize - 1; i++) Array.Copy(wavHist[i + 1], 0, wavHist[i], 0, fftSize);

            // Update the last spot with the new data from the WAV buffer
            for(int i = 0; i < fftSize; i++) wavHist[histSize - 1][i] = wavDstBufL[i];
        }

        private static (int Width, int Height) FFT2Pts(int x, int w, int h, int fftSize, double scale = 1.0) {
            double v = (FFTAvg(x) / fftWindowSum) / 10.0 * scale;
            v = Math.Min(Math.Log10(v + 1) / 10.0 * w, h);
            x = (int)Math.Min(Math.Log10(x + 1) / Math.Log10(fftSize2 - 1) * w, w);

            return (x, (int)v);
        }

        private static double FFTAvg(int x) {
            double v = 0;
            for(int i = 0; i < histSize; i++) {
                v += fftHist[i][x];
            }
            return v / histSize;
        }

        private static double WAVAvg(int x) {
            double v = 0;
            for(int i = 0; i < histSize; i++) {
                v += wavHist[i][x];
            }
            return v / histSize;
        }
    }
}