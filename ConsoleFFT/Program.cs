using OpenTK;
using OpenTK.Audio.OpenAL;
using System;
using System.Collections.Generic;
using System.IO;
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
        private static double scaleWav = 0.0005;

        private static ALCaptureDevice audioCapture;

        private static short[] buffer = new short[512];
        private static int bufferStride;
        private static char[] conBuffer;
        private static int h1;
        private static int h2;
        private static readonly char[] c = { '\u2588', '\u25a0', '\u00b7' }; // █ ■ ·

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

            if(string.IsNullOrEmpty(deviceName)) ParseCommandline(new string[1] { "--device=0" }); // Set default capture device

            bufferStride = BlittableValueType.StrideOf(buffer);

            InitFFT();
            StartMonitoring();
        }

        private static void StartMonitoring() {
            double bufferLengthMs = 15;
            int bufferLengthSamples = (int)(bufferLengthMs * samplingRate * 0.002 / bufferStride);

            stdout = new StreamWriter(Console.OpenStandardOutput());

            audioCapture = ALC.CaptureOpenDevice(deviceName, samplingRate, samplingFormat, bufferLengthSamples);
            ALC.CaptureStart(audioCapture);

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
                    PrintHelp();
                    stdout.Write(conBuffer, 0, conBuffer.Length - (OperatingSystem.IsWindows() ? 1 : 0));
                    stdout.Flush();
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

            ALC.CaptureCloseDevice(audioCapture);

            Console.Clear();
            Console.CursorVisible = true;
        }

        private static void InitRenderer() {
            if(Console.WindowWidth != consoleWidth || Console.WindowHeight != consoleHeight) {
                Console.Clear();
                Console.OutputEncoding = System.Text.Encoding.UTF8; // chcp 65001
                consoleWidth = Console.WindowWidth;
                consoleHeight = Console.WindowHeight;
                Console.CursorVisible = false;
                if(OperatingSystem.IsWindows()) Console.BufferHeight = consoleHeight;

                h1 = (int)(consoleHeight * 0.8);
                h2 = (int)(consoleHeight * 0.3);
                conBuffer = new char[consoleWidth * consoleHeight];
            }

            Array.Fill(conBuffer, '\u0020');
            Console.SetCursorPosition(0, 0);
        }

        private static void RenderFFT() {
            // Log X/Y ==============================================================
            int newDivX;
            (int X, int Y) lastPL = (0, 0);
            int lastW = FFT2Pts(fftSize2 - 1, consoleWidth, consoleHeight, fftSize).Width;
            char bc = c[0];
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
            char bc = c[0];
            double f = short.MaxValue / (scaleWav * 40000.0);
            int x, lx = 0;
            int y, ly = ch2;
            for(int i = 0; i < l; i++) {
                x = (int)((double)i / l * consoleWidth);
                y = (ushort)(fftWavDstBufL[i] / f * ch2 + ch2);

                for(double p = 0; p < 1; p += 0.25) {
                    int tx = Lerp(lx, x, p);
                    int ty = Lerp(ly, y, p);

                    if(ty < ch) {
                        int index = ty * consoleWidth + tx;
                        switch(renderCharMode) {
                            case RenderCharModes.Multiple:
                                if(ty < ch3 || ty > h - ch3)
                                    bc = c[2];
                                else if(ty < ch2 - ch3+4 || ty > ch2 + ch3-4)
                                    bc = c[1];
                                else
                                    bc = c[0];
                                break;
                        }
                        conBuffer[index] = bc;
                    }
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

            string defaultDevice = ALC.GetString(ALDevice.Null, AlcGetString.CaptureDefaultDeviceSpecifier);
            List<string> devices = ALC.GetString(AlcGetStringList.CaptureDeviceSpecifier);
            if(devices.Count > 0) {
                Console.WriteLine("      0: Default Device");

                for(int i = 0; i < devices.Count; i++)
                    Console.WriteLine($"{(devices[i] == defaultDevice ? " [*]" : "    ")} {i + 1,2}: {devices[i]}");
            } else
                Console.WriteLine("No capture/recoding devices found");
        }

        private static void PrintHelp() {
            if(showHelpDelay > 0) {
                string str = $"[+][-]  ScaleFFT: {scaleFFT:F8}\n" +
                             $"[+][-]  ScaleWav: {scaleWav:F8}\n" +
                             $"[C]     Style:    {renderCharMode}\n" +
                             $"[SPACE] Mode:     {renderMode}\n" +
                             $"[ESC]   Exit";

                int x = 0;
                int y = 0;
                int k = 0;
                while(k < str.Length) {
                    if(str[k] == '\n') {
                        x = 0;
                        y++;
                    } else {
                        conBuffer[y * consoleWidth + x++] = str[k];
                    }
                    k++;
                }
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
            Console.WriteLine("--list: List available audio capturing devices");
            Console.WriteLine("--device=n: Set capture to devices by its index. Setting n=0 will select the default device.");
            Console.WriteLine($"--frequency=n: Set the sampling rate frequency. By default, set to {samplingRate:N0} KHz");
            Console.WriteLine($"--bits=n: Set the sampling bit rate. Valid values are 8 or 16. By default, set to {samplingFormat.ToString().Replace("Mono", "")} bits");
            Console.WriteLine($"--fft=n: Set the size of Fourier transform. By default, set to {fftSize:N0} bands");
            Console.WriteLine($"--scaleFFT=n: Set the FFT graph scale. By default, set to {scaleFFT:.################}"); // https://stackoverflow.com/questions/14964737/double-tostring-no-scientific-notation
            Console.WriteLine($"--scaleWav=n: Set the WaveForm graph scale. By default, set to {scaleWav:.################}");
            Console.WriteLine("--help: This printout");

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