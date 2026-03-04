using OpenTK;
using OpenTK.Audio.OpenAL;
using OpenTK.Compute.OpenCL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleFFT {
    public static partial class Program {
        private enum RenderModes {
            FFT = 0,
            Waveform = 1
        }
        private static RenderModes renderMode = RenderModes.FFT;

        private static string deviceName = "";
        private static int samplingRate = 44100;
        private static int fftSize = 1024;
        private static double bufferLengthMs = 8;
        private static ALFormat samplingFormat = ALFormat.Mono16;
        private static double scaleFFT = 0.004;
        private static double scaleWav = 0.0005;

        private static ALCaptureDevice audioCapture;

        private static short[] buffer = new short[512];
        private static int bufferStride;
        private static int h1;
        private static int h2;
        private static readonly char[] c = ['\u2588', '\u25a0', '\u00b7']; // █ ■ ·
        private const string CSI = "\e["; // Control Sequence Introducer (ANSI escape code)

        private const byte SampleToByte = 2;

        private static StreamWriter stdout;

        private static int consoleWidth = 0;
        private static int consoleHeight = 0;

        private const int helpDelay = 400;
        private static int showHelpDelay = helpDelay;

        private static void Main(string[] args) {
            if(OperatingSystem.IsLinux()) {
                scaleFFT /= 2500;
                scaleWav /= 20;
            }

            PrintHeader();

            try {
                if(!ParseCommandline(args)) return;
            } catch(ArgumentException e) {
                Console.WriteLine(e.Message);
                Console.WriteLine();
                PrintDocumentation();
                return;
            }

            if(string.IsNullOrEmpty(deviceName)) ParseCommandline(["--device=0"]); // Set default capture device

            bufferStride = BlittableValueType.StrideOf(buffer);

            InitFFT();
            StartMonitoring();
        }

        private static void StartMonitoring() {
            //stdout = new(Console.OpenStandardOutput(), Encoding.UTF8);
            //Console.SetOut(stdout);
            Console.CursorVisible = false;

            int bufferLengthSamples = (int)((1.0 / bufferLengthMs * samplingRate) / bufferStride);
            audioCapture = ALC.CaptureOpenDevice(deviceName, samplingRate, samplingFormat, bufferLengthSamples);
            ALC.CaptureStart(audioCapture);

            int delay = 30;
            Task.Run(async () => {
                while(true) {
                    await Task.Delay(delay/2);
                    GetSamples();
                }
            });

            Task.Run(async () => {
                StringBuilder sb = new();
                while(true) {
                    await Task.Delay(delay);

                    InitRenderer(sb);
                    switch(renderMode) {
                        case RenderModes.FFT:
                            RenderFFT(sb);
                            break;
                        case RenderModes.Waveform:
                            RenderWaveform(sb);
                            break;
                    }
                    PrintHelp(sb);

                    Console.Write(sb.ToString());
                }
            });

            bool doLoop = true;
            while(doLoop) {
                switch(Console.ReadKey(true).Key) {
                    //switch(ConsoleKey.OemComma) { // For debugging within VS Code
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

            ALC.CaptureStop(audioCapture);
            ALC.CaptureCloseDevice(audioCapture);

            Console.Clear();
            Console.CursorVisible = true;
        }

        private static void InitRenderer(StringBuilder sb) {
            if(Console.WindowWidth != consoleWidth || Console.WindowHeight != consoleHeight) {
                consoleWidth = Console.WindowWidth;
                consoleHeight = Console.WindowHeight;

                h1 = (int)(consoleHeight * 0.8);
                h2 = (int)(consoleHeight * 0.3);
            }

            sb.Clear();
            sb.Append($"{CSI}1;1H{CSI}0J{CSI}0m");
        }

        private static void RenderFFT(StringBuilder sb) {
            // Log X/Y ==============================================================
            int newDivX;
            (int X, int Y) lastPL = (0, 0);
            int lastW = FFT2Pts(fftSize2 - 1, consoleWidth, consoleHeight, fftSize).Width;
            char bc = c[0];
            for(int x = 0; x < fftSize2; x++) {
                (int Width, int Height) s = FFT2Pts(x, consoleWidth, consoleHeight, fftSize, scaleFFT / 100.0);
                newDivX = x / fftSize2 * (consoleWidth - lastW) + s.Width;

                int v = Math.Min(consoleHeight, lastPL.Y);
                for(int xi = lastPL.X; xi < newDivX; xi++) {
                    for(int yi = consoleHeight - v; yi < consoleHeight; yi++) {
                        sb.Append($"{CSI}{yi};{xi}H{bc}");
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

        private static void RenderWaveform(StringBuilder sb) {
            int ch2 = consoleHeight / 2;
            int ch3 = consoleHeight / 4;
            int ch = consoleHeight - 2;
            int l = wavHist[0].Length; // wavDstBufL.Length;
            double hl = (double)l / consoleWidth;
            char bc = c[0];
            double f = short.MaxValue / (scaleWav * 40000.0);
            int x, lx = 0;
            int y, ly = ch2;
            for(int i = 0; i < l; i++) {
                x = (int)(i / hl);
                //y = (ushort)(wavDstBufL[i] / f * ch2 + ch2);
                y = (ushort)(WAVAvg(i) / f * ch2 + ch2);

                for(double p = 0; p < 1; p += 0.25) {
                    int tx = Lerp(lx, x, p);
                    int ty = Lerp(ly, y, p);

                    if(ty < ch) sb.Append($"{CSI}{y};{x}H{bc}");
                    if(tx == x && ty == y) break;
                }
                lx = x;
                ly = y;
            }
        }

        private static int Lerp(int start, int end, double percentage) {
            return (int)(start + percentage * (end - start));
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

                if(param.StartsWith("--"))
                    param = param[2..];
                else
                    throw new ArgumentException("Invalid argument", param);

                switch(param) {
                    case "list": // List available devices
                        ListAvailableCaptureDevices();
                        return false;
                    case "device": // Set capture device
                        if(!int.TryParse(value, out int deviceIndex)) throw new ArgumentException("Invalid argument value", value);

                        string defaultDevice = ALC.GetString(ALDevice.Null, AlcGetString.CaptureDefaultDeviceSpecifier);
                        List<string> devices = ALC.GetString(AlcGetStringList.CaptureDeviceSpecifier);
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
                        if(!int.TryParse(value, out int b)) throw new ArgumentException("Invalid argument value", value);
                        samplingFormat = b switch {
                            8 => ALFormat.Mono8,
                            16 => ALFormat.Mono16,
                            _ => throw new ArgumentException("Invalid argument value", value),
                        };
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

            string defaultDevice = ALC.GetString(ALDevice.Null, AlcGetString.CaptureDefaultDeviceSpecifier);
            List<string> devices = ALC.GetString(AlcGetStringList.CaptureDeviceSpecifier);
            if(devices.Count > 0) {
                Console.WriteLine("      0: Default Device");

                for(int i = 0; i < devices.Count; i++)
                    Console.WriteLine($"{(devices[i] == defaultDevice ? " [*]" : "    ")} {i + 1,2}: {devices[i]}");
            } else
                Console.WriteLine("No capture/recoding devices found");
        }

        private static void PrintHelp(StringBuilder sb) {
            if(showHelpDelay > 0) {
                sb.Append($"{CSI}1;1H{CSI}97;49m[+][-]  {CSI}94;49mScaleFFT: {CSI}36;49m{scaleFFT:F8}");
                sb.Append($"{CSI}2;1H{CSI}97;49m[+][-]  {CSI}94;49mScaleWav: {CSI}36;49m{scaleWav:F8}");
                sb.Append($"{CSI}3;1H{CSI}97;49m[SPACE] {CSI}94;49mMode:     {CSI}36;49m{renderMode}");
                sb.Append($"{CSI}4;1H{CSI}97;49m[ESC]   {CSI}94;49mExit");

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
            Console.WriteLine($"--list        List available audio capturing devices");
            Console.WriteLine($"--device=n    Set capture to devices by its index. Setting n=0 will select the default device.");
            Console.WriteLine($"--frequency=n Set the sampling rate frequency. By default, set to {samplingRate:N0} KHz");
            Console.WriteLine($"--bits=n      Set the sampling bit rate. Valid values are 8 or 16. By default, set to {samplingFormat.ToString().Replace("Mono", "")} bits");
            Console.WriteLine($"--fft=n       Set the size of Fourier transform. By default, set to {fftSize:N0} bands");
            Console.WriteLine($"--scaleFFT=n  Set the FFT graph scale. By default, set to {scaleFFT:.################}"); // https://stackoverflow.com/questions/14964737/double-tostring-no-scientific-notation
            Console.WriteLine($"--scaleWav=n  Set the WaveForm graph scale. By default, set to {scaleWav:.################}");
            Console.WriteLine($"--help        This printout");

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