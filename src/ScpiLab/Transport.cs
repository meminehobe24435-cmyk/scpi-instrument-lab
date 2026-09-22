using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScpiLab
{
    /// <summary>仪器传输层抽象：真实实现可以是串口 / TCP / USBTMC，这里先提供可复现的模拟实现。</summary>
    public interface ITransport
    {
        string Write(string command);
        bool IsOpen { get; }
        void Open();
        void Close();
    }

    /// <summary>
    /// 模拟仪器：按 SCPI 语义实现少量常用子系统，用于无真机可复现的回归测试。
    /// 支持 *IDN? / *RST / SYST:ERR? / SOUR:VOLT / MEAS:VOLT:DC? / MEAS:CURR:DC? / SENS:FREQ 等。
    /// </summary>
    public sealed class MockInstrumentTransport : ITransport
    {
        private readonly List<string> _errors = new List<string>();
        private double _setVoltage;
        private double _setCurrentLimit = 1.0;
        private double _measuredVoltage = 5.0;
        private double _measuredCurrent = 0.1;
        private double _frequency = 1000.0;
        private double _readbackNoise;          /* 人为噪声，用于验证统计与量程判断 */

        public string Idn { get; set; }
        public bool IsOpen { get; private set; }
        public int CommandCount { get; private set; }

        public MockInstrumentTransport(string idn = "MockLab,PSU-1000,SN0001,1.0.0")
        {
            Idn = idn;
            IsOpen = false;
        }

        public void Open() { IsOpen = true; }
        public void Close() { IsOpen = false; }

        /// <summary>注入测量噪声（测试统计与量程相关逻辑时使用）。</summary>
        public void SetReadbackNoise(double noise) { _readbackNoise = noise; }
        public void SetMeasuredVoltage(double v) { _measuredVoltage = v; }
        public void SetMeasuredCurrent(double a) { _measuredCurrent = a; }
        public double GetSetVoltage() { return _setVoltage; }

        public string Write(string command)
        {
            if (!IsOpen) throw new InvalidOperationException("会话未打开");
            if (command == null) throw new ArgumentNullException("command");
            CommandCount++;

            var cmd = ScpiCommand.Parse(command);
            var head = cmd.Header;

            if (head == "*IDN") return Idn;
            if (head == "*RST")
            {
                _setVoltage = 0.0;
                _setCurrentLimit = 1.0;
                _errors.Clear();
                return null;
            }
            if (head == "SYST:ERR" || head == "SYSTEM:ERROR")
            {
                if (_errors.Count == 0) return "0,\"No error\"";
                var e = _errors[0];
                _errors.RemoveAt(0);
                return e;
            }
            if (head == "SOUR:VOLT" || head == "SOURCE:VOLTAGE")
            {
                if (cmd.ParameterCount != 1) { _errors.Add("-109,\"Missing parameter\""); return null; }
                var v = cmd.NumericParameter(0);
                if (v < 0.0 || v > 30.0) { _errors.Add("-222,\"Data out of range\""); return null; }
                _setVoltage = v;
                return null;
            }
            if (head == "SOUR:CURR" || head == "SOURCE:CURRENT")
            {
                if (cmd.ParameterCount != 1) { _errors.Add("-109,\"Missing parameter\""); return null; }
                _setCurrentLimit = cmd.NumericParameter(0);
                return null;
            }
            if (head == "MEAS:VOLT:DC" || head == "MEASURE:VOLTAGE:DC")
                return Format(_measuredVoltage + _readbackNoise);
            if (head == "MEAS:CURR:DC" || head == "MEASURE:CURRENT:DC")
                return Format(_measuredCurrent + _readbackNoise);
            if (head == "SENS:FREQ" || head == "SENSE:FREQUENCY")
            {
                if (cmd.ParameterCount == 1) { _frequency = cmd.NumericParameter(0); return null; }
                return Format(_frequency);
            }
            if (head == "OUTP" || head == "OUTPUT")
            {
                if (cmd.ParameterCount == 1)
                {
                    var on = cmd.Parameters[0].ToUpperInvariant();
                    if (on != "ON" && on != "OFF" && on != "1" && on != "0")
                    {
                        _errors.Add("-224,\"Illegal parameter value\"");
                    }
                    return null;
                }
                return null;
            }
            _errors.Add("-113,\"Undefined header\"");
            return null;
        }

        private static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
