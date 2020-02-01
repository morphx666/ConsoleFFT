using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleFFT {
    public partial class Program {
        static string deviceName = "";
        static int samplingRate = 44100;
        static int fftSize = 2048;
        static ALFormat samplingFormat = ALFormat.Mono16;
        static double scale = 0.00002;

        static AudioCapture audioCapture;

        static short[] buffer = new short[512];

        const byte SampleToByte = 2;

        static readonly bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        static Stream stdo;

        private static void Main(string[] args) {
            PrintHeader();

            try {
                if(!ParseCommandline(args)) return;
            } catch(Exception e) {
                Console.WriteLine(e.Message);
                Console.WriteLine();
                PrintDocumentation();
                return;
            }

            if(deviceName == "") ParseCommandline(new string[1] { "-device=0" }); // Set default capture device

            InitFFT();
            StartMonitoring();
        }

        private static void StartMonitoring() {
            double bufferLengthMs = 50;
            int bufferLengthSamples = (int)(bufferLengthMs * samplingRate * 0.001 / BlittableValueType.StrideOf(buffer));

            audioCapture = new AudioCapture(deviceName, samplingRate, samplingFormat, bufferLengthSamples);
            audioCapture.Start();

            Console.CursorVisible = false;
            if(isWindows) Console.BufferHeight = Console.WindowHeight;
            stdo = Console.OpenStandardOutput();

            int delay = (int)(bufferLengthMs / 2 + 0.5);
            Task.Run(() => {
                while(true) {
                    Thread.Sleep(delay);
                    GetSamples();
                }
            });

            Task.Run(() => {
                while(true) {
                    Thread.Sleep(delay);
                    RenderFFT();
                }
            });

            while(true) if(Console.ReadKey(true).Key == ConsoleKey.Escape) break;
            Console.CursorVisible = true;
        }

        private static void RenderFFT() {
            int w = Console.WindowWidth;
            int h = Console.WindowHeight;
            int h1 = (int)(h * 0.8);
            int h2 = (int)(h * 0.3);
            byte[] b = new byte[w * h - 1];
            if(!isWindows) b = b.Select(i => (byte)32).ToArray();

            byte[] c = { 0xDB, 0xFE, 0xFA }; // █ ■ ·

            Console.SetCursorPosition(0, 0);

            // Log X/Y ==============================================================
            int newDivX;
            (int X, int Y) lastPL = (0, 0);
            int lastW = FFT2Pts(fftSize2 - 1, w, h, fftSize).Width;
            byte bc;
            for(int x = 0; x < fftSize2; x++) {
                (int Width, int Height) s = FFT2Pts(x, w, h, fftSize, scale);
                newDivX = x / fftSize2 * (w - lastW) + s.Width;

                if(x > 0) {
                    int v = Math.Min(h, lastPL.Y);
                    for(int xi = lastPL.X; xi < newDivX; xi++) {
                        for(int yi = h - v; yi < h; yi++) {
                            int index = yi * w + xi;
                            if(index < b.Length) {
                                if(yi > h1) bc = c[0];
                                else if(yi > h2) bc = c[1];
                                else bc = c[2];

                                b[index] = bc;
                            }
                        }
                    }
                }
                lastPL = (newDivX, s.Height);
            }

            // Y Log ================================================================
            //for(int x = 0; x < fftSize2; x++) {
            //    int xi = (int)(x * (double)w / fftSize2);

            //    double v = (FFTAvg(x) / fftWindowSum * 2) / 20.0;
            //    v = Math.Min(Math.Log10(v + 1) / 10.0 * h, h);
            //    v = Math.Min(h, v);

            //    for(int y = h - (int)v; y < h; y++) {
            //        b[y * w + xi] = c;
            //    }
            //}

            stdo.Write(b, 0, b.Length);
        }

        private static bool ParseCommandline(string[] args) {
            for(int i = 0; i < args.Length; i++) {
                string param = "";
                string value = "";

                if(args[i].Contains('=')) {
                    param = args[i].Split('=')[0];
                    value = args[i].Split('=')[1];
                } else {
                    param = args[i];
                }

                if(param.StartsWith("-")) {
                    param = param.Substring(1);
                } else {
                    throw new ArgumentException("Invalid argument", param);
                }

                switch(param) {
                    case "list": // List available devices
                        ListAvailableCaptureDevices();
                        return false;
                    case "device": // Set capture device
                        int deviceIndex;
                        if(!int.TryParse(value, out deviceIndex)) throw new ArgumentException("Invalid argument value", value);

                        string defaultDevice = AudioCapture.DefaultDevice;
                        IList<string> devices = AudioCapture.AvailableDevices;
                        if(devices.Count > 0) {
                            for(int j = 0; j < devices.Count; j++) {
                                if(deviceIndex == 0 && devices[j] == defaultDevice) {
                                    deviceName = defaultDevice;
                                    break;
                                } else if((j + 1) == deviceIndex) {
                                    deviceName = devices[j];
                                    break;
                                }
                            }
                        }
                        break;
                    case "frequency": // Set sampling rate
                        if(!int.TryParse(value, out samplingRate)) throw new ArgumentException("Invalid argument value", value);
                        break;
                    case "bits": // Set bits per sample
                        int b;
                        if(!int.TryParse(value, out b)) throw new ArgumentException("Invalid argument value", value);
                        switch(b) {
                            case 8:
                                samplingFormat = ALFormat.Mono8;
                                break;
                            case 16:
                                samplingFormat = ALFormat.Mono16;
                                break;
                            default:
                                throw new ArgumentException("Invalid argument value", value);
                        }
                        break;
                    case "fft": // Set FFT size
                        if(!int.TryParse(value, out fftSize)) throw new ArgumentException("Invalid argument value", value);
                        break;
                    case "scale": // Set FFT scale
                        if(!double.TryParse(value, out scale)) throw new ArgumentException("Invalid argument value", value);
                        break;
                    case "help": // Show documentation
                        PrintDocumentation();
                        return false;
                    default:
                        throw new ArgumentException("Unknown argument", param);
                }
            }
            return true;
        }

        private static void ListAvailableCaptureDevices() {
            Console.WriteLine("Capture Devices:\r\n");

            string defaultDevice = AudioCapture.DefaultDevice;
            IList<string> devices = AudioCapture.AvailableDevices;
            if(devices.Count > 0) {
                Console.WriteLine(" 0: Default Device");

                for(int i = 0; i < devices.Count; i++) {
                    Console.WriteLine($"{(i + 1).ToString().PadLeft(2, ' ')}: {devices[i]} {(devices[i] == defaultDevice ? " [*]" : "")}");
                }
            } else {
                Console.WriteLine("No capture/recoding devices found");
            }
        }

        private static void PrintHeader() {
            Console.WriteLine($"ConsoleFFT {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            Console.WriteLine();
        }

        private static void PrintDocumentation() {
            Console.WriteLine("Parameters:\r\n");
            Console.WriteLine("-list: List available audio capturing devices");
            Console.WriteLine("-device=n: Set capture to devices by its index. Setting n=0 will select the default device.");
            Console.WriteLine("-frequency=n: Set the sampling rate frequency. By default, set to 44,100 KHz");
            Console.WriteLine("-bits=n: Set the sampling bit rate. Valid values are 8 or 16. By default, set to 16 bits");
            Console.WriteLine("-fft=n: Set the size of Fourier transform. By default, set to 1,024 bands");
            Console.WriteLine("-scale=n: Set the graph scale. By default, set to 0.0001");
            Console.WriteLine("-help: This printout");
        }
    }
}