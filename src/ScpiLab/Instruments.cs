using System;

namespace ScpiLab
{
    /// <summary>可编程直流电源：设定值 + 量程检查 + 读回校验。</summary>
    public sealed class PowerSupply
    {
        private readonly InstrumentSession _session;
        public double MaxVoltage { get; private set; }
        public double MaxCurrent { get; private set; }

        public PowerSupply(InstrumentSession session, double maxVoltage = 30.0, double maxCurrent = 3.0)
        {
            if (session == null) throw new ArgumentNullException("session");
            _session = session;
            MaxVoltage = maxVoltage;
            MaxCurrent = maxCurrent;
        }

        /// <summary>设置电压：超量程直接拒绝（不让错误命令下到仪器）。</summary>
        public void SetVoltage(double volts)
        {
            if (volts < 0.0 || volts > MaxVoltage)
                throw new ScpiException(string.Format("电压 {0} V 超出量程 0~{1} V", volts, MaxVoltage));
            _session.Write("SOUR:VOLT " + volts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _session.ThrowIfInstrumentError();
        }

        public void SetCurrentLimit(double amps)
        {
            if (amps <= 0.0 || amps > MaxCurrent)
                throw new ScpiException(string.Format("限流 {0} A 超出量程 0~{1} A", amps, MaxCurrent));
            _session.Write("SOUR:CURR " + amps.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _session.ThrowIfInstrumentError();
        }

        public void Output(bool on)
        {
            _session.Write("OUTP " + (on ? "ON" : "OFF"));
            _session.ThrowIfInstrumentError();
        }

        public double MeasureVoltage() { return _session.QueryDouble("MEAS:VOLT:DC?"); }
        public double MeasureCurrent() { return _session.QueryDouble("MEAS:CURR:DC?"); }
    }

    /// <summary>数字万用表（DMM）：直流电压/电流读取 + 量程判断。</summary>
    public sealed class DigitalMultimeter
    {
        private readonly InstrumentSession _session;
        public double MaxVoltage { get; private set; }

        public DigitalMultimeter(InstrumentSession session, double maxVoltage = 1000.0)
        {
            if (session == null) throw new ArgumentNullException("session");
            _session = session;
            MaxVoltage = maxVoltage;
        }

        public double ReadDcVoltage()
        {
            var v = _session.QueryDouble("MEAS:VOLT:DC?");
            if (Math.Abs(v) > MaxVoltage)
                throw new ScpiException(string.Format("读数 {0} V 超出量程 {1} V", v, MaxVoltage));
            return v;
        }

        public double ReadDcCurrent() { return _session.QueryDouble("MEAS:CURR:DC?"); }
    }
}
