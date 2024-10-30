using Design_imGUINET;
using ImGuiNET;
using Microsoft.VisualBasic.Logging;
using SoapySpectrum.Extentions;
using SoapySpectrum.Extentions.Signal_Generator;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;
using Vortice.DXCore;
namespace Signal_Generator
{
    internal class UI : ClickableTransparentOverlay.Overlay
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        public UI() : base(Screen.PrimaryScreen.Bounds.Width,Screen.PrimaryScreen.Bounds.Height)
        {
            base.VSync = false;
        }
        bool transmitting = false;
        static int methodindex;
        public string[] method = new string[] { "Specific","List","Sweep"};
        public double frequency = 0;
        public double level = 0;
        public static bool hasCalibration = false;
        public static Dictionary<double, double> caliData = new Dictionary<double, double>();
        void status()
        {
            var style = ImGui.GetStyle();
            var colorStatus = (transmitting) ? Color.Green : Color.Gray;
            string button_text = (transmitting) ? $"RF ON {FontAwesome5.ChartLine}" : $"RF OFF {FontAwesome5.ChartBar}";
            var temp = style.Colors[ImGuiCol.Text.toInt()];
            style.Colors[ImGuiCol.Text.toInt()] = colorStatus.toVec4();
            ImGui.LabelText("##RFLABEL", button_text);
            style.Colors[ImGuiCol.Text.toInt()] = temp;
            ImGui.LabelText("##frequencylabel", $"Frequency: {frequency}");
            ImGui.LabelText("##levellabel", $"level: {level}");

        }
        public static double formatFreq(string input)
        {
            input = input.ToUpper();
            double exponent = 1;
            if (input.Contains("K"))
                exponent = 1e3;
            if (input.Contains("M"))
                exponent = 1e6;
            if (input.Contains("G"))
                exponent = 1e9;
            double results = 80000000;
            if (!double.TryParse(input.Replace("K", "").Replace("M", "").Replace("G", ""), out results))
            {
                Logger.Error("Invalid Frequency Format, changing to 80000000");
            }
            return results * exponent;
        }
        string frequencyText = string.Empty,levelText = string.Empty;
        void specific()
        {
            if (ImGui.InputTextWithHint("##inputFrequency","Frequency", ref frequencyText,32))
               frequency = formatFreq(frequencyText);
            if (ImGui.InputTextWithHint("##inputLevel", "dB level", ref levelText, 32))
                double.TryParse(levelText, out level);
        }
        string freqStart_text = string.Empty, freqStop_text = string.Empty,
            gainStart_text = string.Empty, gainStop_text = string.Empty, 
            stepText = string.Empty;
        double freqStart, freqStop,
            gainStart,gainStop,
            step;
        void sweep()
        {
            ImGui.PushItemWidth(ImGui.GetWindowSize().X/2 - 20);
            if (ImGui.InputTextWithHint("##sweep_FreqStart", "Frequency Start", ref freqStart_text, 32))
                freqStart = formatFreq(freqStart_text);
            ImGui.SameLine();
            if (ImGui.InputTextWithHint("##sweep_FreqStop", "Frequency Stop", ref freqStop_text, 32))
                freqStop = formatFreq(freqStop_text);

            if (ImGui.InputTextWithHint("##sweep_GainStart", "Gain Start", ref gainStart_text, 32))
                double.TryParse(gainStart_text, out gainStart);
            ImGui.SameLine();
            if (ImGui.InputTextWithHint("##sweep_GainStop", "Gain Stop", ref gainStop_text, 32))
                double.TryParse(gainStop_text, out gainStop);

            ImGui.PopItemWidth();
            if (ImGui.InputTextWithHint("##sweep_stepText", "Step", ref stepText, 32))
                double.TryParse(stepText, out step);
        }
      
        Dictionary<int, Transmission.sweepBurst> list = new Dictionary<int, Transmission.sweepBurst>();
        int length = 1;
        void drawIndex(int idx)
        {
            Transmission.sweepBurst temp;
           if(!list.TryGetValue(idx,out temp))
            {
               temp = new Transmission.sweepBurst()
                {
                    frequency_txt = string.Empty,
                    level_txt = string.Empty,
                    sleep_txt = string.Empty,
                    Frequency = 0,
                    level = 0,
                    sleep = 0
                };
                list.Add(idx, temp);
            }
            ImGui.PushItemWidth(150);
            if(ImGui.InputTextWithHint($"##list_frequency{idx}", $"{FontAwesome5.Wind} Frequency{idx}", ref temp.frequency_txt, 10))
                temp.Frequency = formatFreq(temp.frequency_txt);
            ImGui.SameLine();
            ImGui.PushItemWidth(100);
            if (ImGui.InputTextWithHint($"##list_level{idx}", $"{FontAwesome5.BoltLightning} Level{idx} (dB)", ref temp.level_txt, 10))
                double.TryParse(temp.level_txt,out temp.level);
            ImGui.SameLine();
            ImGui.PushItemWidth(110);
            if (ImGui.InputTextWithHint($"##list_time{idx}", $"{FontAwesome5.Clock} Time{idx} (ms)", ref temp.sleep_txt, 10))
                int.TryParse(temp.sleep_txt, out temp.sleep);
            ImGui.PopItemWidth();
            ImGui.PopItemWidth();
            ImGui.PopItemWidth();
            list[idx] = temp;
        }
        void listTab()
        {
            if (ImGui.Button("add",new System.Numerics.Vector2(184,30))) length++;
            ImGui.SameLine();
            if (ImGui.Button("remove", new System.Numerics.Vector2(184, 30))) length--;
            if (length <= 0) length = 1;
            for(int i = 0;i<length;i++)
            {
                drawIndex(i);
            }
        }
        static ImFontPtr PoppinsFont, IconFont;
        public bool initializedResources = false;
        public bool CalibrationMode = false;
        private double calibrationData;
        private Dictionary<double,double> calibration = new Dictionary<double, double>();
        public unsafe void loadResources()
        {
            Logger.Debug("Loading Application Resources");
            var io = ImGui.GetIO();

            this.ReplaceFont(config =>
            {
                var io = ImGui.GetIO();
                io.Fonts.AddFontFromFileTTF(@"Fonts\Poppins-Light.ttf", 20, config, io.Fonts.GetGlyphRangesChineseSimplifiedCommon());
                config->MergeMode = 1;
                config->OversampleH = 1;
                config->OversampleV = 1;
                config->PixelSnapH = 1;

                var custom2 = new ushort[] { 0xe005, 0xf8ff, 0x00 };
                fixed (ushort* p = &custom2[0])
                {
                    io.Fonts.AddFontFromFileTTF("Fonts\\fa-solid-900.ttf", 16, config, new IntPtr(p));
                }
            });
            Logger.Debug("Replaced font");

            PoppinsFont = io.Fonts.AddFontFromFileTTF(@"Fonts\Poppins-Light.ttf", 16);
            //IconFont = io.Fonts.AddFontFromFileTTF(@"Fonts\fa-solid-900.ttf", 16,, new ushort[] { 0xe005,
            //0xf8ff,0});
        }
        protected unsafe override void Render()
        {
#if DEBUG
            //ImGui.ShowStyleEditor();
#endif
            var style = ImGui.GetStyle();
            if (!initializedResources)
            {
               
                ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2);
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, 0);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, 0);
                style.Colors[ImGuiCol.Border.toInt()] = Color.FromArgb(200,113, 96, 232).toVec4();
                style.Colors[ImGuiCol.TitleBgActive.toInt()] = Color.Black.toVec4();
                style.WindowTitleAlign = new System.Numerics.Vector2(0.5f, 0.5f);
                loadResources();
                initializedResources = true;
            }
            ImGui.Begin($"{FontAwesome5.Bolt} Signal Generator", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
            
            status();
            string button_text = (transmitting) ? "Stop" : "Transmit";
            if (ImGui.Button(button_text))
            {
                transmitting = !transmitting;
                if(transmitting)
                {
                    switch(methodindex){
                        case 0:
                            Transmission.beginSpecific(frequency,level);
                            break;
                        case 1:
                            Transmission.beginList(list);
                            break;
                        case 2:
                            Logger.Info($"Starting sweep {freqStart},{freqStop},{gainStart},{gainStop},{step}");
                            Transmission.beginSweep(freqStart,freqStop,gainStart,gainStop,step);
                            break;
                    }
                } else
                {
                    Transmission.endTask();
                }
            }
            ImGui.SameLine();
            button_text = CalibrationMode ?  "Finish Calibration": "Calibrate";
            if (ImGui.Button(button_text))
            {
                CalibrationMode = !CalibrationMode;
                if (!CalibrationMode)
                {
                    string data = string.Empty;
                    foreach(KeyValuePair<double,double> pair in calibration)
                    {
                        data += $"{pair.Key},{pair.Value}\n";
                    }
                    File.WriteAllText("cal.csv", data);
                }
            }
            if (CalibrationMode)
            {
                ImGui.LabelText("##calibrationText", $"[CALIBRATION] Actual Received Gain\n at FREQ {frequency}, LEVEL: {level}");
                ImGui.InputDouble("##calibrationDB",ref calibrationData);
                if (ImGui.Button("Update Cal Data"))
                {
                    if (CalibrationMode)
                    {
                        var data = level - calibrationData;
                        if (calibration.ContainsKey(frequency))
                        {
                            calibration[frequency] = data;
                        }
                        else
                        {
                            calibration.Add(frequency, data);
                        }
                    }
                    calibrationData = 0;
                }
            }
            var cursorpos = ImGui.GetCursorPos();
            ImGui.LabelText("##modeLabel", "Mode: ");
            ImGui.SetCursorPos(new System.Numerics.Vector2(cursorpos.X + ImGui.CalcTextSize("Mode: ").X + 5, cursorpos.Y));
            ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - 80);
            ImGui.Combo("##method", ref methodindex, method, method.Length);
           
            switch(methodindex)
            {
                case 0:
                    //Specific
                    specific();
                    break;
                case 1:
                    //list
                    listTab();
                    break;
                case 2:
                    //sweep
                    sweep();
                    break;

            }
            ImGui.SetWindowSize(new System.Numerics.Vector2(400, 500));
            ImGui.End();
        }
    }
}
