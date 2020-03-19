using OpenTK;
using System;
using static FFTLib.FFT;

namespace ConsoleFFT {
    public static partial class Program {
        private static double[] fftWavDstBufL;
        private static double[] fftWindowValues;
        private static double[][] fftHist;
        private static ComplexDouble[] fftBuffer;
        private static int fftHistSize = 8;
        private static int fftSize2;
        private static int fftWavDstIndex;
        private static int ffWavSrcBufIndex;
        private static double fftWindowSum;

        private static void InitFFT() {
            fftSize2 = (int)fftSize / 2;
            fftHist = new double[fftHistSize][];
            fftWavDstBufL = new double[(int)fftSize];

            for(int i = 0; i < fftHistSize; i++) fftHist[i] = new double[fftSize2];

            fftBuffer = new ComplexDouble[fftSize];
            for(int i = 0; i < (int)fftSize; i++) fftBuffer[i] = new ComplexDouble();

            fftWindowValues = GetWindowValues((FFTSizeConstants)fftSize, FFTWindowConstants.Hanning);
            fftWindowSum = GetWindowSum((FFTSizeConstants)fftSize, FFTWindowConstants.Hanning);

            fftWavDstIndex = 0;
            ffWavSrcBufIndex = 0;
        }

        private static void GetSamples() {
            int availableSamples = audioCapture.AvailableSamples;
            if(availableSamples * SampleToByte > buffer.Length * BlittableValueType.StrideOf(buffer)) {
                buffer = new short[MathHelper.NextPowerOfTwo(
                    (int)(availableSamples * SampleToByte / (double)BlittableValueType.StrideOf(buffer) + 0.5))];
            }

            if(availableSamples > 0) {
                audioCapture.ReadSamples(buffer, availableSamples);
                FillFFTBuffer(availableSamples);
            }
        }

        private static void FillFFTBuffer(int availableSamples) {
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
                                break;
                            case RenderModes.Waveform:
                                //TODO: Implement Waveform averaging
                                break;
                        }
                        break;
                    }

                    fftWavDstBufL[fftWavDstIndex] = buffer[ffWavSrcBufIndex] *
                        (renderMode == RenderModes.FFT ?
                            fftWindowValues[fftWavDstIndex] : 1.0);

                    fftWavDstIndex++;
                    ffWavSrcBufIndex++;
                }
            } while(fftWavDstIndex != 0 && ffWavSrcBufIndex != 0);
        }

        private static void RunFFT() {
            FourierTransform(fftSize, fftWavDstBufL, fftBuffer, false);

            // Shift history back one spot
            for(int i = 0; i < fftHistSize - 1; i++) Array.Copy(fftHist[i + 1], 0, fftHist[i], 0, fftSize2);

            // Update the last spot with the new data from the FFT using the Power() function.
            for(int i = 0; i < fftSize2; i++) fftHist[fftHistSize - 1][i] = fftBuffer[i].Power();
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
                v += fftHist[i][x];
            }
            return v / fftHistSize;
        }
    }
}