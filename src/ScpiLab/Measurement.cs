using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScpiLab
{
    public sealed class SweepPoint
    {
        public double SetVoltage;
        public double MeasuredVoltage;
        public double MeasuredCurrent;
    }

    public sealed class SweepResult
    {
        public List<SweepPoint> Points = new List<SweepPoint>();

        public double MeanCurrent()
        {
            if (Points.Count == 0) return 0.0;
            double sum = 0.0;
            foreach (var p in Points) sum += p.MeasuredCurrent;
            return sum / Points.Count;
        }

        public double StdDevCurrent()
        {
            if (Points.Count < 2) return 0.0;
            double mean = MeanCurrent(), acc = 0.0;
            foreach (var p in Points) { var d = p.MeasuredCurrent - mean; acc += d * d; }
            return Math.Sqrt(acc / (Points.Count - 1));
        }

        public double MaxVoltageError()
        {
            double worst = 0.0;
            foreach (var p in Points)
            {
                var e = Math.Abs(p.MeasuredVoltage - p.SetVoltage);
                if (e > worst) worst = e;
            }
            return worst;
        }

        /// <summary>导出 CSV（测试数据要能留痕、能被别的工具再分析）。</summary>
        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("set_voltage_V,measured_voltage_V,measured_current_A");
            foreach (var p in Points)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}",
                    p.SetVoltage, p.MeasuredVoltage, p.MeasuredCurrent));
            }
            return sb.ToString();
        }
    }

    /// <summary>可复用的测量脚本：电压扫描 + 采集 + 统计（对应"测试方法标准化、可复用"）。</summary>
    public static class SweepRunner
    {
        public static SweepResult RunVoltageSweep(PowerSupply psu, double from, double to, double step, int settleReads = 3)
        {
            if (psu == null) throw new ArgumentNullException("psu");
            if (step <= 0.0 || to < from) throw new ScpiException("扫描参数不合法");

            var result = new SweepResult();
            for (double v = from; v <= to + 1e-9; v += step)
            {
                psu.SetVoltage(v);
                double measuredV = 0.0, measuredI = 0.0;
                for (int i = 0; i < Math.Max(1, settleReads); i++)
                {
                    measuredV = psu.MeasureVoltage();
                    measuredI = psu.MeasureCurrent();
                }
                result.Points.Add(new SweepPoint
                {
                    SetVoltage = v,
                    MeasuredVoltage = measuredV,
                    MeasuredCurrent = measuredI
                });
            }
            return result;
        }
    }
}
