using System;
using ScpiLab;

namespace ScpiLab.SelfTest
{
    /// <summary>自检：dotnet run 直接跑，输出 PASS/FAIL 与汇总。不依赖任何外部测试框架（离线可跑）。</summary>
    internal static class Program
    {
        private static int _run;
        private static int _fail;

        private static void Group(string name) { Console.WriteLine(); Console.WriteLine("[" + name + "]"); }

        private static void Check(bool cond, string msg)
        {
            _run++;
            if (cond) Console.WriteLine("  PASS  " + msg);
            else { _fail++; Console.WriteLine("  FAIL  " + msg); }
        }

        private static void TestParse()
        {
            Group("SCPI 命令解析（头 / 查询 / 多参数 / 单位）");
            var q = ScpiCommand.Parse("MEAS:VOLT:DC?");
            Check(q.Header == "MEAS:VOLT:DC" && q.IsQuery, "查询命令：去掉 '?' 并标记为查询");
            Check(ScpiCommand.Parse("sour:volt 5").Header == "SOUR:VOLT", "命令头大小写不敏感");

            var m = ScpiCommand.Parse("SOUR:VOLT 5, 0.5");
            Check(m.ParameterCount == 2, "逗号分隔多参数解析为 2 个");
            Check(Math.Abs(m.NumericParameter(1) - 0.5) < 1e-12, "第 2 个参数数值正确");

            Check(Math.Abs(ScpiCommand.ParseValue("5V") - 5.0) < 1e-12, "单位后缀 V 不缩放");
            Check(Math.Abs(ScpiCommand.ParseValue("10mA") - 0.01) < 1e-12, "mA → 0.01 A");
            Check(Math.Abs(ScpiCommand.ParseValue("1.5kHz") - 1500.0) < 1e-9, "kHz → 1500 Hz");
            Check(Math.Abs(ScpiCommand.ParseValue("2e3") - 2000.0) < 1e-9, "科学计数法 2e3");
            Check(Math.Abs(ScpiCommand.ParseValue("-2.5e-3") + 0.0025) < 1e-12, "负指数 -2.5e-3");

            bool threw = false;
            try { ScpiCommand.ParseValue("abc"); } catch (FormatException) { threw = true; }
            Check(threw, "非法数值 → 抛 FormatException");
            threw = false;
            try { ScpiCommand.Parse("   "); } catch (FormatException) { threw = true; }
            Check(threw, "空命令 → 抛 FormatException");
        }

        private static void TestSession()
        {
            Group("仪器会话（打开 / 查询配对 / 错误队列 / 超时参数）");
            var mock = new MockInstrumentTransport();
            using (var session = new InstrumentSession(mock, 1500))
            {
                bool threw = false;
                try { session.Query("*IDN?"); } catch (ScpiException) { threw = true; }
                Check(threw, "未打开会话就查询 → 抛异常（而不是静默返回空）");

                session.Open();
                Check(mock.IsOpen, "会话已打开");
                Check(session.Identify().StartsWith("MockLab"), "*IDN? 返回识别信息");

                threw = false;
                try { session.Query("SOUR:VOLT 5"); } catch (ScpiException) { threw = true; }
                Check(threw, "用非查询命令做查询 → 抛异常（提前失败优于拿到错数据）");

                session.ThrowIfInstrumentError();
                Check(true, "错误队列为空时正常返回");

                session.Write("SOUR:VOLT 100");            /* 超量程 → 仪器侧报 -222 */
                threw = false;
                try { session.ThrowIfInstrumentError(); } catch (ScpiException) { threw = true; }
                Check(threw, "仪器侧错误码被读取并抛出（不吞错）");
            }
        }

        private static void TestPowerSupply()
        {
            Group("电源设备类（量程检查 / 设定读回 / 输出开关）");
            var mock = new MockInstrumentTransport();
            var session = new InstrumentSession(mock);
            session.Open();
            var psu = new PowerSupply(session, 30.0, 3.0);

            bool threw = false;
            try { psu.SetVoltage(40.0); } catch (ScpiException) { threw = true; }
            Check(threw, "超过最大电压 → 上层直接拒绝，不下发命令");

            psu.SetVoltage(12.0);
            Check(Math.Abs(mock.GetSetVoltage() - 12.0) < 1e-9, "设定 12 V 后被仪器接受");
            Check(Math.Abs(psu.MeasureVoltage() - 5.0) < 1e-9, "读回电压来自模拟仪器");

            threw = false;
            try { psu.SetCurrentLimit(5.0); } catch (ScpiException) { threw = true; }
            Check(threw, "超过最大限流 → 拒绝");

            psu.Output(true);
            psu.Output(false);
            session.ThrowIfInstrumentError();
            Check(true, "输出开关命令无仪器错误");
        }

        private static void TestSweep()
        {
            Group("测量脚本（电压扫描 / 统计 / CSV 导出）");
            var mock = new MockInstrumentTransport();
            var session = new InstrumentSession(mock);
            session.Open();
            var psu = new PowerSupply(session, 30.0, 3.0);

            var result = SweepRunner.RunVoltageSweep(psu, 0.0, 10.0, 2.5);
            Check(result.Points.Count == 5, "0→10 V 步进 2.5 → 5 个扫描点");
            Check(Math.Abs(result.Points[4].SetVoltage - 10.0) < 1e-9, "最后一个扫描点电压正确");
            Check(result.MeanCurrent() > 0.0, "平均电流可统计");

            mock.SetReadbackNoise(0.02);
            var noisy = SweepRunner.RunVoltageSweep(psu, 0.0, 4.0, 1.0);
            Check(noisy.StdDevCurrent() > 0.0, "带噪声读数 → 标准差大于 0（统计有效）");
            Check(noisy.MaxVoltageError() > 0.0, "回读与设定值偏差可量化");

            var csv = noisy.ToCsv();
            Check(csv.StartsWith("set_voltage_V"), "CSV 带表头");
            Check(csv.Split('\n').Length >= 6, "CSV 行数 = 表头 + 扫描点");

            bool threw = false;
            try { SweepRunner.RunVoltageSweep(psu, 10.0, 0.0, 1.0); } catch (ScpiException) { threw = true; }
            Check(threw, "终止电压小于起始电压 → 拒绝");
        }

        private static int Main()
        {
            Console.WriteLine("=== SCPI 仪器程控框架 · 自检（解析 / 会话 / 设备类 / 测量脚本） ===");
            TestParse();
            TestSession();
            TestPowerSupply();
            TestSweep();
            Console.WriteLine();
            Console.WriteLine("------------------------------------------------------------");
            Console.WriteLine(string.Format("用例总数 {0}，通过 {1}，失败 {2}", _run, _run - _fail, _fail));
            Console.WriteLine(_fail == 0 ? "RESULT: ALL PASS" : "RESULT: FAILED");
            return _fail == 0 ? 0 : 1;
        }
    }
}
