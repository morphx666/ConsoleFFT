using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleFFT {
    public static partial class Program {
        private enum RenderModes {
            FFT = 0,
            Waveform = 1
        }
        private static RenderModes renderMode = RenderModes.FFT;

        public enum RenderCharModes {
            Simple = 0,
            Multiple = 1
        }
        private static RenderCharModes renderCharMode = RenderCharModes.Simple;

        private static string deviceName = "";
        private static int samplingRate = 44100;
        private static int fftSize = 1024;
        private static ALFormat samplingFormat = ALFormat.Mono16;
        private static double scaleFFT = 0.01;
        private static double scaleWav = 0.001;

        private static AudioCapture audioCapture;

        private static short[] buffer = new short[512];
        private static byte[] conBuffer;
        private static int h1;
        private static int h2;
        private static readonly byte[] c = { 0xDB, 0xFE, 0xFA }; // █ ■ ·
        private static readonly byte[] c1 = { 0xDC, 0xFE, 0xDF }; // ▄ ■ ▀

        private const byte SampleToByte = 2;

        private static readonly bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        private static Stream stdout;

        private static int consoleWidth = 0;
        private static int consoleHeight = 0;

        private const int helpDelay = 400;
        private static int showHelpDelay = helpDelay;

        private static void Main(string[] args) {
            PrintHeader();

            try {
                if(!ParseCommandline(args)) return;
            } catch(ArgumentException e) {
                Console.WriteLine(e.Message);
                Console.WriteLine();
                PrintDocumentation();
                return;
            }

            if(string.IsNullOrEmpty(deviceName)) ParseCommandline(new string[1] { "-device=0" }); // Set default capture device

            InitFFT();
            StartMonitoring();
        }

        private static void StartMonitoring() {
            double bufferLengthMs = 15;
            int bufferLengthSamples = (int)(bufferLengthMs * samplingRate * 0.002 / BlittableValueType.StrideOf(buffer));

            stdout = Console.OpenStandardOutput();

            audioCapture = new AudioCapture(deviceName, samplingRate, samplingFormat, bufferLengthSamples);
            audioCapture.Start();

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
                    InitRenderer();
                    switch(renderMode) {
                        case RenderModes.FFT:
                            RenderFFT();
                            break;
                        case RenderModes.Waveform:
                            RenderWaveform();
                            break;
                    }
                    stdout.Write(conBuffer, 0, conBuffer.Length - (isWindows ? 1 : 0));
                    PrintHelp();
                }
            });

            bool doLoop = true;
            while(doLoop) {
                switch(Console.ReadKey(true).Key) {
                    case ConsoleKey.Escape:
                        doLoop = false;
                        break;
                    case ConsoleKey.Spacebar:
                        switch(renderMode) {
                            case RenderModes.FFT:
                                renderMode = RenderModes.Waveform;
                                break;
                            case RenderModes.Waveform:
                                renderMode = RenderModes.FFT;
                                break;
                        }
                        showHelpDelay = helpDelay;
                        break;
                    case ConsoleKey.C:
                        switch(renderCharMode) {
                            case RenderCharModes.Simple:
                                renderCharMode = RenderCharModes.Multiple;
                                break;
                            case RenderCharModes.Multiple:
                                renderCharMode = RenderCharModes.Simple;
                                break;
                        }
                        showHelpDelay = helpDelay;
                        break;
                    case ConsoleKey.Add:
                        switch(renderMode) {
                            case RenderModes.FFT:
                                scaleFFT *= 1.1;
                                break;
                            case RenderModes.Waveform:
                                scaleWav *= 1.1;
                                break;
                        }
                        showHelpDelay = helpDelay;
                        break;
                    case ConsoleKey.Subtract:
                        switch(renderMode) {
                            case RenderModes.FFT:
                                scaleFFT /= 1.1;
                                break;
                            case RenderModes.Waveform:
                                scaleWav /= 1.1;
                                break;
                        }
                        showHelpDelay = helpDelay;
                        break;
                }
            }
            Console.CursorVisible = true;
        }

        private static void InitRenderer() {
            if(Console.WindowWidth != consoleWidth || Console.WindowHeight != consoleHeight) {
                Console.Clear();
                consoleWidth = Console.WindowWidth;
                consoleHeight = Console.WindowHeight;
                Console.CursorVisible = false;
                if(isWindows) Console.BufferHeight = consoleHeight;

                h1 = (int)(consoleHeight * 0.8);
                h2 = (int)(consoleHeight * 0.3);
                conBuffer = new byte[consoleWidth * consoleHeight];
            }

            conBuffer = conBuffer.Select(i => (byte)32).ToArray();

            Console.SetCursorPosition(0, 0);
        }

        private static void RenderFFT() {
            // Log X/Y ==============================================================
            int newDivX;
            (int X, int Y) lastPL = (0, 0);
            int lastW = FFT2Pts(fftSize2 - 1, consoleWidth, consoleHeight, fftSize).Width;
            byte bc = c[0];
            for(int x = 0; x < fftSize2; x++) {
                (int Width, int Height) s = FFT2Pts(x, consoleWidth, consoleHeight, fftSize, scaleFFT / 100.0);
                newDivX = x / fftSize2 * (consoleWidth - lastW) + s.Width;

                if(x > 0) {
                    int v = Math.Min(consoleHeight, lastPL.Y);
                    for(int xi = lastPL.X; xi < newDivX; xi++) {
                        for(int yi = consoleHeight - v; yi < consoleHeight; yi++) {
                            int index = yi * consoleWidth + xi;
                            if(index < conBuffer.Length) {
                                switch(renderCharMode) {
                                    case RenderCharModes.Multiple:
                                        if(yi > h1) bc = c[0];
                                        else if(yi > h2) bc = c[1];
                                        else bc = c[2];
                                        break;
                                }

                                conBuffer[index] = bc;
                            }
                        }
                    }
                }
                lastPL = (newDivX, s.Height);
            }

            // Lin X / Log Y ========================================================
            //for(int x = 0; x < fftSize2; x++) {
            //    int xi = (int)(x * (double)w / fftSize2);

            //    double v = (FFTAvg(x) / fftWindowSum * 2) / 20.0;
            //    v = Math.Min(Math.Log10(v + 1) / 10.0 * h, h);
            //    v = Math.Min(h, v);

            //    for(int y = h - (int)v; y < h; y++) {
            //        b[y * w + xi] = c;
            //    }
            //}
        }

        private static void RenderWaveform() {
            int h = consoleHeight;
            int ch2 = h / 2;
            int ch3 = h / 4;
            int ch = h - 2;
            int l = fftWavDstBufL.Length;
            byte bc = c[0];
            double f = short.MaxValue / (scaleWav * 40000.0);
            int x;
            int y;
            for(int i = 0; i < l; i++) {
                x = (int)((double)i / l * consoleWidth);
                y = (ushort)(fftWavDstBufL[i] / f * ch2 + ch2);
                if(y < ch) {
                    int index = y * consoleWidth + x;
                    switch(renderCharMode) {
                        case RenderCharModes.Multiple:
                            if(y < ch3 || y > h - ch3)
                                bc = c[2];
                            else if(y < h2 - ch3 || y > h2 + ch3)
                                bc = c[1];
                            else
                                bc = c[0];
                            break;
                    }
                    conBuffer[index] = bc;
                }
            }
        }

        private static bool ParseCommandline(string[] args) {
            for(int i = 0; i < args.Length; i++) {
                string param;
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
                    case "scaleFFT": // Set FFT scale
                        if(!double.TryParse(value, out scaleFFT)) throw new ArgumentException("Invalid argument value", value);
                        break;
                    case "scaleWave": // Set WaveForm scale
                        if(!double.TryParse(value, out scaleWav)) throw new ArgumentException("Invalid argument value", value);
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

        private static void PrintHelp() {
            if(showHelpDelay > 0) {
                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"[+][-]  ScaleFFT: {scaleFFT:F6}");
                Console.WriteLine($"[+][-]  ScaleWav: {scaleWav:F6}");
                Console.WriteLine($"[C]     Style:    {renderCharMode}");
                Console.WriteLine($"[SPACE] Mode:     {renderMode}");
                Console.WriteLine($"[ESC]   Exit");
                showHelpDelay--;
            }
        }

        private static void PrintHeader() {
            string info = $"ConsoleFFT {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
            Console.Title = info;
            Console.WriteLine(info);
        }

        private static void PrintDocumentation() {
            Console.WriteLine("Parameters:\r\n");
            Console.WriteLine("-list: List available audio capturing devices");
            Console.WriteLine("-device=n: Set capture to devices by its index. Setting n=0 will select the default device.");
            Console.WriteLine($"-frequency=n: Set the sampling rate frequency. By default, set to {samplingRate:N0} KHz");
            Console.WriteLine($"-bits=n: Set the sampling bit rate. Valid values are 8 or 16. By default, set to {samplingFormat.ToString().Replace("Mono", "")} bits");
            Console.WriteLine($"-fft=n: Set the size of Fourier transform. By default, set to {fftSize:N0} bands");
            Console.WriteLine($"-scaleFFT=n: Set the FFT graph scale. By default, set to {scaleFFT:.################}"); // https://stackoverflow.com/questions/14964737/double-tostring-no-scientific-notation
            Console.WriteLine($"-scaleWav=n: Set the WaveForm graph scale. By default, set to {scaleWav:.################}");
            Console.WriteLine("-help: This printout");

            Console.WriteLine();
            Console.WriteLine("All parameters are optional");

            PrintShortcuts();
        }

        private static void PrintShortcuts() {
            Console.WriteLine();
            Console.WriteLine("While running:");
            Console.WriteLine("    [+][-] to change the graphic scale");
            Console.WriteLine("    [SPACE] to switch between rendering modes");
            Console.WriteLine("    [C] switch between the rendering character set");
            Console.WriteLine("    [ESC] to exit");
        }
    }
}