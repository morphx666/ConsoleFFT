using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FFTLib.FFT;

namespace ConsoleFFT {
    public partial class Program {
        static double[] fftWavDstBufL;
        static double[] fftWindowValues;
        static double[][] fftHist;
        static ComplexDouble[] fftBuffer;
        static int fftHistSize = 4;
        static int fftSize2;
        static int fftWavDstIndex;
        static int ffWavSrcBufIndex;
        static double fftWindowSum;

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
                        RunFFT();
                        break;
                    }

                    fftWavDstBufL[fftWavDstIndex] = buffer[ffWavSrcBufIndex] * fftWindowValues[fftWavDstIndex];
                    fftWavDstIndex++;
                    ffWavSrcBufIndex++;
                }
            } while(fftWavDstIndex != 0 && ffWavSrcBufIndex != 0);
        }

        private static void RunFFT() {
            FourierTransform(fftSize, fftWavDstBufL, ref fftBuffer, false);

            // Shift history back one spot
            for(int i = 0; i < fftHistSize - 1; i++) Array.Copy(fftHist[i + 1], 0, fftHist[i], 0, fftSize2);
            //for(int j = 0; j < fftSize2; j++) fftHist[i][j] = fftHist[i + 1][j];

            // Update the last spot with the new data from the FFT using the Power() function.
            for(int i = 0; i < fftSize2; i++) fftHist[fftHistSize - 1][i] = fftBuffer[i].Power();
        }

        private static (int Width, int Height) FFT2Pts(int x, int w, int h, int fftSize, double scale = 1.0) {
            double v = ((FFTAvg(x) / fftWindowSum * 2.0) / 20.0) * scale;
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
